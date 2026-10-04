using System;

namespace LiveChat
{
    public enum LiveChatSubscriptionPlan
    {
        Unknown,
        Prime,
        Tier1,
        Tier2,
        Tier3
    }

    [Serializable]
    public class LiveChatChannelPointsRedemption
    {
        public string RewardId;
        public string RedemptionId;
        public string RewardTitle;
        public string UserId;
        public string Username;
        public string UserInput;
        public int Cost;
    }

    [Serializable]
    public class LiveChatRewardRedemption
    {
        public string RewardTitle;
        public int RewardCost;
        public string Message;
    }

    [Serializable]
    public class LiveChatBitsEvent
    {
        public string UserId;
        public string Username;
        public string ChatMessage;
        public int BitsUsed;
        public int TotalBitsUsed;
    }

    [Serializable]
    public class LiveChatSubscriptionEvent
    {
        public string UserId;
        public string Username;
        public int MultiMonthDuration;
        public LiveChatSubscriptionPlan Plan;
    }

    [Serializable]
    public class LiveChatGiftSubscriptionEvent
    {
        public string GifterUserId;
        public string GifterUsername;
        public string RecipientUserId;
        public string RecipientUsername;
        public int MultiMonthDuration;
        public LiveChatSubscriptionPlan Plan;
    }
}
