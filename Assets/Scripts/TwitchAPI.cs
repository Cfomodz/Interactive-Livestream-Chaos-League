using LiveChat;
using LiveChat.Twitch;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Sets up the Twitch connection (chat, stream events, channel points, predictions and polls) through
/// the com.cfomodz.livechat package, and wraps the Helix calls the game makes.
///
/// The bot and the broadcaster log in with Twitch's device code flow: the activation page opens in the
/// browser with the code filled in. Tokens are saved under persistentDataPath and refreshed automatically.
/// </summary>
public class TwitchApi : MonoBehaviour
{
    private static TwitchLiveChatClient _client;

    [SerializeField] private TwitchClient _twitchClient;
    [SerializeField] private TwitchPubSub _twitchPubSub;
    [SerializeField] private GoldDistributor _liveViewCount;

    [SerializeField] private Gradient _customRewardBackgroundColors;
    [SerializeField] private Color _lavaRewardBackgroundColor;
    [SerializeField] private Color _waterRewardBackgroundColor;

    /// <summary>One line describing the Twitch connection, for the settings overlay.</summary>
    public static string StatusText => _client != null ? _client.StatusText : "Not connected";

    private void Start()
    {
        _client = _twitchClient.GetComponent<TwitchLiveChatClient>();
        if (_client == null)
            _client = _twitchClient.gameObject.AddComponent<TwitchLiveChatClient>();

        _client.ClientId = AppConfig.inst.GetS("TwitchClientId");
        _client.BotLogin = AppConfig.inst.GetS("TwitchBotLogin");
        _client.ChannelPoints = true;
        _client.Predictions = true;
        _client.Polls = true;

        _client.AuthorizationRequired -= OnAuthorizationRequired;
        _client.Connected -= OnConnected;
        _client.ChannelPointsConnected -= OnChannelPointsConnected;
        _client.AuthorizationRequired += OnAuthorizationRequired;
        _client.Connected += OnConnected;
        _client.ChannelPointsConnected += OnChannelPointsConnected;

        _twitchClient.Init(_client);
        _twitchPubSub.Init(_client);

        string channel = AppConfig.inst.GetS("TwitchChannel");
        if (string.IsNullOrWhiteSpace(channel))
        {
            Debug.LogError("Set TwitchChannel in StreamingAssets/config.json to the channel to connect to.");
            return;
        }
        _client.Connect(new LiveChatConnectConfig { ChannelName = channel });
    }

    private void OnAuthorizationRequired(TwitchDeviceAuthorization authorization)
    {
        Debug.Log($"Twitch login needed: {authorization.Instructions}");
        Application.OpenURL(authorization.VerificationUri);
    }

    private void OnConnected()
    {
        Secrets.CHANNEL_NAME = _client.Broadcaster.Login;
        Secrets.CHANNEL_ID = _client.Broadcaster.Id;
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
                Title = $"Bid {cost} Spawn Ticket{((cost == 1) ? "" : 's')}",
                Cost = cost,
                Prompt = "Top bidders are guaranteed to spawn! Remaining bids are entered into a raffle. You earn free tickets by watching the stream.",
                BackgroundColor = MyUtil.ColorToHexString(_customRewardBackgroundColors.Evaluate(t)),
            });
        }

        rewards.Add(new TwitchRewardSpec
        {
            Title = "Activate Lava on Throne Tile",
            Cost = AppConfig.inst.GetI("ThroneLavaCost") * 3, //3 times as expensive as bits
            Prompt = $"Mimicks adding {AppConfig.inst.GetI("ThroneLavaCost")} bit cheer to the !lava trigger, for free!",
            BackgroundColor = MyUtil.ColorToHexString(_lavaRewardBackgroundColor),
        });

        rewards.Add(new TwitchRewardSpec
        {
            Title = "Activate Water on Throne Tile",
            Cost = AppConfig.inst.GetI("ThroneWaterCost") * 3, //3 times as expensive as bits
            Prompt = $"Mimicks adding {AppConfig.inst.GetI("ThroneWaterCost")} bit cheer to the !water trigger, for free!",
            BackgroundColor = MyUtil.ColorToHexString(_waterRewardBackgroundColor),
        });

        try
        {
            IReadOnlyDictionary<string, string> ids = await _client.EnsureRewardsAsync(rewards, removeUnlisted: true);
            Debug.Log($"Channel point rewards ready: {ids.Count} of {rewards.Count}");
        }
        catch (Exception ex)
        {
            Debug.LogError("Failed to set up channel point rewards.\n" + ex.Message);
        }
    }

    private static bool IsConnected => _client != null && _client.IsConnected;

    public static async Task<TwitchUser> GetUserByUsername(string username)
    {
        if (string.IsNullOrEmpty(username))
        {
            Debug.Log($"Failed to find user: [{username}] because username is null or empty. Returning null.");
            return null;
        }
        if (!IsConnected)
        {
            Debug.Log($"Failed to find user: [{username}] because Twitch isn't connected yet. Returning null.");
            return null;
        }

        try
        {
            TwitchUser user = await _client.GetUserByLoginAsync(username);
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
        if (!IsConnected)
            return null;

        try
        {
            TwitchUser user = await _client.GetUserByIdAsync(twitchId);
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

        TwitchStream stream = await _client.GetStreamAsync();
        if (stream == null)
            Debug.Log("Failed to find stream");
        return stream;
    }

    public static async Task StartPoll(string title, List<string> choices, int durationSeconds)
    {
        try
        {
            await _client.CreatePollAsync(title, choices, durationSeconds);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to start poll '{title}'. \n{ex.Message}");
        }
    }

    /// <summary>The choices of the most recent poll, or null if there's none.</summary>
    public static async Task<List<TwitchPollChoice>> GetPollResults()
    {
        List<TwitchPoll> polls = await _client.GetPollsAsync(1);
        if (polls.Count <= 0)
        {
            Debug.LogError("Failed to get poll results");
            return null;
        }
        return polls[0].Choices;
    }

    public static async Task<TwitchPrediction> StartPrediction(PredictionObj predictionObj)
    {
        try
        {
            string title = predictionObj.Title.TruncateString(45);
            List<string> outcomes = predictionObj.GetOutcomes();
            Debug.Log($"Starting prediction request: {title} {string.Join(" / ", outcomes)} {predictionObj.PredictionWindowSec}");

            TwitchPrediction prediction = await _client.CreatePredictionAsync(title, outcomes, predictionObj.PredictionWindowSec);
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
        return _client.ResolvePredictionAsync(predictionID, winningOutcomeID);
    }

    public static Task CancelPrediction(string predictionID)
    {
        return _client.CancelPredictionAsync(predictionID);
    }

    public static async Task CancelAllPredictions()
    {
        if (!IsConnected)
            return;

        try
        {
            await _client.CancelOpenPredictionsAsync();
        }
        catch (Exception ex)
        {
            Debug.Log("Failed to cancel open predictions. \n" + ex.Message);
        }
    }
}
