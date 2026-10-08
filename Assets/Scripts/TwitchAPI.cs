using LiveChat;
using LiveChat.Twitch;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Sets up the Twitch connection (chat, stream events, channel points, predictions and polls) through
/// the com.cfomodz.livechat package, and wraps the Helix calls the game makes.
///
/// The bot and the broadcaster log in with Twitch's device code flow: the activation page opens in the
/// browser with the code filled in. Tokens are saved under persistentDataPath and refreshed automatically.
///
/// With UseDebugChat in your config (or -debugchat on the command line) the game runs without Twitch:
/// an on-screen chat box stands in for chat, and DebugChatCommands simulates viewers, bits, subs and
/// redemptions. Predictions and polls are skipped then, and user lookups return made-up users.
/// </summary>
public class TwitchApi : MonoBehaviour
{
    /// <summary>The chat client in use: Twitch, or the debug chat box.</summary>
    private static LiveChatClientBase _chat;
    /// <summary>The Twitch client, for the Helix calls; null in debug chat.</summary>
    private static TwitchLiveChatClient _twitch;

    /// <summary>The debug chat's streamer: it's the channel and the account typing in the chat box.</summary>
    public const string DebugStreamer = "streamer";

    [SerializeField] private TwitchClient _twitchClient;
    [SerializeField] private TwitchPubSub _twitchPubSub;
    [SerializeField] private GoldDistributor _liveViewCount;

    [SerializeField] private Gradient _customRewardBackgroundColors;
    [SerializeField] private Color _lavaRewardBackgroundColor;
    [SerializeField] private Color _waterRewardBackgroundColor;

    public static bool IsDebugChat => _chat is LocalDebugLiveChatClient;

    private static string _setupProblem;

    /// <summary>One line describing the Twitch connection, for the settings overlay.</summary>
    public static string StatusText =>
        IsDebugChat ? "Debug chat (not connected to Twitch)"
        : _setupProblem ?? (_twitch != null ? _twitch.StatusText : "Not connected");

    public static string BidRewardTitle(int cost) => $"Bid {cost} Spawn Ticket{((cost == 1) ? "" : "s")}";
    public const string LavaRewardTitle = "Activate Lava on Throne Tile";
    public const string WaterRewardTitle = "Activate Water on Throne Tile";
    public static int LavaRewardCost => AppConfig.inst.GetI("ThroneLavaCost") * 3; //3 times as expensive as bits
    public static int WaterRewardCost => AppConfig.inst.GetI("ThroneWaterCost") * 3;

    /// <summary>OBS control (ObsEnabled in your config), or null when it's off.</summary>
    public static LiveChat.Obs.ObsController Obs { get; private set; }
    /// <summary>The broadcaster's OBS chat commands: !clscene, !clgolive, !clend, !clobs. Null when OBS control is off.</summary>
    public static LiveChat.Obs.ObsChatCommands ObsCommands { get; private set; }

    private void StartObs()
    {
        Obs = LiveChat.Obs.ObsController.Create(gameObject, new LiveChat.Obs.ObsSettings
        {
            enabled = AppConfig.inst.GetB("ObsEnabled"),
            url = AppConfig.inst.GetS("ObsUrl"),
            password = AppConfig.inst.GetS("ObsPassword"),
            startingScene = AppConfig.inst.GetS("ObsStartingScene"),
            gameScene = AppConfig.inst.GetS("ObsGameScene")
        });
        ObsCommands = Obs == null ? null : new LiveChat.Obs.ObsChatCommands(Obs, "cl");
    }

    private void Start()
    {
        StartObs();

        bool debugChat = AppConfig.inst.GetB("UseDebugChat")
            || Environment.GetCommandLineArgs().Any(arg => string.Equals(arg, "-debugchat", StringComparison.OrdinalIgnoreCase));
        if (debugChat)
        {
            StartDebugChat();
            return;
        }

        _twitch = _twitchClient.GetComponent<TwitchLiveChatClient>();
        if (_twitch == null)
            _twitch = _twitchClient.gameObject.AddComponent<TwitchLiveChatClient>();
        _chat = _twitch;

        _twitch.ChannelPoints = true;
        _twitch.Predictions = true;
        _twitch.Polls = true;

        _twitch.AuthorizationRequired -= OnAuthorizationRequired;
        _twitch.Connected -= OnConnected;
        _twitch.ChannelPointsConnected -= OnChannelPointsConnected;
        _twitch.AuthorizationRequired += OnAuthorizationRequired;
        _twitch.Connected += OnConnected;
        _twitch.ChannelPointsConnected += OnChannelPointsConnected;

        _twitchClient.Init(_twitch);
        _twitchPubSub.Init(_twitch);

        ConnectToTwitch();
    }

