using System.Collections.Generic;
using UnityEngine;

namespace LiveChat.Commands
{
    public class ChatCommandHelp : ChatCommandHandler
    {
        [SerializeField] private ChatCommandRouter _router;
        [SerializeField] private int _maxEntriesPerMessage = 6;
        [SerializeField] private bool _includeAliasesInDetails = true;

        private void Reset()
        {
            SetDefaults(
                command: "help",
                description: "Shows available chat commands.",
                usage: "!help [command]",
                minimumPermission: ChatCommandPermission.Anyone,
                aliases: new[] { "commands" });
        }

        private void Awake()
        {
            if (_router == null)
                _router = GetComponentInParent<ChatCommandRouter>();
        }

        public override void Execute(ChatCommandContext context)
        {
            if (context == null)
                return;

            ChatCommandRouter router = _router != null ? _router : context.Router;
            if (router == null)
            {
                context.Reply("Help is not configured.");
                return;
            }

            if (context.Args.Length > 0)
            {
                string query = router.NormalizeCommand(context.Args[0]);
                ChatCommandHandler handler = router.FindHandler(query);
                if (handler == null)
                {
                    context.Reply($"No command found for \"{query}\".");
                    return;
                }

                string prefix = router.PrimaryPrefix;
                string description = string.IsNullOrEmpty(handler.Description) ? string.Empty : $" - {handler.Description}";
                string usage = string.IsNullOrEmpty(handler.Usage) ? string.Empty : $" Usage: {handler.Usage}";
                string aliases = string.Empty;

                if (_includeAliasesInDetails && handler.Aliases != null && handler.Aliases.Count > 0)
                    aliases = $" Aliases: {FormatAliases(prefix, handler.Aliases)}";

                context.Reply($"{prefix}{handler.Command}{description}{usage}{aliases}");
                return;
            }

            List<string> available = new List<string>();
            for (int i = 0; i < router.Handlers.Count; i++)
            {
                ChatCommandHandler handler = router.Handlers[i];
                if (handler == null || !handler.isActiveAndEnabled)
                    continue;
                if (!handler.CanExecute(context))
                    continue;
                if (string.IsNullOrWhiteSpace(handler.Command))
                    continue;

                available.Add($"{router.PrimaryPrefix}{handler.Command}");
            }

            if (available.Count == 0)
            {
                context.Reply("No commands available.");
                return;
            }

            SendBatched(context, "Commands: ", available, _maxEntriesPerMessage);
        }

        private static void SendBatched(ChatCommandContext context, string prefix, List<string> commands, int maxPerMessage)
        {
            if (maxPerMessage <= 0)
                maxPerMessage = commands.Count;

            for (int i = 0; i < commands.Count; i += maxPerMessage)
            {
                int count = Mathf.Min(maxPerMessage, commands.Count - i);
                string message = prefix + string.Join(", ", commands.GetRange(i, count));
                context.Reply(message);
                prefix = string.Empty;
            }
        }

        private static string FormatAliases(string prefix, IReadOnlyList<string> aliases)
        {
            List<string> formatted = new List<string>();
            for (int i = 0; i < aliases.Count; i++)
            {
                if (string.IsNullOrEmpty(aliases[i]))
                    continue;

                formatted.Add($"{prefix}{aliases[i]}");
            }

            return string.Join(", ", formatted);
        }
    }
}
