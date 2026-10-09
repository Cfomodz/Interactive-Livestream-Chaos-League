using LiveChat.Twitch;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

public class GameManager : MonoBehaviour
{
    [SerializeField] private TwitchClient _twitchClient;
    [SerializeField] private TwitchApi _twitchAPI;
    [SerializeField] private TwitchPubSub _twitchPubSub;
    [SerializeField] private GoldDistributor _liveViewCount;
    [SerializeField] private InvitePromo _invitePromo;

    [SerializeField] private GameObject _playerBallPrefab;
    [SerializeField] private CLDebug _clDebug;
    [SerializeField] private TileController _tileController;
    [SerializeField] private MyHttpClient _myHttpClient;
    [SerializeField] private KingController _kingController;
    [SerializeField] private RebellionController _rebellionController;
    [SerializeField] private SQLiteServiceAsync _sqliteServiceAsync; 

    [SerializeField] public Texture DefaultPFP;
    [SerializeField] private GameObject _pbHologramPrefab; 

    public Dictionary<string, PlayerHandler> PlayerHandlers = new Dictionary<string, PlayerHandler>();

    public HoldingPen HoldingPen;
    public Transform PlayerHandlersRoot;
    public Transform PlayerHandlersPoolRoot;
    public Transform PlayerBallsPoolRoot;

    private float _resourceUnloadTimer = 0;
    private float _secondsBetweenResourceUnloads = 60;

    public ObjectPool<PlayerHandler> PlayerHandlersPool;
    public ObjectPool<PlayerBall> PlayerBallsPool;

    public float BubbleFillHoldAlpha;
    public float BubbleTextHoldAlpha;

    [SerializeField] public Vector3 UsernameOffset;
    [SerializeField] public Vector3 PointsTextOffset;

    private StringBuilder _sb = new StringBuilder();

    [HideInInspector] public Sprite CommunityPointSprite;

    /// <summary>When this run of the game started, to tell players active this session from ones loaded from earlier streams.</summary>
    public DateTime SessionStartTime { get; private set; }

    void Awake()
    {
        Application.targetFrameRate = 60;
        SessionStartTime = DateTime.Now;

        Debug.Log("Unity Main Thread: " + Thread.CurrentThread.ManagedThreadId);

        _clDebug.Initialize();

        LoadAppConfig();

        PlayerHandlersPool = new ObjectPool<PlayerHandler>(PlayerHandlerFactory, TurnOnPlayerHandler, TurnOffPlayerHandler);
        PlayerBallsPool = new ObjectPool<PlayerBall>(PlayerBallFactory, TurnOnPlayerBall, TurnOffPlayerBall);

    }


    public void Update()
    {

        _resourceUnloadTimer += Time.deltaTime;
        if (_resourceUnloadTimer > _secondsBetweenResourceUnloads)
        {
            CLDebug.Inst.Log("Unloading unused assets");
            _resourceUnloadTimer = 0;
            Resources.UnloadUnusedAssets();
        }

        //Save settings changes soon after they're made, so closing the window doesn't lose them
        if (AppConfig.IsDirty && Time.unscaledTime - _lastConfigSaveTime > ConfigSaveDelaySeconds)
        {
            _lastConfigSaveTime = Time.unscaledTime;
            AppConfig.SaveUserConfig();
        }
    }

    private const float ConfigSaveDelaySeconds = 2f;
    private float _lastConfigSaveTime;

    private void OnApplicationQuit()
    {
        if (AppConfig.IsDirty)
            AppConfig.SaveUserConfig();
    }

    public IEnumerator CreateNewPlayerHandler(string twitchId)
    {
        PlayerHandler ph = PlayerHandlersPool.GetObject();
        ph.gameObject.name = twitchId;
        ph.gameObject.transform.SetParent(PlayerHandlersRoot);
        PlayerHandlers.Add(twitchId, ph);
        yield return ph.CInitPlayerHandler(this, twitchId);

    }

