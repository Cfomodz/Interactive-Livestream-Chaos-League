
using LiveChat.Twitch;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using Unity.Loading;
using UnityEngine;
using UnityEngine.Networking;

public enum PlayerHandlerState {King, BiddingQ, Gameplay, Idle }

public class PlayerHandler : MonoBehaviour, TravelingIndicatorIO, TI_Bid_IO
{
    public PlayerHandlerState State = PlayerHandlerState.Idle;

    private GameManager _gm;

    public PlayerProfile pp;
    public PlayerBall pb;
    public PBHologram pbh;

    public PlayerReceiveable ReceivableTarget;

    public Texture PfpTexture;

    public bool Initializing = true;

    //Ball Customizations
    public Color NameTextColor = Color.white;
    private Gradient TrailGradient;

    //private bool trailEnabled;
    private Color speechBubbleFill;
    private Color speechBubbleTxt;

    private float timeoutComboAfter = 3;
    private int comboCount = 0;
    private float comboTimer = 0;

    private CancellationTokenSource _inactivityCTS;

    public int RankScore = 0; //Used for determining the rank on the podium and to allow for ties

    public DateTime LastAccess; //Used for determining if it's safe to unload the ph. Basically for an inactivity timer. 

    public bool FlaggedToUnload = false;

    //public long SessionScoreBeforeTileStarts = 0; 
    public long TilePointsROI = 0; 

    private float _receiveGoldTimer = 0;
    private long _receiveGoldAccumulator = 0;
    private int _receiveGoldChainDepth = 0;

    //Each hop up the invite chain goes faster than the last: quicker hand-off, faster flight, higher pitch
    private const float InviteBonusHoldSeconds = 1.5f;
    private const float InviteBonusSpeedUpPerHop = 1.5f;
    private const float InviteBonusBaseTravelSpeed = 0.1f;
    private const float InviteBonusPitchStepPerHop = 0.12f;

    public IEnumerator CInitPlayerHandler(GameManager gm, string twitchID)
    {
        _gm = gm;
        Initializing = true;
        SetState(PlayerHandlerState.Idle);
        PfpTexture = null;
        FlaggedToUnload = false;

        var t = Task.Run(async () => await SQLiteServiceAsync.GetPlayer(twitchID));
        yield return new WaitUntil(() => t.IsCompleted);

        pp = t.Result;
        LastAccess = DateTime.Now; //CRU

        Initializing = false;
        _inactivityCTS = new CancellationTokenSource();
        _ = InactivityPoll(_inactivityCTS); 
    }

    public void SetState(PlayerHandlerState state)
    {
        LastAccess = DateTime.Now;
        State = state;
    }
    public PlayerHandlerState GetState()
    {
        return State;
    }
    public bool IsKing()
    {
        if (State == PlayerHandlerState.King)
            return true;
        return false;
    }
    public void SetRankScore(int score)
    {
        RankScore = score;
    }
    public int GetRankScore()
    {
        return RankScore;
    }
    public void ResetRankScore()
    {
        RankScore = int.MaxValue;
    }

    public int GetBid()
    {
        return pp.CurrentBid;
    }

    public void IncrementBid(int amount)
    {
        pp.CurrentBid += amount;
        if (pb != null)
            pb.UpdateBidCountText();
    }

    public void DecrementBid(int amount) 
    {
        pp.CurrentBid -= amount;
        if (pp.CurrentBid < 0)
            pp.CurrentBid = 0;
        if (pb != null)
            pb.UpdateBidCountText();
    }


    public void ResetBid()
    {
        pp.CurrentBid = 0; 
        if(pb != null)
            pb.UpdateBidCountText();
    }

    public IEnumerator GetPfp(CoroutineResult<Texture> result)
    {
        if (PfpTexture != null)
        {
            result.Complete(PfpTexture);
            yield break;
        }

        yield return LoadBallPfp();

        if(PfpTexture == null)
        {
            Debug.LogError($"Failed to get ph pfp for {pp.TwitchUsername}");
            result.Complete(GetGameManager().DefaultPFP);
            yield break;
        }
        result.Complete(PfpTexture);
    }

