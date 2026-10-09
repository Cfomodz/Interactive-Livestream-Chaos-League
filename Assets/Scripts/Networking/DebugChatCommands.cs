using System;
using System.Collections.Generic;
using System.Linq;
using LiveChat;

/// <summary>
/// Test commands for debug chat (UseDebugChat): lines starting with "/" in the chat box simulate other
/// viewers and stream events, so the whole game can be played through without Twitch.
/// </summary>
public static class DebugChatCommands
{
    private static readonly string[] Help =
    {
        "/as <viewer> <message>: chat as another viewer (e.g. /as alice !invitedby @bob)",
        "/bits <viewer> <amount> [message]: cheer (e.g. /bits alice 300 !lava)",
        "/redeem <viewer> <tickets|lava|water|autojoin20|autojoin50|autojoin100>: channel points (e.g. /redeem alice 100)",
        "/sub <viewer> [months]   /gift <gifter|anon> <viewer>   /giftbomb <gifter|anon> <count>",
        "/raid <channel> <viewers>   /follow <viewer>",
        "Chat without / is you, the streamer. Add cheer100 to any message to cheer.",
    };

    public static void Run(LocalDebugLiveChatClient chat, string line)
    {
        string[] words = line.Substring(1).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return;

        try
        {
            if (!Handle(chat, words[0].ToLowerInvariant(), words.Skip(1).ToArray()))
                Reply(chat, "Unknown or incomplete debug command. /help lists them.");
        }
        catch (FormatException)
        {
            Reply(chat, "Expected a number there. /help shows examples.");
        }
    }

    private static bool Handle(LocalDebugLiveChatClient chat, string command, string[] args)
    {
        switch (command)
        {
            case "help":
                foreach (string help in Help)
                    Reply(chat, help);
                return true;

            case "as" when args.Length >= 2:
                chat.SimulateIncoming(Rest(args, 1), Viewer(args[0]), args[0]);
                return true;

            case "bits" when args.Length >= 2:
                int bits = int.Parse(args[1]);
                chat.SimulateIncoming($"{Rest(args, 2)} cheer{bits}".Trim(), Viewer(args[0]), args[0]);
                return true;

            case "redeem" when args.Length >= 2:
                string reward = args[1].ToLowerInvariant();
                if (reward == "lava")
                    chat.SimulateRedemption(TwitchApi.LavaRewardTitle, Viewer(args[0]), null, TwitchApi.LavaRewardCost);
                else if (reward == "water")
                    chat.SimulateRedemption(TwitchApi.WaterRewardTitle, Viewer(args[0]), null, TwitchApi.WaterRewardCost);
                else if (reward.StartsWith("autojoin"))
                {
                    int rounds = int.Parse(reward.Substring("autojoin".Length));
                    if (!TwitchApi.AutoJoinRewards.Any(r => r.Rounds == rounds))
                        return false;
                    chat.SimulateRedemption(TwitchApi.AutoJoinRewardTitle(rounds), Viewer(args[0]), null, TwitchApi.AutoJoinRewards.First(r => r.Rounds == rounds).Cost);
                }
                else
                {
                    int tickets = int.Parse(reward);
                    chat.SimulateRedemption(TwitchApi.BidRewardTitle(tickets), Viewer(args[0]), null, tickets);
                }
                return true;

            case "sub" when args.Length >= 1:
                chat.SimulateSubscription(Viewer(args[0]), args.Length > 1 ? int.Parse(args[1]) : 1);
                return true;

            case "gift" when args.Length >= 2:
                chat.SimulateGiftSubscription(Gifter(args[0]), Viewer(args[1]));
                return true;

            case "giftbomb" when args.Length >= 2:
                int count = Math.Clamp(int.Parse(args[1]), 1, 50);
                List<string> recipients = Enumerable.Range(1, count).Select(i => $"giftee{i}").ToList();
                chat.SimulateCommunityGift(Gifter(args[0]), recipients);
                return true;

            case "raid" when args.Length >= 2:
                chat.SimulateRaid(Viewer(args[0]), int.Parse(args[1]));
                return true;

            case "follow" when args.Length >= 1:
                chat.SimulateFollow(Viewer(args[0]));
                return true;

            default:
                return false;
        }
    }

    //The debug chat uses names as user IDs and lowercases them for events, so viewers are lowercase everywhere
    private static string Viewer(string name) => name.TrimStart('@').ToLowerInvariant();

    private static string Gifter(string name) => string.Equals(name, "anon", StringComparison.OrdinalIgnoreCase) ? null : Viewer(name);

    private static string Rest(string[] args, int from) => string.Join(" ", args.Skip(from));

    private static void Reply(LocalDebugLiveChatClient chat, string text) => chat.SendMessage(text);
}
