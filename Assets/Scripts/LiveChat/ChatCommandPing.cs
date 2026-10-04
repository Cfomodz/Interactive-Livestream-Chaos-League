using UnityEngine;

namespace LiveChat.Commands
{
    public class ChatCommandPing : ChatCommandHandler
    {
        [SerializeField] private string _reply = "Pong!";

        private void Reset()
        {
            SetDefaults(
                command: "ping",
                description: "Quick connectivity check.",
                usage: "!ping",
                minimumPermission: ChatCommandPermission.Anyone);
        }

        public override void Execute(ChatCommandContext context)
        {
            if (context == null)
                return;

            context.Reply(_reply);
        }
    }
}