    public IEnumerator LoadBallPfp()
    {
        var t = TwitchApi.GetUserByUsername(pp.TwitchUsername);
        yield return new WaitUntil(() => t.IsCompleted);

        TwitchUser user = t.Result;

        if(user == null)
        {
            Debug.Log($"Failed to load ball Pfp for {pp.TwitchUsername}");
            PfpTexture = GetGameManager().DefaultPFP;
            if (pb != null)
                pb._mainBody.material.mainTexture = PfpTexture;
            pbh.MainBody.material.mainTexture = PfpTexture;//Also set the hologram pfp no matter what

            yield break;
        }

        yield return GetTextureFromURL(user.ProfileImageUrl);

        if(pb != null)
            pb._mainBody.material.mainTexture = PfpTexture;
        pbh.MainBody.material.mainTexture = PfpTexture;//Also set the hologram pfp no matter what

    }


    //Do this only when in full ball mode
    private IEnumerator GetTextureFromURL(string url)
    {
        /*if (PfpTexture != null)
        {
            Destroy(PfpTexture);
            yield return new WaitForEndOfFrame();
            Debug.Log("Done destroying previous pfpTexture"); 
        }*/

        PfpTexture = null;

        using (UnityWebRequest www = UnityWebRequestTexture.GetTexture(url))
        {
            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
                Debug.LogError(www.error);
            else
                PfpTexture = ((DownloadHandlerTexture)www.downloadHandler).texture;
        }
    }

    private void Update()
    {
        if (comboCount > 0)
        {
            if (comboTimer > 0)
                comboTimer -= Time.deltaTime;
            else
                comboCount = 0;
        }

        if(_receiveGoldAccumulator > 0)
        {
            if(_receiveGoldTimer > 0)
            {
                _receiveGoldTimer -= Time.deltaTime;
                return;
            }
            //Send the invite bonus
            StartCoroutine(CheckBonusToInviter(_receiveGoldAccumulator, _receiveGoldChainDepth + 1));
            _receiveGoldAccumulator = 0;
            _receiveGoldChainDepth = 0;
        }
    }


    public async Task InactivityPoll(CancellationTokenSource cts)
    {
        while (true)
        {
            await Task.Delay(1_000);

            if (cts.IsCancellationRequested)
                return;

            try
            {
                //Don't unload multiple times while we're still waiting
                if (FlaggedToUnload)
                    continue;

                //If they have a player ball don't despawn
                if (pb != null)
                    continue;

                //If they're not in the idle state, don't despawn
                if (GetState() != PlayerHandlerState.Idle)
                    continue;

                //If the hologram is active on the podium, don't despawn
                if (pbh.gameObject.activeSelf)
                    continue;

                //If they're a player on the current gameplay tile, even if they're dead, don't despawn them
                TileController tc = _gm.GetTileController();
                if (tc.GameplayTile != null && tc.GameplayTile.Players.Contains(this))
                    continue;

                //If they have an active rebellion, don't despawn them TODO
                RebellionController rc = _gm.GetRebellionController();
                if (rc.DoesPlayerHaveActiveRebellion(this))
                    continue;

                var expireTime = LastAccess.AddSeconds(AppConfig.inst.GetF("destroyPlayerHandlerAfterSecInactivity"));
                //If they have nothing on screen, and they haven't interacted with the game in __ seconds, destroy the player handler
                if (DateTime.Now >= expireTime)
                {
                    Debug.Log($"Starting to unload player handler in late update {pp.TwitchUsername}");
                    await UnloadPh();
                }
                else
                    FlaggedToUnload = false; 
            }
            catch (Exception ex)
            {
                Debug.Log($"Failed to destroy player handler: " + ex.Message);
            }

        }
    }

