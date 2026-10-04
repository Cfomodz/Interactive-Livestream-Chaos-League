using System;
using System.Collections.Generic;
using UnityEngine;

namespace LiveChat.Commands
{
    public abstract class ChatCommandHandler : MonoBehaviour
    {
        [SerializeField] private string _command = string.Empty;
        [SerializeField] private string[] _aliases = new string[0];
        [SerializeField, TextArea(1, 3)] private string _description = string.Empty;
        [SerializeField] private string _usage = string.Empty;
        [SerializeField] private ChatCommandPermission _minimumPermission = ChatCommandPermission.Anyone;

        public string Command => _command;
        public IReadOnlyList<string> Aliases => _aliases;
        public string Description => _description;
        public string Usage => _usage;
        public ChatCommandPermission MinimumPermission => _minimumPermission;

        public virtual bool CanExecute(ChatCommandContext context)
        {
            return context != null && context.HasPermission(_minimumPermission);
        }

        public bool Matches(string command, StringComparer comparer)
        {
            if (string.IsNullOrWhiteSpace(command))
                return false;

            if (!string.IsNullOrEmpty(_command) && comparer.Equals(_command, command))
                return true;

            if (_aliases == null)
                return false;

            for (int i = 0; i < _aliases.Length; i++)
            {
                if (string.IsNullOrEmpty(_aliases[i]))
                    continue;

                if (comparer.Equals(_aliases[i], command))
                    return true;
            }

            return false;
        }

        public abstract void Execute(ChatCommandContext context);

        protected void SetDefaults(string command, string description, string usage,
            ChatCommandPermission minimumPermission, params string[] aliases)
        {
            _command = command ?? string.Empty;
            _description = description ?? string.Empty;
            _usage = usage ?? string.Empty;
            _minimumPermission = minimumPermission;
            _aliases = aliases ?? Array.Empty<string>();
        }
    }
}
