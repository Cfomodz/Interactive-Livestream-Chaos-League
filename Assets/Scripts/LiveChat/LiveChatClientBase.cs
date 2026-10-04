using System;
using UnityEngine;

namespace LiveChat
{
    public abstract class LiveChatClientBase : MonoBehaviour
    {
        public event Action Connected;
        public event Action<string> JoinedChannel;
        public event Action Disconnected;
        public event Action<Exception> Error;
        public event Action<LiveChatMessage> MessageReceived;

        public string DefaultChannel { get; protected set; }
        public bool IsConnected { get; protected set; }

        public abstract void Connect(LiveChatConnectConfig config);
        public abstract void Disconnect();
        public abstract void SendMessage(string channel, string message);
        public abstract void SendReply(string channel, string replyToMessageId, string message);

        public void SendMessage(string message)
        {
            if (string.IsNullOrEmpty(DefaultChannel))
                return;

            SendMessage(DefaultChannel, message);
        }

        public void SendReply(string replyToMessageId, string message)
        {
            if (string.IsNullOrEmpty(DefaultChannel))
                return;

            SendReply(DefaultChannel, replyToMessageId, message);
        }

        protected void RaiseConnected() => Connected?.Invoke();
        protected void RaiseJoinedChannel(string channel) => JoinedChannel?.Invoke(channel);
        protected void RaiseDisconnected() => Disconnected?.Invoke();
        protected void RaiseError(Exception exception) => Error?.Invoke(exception);
        protected void RaiseMessageReceived(LiveChatMessage message) => MessageReceived?.Invoke(message);
    }
}