    public IEnumerator GetPlayerHandler(string twitchID, CoroutineResult<PlayerHandler> phResult)
    {
        //Debug.Log("at the top of get player handler");
        PlayerHandler ph = null; 
        PlayerHandlers.TryGetValue(twitchID, out ph);

        //If we were going to unload it this frame, cancel it
        if (ph != null)
            ph.LastAccess = DateTime.Now;

        //This ensures we don't create a duplicate player handler if it isn't done with the first one yet
        while (ph != null && ph.Initializing)
            yield return null; 

        //If the ph doesn't exist yet, create one
        if(ph == null)
        {
            yield return CreateNewPlayerHandler(twitchID);
            PlayerHandlers.TryGetValue(twitchID, out ph);
        }

        //Assert that we suceeded in creating a player handler
        if(ph == null)
        {
            Debug.LogError($"Failed to get player handler with id: {twitchID}");
            phResult.Complete(null); 
            yield break;
        }

        ph.LastAccess = DateTime.Now; 
        ph.SetCustomizationsFromPP();

        phResult.Complete(ph);
    }


    public IEnumerator GetPlayerByUsername(string twitchUsername, CoroutineResult<PlayerHandler> coResult)
    {
        if (string.IsNullOrEmpty(twitchUsername))
        {
            Debug.Log($"Failed to get player by username: {twitchUsername}");
            coResult.Complete(null);
            yield break;
        }

        var ph = PlayerHandlers.Values.FirstOrDefault(ph => string.Equals(ph.pp.TwitchUsername, twitchUsername, StringComparison.OrdinalIgnoreCase));

        if (ph != null)
        {
            coResult.Complete(ph);
            yield break;
        }

        var t = TwitchApi.GetUserByUsername(twitchUsername);
        yield return new WaitUntil(() => t.IsCompleted);

        TwitchUser user = t.Result;

        if(user == null)
        {
            Debug.Log($"Failed to get player by username: {twitchUsername}");
            coResult.Complete(null);
            yield break;
        }

        yield return GetPlayerHandler(user.Id, coResult);
        coResult.Complete(coResult.Result);
    }

    public void DestroyPlayerBall(PlayerBall pb)
    {
        pb.Ph.SetState(PlayerHandlerState.Idle);

        PlayerHandler ph = pb.Ph;
        ph.ReceivableTarget = null;

        GameTile gameplayTile = _tileController.GameplayTile;

        //If they're on the conveyor belt or in gameplay, eliminate them
        if(gameplayTile != null && (gameplayTile.ConveyorBelt.Contains(ph) || gameplayTile.AlivePlayers.Contains(ph)))
            gameplayTile.EliminatePlayer(pb.Ph, true);

        pb.Ph.pb = null;
        pb.gameObject.name = "pooledPB";
        pb.transform.SetParent(PlayerBallsPoolRoot);

        PlayerBallsPool.ReturnObject(pb);

        //After they're eliminated, if they bid while in gameplay, automatically enter the bidding Q for the next tile
        if (ph.GetBid() > 0)
        {
            _tileController.BidHandler.TryAddToBiddingQ(ph);
            return;
        }
    }

    /// <summary>
    /// Sizes the window from WindowWidth / WindowHeight / WindowMode in your config, every launch. Unity
    /// otherwise reuses whatever size the window last had (or a -screen-width from the command line).
    /// </summary>
    private void ApplyWindowSettings()
    {
#if !UNITY_EDITOR
        int width = AppConfig.inst.GetI("WindowWidth");
        int height = AppConfig.inst.GetI("WindowHeight");
        if (width <= 0 || height <= 0)
            return;

        FullScreenMode mode;
        switch (AppConfig.inst.GetS("WindowMode").Trim().ToLowerInvariant())
        {
            case "borderless": mode = FullScreenMode.FullScreenWindow; break;
            case "fullscreen": mode = FullScreenMode.ExclusiveFullScreen; break;
            default: mode = FullScreenMode.Windowed; break;
        }
        Screen.SetResolution(width, height, mode);
        Debug.Log($"Window: {width}x{height} {mode}");
#endif
    }

