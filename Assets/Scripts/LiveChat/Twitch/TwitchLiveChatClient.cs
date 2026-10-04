using System;
using System.Collections.Generic;
using LiveChat;
using TwitchLib.Client.Events;
using TwitchLib.Client.Models;
using TwitchLib.Unity;
using UnityEngine;

namespace LiveChat.Twitch
{
    public class TwitchLiveChatClient : LiveChatClientBase
    {
        private Client _client;

        public override void Connect(LiveChatConnectConfig config)
        {
            if (config == null)
            {
                RaiseError(new ArgumentNullException(nameof(config)));
                return;
            }

            DisconnectInternal();

            ConnectionCredentials credentials = new ConnectionCredentials(config.ChannelName, config.BotAccessToken);
            _client = new Client();
            _client.Initialize(credentials, config.ChannelName);

            _client.OnConnected += OnConnected;
            _client.OnJoinedChannel += OnJoinedChannel;
            _client.OnMessageReceived += OnMessageReceived;
            _client.OnError += OnError;

            DefaultChannel = config.ChannelName;
            _client.Connect();
        }

        public override void Disconnect()
        {
            DisconnectInternal();
        }

        public override void SendMessage(string channel, string message)
        {
            if (_client == null || string.IsNullOrEmpty(channel) || string.IsNullOrEmpty(message))
                return;

            _client.SendMessage(channel, message);
        }

        public override void SendReply(string channel, string replyToMessageId, string message)
        {
            if (_client == null || string.IsNullOrEmpty(channel) || string.IsNullOrEmpty(message))
                return;

            if (string.IsNullOrEmpty(replyToMessageId))
            {
                _client.SendMessage(channel, message);
                return;
            }

            _client.SendReply(channel, replyToMessageId, message);
        }

        private void OnConnected(object sender, OnConnectedArgs e)
        {
            IsConnected = true;
            RaiseConnected();
        }

        private void OnJoinedChannel(object sender, OnJoinedChannelArgs e)
        {
            DefaultChannel = e.Channel;
            RaiseJoinedChannel(e.Channel);
        }

        private void OnError(object sender, TwitchLib.Communication.Events.OnErrorEventArgs e)
        {
            RaiseError(e.Exception);
        }

        private void OnMessageReceived(object sender, OnMessageReceivedArgs e)
        {
            var chatMessage = e.ChatMessage;
            var emotes = new List<LiveChatEmote>();

            if (chatMessage.EmoteSet?.Emotes != null)
            {
                foreach (var emote in chatMessage.EmoteSet.Emotes)
                {
                    emotes.Add(new LiveChatEmote
                    {
                        Id = emote.Id,
                        Name = emote.Name,
                        StartIndex = emote.StartIndex,
                        EndIndex = emote.EndIndex,
                        ImageUrl = emote.ImageUrl
                    });
                }
            }

            Color usernameColor = Color.white;
            if (!string.IsNullOrEmpty(chatMessage.ColorHex))
                ColorUtility.TryParseHtmlString(chatMessage.ColorHex, out usernameColor);

            var message = new LiveChatMessage
            {
                MessageId = chatMessage.Id,
                UserId = chatMessage.UserId,
                Username = chatMessage.Username,
                DisplayName = chatMessage.DisplayName,
                Channel = chatMessage.Channel,
                RawMessage = chatMessage.Message,
                RawIrcMessage = chatMessage.RawIrcMessage,
                UsernameColor = usernameColor,
                IsSubscriber = chatMessage.IsSubscriber,
                IsFirstMessage = chatMessage.IsFirstMessage,
                Bits = chatMessage.Bits,
                IsModerator = chatMessage.IsModerator,
                IsBroadcaster = chatMessage.IsBroadcaster,
                IsVip = chatMessage.IsVip,
                IsMe = chatMessage.IsMe,
                Emotes = emotes
            };

            RaiseMessageReceived(message);
        }

        private void DisconnectInternal()
        {
            if (_client == null)
                return;

            _client.OnConnected -= OnConnected;
            _client.OnJoinedChannel -= OnJoinedChannel;
            _client.OnMessageReceived -= OnMessageReceived;
            _client.OnError -= OnError;

            if (_client.IsConnected)
                _client.Disconnect();

            _client = null;
            IsConnected = false;
            RaiseDisconnected();
        }
    }
}
