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

    // Stream events (channel points, subs, gifts) from the live chat client. The name is from when these
    // came over Twitch PubSub, which Twitch shut down; they now arrive through EventSub. Bits arrive on
    // chat messages, and TwitchClient passes them to HandleOnBitsReceived.
    private LiveChatClientBase _client;

    /// <param name="client">Twitch, or the debug chat box.</param>
    public void Init(LiveChatClientBase client)
    {
        if (_client != null)
        {
            _client.ChannelPointsRedeemed -= OnChannelPointsRedeemed;
            _client.Subscribed -= OnChannelSubscription;
            _client.SubscriptionGifted -= OnGiftSubscription;
        }

        _client = client;
        _client.ChannelPointsRedeemed += OnChannelPointsRedeemed;
        _client.Subscribed += OnChannelSubscription;
        _client.SubscriptionGifted += OnGiftSubscription;
    }

    private void OnChannelPointsRedeemed(LiveChatChannelPointsRedemption redemption)
    {
        if (redemption == null)
            return;

        Debug.Log($"reward redeemed rewardID: {redemption.RewardId} redemptionID: {redemption.RedemptionId}");

        StartCoroutine(HandleOnChannelPointsRedeemed(redemption.UserId, redemption.Username, redemption.RewardTitle, redemption.UserInput, redemption.Cost, redemption));
    }

    /// <summary>
    /// Handles a redemption, then fulfils it, or refunds it if the player couldn't be loaded.
    /// Redemptions left unfulfilled are refunded the next time the game starts.
    /// </summary>
    public IEnumerator HandleOnChannelPointsRedeemed(string twitchId, string twitchUsername, string rewardTitle, string msg, int cost, LiveChatChannelPointsRedemption redemption = null)
    {
        //Get the player handler of the player redeeming tickets
        CoroutineResult<PlayerHandler> coResult = new CoroutineResult<PlayerHandler>();
        yield return _gm.GetPlayerHandler(twitchId, coResult);

        PlayerHandler ph = coResult.Result;
        if (ph == null)
        {
            Debug.LogError("Failed to find player handler");
            _client?.CompleteRedemption(redemption, fulfilled: false);
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

        _client?.CompleteRedemption(redemption, fulfilled: true);
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

    private void OnChannelSubscription(LiveChatSubscriptionEvent subEvent)
    {
        if (!AppConfig.inst.GetB("EnableNewSubTrigger"))
            return;

        if (subEvent == null)
            return;

        StartCoroutine(HandleOnSubscription(subEvent.UserId, subEvent.Username, Math.Max(1, subEvent.DurationMonths), subEvent.Plan));
    }

    private void OnGiftSubscription(LiveChatGiftSubscriptionEvent giftEvent)
    {
        if (!AppConfig.inst.GetB("EnableNewSubTrigger"))
            return;

        if (giftEvent == null)
            return;

        //Anonymous gifts have no player to give the bonus to
        if (giftEvent.GifterIsAnonymous || string.IsNullOrEmpty(giftEvent.GifterUserId))
        {
            CLDebug.Inst.ReportDonation("NEW GIFT SUB", $"An anonymous gifter gifted {giftEvent.RecipientUsername} {giftEvent.DurationMonths} {giftEvent.Plan}");
            return;
        }

        StartCoroutine(HandleGiftSubscription(giftEvent.GifterUserId, giftEvent.GifterUsername, giftEvent.RecipientUserId, giftEvent.RecipientUsername, Math.Max(1, giftEvent.DurationMonths), giftEvent.Plan));
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

    private void OnDestroy()
    {
        if (_client != null)
        {
            _client.ChannelPointsRedeemed -= OnChannelPointsRedeemed;
            _client.Subscribed -= OnChannelSubscription;
            _client.SubscriptionGifted -= OnGiftSubscription;
        }
    }
}