using UnityEngine;
using System;
using System.IO;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Linq;
using LiveChat;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Debug = UnityEngine.Debug;

/// <summary>
/// Offline text to speech. Uses Piper (neural voices) when it's installed in StreamingAssets/Piper
/// (Tools/get-piper.ps1 downloads it), and otherwise the voices built into Windows. Either way each
/// line is rendered to a wav file in the user data folder and played through the game's own audio,
/// so it follows the game's output device and volume.
///
/// TtsAnnouncerVoice / TtsPlayerVoice in your config pick the voices: a Piper voice name (a model in
/// StreamingAssets/Piper/voices) or an installed Windows voice. With a multi-speaker Piper voice for
/// players, each player always gets the same voice of their own.
/// </summary>
public class MyTTS : MonoBehaviour
{
    public AudioSource audioSource;

    [SerializeField] private AudioSource _lowPitchAudioSource;
    [SerializeField] private AudioSource _regularPitchAudioSource;
    [SerializeField] private AudioSource _highPitchAudioSource;

    public static MyTTS inst;

    [SerializeField] private int max_TTS_string_length = 400;

    [SerializeField] private string textToSpeak;
    [SerializeField] private bool speakButton;
    [SerializeField] private AudioPitch audioPitch;

    private Queue<(AudioClip clip, float pitch)> audioQ = new Queue<(AudioClip clip, float pitch)>();

    public enum AudioPitch { Low, Reg, High };

    /// <summary>Announcements (subs, raids, new king) or a player's own words.</summary>
    public enum TtsVoice { Announcer, Player };

    private Dictionary<string, SubGifter> _giftedSubs = new Dictionary<string, SubGifter>();
    [SerializeField] private float _aggregateGiftsDuration = 2f;

    private const int AudioFileCount = 20;
    private const float MaxClipSeconds = 30f;
    private int _nextFileNumber;

