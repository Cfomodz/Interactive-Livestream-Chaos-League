using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Serialization;

/// <summary>
/// Plays the music files in the music folder (StreamingAssets/Music unless MusicFolder is set in
/// config): shuffled in the background, and the king can pick a song with !song or skip with
/// !skipsong. Only put music in the folder that you're licensed to play on stream.
/// </summary>
public class MusicDJ : MonoBehaviour
{
    private static readonly string[] SupportedExtensions = { ".mp3", ".ogg", ".wav" };
    private const float DefaultVolume = 0.2f;

    [SerializeField] private TwitchClient _twitchClient;
    [FormerlySerializedAs("_spotifyConnectionStatus")]
    [SerializeField] private TextMeshProUGUI _statusText;
    [SerializeField] private TextMeshPro _songDisplayText;

    private AudioSource _audioSource;
    private readonly List<string> _tracks = new List<string>();
    private readonly List<string> _upNext = new List<string>();
    private string _currentTrack;
    private int _loadVersion;
    private bool _loading;
    private Coroutine _animateTextCoroutine;

    public string NowPlaying => _currentTrack == null ? "nothing" : TrackName(_currentTrack);

    public string MusicFolder
    {
        get
        {
            string configured = AppConfig.inst.GetS("MusicFolder");
            if (string.IsNullOrWhiteSpace(configured))
                return Path.Combine(Application.streamingAssetsPath, "Music");
            return Path.IsPathRooted(configured) ? configured : Path.Combine(Application.streamingAssetsPath, configured);
        }
    }

    private void Start()
    {
        // A sibling of the sound effects, so the settings overlay gives it a "Music" volume slider
        AudioSource sfx = AudioController.inst != null ? AudioController.inst.CollectGold : null;
        GameObject musicObject = new GameObject("Music");
        musicObject.transform.SetParent(sfx != null ? sfx.transform.parent : transform, false);
        _audioSource = musicObject.AddComponent<AudioSource>();
        _audioSource.playOnAwake = false;
        _audioSource.loop = false;
        _audioSource.spatialBlend = 0;
        if (sfx != null)
            _audioSource.outputAudioMixerGroup = sfx.outputAudioMixerGroup;
        _audioSource.volume = AppConfig.inst.volumes.TryGetValue("Music", out float volume) ? volume : DefaultVolume;

        _songDisplayText.color = _songDisplayText.color.WithAlpha(0);
        RescanMusicFolder();
    }

    private void Update()
    {
        //When a song ends, play the next one
        if (_currentTrack != null && !_loading && !_audioSource.isPlaying)
            PlayNext();
    }