    //Executes after the update loop and coroutines are finished
    private void LateUpdate()
    {
        if (!FlaggedToUnload)
            return;

        var expireTime = LastAccess.AddSeconds(AppConfig.inst.GetF("destroyPlayerHandlerAfterSecInactivity"));
        //If the player became active in the time it took to save their profile, cancel the unloading!
        if (DateTime.Now < expireTime)
        {
            Debug.Log($"New activty for {pp.TwitchUsername} found! Cancelling player unload.");
            FlaggedToUnload = false; 
            return;
        }

        SetState(PlayerHandlerState.Idle);
        PfpTexture = null;
        _gm.PlayerHandlers.Remove(pp.TwitchID);
        gameObject.name = "pooledPH";
        OnDestroy(); //Stop the polling for inactivity
        _gm.PlayerHandlersPool.ReturnObject(this);
        Debug.Log("Done sending player back to pool");
        FlaggedToUnload = false;
    }

    public void SpeechBubble(string message)
    {
        if (pb == null)
            return;

        if (message.Length <= 0)
            return;

        //Don't do the speech bubble if they're pinging someone in gameplay. That is used for gameplay.
        if (message.StartsWith('@') && State == PlayerHandlerState.Gameplay)
            return;

        pb._sbcV2.ActivateSpeechBubble(message);
    }

    public void SetCustomizationsFromPP()
    {
        NameTextColor = MyUtil.GetColorFromHex(pp.NameColorHex, Color.white);

        speechBubbleFill = MyUtil.GetColorFromHex(pp.SpeechBubbleFillHex, Color.white); 
        speechBubbleTxt = MyUtil.GetColorFromHex(pp.SpeechBubbleTxtHex, Color.black);

        if (!string.IsNullOrEmpty(pp.TrailGradientJSON))
            TrailGradient = GradientSerializer.DeserializeGradient(pp.TrailGradientJSON);
        else
            TrailGradient = null; 

        pp.TwitchUsername = pp.TwitchUsername;
        //trailEnabled = pp.IsSubscriber; 
        SetBallCustomizations();
    }

    public void SetBallCustomizations()
    {
        //Also set the hologram
        pbh.UsernameText.SetText(pp.TwitchUsername);
        pbh.UsernameText.color = NameTextColor;
        UpdateBallPointsText(); //If they never gain or lose points on the tile, we need to update the hologram points when they are loaded to match

        if (pb == null)
            return;

        pb._usernameText.SetText(pp.TwitchUsername);
        pb._usernameText.color = NameTextColor;
        //string backgroundHighlightHex = MyUtil.ColorToHexString(NameTextColor.BlackOrWhiteHighestContrast());
        pb._usernameBackgroundHighlight.SetText($"<mark=#000000FF>" + pp.TwitchUsername);
        pb._usernameBackgroundHighlight.color = NameTextColor; 

        //Set the colors of the speech bubble and text, but preserve the alpha if they are already faded so things don't pop into existence.
        pb._sbcV2.BubbleContainer.color = MyUtil.SetColorSaveAlpha(speechBubbleFill, pb._sbcV2.BubbleContainer.color);
        pb._sbcV2.lineRenderer.startColor = MyUtil.SetColorSaveAlpha(speechBubbleFill, pb._sbcV2.lineRenderer.startColor);
        pb._sbcV2.lineRenderer.endColor = MyUtil.SetColorSaveAlpha(speechBubbleFill, pb._sbcV2.lineRenderer.endColor);

        pb._sbcV2.BubbleText.color = MyUtil.SetColorSaveAlpha(speechBubbleTxt, pb._sbcV2.BubbleText.color);

        //Set the custom trail renderer if they purchased a trail with gold 
        if (TrailGradient == null)
            pb._trailRenderer.enabled = false;
        else
        {
            pb._trailRenderer.enabled = true;
            pb._trailRenderer.colorGradient = TrailGradient;
            if (TrailGradient.colorKeys.Length >= 3)
                pb._trailRenderer.time = AppConfig.inst.GetF("TrailTierThreeTime");
            else if (TrailGradient.colorKeys.Length >= 2)
                pb._trailRenderer.time = AppConfig.inst.GetF("TrailTierTwoTime");
            else
                pb._trailRenderer.time = AppConfig.inst.GetF("TrailTierOneTime");

        }
    }

