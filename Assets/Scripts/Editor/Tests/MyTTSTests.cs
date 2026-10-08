using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Drives the real speech engines: a line goes in, a wav comes out and is reported finished. Piper
/// cases are skipped when Tools/get-piper.ps1 hasn't been run.
/// </summary>
public class MyTTSTests
{
    private string _folder;
    private GameObject _gameObject;
    private MyTTS _tts;

    [SetUp]
    public void SetUp()
    {
        _folder = Path.Combine(Path.GetTempPath(), "ChaosLeagueTtsTests_" + System.Guid.NewGuid().ToString("N"));
        UserData.FolderOverride = _folder;
        AppConfig.Load();

        _gameObject = new GameObject("MyTTS");
        _tts = _gameObject.AddComponent<MyTTS>();
        _tts.audioSource = _gameObject.AddComponent<AudioSource>();
        foreach (string field in new[] { "_lowPitchAudioSource", "_regularPitchAudioSource", "_highPitchAudioSource" })
            typeof(MyTTS).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(_tts, _gameObject.AddComponent<AudioSource>());
    }

    [TearDown]
    public void TearDown()
    {
        typeof(MyTTS).GetMethod("StopEngines", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(_tts, null);
        Object.DestroyImmediate(_gameObject);
        UserData.FolderOverride = null;
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, true);
    }

    private static bool PiperInstalled =>
        File.Exists(Path.Combine(Application.streamingAssetsPath, "Piper", "piper", "piper.exe"));

    /// <summary>Waits for the engine to report the line's wav, then checks the wav is real audio.</summary>
    private IEnumerator SpeakAndWait(MyTTS.TtsVoice voice, string speakerKey)
    {
        var finished = (ConcurrentQueue<string>)typeof(MyTTS).GetField("_finishedFiles", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(_tts);
        var pending = (IDictionary)typeof(MyTTS).GetField("_pending", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(_tts);

        _tts.SpeechMaster("Throne captured by Zoë the Destroyer", voice, MyTTS.AudioPitch.Reg, true, speakerKey);
        Assert.AreEqual(1, pending.Count, "The line was sent to an engine");
        string expected = null;
        foreach (object key in pending.Keys)
            expected = (string)key;

        string done = null;
        double deadline = UnityEditor.EditorApplication.timeSinceStartup + 60;
        while (done == null && UnityEditor.EditorApplication.timeSinceStartup < deadline)
        {
            finished.TryDequeue(out done);
            yield return null;
        }

        Assert.AreEqual(expected, done, "The engine reported the same wav the line was sent for");
        Assert.Greater(new FileInfo(done).Length, 10_000, "The wav holds audio");
    }

    [UnityTest]
    public IEnumerator PiperAnnouncerSpeaks()
    {
        if (!PiperInstalled)
            Assert.Ignore("Piper isn't installed; run Tools/get-piper.ps1");
        _tts.Start();
        yield return SpeakAndWait(MyTTS.TtsVoice.Announcer, null);
    }

    [UnityTest]
    public IEnumerator PiperPlayerVoiceSpeaksAsThatPlayer()
    {
        if (!PiperInstalled)
            Assert.Ignore("Piper isn't installed; run Tools/get-piper.ps1");
        _tts.Start();
        yield return SpeakAndWait(MyTTS.TtsVoice.Player, "12345");
    }

    [UnityTest]
    public IEnumerator WindowsVoicesSpeakWhenTheVoiceIsntAPiperVoice()
    {
        AppConfig.inst.SetV("TtsAnnouncerVoice", "not-a-piper-voice");
        _tts.Start();
        yield return SpeakAndWait(MyTTS.TtsVoice.Announcer, null);
    }

    [Test]
    public void TurningTtsOffSendsNothing()
    {
        AppConfig.inst.SetV("enableTTS", false);
        _tts.Start();
        _tts.SpeechMaster("hello", MyTTS.TtsVoice.Announcer, MyTTS.AudioPitch.Reg, true);
        var pending = (IDictionary)typeof(MyTTS).GetField("_pending", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(_tts);
        Assert.AreEqual(0, pending.Count);
    }
}