    /// <summary>Reloads the list of songs from the music folder. Wired to the settings menu button.</summary>
    public void RescanMusicFolder()
    {
        string folder = MusicFolder;
        _tracks.Clear();
        _upNext.Clear();
        if (Directory.Exists(folder))
        {
            _tracks.AddRange(Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Where(file => SupportedExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
                .OrderBy(file => file, StringComparer.OrdinalIgnoreCase));
        }

        //The on-screen status is one short line; the folder goes to the log
        Debug.Log($"Music folder: {folder}");
        SetStatus(_tracks.Count == 0 ? "No music found (mp3, ogg or wav)" : $"Music: {_tracks.Count} songs");

        if (_tracks.Count > 0 && _currentTrack == null)
            PlayNext();
    }

    /// <summary>The king asked for a song by (part of) its name.</summary>
    public void PlayRequest(string messageId, string query, PlayerHandler ph)
    {
        if (_tracks.Count == 0)
        {
            _twitchClient.ReplyToPlayer(messageId, ph.pp.TwitchUsername, "There's no music set up on this stream.");
            return;
        }

        string track = FindTrack(query);
        if (track == null)
        {
            _twitchClient.ReplyToPlayer(messageId, ph.pp.TwitchUsername, "Couldn't find that song. Type !playlist to see what's available.");
            return;
        }

        Play(track);
        _twitchClient.ReplyToPlayer(messageId, ph.pp.TwitchUsername, $"As ruler of the throne, you changed the song to {TrackName(track)}");
    }

    public void SkipSong()
    {
        if (_tracks.Count > 0)
            PlayNext();
    }

    /// <summary>The song list for chat, trimmed to fit in one message.</summary>
    public string GetPlaylistText(int maxLength = 450)
    {
        if (_tracks.Count == 0)
            return "There's no music set up on this stream.";

        StringBuilder sb = new StringBuilder($"Songs ({_tracks.Count}): ");
        for (int i = 0; i < _tracks.Count; i++)
        {
            string name = TrackName(_tracks[i]);
            string more = $" ...and {_tracks.Count - i} more";
            if (sb.Length + name.Length + 2 + more.Length > maxLength)
            {
                sb.Append(more);
                break;
            }
            if (i > 0)
                sb.Append(", ");
            sb.Append(name);
        }
        return sb.ToString();
    }

    /// <summary>The song whose name contains every word of the query; the shortest name wins, so exact matches come first.</summary>
    private string FindTrack(string query)
    {
        string[] words = Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return null;

        return _tracks
            .Select(track => (track, name: Normalize(TrackName(track))))
            .Where(t => words.All(word => t.name.Contains(word)))
            .OrderBy(t => t.name.Length)
            .Select(t => t.track)
            .FirstOrDefault();
    }

    private static string Normalize(string text)
    {
        StringBuilder sb = new StringBuilder();
        foreach (char c in (text ?? "").ToLowerInvariant())
            sb.Append(char.IsLetterOrDigit(c) ? c : ' ');
        return sb.ToString();
    }

    private static string TrackName(string path) => Path.GetFileNameWithoutExtension(path).Replace('_', ' ').Trim();

    private void PlayNext()
    {
        if (_tracks.Count == 0)
        {
            _currentTrack = null;
            return;
        }

        //Shuffle through every song before repeating, and don't play the same one twice in a row
        if (_upNext.Count == 0)
        {
            _upNext.AddRange(_tracks.OrderBy(_ => UnityEngine.Random.value));
            if (_upNext.Count > 1 && _upNext[0] == _currentTrack)
            {
                _upNext.RemoveAt(0);
                _upNext.Add(_currentTrack);
            }
        }

        string next = _upNext[0];
        _upNext.RemoveAt(0);
        Play(next);
    }

    private void Play(string track)
    {
        _currentTrack = track;
        _upNext.Remove(track);
        StartCoroutine(LoadAndPlay(track, ++_loadVersion));
    }

    private IEnumerator LoadAndPlay(string track, int version)
    {
        _loading = true;
        using UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(new Uri(track).AbsoluteUri, AudioTypeFor(track));
        ((DownloadHandlerAudioClip)request.downloadHandler).streamAudio = true;
        yield return request.SendWebRequest();

        //A newer song was asked for while this one loaded
        if (version != _loadVersion)
            yield break;
        _loading = false;

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"Couldn't load song {track}: {request.error}");
            _tracks.Remove(track);
            yield break; //Update moves on to the next song
        }

        AudioClip previous = _audioSource.clip;
        _audioSource.clip = DownloadHandlerAudioClip.GetContent(request);
        _audioSource.Play();
        if (previous != null)
            Destroy(previous);
        Debug.Log($"Now playing: {TrackName(track)}");

        _songDisplayText.SetText($"!song: {TrackName(track)}");
        if (_animateTextCoroutine != null)
            StopCoroutine(_animateTextCoroutine);
        _animateTextCoroutine = StartCoroutine(DisplayNewSongTextAnimation(1, 5, 5));
    }

    private static AudioType AudioTypeFor(string path)
    {
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".mp3": return AudioType.MPEG;
            case ".ogg": return AudioType.OGGVORBIS;
            case ".wav": return AudioType.WAV;
            default: return AudioType.UNKNOWN;
        }
    }

    private void SetStatus(string text)
    {
        Debug.Log(text);
        if (_statusText != null)
            _statusText.SetText(text);
    }

    IEnumerator DisplayNewSongTextAnimation(float spinDuration, float holdDuration, float fadeDuration)
    {
        _songDisplayText.color = _songDisplayText.color.WithAlpha(1);

        float timer = 0;
        while (timer < spinDuration)
        {
            float t = timer / spinDuration;
            float rot = Mathf.Lerp(0, 360, t);
            _songDisplayText.transform.eulerAngles = new Vector3(rot, 0, 0);
            timer += Time.deltaTime;

            yield return null;
        }

        yield return new WaitForSeconds(holdDuration);

        timer = 0;
        while(timer < fadeDuration)
        {
            Color col = _songDisplayText.color;
            _songDisplayText.color = col.WithAlpha(Mathf.Lerp(1, 0.05f, timer / fadeDuration));

            timer += Time.deltaTime;
            yield return null;
        }

    }
}