    private void LoadAppConfig()
    {
        AppConfig.Load();
        Debug.Log($"Your settings and player data are in {UserData.Folder}");
        ApplyWindowSettings();

        //QuipBattlePrompts.LoadPrompts(promptsJSON);

        string communityPointSprite = Path.Combine(Application.streamingAssetsPath, "communityPointSprite.png");

        // Check if custom sprite exists
        if (File.Exists(communityPointSprite))
        {
            // Load the custom sprite
            byte[] fileData = File.ReadAllBytes(communityPointSprite);
            Texture2D texture = new Texture2D(1, 1, TextureFormat.Alpha8, false); // Create new "empty" texture
            //texture.filterMode = FilterMode.Bilinear;
            //texture.anisoLevel = 0; 
            texture.LoadImage(fileData); // Load the image data into the texture (this will also resize the texture to the correct dimensions)
            texture.wrapMode = TextureWrapMode.Clamp;
            //texture.alphaIsTransparency = true;

            // Create a sprite from the texture
            CommunityPointSprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
        }
        else
        {
            Debug.LogError("Failed to find community point sprite in streaming assets folder (communityPointSprite.png)"); 
        }
    }


    public PlayerBall CreatePlayerBall(PlayerHandler ph)
    {
        PlayerBall pb = PlayerBallsPool.GetObject();
        pb.gameObject.name = ph.pp.TwitchUsername;
        ph.pb = pb;
        pb.transform.SetParent(ph.transform);

        pb.InitPB(this, ph);

        //Move the text off the screen the first frame so it doesn't pop in
        if (pb._usernameText != null)
            pb._usernameText.transform.position = HoldingPen.GetReceivePosition();
        if (pb._pointsText != null)
            pb._pointsText.transform.position = HoldingPen.GetReceivePosition();

        return pb; 
    }

    private void TurnOnPlayerHandler(PlayerHandler ph)
    {
        ph.gameObject.SetActive(true);
    }
    private void TurnOffPlayerHandler(PlayerHandler ph)
    {
        ph.gameObject.SetActive(false);
    }

    private PlayerHandler PlayerHandlerFactory()
    {
        GameObject playerHandler = new GameObject();
        PlayerHandler ph = playerHandler.AddComponent<PlayerHandler>();
        ph.pbh = Instantiate(_pbHologramPrefab, ph.transform).GetComponent<PBHologram>();
        ph.pbh.InitPBHologram(ph);
        ph.DisableHologram();
        ph.pbh.transform.position = HoldingPen.transform.position; //Have to do this here because the ph isn't instantiated yet so it can't find the holding pen 

        return ph;
    }

    private void TurnOnPlayerBall(PlayerBall pb)
    {
        pb.gameObject.SetActive(true);
        pb._rb2D.transform.position = HoldingPen.Get_TI_IO_Position();
    }

    private void TurnOffPlayerBall(PlayerBall pb)
    {
        pb.gameObject.SetActive(false);
        pb._rb2D.transform.position = HoldingPen.Get_TI_IO_Position();
    }

    private PlayerBall PlayerBallFactory()
    {
        GameObject newObj = Instantiate(_playerBallPrefab);
        newObj.SetActive(false);
        PlayerBall pb = newObj.GetComponent<PlayerBall>();

        return pb;
    }

    public void SaveAndQuitButtonClick()
    {
        StartCoroutine(SaveAndQuit());
    }


    public TwitchClient TwitchClient => _twitchClient;

