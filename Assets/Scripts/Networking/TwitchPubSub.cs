using LiveChat;
using LiveChat.Twitch;
using System;
using System.Collections;
using UnityEngine;
public enum BidType { ChannelPoints, Bits, NewPlayerBonus, NewSubBonus}
public class TwitchPubSub : MonoBehaviour
{
    [SerializeField] private GameManager _gm;
    [SerializeField] private AutoPredictions _autoPredictions;
    [SerializeField] private BidHandler _ticketHandler;
    [SerializeField] private TwitchClient _twitchClient;
    [SerializeField] private BitTrigger _lavaBitTrigger;
    [SerializeField] private BitTrigger _waterBitTrigger;
    [SerializeField] private RebellionController _rebellionController;

    private TwitchPubSubClient _pubSub;

    public void Init(string channelID, string botAccessToken)
    {
        if (_pubSub == null)
            _pubSub = GetComponent<TwitchPubSubClient>();

        if (_pubSub == null)
            _pubSub = gameObject.AddComponent<TwitchPubSubClient>();

        _pubSub.Connected -= OnPubSubServiceConnected;
        _pubSub.ChannelPointsRedeemed -= OnChannelPointsRedeemed;
        _pubSub.RewardRedeemed -= OnRewardRedeemed;
        _pubSub.BitsReceived -= OnBitsReceived;
        _pubSub.SubscriptionReceived -= OnChannelSubscription;
        _pubSub.GiftSubscriptionReceived -= OnGiftSubscription;
        _pubSub.WhisperReceived -= OnWhisper;
        _pubSub.Error -= OnPubSubError;

        _pubSub.Connected += OnPubSubServiceConnected;
        _pubSub.ChannelPointsRedeemed += OnChannelPointsRedeemed;
        _pubSub.RewardRedeemed += OnRewardRedeemed;
        _pubSub.BitsReceived += OnBitsReceived;
        _pubSub.SubscriptionReceived += OnChannelSubscription;
        _pubSub.GiftSubscriptionReceived += OnGiftSubscription;
        _pubSub.WhisperReceived += OnWhisper;
        _pubSub.Error += OnPubSubError;

        _pubSub.Connect(channelID, botAccessToken);
        Debug.Log($"Done Initializing PubSub");
    }

    private void OnPubSubServiceConnected()
    {
        Debug.Log("Connected to Twitch PubSub!");
    }

    private void OnPubSubError(Exception exception)
    {
        Debug.LogError($"PubSub error: {exception}");
    }

    private void OnChannelPointsRedeemed(LiveChatChannelPointsRedemption redemption)
    {
        if (redemption == null)
            return;

        Debug.Log($"reward redeemed rewardID: {redemption.RewardId} redemptionID: {redemption.RedemptionId}");

        StartCoroutine(HandleOnChannelPointsRedeemed(redemption.UserId, redemption.Username, redemption.RewardTitle, redemption.UserInput, redemption.Cost));
    }
    public IEnumerator HandleOnChannelPointsRedeemed(string twitchId, string twitchUsername, string rewardTitle, string msg, int cost)
    {
        //Get the player handler of the player redeeming tickets
        CoroutineResult<PlayerHandler> coResult = new CoroutineResult<PlayerHandler>();
        yield return _gm.GetPlayerHandler(twitchId, coResult);

        PlayerHandler ph = coResult.Result;
        if (ph == null)
        {
            Debug.LogError("Failed to find player handler");
            yield break;
        }

        ph.pp.LastInteraction = DateTime.Now;
        ph.pp.TwitchUsername = twitchUsername;
        ph.pp.TotalTicketsSpent += cost; 

        if (rewardTitle.StartsWith("Activate Lava"))
            _lavaBitTrigger.AddBits(twitchUsername, AppConfig.inst.GetI("ThroneLavaCost"));
        else if (rewardTitle.StartsWith("Activate Water"))
            _waterBitTrigger.AddBits(twitchUsername, AppConfig.inst.GetI("ThroneWaterCost"));
        else
            _ticketHandler.BidRedemption(ph, cost, BidType.ChannelPoints);

    }

    private void OnBitsReceived(LiveChatBitsEvent bitsEvent)
    {
        if (bitsEvent == null)
            return;

        Debug.Log($"Inside bits received v2 total bits: {bitsEvent.TotalBitsUsed} {bitsEvent.BitsUsed}");
        //Bits used is the amount contained in the message, total bits sums up the total bits the user has donated over time. Not sure over what timespan.

        StartCoroutine(HandleOnBitsReceived(bitsEvent.UserId, bitsEvent.Username, bitsEvent.ChatMessage, bitsEvent.BitsUsed));
    }

