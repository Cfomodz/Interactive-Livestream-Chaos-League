using LiveChat;

namespace LiveChat.Commands
{
    public sealed class ChatCommandContext
    {
        public ChatCommandRouter Router { get; }
        public LiveChatClientBase Client { get; }
        public LiveChatMessage Message { get; }
        public string RawText { get; }
        public string Command { get; }
        public string ArgsRaw { get; }
        public string[] Args { get; }

        public string Channel => !string.IsNullOrEmpty(Message?.Channel)
            ? Message.Channel
            : Client?.DefaultChannel ?? string.Empty;

        public string Username => Message?.Username ?? string.Empty;
        public string DisplayName => string.IsNullOrEmpty(Message?.DisplayName) ? Username : Message.DisplayName;

        public bool IsSubscriber => Message != null && Message.IsSubscriber;
        public bool IsVip => Message != null && Message.IsVip;
        public bool IsModerator => Message != null && Message.IsModerator;
        public bool IsBroadcaster => Message != null && Message.IsBroadcaster;
        public bool IsMe => Message != null && Message.IsMe;

        public ChatCommandContext(ChatCommandRouter router, LiveChatClientBase client, LiveChatMessage message,
            string rawText, string command, string argsRaw, string[] args)
        {
            Router = router;
            Client = client;
            Message = message;
            RawText = rawText;
            Command = command;
            ArgsRaw = argsRaw;
            Args = args ?? System.Array.Empty<string>();
        }

        public bool HasPermission(ChatCommandPermission permission)
        {
            if (Message == null)
                return true;

            switch (permission)
            {
                case ChatCommandPermission.Anyone:
                    return true;
                case ChatCommandPermission.Subscriber:
                    return IsSubscriber || IsVip || IsModerator || IsBroadcaster;
                case ChatCommandPermission.Vip:
                    return IsVip || IsModerator || IsBroadcaster;
                case ChatCommandPermission.Moderator:
                    return IsModerator || IsBroadcaster;
                case ChatCommandPermission.Broadcaster:
                    return IsBroadcaster;
                default:
                    return false;
            }
        }

        public void Reply(string message)
        {
            if (Client == null || string.IsNullOrEmpty(message))
                return;

            string channel = Channel;
            if (string.IsNullOrEmpty(channel))
                return;

            if (!string.IsNullOrEmpty(Message?.MessageId))
                Client.SendReply(channel, Message.MessageId, message);
            else
                Client.SendMessage(channel, message);
        }

        public void Say(string message)
        {
            if (Client == null || string.IsNullOrEmpty(message))
                return;

            string channel = Channel;
            if (string.IsNullOrEmpty(channel))
                return;

            Client.SendMessage(channel, message);
        }
    }
}
