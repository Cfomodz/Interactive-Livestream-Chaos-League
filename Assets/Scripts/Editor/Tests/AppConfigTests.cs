using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

/// <summary>Your config holds your personal details plus whatever differs from config.sample.json, and survives a reload.</summary>
public class AppConfigTests
{
    private string _folder;

    [SetUp]
    public void SetUp()
    {
        _folder = Path.Combine(Path.GetTempPath(), "ChaosLeagueConfigTests_" + System.Guid.NewGuid().ToString("N"));
        UserData.FolderOverride = _folder;
    }

    [TearDown]
    public void TearDown()
    {
        UserData.FolderOverride = null;
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, true);
    }

    private static JObject ReadUserConfig() => JObject.Parse(File.ReadAllText(UserData.ConfigPath));

    [Test]
    public void FirstRunCreatesConfigWithOnlyPersonalKeys()
    {
        AppConfig.Load();

        JObject values = (JObject)ReadUserConfig()["values"];
        Assert.AreEqual("", (string)values["TwitchChannel"]);
        Assert.IsTrue(values.ContainsKey("TwitchClientId"));
        Assert.IsFalse(values.ContainsKey("recruitPointReward"), "Unchanged defaults stay out of your config");
        Assert.AreEqual(0, ((JObject)ReadUserConfig()["volumes"]).Count);
    }

    [Test]
    public void ChangedValuesAndVolumesSurviveAReload()
    {
        AppConfig.Load();
        AppConfig.inst.SetV("TwitchChannel", "mychannel");
        AppConfig.inst.SetV("recruitPointReward", 3000L);
        AppConfig.inst.volumes["Music"] = 0.5f;
        AppConfig.SaveUserConfig();

        AppConfig.Load();

        Assert.AreEqual("mychannel", AppConfig.inst.GetS("TwitchChannel"));
        Assert.AreEqual(3000, AppConfig.inst.GetI("recruitPointReward"));
        Assert.AreEqual(0.5f, AppConfig.inst.volumes["Music"], 0.0001f);
    }

    [Test]
    public void SettingBackToTheDefaultDropsItFromYourConfig()
    {
        AppConfig.Load();
        int defaultReward = AppConfig.inst.GetI("recruitPointReward");
        AppConfig.inst.SetV("recruitPointReward", 3000L);
        AppConfig.SaveUserConfig();
        Assert.IsTrue(((JObject)ReadUserConfig()["values"]).ContainsKey("recruitPointReward"));

        AppConfig.inst.SetV("recruitPointReward", (long)defaultReward);
        AppConfig.SaveUserConfig();

        Assert.IsFalse(((JObject)ReadUserConfig()["values"]).ContainsKey("recruitPointReward"));
    }

    [Test]
    public void ChangingASettingMarksTheConfigToSave()
    {
        AppConfig.Load();
        Assert.IsFalse(AppConfig.IsDirty);

        AppConfig.inst.SetV("recruitPointReward", 3000L);

        Assert.IsTrue(AppConfig.IsDirty);
    }

    [Test]
    public void UnknownKeysInYourConfigAreKept()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(UserData.ConfigPath, "{ \"values\": { \"SomeOldSetting\": \"keep me\" }, \"volumes\": {} }");

        AppConfig.Load();
        AppConfig.SaveUserConfig();

        Assert.AreEqual("keep me", (string)ReadUserConfig()["values"]["SomeOldSetting"]);
    }
}