    public IEnumerator HandleOnBitsReceived(string twitchId, string twitchUsername, string rawMsg, int bitsInMessage)
    {
        CLDebug.Inst.ReportDonation("NEW BIT DONATION", $"{bitsInMessage} bits (${bitsInMessage/100f}) from {twitchUsername}.\nMessage: {rawMsg}"); 

        CoroutineResult<PlayerHandler> coResult = new CoroutineResult<PlayerHandler>();
        yield return _gm.GetPlayerHandler(twitchId, coResult);

        PlayerHandler ph = coResult.Result;
        if (ph == null)
        {
            Debug.LogError("Failed to find player handler");
            yield break;
        }

        ph.pp.LastInteraction = DateTime.Now;
        ph.pp.TwitchUsername = twitchUsername;

        if (rawMsg.ToLower().Contains("!lava"))
        {
            _lavaBitTrigger.AddBits(twitchUsername, bitsInMessage);
            yield break;
        }
        if (rawMsg.ToLower().Contains("!water"))
        {
            _waterBitTrigger.AddBits(twitchUsername, bitsInMessage);
            
            yield break;
        }

        if(bitsInMessage >= 200)
            StartCoroutine(_rebellionController.CreateRebellion(ph, bitsInMessage, rawMsg));
        else
            _twitchClient.PingReplyPlayer(twitchUsername, "Rebellion requires minimum of 200 bits. Each 100 bits in message increases multiplier by 1.");
        

        _ticketHandler.BidRedemption(ph, bitsInMessage, BidType.Bits);
    }

    private void OnRewardRedeemed(LiveChatRewardRedemption redemption)
    {
        if (redemption == null)
            return;

        Debug.Log($"reward redeemed: {redemption.RewardTitle} {redemption.RewardCost} message {redemption.Message}");
    }

    private void OnChannelSubscription(LiveChatSubscriptionEvent subEvent)
    {
        if (!AppConfig.inst.GetB("EnableNewSubTrigger"))
            return;

        if (subEvent == null)
            return;

        StartCoroutine(HandleOnSubscription(subEvent.UserId, subEvent.Username, subEvent.MultiMonthDuration, subEvent.Plan));
    }

    private void OnGiftSubscription(LiveChatGiftSubscriptionEvent giftEvent)
    {
        if (!AppConfig.inst.GetB("EnableNewSubTrigger"))
            return;

        if (giftEvent == null)
            return;

        StartCoroutine(HandleGiftSubscription(giftEvent.GifterUserId, giftEvent.GifterUsername, giftEvent.RecipientUserId, giftEvent.RecipientUsername, giftEvent.MultiMonthDuration, giftEvent.Plan));
    }

    public IEnumerator HandleOnSubscription(string twitchId, string username, int MultiMonthDuration, LiveChatSubscriptionPlan subPlan)
    {
        CLDebug.Inst.ReportDonation("NEW SUB", $"{username} subbed {MultiMonthDuration} {subPlan}");

        CoroutineResult<PlayerHandler> coResult = new CoroutineResult<PlayerHandler>();
        yield return _gm.GetPlayerHandler(twitchId, coResult);

        PlayerHandler ph = coResult.Result;
        if (ph == null)
        {
            Debug.LogError("Failed to find player handler");
            yield break;
        }

        ph.pp.LastInteraction = DateTime.Now;
        ph.pp.TwitchUsername = username;

        MyTTS.inst.Announce($"{username} subed {MultiMonthDuration} month{((MultiMonthDuration > 1) ? "s" : "")} with {subPlan}. Brofist.");

        int bidAmount = AppConfig.inst.GetI("NewSubBonusBid") * MultiMonthDuration;
        if (subPlan == LiveChatSubscriptionPlan.Tier2)
            bidAmount *= 2;
        else if(subPlan == LiveChatSubscriptionPlan.Tier3)
            bidAmount *= 3;

        _ticketHandler.BidRedemption(ph, bidAmount, BidType.NewSubBonus);
    }

    public IEnumerator HandleGiftSubscription(string twitchId, string username, string recipientId, string recipientUsername, int MultiMonthDuration, LiveChatSubscriptionPlan subPlan)
    {
        CLDebug.Inst.ReportDonation("NEW GIFT SUB", $"{username} gifted {recipientUsername} {MultiMonthDuration} {subPlan}");

        CoroutineResult<PlayerHandler> coResult = new CoroutineResult<PlayerHandler>();
        yield return _gm.GetPlayerHandler(twitchId, coResult);

        PlayerHandler ph = coResult.Result;
        if (ph == null)
        {
            Debug.LogError("Failed to find player handler");
            yield break;
        }

        ph.pp.LastInteraction = DateTime.Now;
        ph.pp.TwitchUsername = username;

        MyTTS.inst.AggregateSubGift(username, MultiMonthDuration, subPlan); 
        //MyTTS.inst.Announce($"{username} gifted {MultiMonthDuration} {subPlan} sub{((MultiMonthDuration > 1) ? "s" : "")} to {recipientUsername}. What a bro.");

        int bidAmount = AppConfig.inst.GetI("NewSubBonusBid") * MultiMonthDuration;
        if (subPlan == LiveChatSubscriptionPlan.Tier2)
            bidAmount *= 2;
        else if (subPlan == LiveChatSubscriptionPlan.Tier3)
            bidAmount *= 3;

        _ticketHandler.BidRedemption(ph, bidAmount, BidType.NewSubBonus);
    }

    private void OnWhisper(string message)
    {
        Debug.Log($"{message}");
        // Do your bits logic here.
    }

    private void OnDestroy()
    {
        // Cleanup when the object is destroyed
        if(_pubSub != null )
            _pubSub.Disconnect();
    }
}