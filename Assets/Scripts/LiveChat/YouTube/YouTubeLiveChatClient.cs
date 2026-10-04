using System;
using LiveChat;
using UnityEngine;

namespace LiveChat.YouTube
{
    public class YouTubeLiveChatClient : LiveChatClientBase
    {
        [SerializeField] private string _liveChatId;

        public override void Connect(LiveChatConnectConfig config)
        {
            RaiseError(new NotImplementedException("YouTube live chat client is a stub. Wire it to the YouTube Data API."));
        }

        public override void Disconnect()
        {
            IsConnected = false;
            RaiseDisconnected();
        }

        public override void SendMessage(string channel, string message)
        {
        }

        public override void SendReply(string channel, string replyToMessageId, string message)
        {
        }
    }
}