    /// <summary>
    /// Connects with the channel, client ID and bot from your config (the settings menu's Networking
    /// page, or config.json). If a login is waiting, opens Twitch's activation page for it again.
    /// Wired to the settings menu's Connect button.
    /// </summary>
    public void ConnectToTwitch()
    {
        if (_twitch == null)
            return;

        if (_twitch.PendingAuthorization != null)
        {
            OnAuthorizationRequired(_twitch.PendingAuthorization);
            return;
        }

        string channel = AppConfig.inst.GetS("TwitchChannel").Trim();
        string clientId = AppConfig.inst.GetS("TwitchClientId").Trim();
        if (string.IsNullOrEmpty(channel) || string.IsNullOrEmpty(clientId))
        {
            _setupProblem = "Set TwitchChannel and TwitchClientId (settings, Networking), then press Connect";
            Debug.LogError($"{_setupProblem}. They're saved in {UserData.ConfigPath}. Or turn on UseDebugChat to play without Twitch.");
            return;
        }

        _setupProblem = null;
        _twitch.ClientId = clientId;
        _twitch.BotLogin = AppConfig.inst.GetS("TwitchBotLogin").Trim();
        _twitch.TokenFilePath = Path.Combine(UserData.Folder, "livechat-tokens.json");
        //Set once the broadcaster logs in; empty fields leave that part of the stream info alone
        _twitch.StreamInfo = new TwitchStreamInfo
        {
            Title = AppConfig.inst.GetS("StreamTitle").Trim(),
            Category = AppConfig.inst.GetS("StreamCategory").Trim(),
            Tags = AppConfig.inst.GetS("StreamTags").Split(',').Select(tag => tag.Trim()).Where(tag => tag.Length > 0).ToArray()
        };
        _twitch.Connect(new LiveChatConnectConfig { ChannelName = channel });
    }

    /// <summary>Forgets the saved Twitch logins and logs in from scratch. Wired to the settings menu.</summary>
    public void LogInToTwitchAgain()
    {
        if (_twitch == null)
            return;

        _twitch.Disconnect();
        string tokens = Path.Combine(UserData.Folder, "livechat-tokens.json");
        if (File.Exists(tokens))
            File.Delete(tokens);
        Debug.Log("Forgot the saved Twitch logins; logging in again.");
        ConnectToTwitch();
    }

    private void StartDebugChat()
    {
        LocalDebugLiveChatClient debug = _twitchClient.GetComponent<LocalDebugLiveChatClient>();
        if (debug == null)
            debug = _twitchClient.gameObject.AddComponent<LocalDebugLiveChatClient>();
        _chat = debug;

        //Lowercase, because the debug client uses names as user IDs and lowercases them for events
        debug.Username = DebugStreamer;
        debug.IsBroadcaster = true;
        debug.Hint = "Debug chat: you're the streamer. /help lists test commands. ` hides this box.";

        Secrets.CHANNEL_NAME = DebugStreamer;
        Secrets.CHANNEL_ID = DebugStreamer;

        _twitchClient.Init(debug);
        _twitchPubSub.Init(debug);
        debug.Connect(new LiveChatConnectConfig { ChannelName = DebugStreamer });
        Debug.Log("Debug chat is on: the game isn't connected to Twitch. Type in the box at the bottom left; /help lists test commands.");

        string script = UserData.CommandLineValue("-debugchatscript");
        if (!string.IsNullOrEmpty(script))
            StartCoroutine(RunDebugChatScript(debug, script));
    }