    public void ZeroPoints(bool kill, bool createTextPopup, bool contributeToROI = true)
    {
        ZeroPoints(kill, createTextPopup, Vector2.up, contributeToROI);
    }
    public void ZeroPoints(bool kill, bool createTextPopup, Vector3 textPopupDirection, bool contributeToROI = true)
    {

        if (createTextPopup)
            TextPopupMaster.Inst.CreateTextPopup(Get_TI_IO_Position(), textPopupDirection, "-" + MyUtil.AbbreviateNum4Char(pp.SessionScore), Color.red);

        if(contributeToROI)
            TilePointsROI -= pp.SessionScore; 

        pp.SessionScore = 0;

        UpdateBallPointsText();

        if (pb == null)
            return;

        if (kill)
            pb.ExplodeBall();

    }
    public void SubtractPoints(long amount, bool canKill, bool createTextPopup, bool contributeToROI = true)
    {
        SubtractPoints(amount, canKill, createTextPopup, Vector2.up, contributeToROI);
    }
    public void SubtractPoints(long amount, bool canKill, bool createTextPopup, Vector3 textPopupDirection, bool contributeToROI = true)
    {
        pp.SessionScore -= amount;

        if (contributeToROI)
            TilePointsROI -= amount; 

        if (pp.SessionScore <= 0)
        {
            pp.SessionScore = 0;

            if (canKill && pb != null)
            {
                pb._pointsText.SetText("");
                pb.ExplodeBall();
                return;
            }
        }

        UpdateBallPointsText();

        if (pb == null && !pbh.gameObject.activeSelf)
            return;


        if (createTextPopup)
            TextPopupMaster.Inst.CreateTextPopup(Get_TI_IO_Position(), textPopupDirection, "-" + MyUtil.AbbreviateNum4Char(amount), Color.red);

    }
    /// <param name="chainDepth">Invite-bonus hops this gold has already travelled (0 = earned directly).</param>
    public void AddGold(int amount, bool createTextPopup, bool doInviteBonus, int chainDepth = 0)
    {
        pp.Gold += amount;

        //Handle the invite bonus in the Update loop. The further down the chain, the sooner it's passed on
        if (doInviteBonus)
        {
            _receiveGoldAccumulator += amount;
            _receiveGoldChainDepth = Math.Max(_receiveGoldChainDepth, chainDepth);
            _receiveGoldTimer = Mathf.Max(0.1f, InviteBonusHoldSeconds / Mathf.Pow(InviteBonusSpeedUpPerHop, _receiveGoldChainDepth));
        }


        if (State == PlayerHandlerState.King)
            _gm.GetKingController().UpdateGoldText();

        float pitch = 1f + Mathf.Min(chainDepth * InviteBonusPitchStepPerHop, 1.5f);
        AudioController.inst.PlaySound(AudioController.inst.CollectGold, pitch - 0.05f, pitch + 0.05f);
        if (createTextPopup)
            TextPopupMaster.Inst.CreateTextPopup(Get_TI_IO_Position(), Vector3.up, "+" + MyUtil.AbbreviateNum4Char(amount), MyColors.Gold);
    }
    public void SubtractGold(int amount, bool createTextPopup)
    {
        pp.Gold -= amount;
        if (pp.Gold <= 0)
            pp.Gold = 0;

        if (State == PlayerHandlerState.King)
            _gm.GetKingController().UpdateGoldText();

        if (createTextPopup)
            TextPopupMaster.Inst.CreateTextPopup(Get_TI_IO_Position(), Vector3.up, "-" + MyUtil.AbbreviateNum4Char(amount), MyColors.DarkRed);
    }
    public void AddPoints(long points, bool contributeToROI = true)
    {
        AddPoints(points, false, Vector3.zero, contributeToROI);
    }
    public void AddPoints(long points, bool createTextPopup, Vector3 textPopupDirection,  bool contributeToROI = true, bool doInviteBonus = true)
    {
        pp.SessionScore += points;

        //if (doInviteBonus)
        //    StartCoroutine(CheckBonusToInviter(points));
        if (contributeToROI)
            TilePointsROI += points;

        UpdateBallPointsText();

        if (pb == null && !pbh.gameObject.activeSelf)
            return;

        if (createTextPopup)
            TextPopupMaster.Inst.CreateTextPopup(Get_TI_IO_Position(), textPopupDirection, "+" + MyUtil.AbbreviateNum4Char(points), Color.yellow);

        int combo = AddCombo();
        float comboPitch = Mathf.Lerp(0.8f, 1.2f, Mathf.Clamp01((float)combo / 15f));
        AudioController.inst.PlaySound(AudioController.inst.AddPoints, comboPitch, comboPitch);

    }

