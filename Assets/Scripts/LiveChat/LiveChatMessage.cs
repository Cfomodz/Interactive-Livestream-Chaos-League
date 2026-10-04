using System;
using System.Collections.Generic;
using UnityEngine;

namespace LiveChat
{
    [Serializable]
    public class LiveChatMessage
    {
        public string MessageId;
        public string UserId;
        public string Username;
        public string DisplayName;
        public string Channel;
        public string RawMessage;
        public string RawIrcMessage;
        public Color UsernameColor;
        public bool IsSubscriber;
        public bool IsFirstMessage;
        public int Bits;
        public bool IsModerator;
        public bool IsBroadcaster;
        public bool IsVip;
        public bool IsMe;
        public List<LiveChatEmote> Emotes;
    }
}