    /// <summary>
    /// Types each line of a file into debug chat, for repeatable test sessions: chat as the streamer,
    /// "/" test commands, "/wait seconds" to pause, and "#" comments.
    /// </summary>
    private System.Collections.IEnumerator RunDebugChatScript(LocalDebugLiveChatClient debug, string path)
    {
        if (!System.IO.File.Exists(path))
        {
            Debug.LogError($"Debug chat script not found: {path}");
            yield break;
        }

        yield return new WaitForSeconds(3); //Let the game finish starting up
        foreach (string raw in System.IO.File.ReadAllLines(path))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#"))
                continue;
            if (line.StartsWith("/wait ", StringComparison.OrdinalIgnoreCase)
                && float.TryParse(line.Substring(6), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float seconds))
            {
                yield return new WaitForSeconds(seconds);
                continue;
            }
            debug.SimulateIncoming(line);
            yield return null;
        }
        Debug.Log($"Debug chat script finished: {path}");
    }

    private void OnAuthorizationRequired(TwitchDeviceAuthorization authorization)
    {
        Debug.Log($"Twitch login needed: {authorization.Instructions}");
        Application.OpenURL(authorization.VerificationUri);
    }

    private void OnConnected()
    {
        Secrets.CHANNEL_NAME = _twitch.Broadcaster.Login;
        Secrets.CHANNEL_ID = _twitch.Broadcaster.Id;
        Debug.Log($"Connected to Twitch channel {Secrets.CHANNEL_NAME} ({Secrets.CHANNEL_ID})");
    }

    private void OnChannelPointsConnected()
    {
        _ = CreateCustomPointRewards();
    }

    /// <summary>
    /// Creates the game's channel point rewards, or updates and reopens the ones it made before.
    /// Only rewards this app created can be fulfilled or refunded, so rewards with the same titles
    /// made another way have to be deleted in the Creator Dashboard first.
    /// </summary>
    public async Task CreateCustomPointRewards()
    {
        List<TwitchRewardSpec> rewards = new List<TwitchRewardSpec>();

        int[] costs = new int[] { 1, 2, 5, 10, 25, 50, 100, 200, 500, 1000, 2500, 5000, 10_000, 20_000, 50_000, 100_000 };
        for (int i = 0; i < costs.Length; i++)
        {
            int cost = costs[i];
            float t = i / (float)costs.Length;
            rewards.Add(new TwitchRewardSpec
            {
                Title = BidRewardTitle(cost),
                Cost = cost,
                Prompt = "Top bidders are guaranteed to spawn! Remaining bids are entered into a raffle. You earn free tickets by watching the stream.",
                BackgroundColor = MyUtil.ColorToHexString(_customRewardBackgroundColors.Evaluate(t)),
            });
        }

        rewards.Add(new TwitchRewardSpec
        {
            Title = LavaRewardTitle,
            Cost = LavaRewardCost,
            Prompt = $"Mimicks adding {AppConfig.inst.GetI("ThroneLavaCost")} bit cheer to the !lava trigger, for free!",
            BackgroundColor = MyUtil.ColorToHexString(_lavaRewardBackgroundColor),
        });

        rewards.Add(new TwitchRewardSpec
        {
            Title = WaterRewardTitle,
            Cost = WaterRewardCost,
            Prompt = $"Mimicks adding {AppConfig.inst.GetI("ThroneWaterCost")} bit cheer to the !water trigger, for free!",
            BackgroundColor = MyUtil.ColorToHexString(_waterRewardBackgroundColor),
        });

        try
        {
            //Never removeUnlisted: games sharing this Twitch app (the same client ID, like chat-minigames)
            //count as the same app, so their rewards would look unlisted and get deleted
            IReadOnlyDictionary<string, string> ids = await _twitch.EnsureRewardsAsync(rewards, removeUnlisted: false);
            Debug.Log($"Channel point rewards ready: {ids.Count} of {rewards.Count}");
        }
        catch (Exception ex)
        {
            Debug.LogError("Failed to set up channel point rewards.\n" + ex.Message);
        }
    }

    private static bool IsConnected => _twitch != null && _twitch.IsConnected;

    /// <summary>A made-up user for debug chat, with the same ID scheme as the debug chat box (the lowercase name).</summary>
    private static TwitchUser DebugUser(string login) => new TwitchUser
    {
        Id = login.ToLowerInvariant(),
        Login = login.ToLowerInvariant(),
        DisplayName = login
    };

    public static async Task<TwitchUser> GetUserByUsername(string username)
    {
        if (string.IsNullOrEmpty(username))
        {
            Debug.Log($"Failed to find user: [{username}] because username is null or empty. Returning null.");
            return null;
        }
        if (IsDebugChat)
            return DebugUser(username);
        if (!IsConnected)
        {
            Debug.Log($"Failed to find user: [{username}] because Twitch isn't connected yet. Returning null.");
            return null;
        }

        try
        {
            TwitchUser user = await _twitch.GetUserByLoginAsync(username);
            if (user == null)
                Debug.Log($"Failed to find user: [{username}]. Returning null.");
            return user;
        }
        catch (Exception ex)
        {
            Debug.Log($"Failed to find user: [{username}] due to exception in API call. Returning null. Exception: {ex}");
            return null;
        }
    }

    public static async Task<TwitchUser> GetUserById(string twitchId)
    {
        if (IsDebugChat)
            return DebugUser(twitchId);
        if (!IsConnected)
            return null;

        try
        {
            TwitchUser user = await _twitch.GetUserByIdAsync(twitchId);
            if (user == null)
                Debug.LogError($"Failed to find user from twitchId: {twitchId}. Returning null.");
            return user;
        }
        catch (Exception ex)
        {
            Debug.Log($"Failed to find user from twitchId: {twitchId} due to exception in API call. Returning null. Exception: {ex}");
            return null;
        }
    }

    /// <summary>The channel's stream, or null when it isn't live or Twitch isn't connected.</summary>
    public async Task<TwitchStream> GetStream()
    {
        if (!IsConnected)
            return null;

        TwitchStream stream = await _twitch.GetStreamAsync();
        if (stream == null)
            Debug.Log("Failed to find stream");
        return stream;
    }

    public static async Task StartPoll(string title, List<string> choices, int durationSeconds)
    {
        if (!IsConnected)
        {
            Debug.Log($"Skipping poll '{title}': not connected to Twitch.");
            return;
        }

        try
        {
            await _twitch.CreatePollAsync(title, choices, durationSeconds);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to start poll '{title}'. \n{ex.Message}");
        }
    }

    /// <summary>The choices of the most recent poll, or null if there's none.</summary>
    public static async Task<List<TwitchPollChoice>> GetPollResults()
    {
        if (_twitch == null)
            return null;

        List<TwitchPoll> polls = await _twitch.GetPollsAsync(1);
        if (polls.Count <= 0)
        {
            Debug.LogError("Failed to get poll results");
            return null;
        }
        return polls[0].Choices;
    }

    public static async Task<TwitchPrediction> StartPrediction(PredictionObj predictionObj)
    {
        if (!IsConnected)
        {
            Debug.Log($"Skipping prediction '{predictionObj.Title}': not connected to Twitch.");
            return null;
        }

        try
        {
            string title = predictionObj.Title.TruncateString(45);
            List<string> outcomes = predictionObj.GetOutcomes();
            Debug.Log($"Starting prediction request: {title} {string.Join(" / ", outcomes)} {predictionObj.PredictionWindowSec}");

            TwitchPrediction prediction = await _twitch.CreatePredictionAsync(title, outcomes, predictionObj.PredictionWindowSec);
            if (prediction == null)
                Debug.LogError("Failed to create prediction");
            return prediction;
        }
        catch (Exception e)
        {
            Debug.LogError(e);
            await CancelAllPredictions();
            return null;
        }
    }

    public static Task FinishPrediction(string predictionID, string winningOutcomeID)
    {
        return _twitch == null ? Task.CompletedTask : _twitch.ResolvePredictionAsync(predictionID, winningOutcomeID);
    }

    public static Task CancelPrediction(string predictionID)
    {
        return _twitch == null ? Task.CompletedTask : _twitch.CancelPredictionAsync(predictionID);
    }

    public static async Task CancelAllPredictions()
    {
        if (!IsConnected)
            return;

        try
        {
            await _twitch.CancelOpenPredictionsAsync();
        }
        catch (Exception ex)
        {
            Debug.Log("Failed to cancel open predictions. \n" + ex.Message);
        }
    }
}