    /// <summary>
    /// A player names who invited them (!invitedby @name in chat). The first claim sticks and can't be
    /// changed. Players can't invite themselves.
    /// </summary>
    public IEnumerator HandleInviteClaim(string messageId, PlayerHandler invitedPh, string inviterUsername)
    {
        string invitedName = invitedPh.pp.TwitchUsername;
        inviterUsername = inviterUsername?.Trim().TrimStart('@');

        if (!string.IsNullOrEmpty(invitedPh.pp.InvitedByID))
        {
            yield return invitedPh.ReplyAlreadyInvited(messageId, _twitchClient);
            yield break;
        }

        if (string.IsNullOrEmpty(inviterUsername))
        {
            _twitchClient.ReplyToPlayer(messageId, invitedName, "Who invited you? Correct format is: !invitedby @username");
            yield break;
        }

        CoroutineResult<PlayerHandler> coResult = new CoroutineResult<PlayerHandler>();
        yield return GetPlayerByUsername(inviterUsername, coResult);
        PlayerHandler inviterPh = coResult.Result;

        if (inviterPh == null)
        {
            _twitchClient.ReplyToPlayer(messageId, invitedName, $"Couldn't find a Twitch user named @{inviterUsername}.");
            yield break;
        }

        //Inviting yourself is the only thing not allowed. Loops (A invited B, B invited A) are fine:
        //the gold share shrinks by 75% each hop and stops below 1
        if (inviterPh.pp.TwitchID == invitedPh.pp.TwitchID)
        {
            _twitchClient.ReplyToPlayer(messageId, invitedName, "You can't invite yourself.");
            yield break;
        }

        yield return invitedPh.SetInvitor(inviterPh, _twitchClient, _invitePromo, messageId);
    }

    public KingController GetKingController()
    {
        return _kingController; 
    }

    public TileController GetTileController()
    {
        return _tileController;
    }
    public RebellionController GetRebellionController() 
    {
        return _rebellionController; 
    }


    private IEnumerator SaveAndQuit()
    {
        yield return SaveAllPlayerProfilesToDB(); 

        AppConfig.SaveUserConfig();

        SQLiteServiceAsync.CloseConnection(); 

#if !UNITY_EDITOR
        SavePreviousLog(); 
        Application.Quit();
#endif

#if UNITY_EDITOR
        EditorApplication.isPlaying = false;
#endif
        yield return new WaitForSeconds(1f); //Wait for SQLiteService to close connections
    }

    public IEnumerator SaveAllPlayerProfilesToDB()
    {
        //Save all player profiles to db
        string[] keys = PlayerHandlers.Keys.ToArray();
        foreach (string key in keys)
        {
            PlayerHandlers.TryGetValue(key, out PlayerHandler ph);
            if (ph == null)
                continue;

            var t = Task.Run(async () => await SQLiteServiceAsync.UpdatePlayer(ph.pp));
            yield return new WaitUntil(() => t.IsCompleted);
        }
    }

    public void ClearAllInvitesData()
    {
        string[] keys = PlayerHandlers.Keys.ToArray();
        foreach (string key in keys)
        {
            PlayerHandlers[key].pp.InvitedByID = "";
            PlayerHandlers[key].pp.InvitesJSON = "";
        }

        _sqliteServiceAsync.ClearInvitesData(); 
    }

    private void SavePreviousLog()
    {
        // Get the path to the Player.log file
        string logFilePath = Path.Combine(Application.persistentDataPath, "Player.log");

        // Generate a new file name with a timestamp
        string newFileName = "PlayerLog_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".log";

        string oldLogsFolderPath = Path.Combine(Application.persistentDataPath, "OldLogs");
        if (!Directory.Exists(oldLogsFolderPath))
        {
            Directory.CreateDirectory(oldLogsFolderPath);
        }

        // Generate the full path for the new log file
        string newFilePath = Path.Combine(oldLogsFolderPath, newFileName);

        try
        {
            // Check if the Player.log file exists
            if (File.Exists(logFilePath))
            {
                // Copy the Player.log file to the new file name
                File.Copy(logFilePath, newFilePath);

                CLDebug.Inst.Log($"Player.log copied to: {newFilePath}");
            }
            else
            {
                Debug.LogWarning("Player.log file not found.");
            }
        }
        catch (IOException ex)
        {
            Debug.LogError($"Error copying the log file: {ex.Message}");
        }
    }


}
