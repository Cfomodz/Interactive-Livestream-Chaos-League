using System;
using System.Collections.Generic;
using UnityEngine;

namespace LiveChat.Commands
{
    public class ChatCommandRouter : MonoBehaviour
    {
        [Header("Chat Source")]
        [SerializeField] private LiveChatClientBase _client;

        [Header("Command Parsing")]
        [SerializeField] private string[] _commandPrefixes = new string[] { "!" };
        [SerializeField] private bool _ignoreCase = true;

        [Header("Handler Discovery")]
        [SerializeField] private bool _autoDiscoverHandlers = true;
        [SerializeField] private Transform _handlerRoot;
        [SerializeField] private bool _includeInactiveHandlers = true;
        [SerializeField] private List<ChatCommandHandler> _manualHandlers = new List<ChatCommandHandler>();

        [Header("Responses")]
        [SerializeField] private bool _replyOnPermissionDenied = false;
        [SerializeField] private string _permissionDeniedMessage = "You don't have permission to use {command}.";
        [SerializeField] private bool _replyOnUnknownCommand = false;
        [SerializeField] private string _unknownCommandMessage = "Unknown command: {command}";
        [SerializeField] private bool _logCommands = false;
        [SerializeField] private bool _logUnknownCommands = true;

        private readonly List<ChatCommandHandler> _handlerCache = new List<ChatCommandHandler>();
        private Dictionary<string, List<ChatCommandHandler>> _handlerLookup;
        private StringComparer _commandComparer;

        public IReadOnlyList<ChatCommandHandler> Handlers => _handlerCache;
        public string PrimaryPrefix => _commandPrefixes != null && _commandPrefixes.Length > 0 ? _commandPrefixes[0] : "!";

        private void Awake()
        {
            BuildComparer();
            RefreshHandlers();
        }

        private void OnEnable()
        {
            if (_client == null)
                _client = GetComponent<LiveChatClientBase>();

            if (_client != null)
                _client.MessageReceived += OnMessageReceived;
        }

        private void OnDisable()
        {
            if (_client != null)
                _client.MessageReceived -= OnMessageReceived;
        }

        private void OnValidate()
        {
            BuildComparer();
        }

        public void RefreshHandlers()
        {
            _handlerCache.Clear();

            if (_manualHandlers != null)
                AddHandlers(_manualHandlers);

            if (_autoDiscoverHandlers)
            {
                Transform root = _handlerRoot != null ? _handlerRoot : transform;
                ChatCommandHandler[] discovered = root.GetComponentsInChildren<ChatCommandHandler>(_includeInactiveHandlers);
                AddHandlers(discovered);
            }

            RebuildLookup();
        }

        public void RegisterHandler(ChatCommandHandler handler)
        {
            if (handler == null || _handlerCache.Contains(handler))
                return;

            _handlerCache.Add(handler);
            RegisterCommand(handler.Command, handler);
            var aliases = handler.Aliases;
            if (aliases == null)
                return;

            for (int i = 0; i < aliases.Count; i++)
                RegisterCommand(aliases[i], handler);
        }

        public void UnregisterHandler(ChatCommandHandler handler)
        {
            if (handler == null)
                return;

            if (_handlerCache.Remove(handler))
                RebuildLookup();
        }

        public ChatCommandHandler FindHandler(string command)
        {
            if (string.IsNullOrWhiteSpace(command) || _handlerLookup == null)
                return null;

            if (!_handlerLookup.TryGetValue(command, out List<ChatCommandHandler> handlers))
                return null;

            for (int i = 0; i < handlers.Count; i++)
            {
                var handler = handlers[i];
                if (handler != null && handler.isActiveAndEnabled)
                    return handler;
            }

            return null;
        }

        public string NormalizeCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
                return string.Empty;

            string trimmed = command.Trim();
            if (_commandPrefixes == null)
                return trimmed;

            for (int i = 0; i < _commandPrefixes.Length; i++)
            {
                string prefix = _commandPrefixes[i];
                if (string.IsNullOrEmpty(prefix))
                    continue;

                if (trimmed.StartsWith(prefix, _ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    return trimmed.Substring(prefix.Length);
            }

            return trimmed;
        }

        public bool ProcessMessage(LiveChatMessage message)
        {
            if (message == null)
                return false;

            if (!ChatCommandParser.TryParse(message.RawMessage, _commandPrefixes, _ignoreCase, out ChatCommandParseResult parsed))
                return false;

            ChatCommandHandler handler = FindHandler(parsed.Command);
            if (handler == null)
            {
                if (_logUnknownCommands)
                    Debug.Log($"[ChatCommandRouter] Unknown command '{parsed.Command}'.");
                if (_replyOnUnknownCommand)
                {
                    ChatCommandContext context = new ChatCommandContext(this, _client, message, parsed.RawText, parsed.Command, parsed.ArgsRaw, parsed.Args);
                    context.Reply(_unknownCommandMessage.Replace("{command}", parsed.Command));
                }
                return false;
            }

            ChatCommandContext commandContext = new ChatCommandContext(this, _client, message, parsed.RawText, parsed.Command, parsed.ArgsRaw, parsed.Args);
            if (!handler.CanExecute(commandContext))
            {
                if (_replyOnPermissionDenied)
                    commandContext.Reply(_permissionDeniedMessage.Replace("{command}", parsed.Command));
                return false;
            }

            if (_logCommands)
                Debug.Log($"[ChatCommandRouter] {commandContext.Username}: {parsed.Command} {parsed.ArgsRaw}");

            try
            {
                handler.Execute(commandContext);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }

            return true;
        }

        private void OnMessageReceived(LiveChatMessage message)
        {
            ProcessMessage(message);
        }

        private void AddHandlers(IEnumerable<ChatCommandHandler> handlers)
        {
            if (handlers == null)
                return;

            foreach (var handler in handlers)
            {
                if (handler == null || _handlerCache.Contains(handler))
                    continue;

                _handlerCache.Add(handler);
            }
        }

        private void RebuildLookup()
        {
            if (_handlerLookup == null || _handlerLookup.Comparer != _commandComparer)
                BuildComparer();
            else
                _handlerLookup.Clear();

            for (int i = 0; i < _handlerCache.Count; i++)
            {
                ChatCommandHandler handler = _handlerCache[i];
                if (handler == null)
                    continue;

                RegisterCommand(handler.Command, handler);
                var aliases = handler.Aliases;
                if (aliases == null)
                    continue;

                for (int aliasIndex = 0; aliasIndex < aliases.Count; aliasIndex++)
                    RegisterCommand(aliases[aliasIndex], handler);
            }
        }

        private void RegisterCommand(string command, ChatCommandHandler handler)
        {
            if (string.IsNullOrWhiteSpace(command) || handler == null)
                return;

            if (!_handlerLookup.TryGetValue(command, out List<ChatCommandHandler> list))
            {
                list = new List<ChatCommandHandler>();
                _handlerLookup[command] = list;
            }

            if (!list.Contains(handler))
                list.Add(handler);
        }

        private void BuildComparer()
        {
            _commandComparer = _ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            _handlerLookup = new Dictionary<string, List<ChatCommandHandler>>(_commandComparer);
        }
    }
}