    private int AddCombo()
    {
        comboTimer = timeoutComboAfter;
        return comboCount++;
    }

    public void MultiplyPoints(float multiplier, bool createTextPopup, Vector3 textPopupDirection, bool contributeToROI = true, bool doInviteBonus = true)
    {
        long prevScore = pp.SessionScore;
        pp.SessionScore = (long)(pp.SessionScore * multiplier);

        if (contributeToROI)
            TilePointsROI += (pp.SessionScore - prevScore);

        //if(doInviteBonus)
        //    StartCoroutine(CheckBonusToInviter(pp.SessionScore - prevScore));

        UpdateBallPointsText();

        if (pb == null && !pbh.gameObject.activeSelf)
            return;

        if (createTextPopup)
            TextPopupMaster.Inst.CreateTextPopup(Get_TI_IO_Position(), textPopupDirection, "x" + multiplier, Color.magenta);

        int combo = AddCombo();
        float comboPitch = Mathf.Lerp(0.8f, 1.5f, Mathf.Clamp01((float)combo / 5f));
        AudioController.inst.PlaySound(AudioController.inst.MultiplyPoints, comboPitch, comboPitch);

    }

    public void DividePoints(float divideAmount, bool textPopup, bool contributeToROI = true)
    {
        DividePoints(divideAmount, textPopup, Vector2.up, contributeToROI);
    }
    public void DividePoints(float divideAmount, bool textPopup, Vector2 textPopupDirection, bool contributeToROI = true)
    {
        long scoreBefore = pp.SessionScore; 
        pp.SessionScore = (long)(pp.SessionScore / divideAmount);

        if (contributeToROI)
            TilePointsROI += (pp.SessionScore - scoreBefore); 

        if (textPopup)
            TextPopupMaster.Inst.CreateTextPopup(Get_TI_IO_Position(), textPopupDirection, "1/" + divideAmount, Color.red);

        UpdateBallPointsText();
    }

    private void UpdateBallPointsText()
    {
        pbh.UpdateHologramPoints();

        if (pb == null)
            return;

        pb.UpdatePointsText();
    }

    public Vector3 GetBallPos()
    {
        if (pb != null)
            return pb.GetPosition();

        if(ReceivableTarget != null)
            return ReceivableTarget.GetReceivePosition(); //NakedPhPos.position;

        return _gm.GetTileController().BidHandler.BidQueueOrigin.GetReceivePosition(); 
    }

    public PlayerBall GetPlayerBall()
    {
        if (pb != null)
            return pb;

        return _gm.CreatePlayerBall(this);
    }

