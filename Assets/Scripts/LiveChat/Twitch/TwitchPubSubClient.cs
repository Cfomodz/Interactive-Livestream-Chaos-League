using System;
using LiveChat;
using TwitchLib.PubSub.Enums;
using TwitchLib.PubSub.Events;
using TwitchLib.Unity;
using UnityEngine;

namespace LiveChat.Twitch
{
    public class TwitchPubSubClient : MonoBehaviour
    {
        public event Action Connected;
        public event Action<LiveChatChannelPointsRedemption> ChannelPointsRedeemed;
        public event Action<LiveChatRewardRedemption> RewardRedeemed;
        public event Action<LiveChatBitsEvent> BitsReceived;
        public event Action<LiveChatSubscriptionEvent> SubscriptionReceived;
        public event Action<LiveChatGiftSubscriptionEvent> GiftSubscriptionReceived;
        public event Action<string> WhisperReceived;
        public event Action<Exception> Error;

        private PubSub _pubSub;

        public void Connect(string channelId, string accessToken)
        {
            Disconnect();

            _pubSub = new PubSub();
            _pubSub.OnWhisper += OnWhisper;
            _pubSub.OnPubSubServiceConnected += OnPubSubServiceConnected;
            _pubSub.OnChannelPointsRewardRedeemed += OnChannelPointsRedeemed;
            _pubSub.OnRewardRedeemed += OnRewardRedeemed;
            _pubSub.OnBitsReceivedV2 += OnBitsReceivedV2;
            _pubSub.OnChannelSubscription += OnChannelSubscription;

            _pubSub.Connect();
            _pubSub.ListenToChannelPoints(channelId);
            _pubSub.ListenToWhispers(channelId);
            _pubSub.ListenToBitsEventsV2(channelId);
            _pubSub.ListenToSubscriptions(channelId);
            _pubSub.SendTopics(accessToken);
        }

        public void Disconnect()
        {
            if (_pubSub == null)
                return;

            _pubSub.OnWhisper -= OnWhisper;
            _pubSub.OnPubSubServiceConnected -= OnPubSubServiceConnected;
            _pubSub.OnChannelPointsRewardRedeemed -= OnChannelPointsRedeemed;
            _pubSub.OnRewardRedeemed -= OnRewardRedeemed;
            _pubSub.OnBitsReceivedV2 -= OnBitsReceivedV2;
            _pubSub.OnChannelSubscription -= OnChannelSubscription;

            _pubSub.Disconnect();
            _pubSub = null;
        }

        private void OnPubSubServiceConnected(object sender, EventArgs e)
        {
            Connected?.Invoke();
        }

        private void OnChannelPointsRedeemed(object sender, OnChannelPointsRewardRedeemedArgs e)
        {
            var redemption = e.RewardRedeemed?.Redemption;
            if (redemption == null)
                return;

            ChannelPointsRedeemed?.Invoke(new LiveChatChannelPointsRedemption
            {
                RewardId = redemption.Reward.Id,
                RedemptionId = redemption.Id,
                RewardTitle = redemption.Reward.Title,
                UserId = redemption.User.Id,
                Username = redemption.User.Login,
                UserInput = redemption.UserInput,
                Cost = redemption.Reward.Cost
            });
        }

        private void OnRewardRedeemed(object sender, TwitchLib.PubSub.Events.OnRewardRedeemedArgs e)
        {
            RewardRedeemed?.Invoke(new LiveChatRewardRedemption
            {
                RewardTitle = e.RewardTitle,
                RewardCost = e.RewardCost,
                Message = e.Message
            });
        }

        private void OnBitsReceivedV2(object sender, OnBitsReceivedV2Args e)
        {
            BitsReceived?.Invoke(new LiveChatBitsEvent
            {
                UserId = e.UserId,
                Username = e.UserName,
                ChatMessage = e.ChatMessage,
                BitsUsed = e.BitsUsed,
                TotalBitsUsed = e.TotalBitsUsed
            });
        }

        private void OnChannelSubscription(object sender, OnChannelSubscriptionArgs e)
        {
            var subscription = e.Subscription;
            if (subscription == null)
                return;

            int duration = 1;
            if (subscription.MultiMonthDuration.HasValue && subscription.MultiMonthDuration.Value >= 1)
                duration = subscription.MultiMonthDuration.Value;

            LiveChatSubscriptionPlan plan = MapPlan(subscription.SubscriptionPlan);

            if (subscription.IsGift.HasValue && subscription.IsGift.Value)
            {
                GiftSubscriptionReceived?.Invoke(new LiveChatGiftSubscriptionEvent
                {
                    GifterUserId = subscription.UserId,
                    GifterUsername = subscription.Username,
                    RecipientUserId = subscription.RecipientId,
                    RecipientUsername = subscription.RecipientName,
                    MultiMonthDuration = duration,
                    Plan = plan
                });
                return;
            }

            SubscriptionReceived?.Invoke(new LiveChatSubscriptionEvent
            {
                UserId = subscription.UserId,
                Username = subscription.Username,
                MultiMonthDuration = duration,
                Plan = plan
            });
        }

        private void OnWhisper(object sender, TwitchLib.PubSub.Events.OnWhisperArgs e)
        {
            WhisperReceived?.Invoke(e.Whisper.Data);
        }

        private LiveChatSubscriptionPlan MapPlan(SubscriptionPlan plan)
        {
            switch (plan)
            {
                case SubscriptionPlan.Prime:
                    return LiveChatSubscriptionPlan.Prime;
                case SubscriptionPlan.Tier1:
                    return LiveChatSubscriptionPlan.Tier1;
                case SubscriptionPlan.Tier2:
                    return LiveChatSubscriptionPlan.Tier2;
                case SubscriptionPlan.Tier3:
                    return LiveChatSubscriptionPlan.Tier3;
                default:
                    return LiveChatSubscriptionPlan.Unknown;
            }
        }
    }
}