    /// <summary>Lines waiting for their wav, by the wav's full path.</summary>
    private readonly Dictionary<string, (AudioPitch pitch, bool addToQ)> _pending = new Dictionary<string, (AudioPitch, bool)>(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<string> _finishedFiles = new ConcurrentQueue<string>();
    private readonly ConcurrentQueue<string> _engineMessages = new ConcurrentQueue<string>();

    private string PiperFolder => Path.Combine(Application.streamingAssetsPath, "Piper");
    private string PiperExe => Path.Combine(PiperFolder, "piper", "piper.exe");
    private readonly Dictionary<string, SpeechProcess> _piperVoices = new Dictionary<string, SpeechProcess>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _piperSpeakerCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    private SpeechProcess _windowsVoices;
    private string[] _installedWindowsVoices = new string[0];

    private class SpeechProcess
    {
        public Process Process;
        public StreamWriter Input;
        public bool Alive => Process != null && !Process.HasExited;
    }

    // Windows voices (System.Speech): reads JSON lines ({voice, rate, text, file}) and prints the file when done
    private const string WindowsSpeechScript = @"
$ProgressPreference = 'SilentlyContinue'
[Console]::InputEncoding = [Text.UTF8Encoding]::new($false)
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
Add-Type -AssemblyName System.Speech
$synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
$voices = @($synth.GetInstalledVoices() | Where-Object { $_.Enabled } | ForEach-Object { $_.VoiceInfo.Name })
[Console]::Out.WriteLine('VOICES ' + ($voices -join '|'))
[Console]::Out.Flush()
while ($null -ne ($line = [Console]::In.ReadLine())) {
    $request = $null
    try {
        $request = $line | ConvertFrom-Json
        if ($request.voice) { $synth.SelectVoice([string]$request.voice) }
        $synth.Rate = [int]$request.rate
        $synth.SetOutputToWaveFile([string]$request.file)
        $synth.Speak([string]$request.text)
        $synth.SetOutputToNull()
        [Console]::Out.WriteLine([string]$request.file)
    } catch {
        $synth.SetOutputToNull()
        [Console]::Out.WriteLine('FAIL ' + $_.Exception.Message)
    }
    [Console]::Out.Flush()
}
";

    public void Start()
    {
        inst = this;

        _lowPitchAudioSource.pitch = 0.5f;
        _regularPitchAudioSource.pitch = 1;
        _highPitchAudioSource.pitch = 1.5f;

        if (Application.platform != RuntimePlatform.WindowsPlayer && Application.platform != RuntimePlatform.WindowsEditor)
        {
            Debug.LogWarning("Text to speech is Windows-only for now, so it's off on this platform.");
            return;
        }

        //Start the configured voices now, so the first line isn't slow
        foreach (TtsVoice voice in new[] { TtsVoice.Announcer, TtsVoice.Player })
        {
            string setting = VoiceSetting(voice);
            if (PiperModelPath(setting) != null)
                GetPiperVoice(setting);
            else
                GetWindowsVoices();
        }
        Debug.Log($"Text to speech: announcer {DescribeVoice(TtsVoice.Announcer)}, players {DescribeVoice(TtsVoice.Player)}");
    }

    private void OnValidate()
    {
        if (speakButton)
        {
            speakButton = false;
            if (Application.isPlaying)
                SpeechMaster(textToSpeak, TtsVoice.Announcer, audioPitch, false);
        }
    }

    private void Update()
    {
        if(_giftedSubs.Count > 0)
        {
            string[] keys = _giftedSubs.Keys.ToArray();
            foreach(string key in keys)
            {
                SubGifter gifter = _giftedSubs[key];
                gifter.timer -= Time.deltaTime;
                _giftedSubs[key] = gifter;

                if(gifter.timer <= 0)
                {
                    _giftedSubs.Remove(key);
                    Announce($"{gifter.username} gifted {gifter.count} {gifter.tier} sub{((gifter.count > 1) ? "s" : "")}. What a bro.");
                }
            }
        }

        while (_engineMessages.TryDequeue(out string message))
            Debug.LogWarning("Text to speech: " + message);

        while (_finishedFiles.TryDequeue(out string file))
        {
            if (_pending.TryGetValue(file, out var request))
            {
                _pending.Remove(file);
                StartCoroutine(PlaySpeechFile(file, request.pitch, request.addToQ));
            }
        }

        if (audioQ.Count <= 0)
            return;

        if (audioSource.isPlaying)
            return;

        var tts = audioQ.Dequeue();
        audioSource.pitch = tts.pitch;
        audioSource.clip = tts.clip;
        audioSource.Play();
    }

    /// <summary>A player's own words (the king's chat). Off when enableKingTTS is off.</summary>
    public void PlayerSpeech(string textToSpeak, TtsVoice voice, string speakerKey = null)
    {
        if (!AppConfig.inst.GetB("enableKingTTS"))
            return;
        SpeechMaster(textToSpeak, voice, AudioPitch.Reg, addToQ:false, speakerKey);
    }

    public void Announce(string textToSpeak)
    {
        SpeechMaster(textToSpeak, TtsVoice.Announcer, AudioPitch.Reg, addToQ:true);
    }

    public void AggregateSubGift(string gifterUsername, int multiMonthDuration, LiveChatSubscriptionPlan tier)
    {
        SubGifter sg;
        if(!_giftedSubs.TryGetValue(gifterUsername, out sg))
            sg = new SubGifter() { username = gifterUsername, count = 0, multimonthduration = multiMonthDuration, tier = tier};

        sg.count++;
        sg.timer = _aggregateGiftsDuration;
        _giftedSubs[gifterUsername] = sg;

    }

    /// <param name="speakerKey">Who is talking (a Twitch ID), so a multi-speaker voice can give each player their own voice.</param>
    public void SpeechMaster(string textToSpeak, TtsVoice voice, AudioPitch pitch, bool addToQ, string speakerKey = null)
    {
        if (!AppConfig.inst.GetB("enableTTS") || string.IsNullOrWhiteSpace(textToSpeak))
            return;

        if(textToSpeak.Length > max_TTS_string_length)
        {
            CLDebug.Inst.Log($"String length {textToSpeak.Length} is greater than max allowed {max_TTS_string_length}. Skipping TTS in speech Master.");
            return;
        }

        string folder = Path.Combine(UserData.Folder, "tts");
        Directory.CreateDirectory(folder);
        string file = Path.GetFullPath(Path.Combine(folder, $"tts{_nextFileNumber++ % AudioFileCount}.wav"));

        string setting = VoiceSetting(voice);
        SpeechProcess piper = PiperModelPath(setting) != null ? GetPiperVoice(setting) : null;
        string request;
        SpeechProcess engine;
        if (piper != null)
        {
            engine = piper;
            JObject line = new JObject { ["text"] = textToSpeak, ["output_file"] = file };
            int speakers = _piperSpeakerCounts.TryGetValue(setting, out int count) ? count : 1;
            if (speakers > 1)
                line["speaker_id"] = string.IsNullOrEmpty(speakerKey) ? 0 : StableHash(speakerKey) % speakers;
            request = line.ToString(Formatting.None);
        }
        else
        {
            engine = GetWindowsVoices();
            request = JsonConvert.SerializeObject(new { voice = WindowsVoiceName(voice), rate = Rate, text = textToSpeak, file });
        }

        if (engine == null || !engine.Alive)
            return;

        _pending[file] = (pitch, addToQ);
        try
        {
            engine.Input.WriteLine(request);
        }
        catch (Exception e)
        {
            _pending.Remove(file);
            Debug.LogError($"Text to speech stopped: {e.Message}");
        }
    }

    private static int Rate => Mathf.Clamp(AppConfig.inst.GetI("TtsRate"), -10, 10);

    private static string VoiceSetting(TtsVoice voice) =>
        AppConfig.inst.GetS(voice == TtsVoice.Announcer ? "TtsAnnouncerVoice" : "TtsPlayerVoice").Trim();

    private string DescribeVoice(TtsVoice voice)
    {
        string setting = VoiceSetting(voice);
        if (PiperModelPath(setting) != null)
            return $"{setting} (Piper)";
        return $"{(string.IsNullOrEmpty(WindowsVoiceName(voice)) ? "the default voice" : WindowsVoiceName(voice))} (Windows)";
    }

    // FNV-1a: the same player gets the same speaker in every session
    private static int StableHash(string text)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (char c in text)
                hash = (hash ^ c) * 16777619;
            return (int)(hash & 0x7FFFFFFF);
        }
    }

    // ---- Piper ----

    private string PiperModelPath(string voiceName)
    {
        if (string.IsNullOrEmpty(voiceName) || !File.Exists(PiperExe))
            return null;
        string model = Path.Combine(PiperFolder, "voices", voiceName + ".onnx");
        return File.Exists(model) && File.Exists(model + ".json") ? model : null;
    }

    private SpeechProcess GetPiperVoice(string voiceName)
    {
        if (_piperVoices.TryGetValue(voiceName, out SpeechProcess existing) && existing.Alive)
            return existing;

        string model = PiperModelPath(voiceName);
        try
        {
            JObject voiceConfig = JObject.Parse(File.ReadAllText(model + ".json"));
            _piperSpeakerCounts[voiceName] = (int?)voiceConfig["num_speakers"] ?? 1;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Couldn't read {voiceName}'s voice settings: {e.Message}");
            _piperSpeakerCounts[voiceName] = 1;
        }

        //Faster speech is a shorter length scale
        float lengthScale = Mathf.Clamp(1f - Rate * 0.05f, 0.5f, 1.5f);
        SpeechProcess voice = StartSpeechProcess(PiperExe,
            $"--model \"{model}\" --json-input --quiet --length_scale {lengthScale.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            Path.GetDirectoryName(PiperExe));
        if (voice != null)
            _piperVoices[voiceName] = voice;
        return voice;
    }

    // ---- Windows voices ----

    private SpeechProcess GetWindowsVoices()
    {
        if (_windowsVoices != null && _windowsVoices.Alive)
            return _windowsVoices;

        string encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes(WindowsSpeechScript));
        _windowsVoices = StartSpeechProcess("powershell.exe",
            $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encodedScript}", null);
        return _windowsVoices;
    }

    /// <summary>The configured Windows voice if it's installed, otherwise the first voice announces and the second (if any) speaks for players.</summary>
    private string WindowsVoiceName(TtsVoice voice)
    {
        string configured = VoiceSetting(voice);
        if (!string.IsNullOrEmpty(configured) && _installedWindowsVoices.Any(v => string.Equals(v, configured, StringComparison.OrdinalIgnoreCase)))
            return configured;
        if (_installedWindowsVoices.Length == 0)
            return "";
        return voice == TtsVoice.Player && _installedWindowsVoices.Length > 1 ? _installedWindowsVoices[1] : _installedWindowsVoices[0];
    }

    // ---- Shared ----

    /// <summary>Starts an engine that reads one JSON request per line and prints each finished wav's path.</summary>
    private SpeechProcess StartSpeechProcess(string fileName, string arguments, string workingDirectory)
    {
        try
        {
            ProcessStartInfo startInfo = new ProcessStartInfo(fileName, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8
            };
            if (workingDirectory != null)
                startInfo.WorkingDirectory = workingDirectory;

            Process process = new Process { StartInfo = startInfo };
            process.OutputDataReceived += (sender, e) => OnEngineOutput(e.Data);
            //PowerShell reports progress on stderr as CLIXML; only pass on real errors
            process.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data) && !e.Data.StartsWith("#< CLIXML") && !e.Data.StartsWith("<Objs"))
                    _engineMessages.Enqueue(e.Data);
            };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            return new SpeechProcess
            {
                Process = process,
                Input = new StreamWriter(process.StandardInput.BaseStream, new UTF8Encoding(false)) { AutoFlush = true }
            };
        }
        catch (Exception e)
        {
            Debug.LogError($"Couldn't start text to speech ({Path.GetFileName(fileName)}): {e.Message}");
            return null;
        }
    }

    //Runs on the process's output thread
    private void OnEngineOutput(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;
        if (line.StartsWith("VOICES "))
            _installedWindowsVoices = line.Substring("VOICES ".Length).Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
        else if (line.StartsWith("FAIL "))
            _engineMessages.Enqueue(line.Substring(5));
        else
        {
            try
            {
                _finishedFiles.Enqueue(Path.GetFullPath(line.Trim()));
            }
            catch (Exception)
            {
                _engineMessages.Enqueue(line);
            }
        }
    }

    private IEnumerator PlaySpeechFile(string file, AudioPitch pitch, bool addToQ)
    {
        using UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(new Uri(file).AbsoluteUri, AudioType.WAV);
        yield return www.SendWebRequest();

        if (www.result != UnityWebRequest.Result.Success)
        {
            Debug.Log($"Couldn't load text to speech audio: {www.error}");
            yield break;
        }

        AudioClip clip = DownloadHandlerAudioClip.GetContent(www);
        if (clip.length > MaxClipSeconds)
        {
            CLDebug.Inst.Log("Text to speech clip over 30 seconds long. Not playing it to avoid annoyance. Length: " + clip.length);
            yield break;
        }

        float pitchVal = 1;
        if (pitch == AudioPitch.Low)
            pitchVal = 0.5f;
        else if (pitch == AudioPitch.High)
            pitchVal = 1.5f;

        if (addToQ)
            audioQ.Enqueue((clip, pitchVal));
        else if (pitch == AudioPitch.Low)
            _lowPitchAudioSource.PlayOneShot(clip);
        else if(pitch == AudioPitch.Reg)
            _regularPitchAudioSource.PlayOneShot(clip);
        else
            _highPitchAudioSource.PlayOneShot(clip);
    }

    private void OnDestroy() => StopEngines();

    private void OnApplicationQuit() => StopEngines();

    private void StopEngines()
    {
        foreach (SpeechProcess engine in _piperVoices.Values.Append(_windowsVoices))
        {
            if (engine == null)
                continue;
            try
            {
                engine.Input?.Close();
                if (engine.Alive && !engine.Process.WaitForExit(1000))
                    engine.Process.Kill();
            }
            catch (Exception)
            {
            }
        }
        _piperVoices.Clear();
        _windowsVoices = null;
    }

}

public struct SubGifter
{
    public float timer;
    public string username;
    public int count;
    public int multimonthduration;
    public LiveChatSubscriptionPlan tier;
}