    public void ReceiveTravelingIndicator(TravelingIndicator TI)
    {
        LastAccess = DateTime.Now;

        if (TI.TI_Type == TI_Type.GiveGold)
        {
            AddGold((int)TI.value, true, doInviteBonus:false); 
            return;
        }

        if (TI.TI_Type == TI_Type.GiveGoldDoBonus)
        {
            AddGold((int)TI.value, true, doInviteBonus: true, chainDepth: TI.ChainDepth);
            return;
        }

        if(TI.TI_Type == TI_Type.Tomato)
        {
            //Move player with force relative to the amount of points they are hit with compared to how many points they have
            // Ensure we don't divide by zero by checking if pp.SessionScore is greater than 0
            if(pb != null && pb._rb2D.gameObject.activeSelf)
            {
                double f = pp.SessionScore > 0 ? Math.Abs(TI.value) / (double)pp.SessionScore : 0;

                // Clamp 'f' to be within the range of 0 to 1
                f = Math.Clamp(f, 0, 1);

                float power = Mathf.Lerp(0, pb.MaxTomatoKickbackForce, (float)f);
                Vector2 direction = (GetBallPos() - TI.Origin).normalized;
                pb._rb2D.AddForce(direction * power);
                Debug.Log($"IMPACT f: {f} power: {power} direction: {direction}");
            }


            SubtractPoints(TI.value, false, true, contributeToROI:false);
            return;
        }

        if (TI.value >= 0)
        {
            //bool doInviteBonus = (TI.TI_Type == TI_Type.GivePoints) ? false : true;
            AddPoints(TI.value, true, Vector2.up, contributeToROI:true, doInviteBonus:false);
            return;
        }
        if (TI.value < 0)
        {
            SubtractPoints(TI.value, false, true, false);
            return;
        }
    }

/*    private IEnumerator CheckBonusToInviter(long amount)
    {
        if (string.IsNullOrEmpty(pp.InvitedByID))
            yield break;

        long bonus = amount / 2;

        if (bonus <= 0)
            yield break;

        //Get the inviter Ph
        CoroutineResult<PlayerHandler> coResult = new CoroutineResult<PlayerHandler>();
        yield return _gm.GetPlayerHandler(pp.InvitedByID, coResult);
        PlayerHandler inviterPh = coResult.Result;

        if (inviterPh == null)
        {
            Debug.LogError($"Failed to find ph for invitedbyId: {pp.InvitedByID}");
            yield break;
        }

        TextPopupMaster.Inst.CreateTravelingIndicator($"Invite Bonus +{MyUtil.AbbreviateNum4Char(bonus)}", bonus, this, inviterPh, 0.1f, Color.cyan, inviterPh.PfpTexture); 
    }*/

    /// <summary>
    /// Passes 25% of gold received up to this player's inviter, as hop <paramref name="hop"/> of the chain.
    /// Whole gold only: the chain stops once the bonus rounds down below 1, so loops (A invited B, B
    /// invited A) always end.
    /// </summary>
    private IEnumerator CheckBonusToInviter(long goldAmount, int hop)
    {
        if (string.IsNullOrEmpty(pp.InvitedByID) || !AppConfig.inst.GetB("EnablePyramidSchemeInvites"))
            yield break;

        long bonus = goldAmount / 4;

        if (bonus < 1)
            yield break;

        //Get the inviter Ph
        CoroutineResult<PlayerHandler> coResult = new CoroutineResult<PlayerHandler>();
        yield return _gm.GetPlayerHandler(pp.InvitedByID, coResult);
        PlayerHandler inviterPh = coResult.Result;

        if (inviterPh == null)
        {
            Debug.LogError($"Failed to find ph for invitedbyId: {pp.InvitedByID}");
            yield break;
        }

        float speed = InviteBonusBaseTravelSpeed * Mathf.Pow(InviteBonusSpeedUpPerHop, hop - 1);
        TravelingIndicator ti = TextPopupMaster.Inst.CreateTravelingIndicator($"Invite Bonus +{MyUtil.AbbreviateNum4Char(bonus)}", bonus, this, inviterPh, speed, MyColors.Gold, inviterPh.PfpTexture, TI_Type.GiveGoldDoBonus);
        ti.ChainDepth = hop;

    }

    public Vector3 Get_TI_IO_Position()
    {
        if (pbh.gameObject.activeSelf)
        {
            return pbh.transform.position;
        }

        if (pb == null || !pb.gameObject.activeSelf)
            return _gm.HoldingPen.Get_TI_IO_Position();

        return GetBallPos() - Vector3.forward;
    }

/*    public GameObject GetGameObject()
    {
        return gameObject;
    }*/

    public async Task UnloadPh()
    {
        await SQLiteServiceAsync.UpdatePlayer(pp);

        FlaggedToUnload = true; 
    }

