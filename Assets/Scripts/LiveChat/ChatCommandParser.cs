using System;
using System.Collections.Generic;
using System.Text;

namespace LiveChat.Commands
{
    public readonly struct ChatCommandParseResult
    {
        public string Prefix { get; }
        public string Command { get; }
        public string ArgsRaw { get; }
        public string[] Args { get; }
        public string RawText { get; }

        public ChatCommandParseResult(string prefix, string command, string argsRaw, string[] args, string rawText)
        {
            Prefix = prefix;
            Command = command;
            ArgsRaw = argsRaw;
            Args = args ?? Array.Empty<string>();
            RawText = rawText;
        }
    }

    public static class ChatCommandParser
    {
        public static bool TryParse(string rawMessage, string[] prefixes, bool ignoreCase, out ChatCommandParseResult result)
        {
            result = default;
            if (string.IsNullOrWhiteSpace(rawMessage))
                return false;

            if (prefixes == null || prefixes.Length == 0)
                return false;

            string trimmed = rawMessage.TrimStart();
            string matchedPrefix = null;
            foreach (string prefix in prefixes)
            {
                if (string.IsNullOrEmpty(prefix))
                    continue;

                if (trimmed.StartsWith(prefix, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                {
                    matchedPrefix = prefix;
                    break;
                }
            }

            if (matchedPrefix == null)
                return false;

            string commandText = trimmed.Substring(matchedPrefix.Length).TrimStart();
            if (string.IsNullOrEmpty(commandText))
                return false;

            string command;
            string argsRaw;
            int whitespaceIndex = IndexOfWhitespace(commandText);
            if (whitespaceIndex < 0)
            {
                command = commandText;
                argsRaw = string.Empty;
            }
            else
            {
                command = commandText.Substring(0, whitespaceIndex);
                argsRaw = commandText.Substring(whitespaceIndex + 1).TrimStart();
            }

            string[] args = ParseArguments(argsRaw);
            result = new ChatCommandParseResult(matchedPrefix, command, argsRaw, args, trimmed);
            return true;
        }

        public static string[] ParseArguments(string argsRaw)
        {
            if (string.IsNullOrWhiteSpace(argsRaw))
                return Array.Empty<string>();

            List<string> args = new List<string>();
            StringBuilder current = new StringBuilder();
            bool inQuotes = false;
            char quoteChar = '\0';

            for (int i = 0; i < argsRaw.Length; i++)
            {
                char c = argsRaw[i];
                if (inQuotes)
                {
                    if (c == '\\' && i + 1 < argsRaw.Length)
                    {
                        char next = argsRaw[i + 1];
                        if (next == quoteChar || next == '\\')
                        {
                            current.Append(next);
                            i++;
                            continue;
                        }
                    }

                    if (c == quoteChar)
                    {
                        inQuotes = false;
                        continue;
                    }

                    current.Append(c);
                    continue;
                }

                if (char.IsWhiteSpace(c))
                {
                    if (current.Length > 0)
                    {
                        args.Add(current.ToString());
                        current.Clear();
                    }
                    continue;
                }

                if (IsQuote(c))
                {
                    inQuotes = true;
                    quoteChar = c;
                    continue;
                }

                if (c == '\\' && i + 1 < argsRaw.Length)
                {
                    char next = argsRaw[i + 1];
                    if (next == '"' || next == '\'' || next == '\\' || char.IsWhiteSpace(next))
                    {
                        current.Append(next);
                        i++;
                        continue;
                    }
                }

                current.Append(c);
            }

            if (current.Length > 0)
                args.Add(current.ToString());

            return args.ToArray();
        }

        private static int IndexOfWhitespace(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsWhiteSpace(text[i]))
                    return i;
            }

            return -1;
        }

        private static bool IsQuote(char value) => value == '"' || value == '\'';
    }
}
