using UnityEngine;

namespace LiveChat.Commands
{
    public class ChatCommandEcho : ChatCommandHandler
    {
        [SerializeField] private bool _replyAsBot = true;
        [SerializeField] private bool _allowEmptyMessage = false;

        private void Reset()
        {
            SetDefaults(
                command: "echo",
                description: "Repeats a message back to chat.",
                usage: "!echo <message>",
                minimumPermission: ChatCommandPermission.Moderator,
                aliases: new[] { "say" });
        }

        public override void Execute(ChatCommandContext context)
        {
            if (context == null)
                return;

            if (context.Args.Length == 0)
            {
                if (_allowEmptyMessage)
                    return;

                context.Reply("Usage: !echo <message>");
                return;
            }

            string message = context.ArgsRaw;
            if (_replyAsBot)
                context.Reply(message);
            else
                context.Say(message);
        }
    }
}