    /// <summary>
    /// Records who invited this player and gives them both the recruit reward. The first inviter
    /// sticks: once set, it can't be changed.
    /// </summary>
    public IEnumerator SetInvitor(PlayerHandler inviter, TwitchClient twitchClient, InvitePromo invitePromo, string messageId = null)
    {
        //Checked again here, with no yield before the inviter is set, so the first of two quick claims wins
        if (!string.IsNullOrEmpty(pp.InvitedByID))
        {
            yield return ReplyAlreadyInvited(messageId, twitchClient);
            yield break;
        }

        if(string.Equals(pp.TwitchID, inviter.pp.TwitchID, StringComparison.OrdinalIgnoreCase))
        {
            twitchClient.ReplyToPlayer(messageId, pp.TwitchUsername, "You can't invite yourself.");
            yield break;
        }

        pp.InvitedByID = inviter.pp.TwitchID;
        inviter.pp.AddInvite(pp.TwitchID);
        pp.LastInteraction = DateTime.Now;
        inviter.pp.LastInteraction = DateTime.Now;

        long reward = Math.Max(0, AppConfig.inst.GetI("recruitPointReward"));
        if (reward > 0)
        {
            AddPoints(reward, true, Vector3.up, contributeToROI: false);
            inviter.AddPoints(reward, true, Vector3.up, contributeToROI: false);
        }

        string share = AppConfig.inst.GetB("EnablePyramidSchemeInvites") ? $" @{inviter.pp.TwitchUsername} also earns 25% of the gold you earn." : "";
        twitchClient.ReplyToPlayer(messageId, pp.TwitchUsername, $"@{inviter.pp.TwitchUsername} is now your inviter, and you both earned {reward:N0} points!{share}");

        invitePromo.AnnounceNewInvite(inviter.pp.TwitchID, pp.TwitchID);

        if (pb != null)
            yield return pb.UpdateInviterIndicator();

    }

    public IEnumerator ReplyAlreadyInvited(string messageId, TwitchClient twitchClient)
    {
        CoroutineResult<PlayerHandler> coResult = new CoroutineResult<PlayerHandler>();
        yield return _gm.GetPlayerHandler(pp.InvitedByID, coResult);
        string inviterName = coResult.Result?.pp.TwitchUsername;
        string by = string.IsNullOrEmpty(inviterName) ? "someone" : $"@{inviterName}";
        twitchClient.ReplyToPlayer(messageId, pp.TwitchUsername, $"You were already invited by {by}, and that can't be changed.");
    }

    private const int InviteReminderEveryRounds = 10;
    private const int InviteReminderRoundsPerDay = 30;

    /// <summary>
    /// Counts a round this player joined. Players with no inviter yet are reminded to name one every
    /// 10th round they join, for their first 30 rounds each day.
    /// </summary>
    public void CountRoundJoined()
    {
        string today = DateTime.Now.ToString("yyyy-MM-dd");
        if (pp.RoundsJoinedDate != today)
        {
            pp.RoundsJoinedDate = today;
            pp.RoundsJoinedToday = 0;
        }
        pp.RoundsJoinedToday++;

        if (!string.IsNullOrEmpty(pp.InvitedByID) || pp.TwitchID == Secrets.CHANNEL_ID)
            return;
        if (pp.RoundsJoinedToday > InviteReminderRoundsPerDay || pp.RoundsJoinedToday % InviteReminderEveryRounds != 0)
            return;

        long reward = Math.Max(0, AppConfig.inst.GetI("recruitPointReward"));
        _gm.TwitchClient.PingReplyPlayer(pp.TwitchUsername, $"Who invited you to Chaos League? Type !invitedby @theirname and you'll both get {reward:N0} points.");
    }


    public void ReceiveBid(TI_Bid TI_Bid)
    {

        Debug.Log($"Receiving bid in: {pp.TwitchUsername} in state {Enum.GetName(typeof(PlayerHandlerState), GetState())}");

        //Check if we're inside a multiplier zone. Multiply the bid amount by the total of the multipliers

        int goldenMultiplier = 0; 

        TileController tc = _gm.GetTileController();
        //If the player is in the bidding Q, and the bidding Q is bidding on a golden tile, then multiply the ticket redemption
        if (this.State != PlayerHandlerState.Gameplay && tc.CurrentBiddingTile.IsGolden)
            goldenMultiplier += AppConfig.inst.GetI("GoldenTileMultiplier");
        //Or if the player is in gameplay on a golden tile, and they receive a bid, multiply it by 100
        else if (this.State == PlayerHandlerState.Gameplay && tc.GameplayTile != null && tc.GameplayTile.IsGolden)
            goldenMultiplier += AppConfig.inst.GetI("GoldenTileMultiplier");

        int zoneMultiplier = GetZoneMultiplierTotal();

        int totalMultiplier = ((zoneMultiplier == 1) ? 0 : zoneMultiplier) + goldenMultiplier;
 
        TI_Bid.Amount *= Math.Max(1, totalMultiplier); 

        //Spawn Ticket Particles based on amount
        if (TI_Bid.BidType == BidType.ChannelPoints)
            TI_Bid.BidHandler.BurstCommunityPointParticles(Get_Ticket_IO_Position(), TI_Bid.Amount);
        else if (TI_Bid.BidType == BidType.Bits)
            TI_Bid.BidHandler.BurstBitParticles(Get_Ticket_IO_Position(), TI_Bid.Amount);
        else
            TI_Bid.BidHandler.BurstCommunityPointParticles(Get_Ticket_IO_Position(), TI_Bid.Amount);

        IncrementBid(TI_Bid.Amount);

        TI_Bid.BidHandler.TryAddToBiddingQ(this);

    }

    public Vector3 Get_Ticket_IO_Position()
    {
        return GetBallPos() - Vector3.forward;
    }

    public void SetTicketQPos(PlayerReceiveable target, bool isRaffle, BidHandler th)
    {
        //If they're not in the raffle, move the ball to the target, and create a ball if necessary
        if(pb == null && !isRaffle)
        {
            _gm.CreatePlayerBall(this);
            pb._rb2D.transform.position = (Vector2)th.TicketIO_Target.position;
        }

        //If they're not in the raffle, and they already have a ball, activate it and move to the target;
        if (pb != null && !isRaffle)
            pb.Reactivate(); 

        //If they don't have a ball, and are in the raffle, don't do anything

        ReceivableTarget = target;
    }

    public GameManager GetGameManager()
    {
        return _gm;
    }

    public void EnableHologram()
    {
        pbh.gameObject.SetActive(true);
    }

    public void DisableHologram()
    {
        pbh.gameObject.SetActive(false);
        if(_gm != null)
            pbh.transform.position = _gm.HoldingPen.transform.position;
    }

    public int GetZoneMultiplierTotal()
    {
        if (pb == null || !pb._rb2D.gameObject.activeSelf)
        {
            Vector2 pos = Get_Ticket_IO_Position();
            return _gm.GetRebellionController().GetMultiplierByPos(pos);
        }

        int total = 0;
        foreach (MultiplierZone z in pb.OverlappingZones)
        {
            if (z.Multiplier >= 2)
                total += z.Multiplier;
        }

        if (total < 1)
            return 1;

        return total;
    }

    public void ThrowTomato(long desiredTomatoAmount, PlayerHandler targetPlayer)
    {
        //If the user tries to use more points than they have, just clamp it
        if (pp.SessionScore < desiredTomatoAmount)
            desiredTomatoAmount = pp.SessionScore;

        //Apply kickback force to player proportional to the amount of points they threw compared to how many points they have
        if(pb != null && pb._rb2D.gameObject.activeSelf)
        {
            double f = (double)desiredTomatoAmount / pp.SessionScore;
            float power = Mathf.Lerp(0, pb.MaxTomatoKickbackForce, (float)f);
            Vector2 direction = (GetBallPos() - targetPlayer.GetBallPos()).normalized;
            pb._rb2D.AddForce(direction * power);
            Debug.Log($"KICKBACK f: {f} power: {power} direction: {direction}");
        }

        SubtractPoints(desiredTomatoAmount, canKill: false, createTextPopup: true);

        float t = EasingFunction.EaseOutExpo(0, 1, desiredTomatoAmount / 10_000f);
        Vector3 tomatoScale = Vector3.Lerp(new Vector3(0.3f, 0.3f, 0.3f), new Vector3(1.3f, 1.3f, 1.3f), t);

        TextPopupMaster.Inst.CreateTravelingIndicator("🍅", desiredTomatoAmount, Get_TI_IO_Position(), targetPlayer, 0.2f, tomatoScale, Color.green, null, true, ti_type: TI_Type.Tomato);


    }

    private void OnDestroy()
    {
        _inactivityCTS?.Cancel();
    }

}
