using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using Unity.VisualScripting;
using UnityEngine;

//                     0        1       2       3         4           5        6
public enum UIMenu { Connect, Audio, Extras, GamePlay, Networking, Youtube, Advanced }


public class ConfigItem : INotifyPropertyChanged
{
    public UIMenu Menu { get; set; }

    private object _value { get; set; }  //Used to check for changes listeners


    public object Value 
        {
        get => _value;
        set
        {
            if (_value != value)
            {
                _value = value;
                OnPropertyChanged();
            }
        }
    }
    
    public string UIDisplayText { get; set; }

    /// <summary>Your own details (channel, links, keys): always written to your config so they're easy to find and fill in.</summary>
    public bool Personal { get; set; }


    public event PropertyChangedEventHandler PropertyChanged;

    protected void OnPropertyChanged()
    {
        PropertyChanged?.Invoke(this.Value, new PropertyChangedEventArgs("Value"));
    }
    
}



[System.Serializable]
public class AppConfig
{
    public Dictionary<string, ConfigItem> configData;

    public Dictionary<string, float> volumes;

    /// <summary>Emote id -> index in the dynamic emote sprite sheet. Kept in its own file, saved with the sheet.</summary>
    [JsonIgnore]
    public Dictionary<string, int> downloadedEmoteIndexMap = new Dictionary<string, int>();

    private const string SampleFileName = "config.sample.json";
    private const string EmoteMapSeedFileName = "emote_index_map.json";

    private static Dictionary<string, JToken> _defaultValues = new Dictionary<string, JToken>();
    private static Dictionary<string, float> _defaultVolumes = new Dictionary<string, float>();
    /// <summary>Values in your config for keys the sample doesn't have (kept so switching versions doesn't lose them).</summary>
    private static Dictionary<string, object> _unknownValues = new Dictionary<string, object>();

    /// <summary>A setting changed since your config was last saved.</summary>
    public static bool IsDirty { get; private set; }
    public static void MarkDirty() => IsDirty = true;

    private class UserConfigFile
    {
        public Dictionary<string, object> values = new Dictionary<string, object>();
        public Dictionary<string, float> volumes = new Dictionary<string, float>();
    }

    public static QuipBattleGameQuestions QuipBattleQuestions = new QuipBattleGameQuestions(); 

    public static AppConfig inst { get; private set; }

    public static readonly string[] BadWords = { "Homosexual", "Homophobic", "Racist", "Gay", "Lgbt", "Jew", "Jewish", "Anti-semitic", "Chink", "Muslims", "Muslim", "Isis", "Islamophobe", "homophobe ", "Bombing", "Sexyhot", "Bastard", "Bitch", "Fucker", "Cunt", "Fuck", "Goddamn", "Motherfucker", "Nigga", "Nigger", "Nigers", "Knee Grr", "Knee Gr", "neegro", "Knee", "Prick", "Shit", "shit ass", "Shitass", "son of a bitch", "Whore", "Thot", "Slut", "Faggot", "Dick", "Pussy", "Penis", "Vagina", "Negro", "Coon", "Bitched", "Sexist", "Freaking", "Cock", "Sucker", "Lick", "Licker", "Rape", "Molest", "Anal", "Buttrape", "Coont", "Cancer", "Sex", "Retard", "Fuckface", "Dumbass", "5h1t", "5hit", "A_s_s", "a2m", "a55", "adult", "amateur", "anal", "anal impaler", "anal leakage", "anilingus", "anus", "ar5e", "arrse", "arse", "arsehole", "ass fuck", "asses", "assfucker", "ass-fucker", "assfukka", "asshole", "asshole", "assholes", "assmucus", "assmunch", "asswhole", "autoerotic", "b!tch", "b00bs", "b17ch", "b1tch", "ballbag", "ballsack", "bang (one's) box", "bangbros", "bareback", "bastard", "beastial", "beastiality", "beef curtain", "bellend", "bestial", "bestiality", "bi+ch", "biatch", "bimbos", "birdlock", "bitch", "bitch tit", "bitcher", "bitchers", "bitches", "bitchin", "bitching", "bloody", "blow job", "blow me", "blow mud", "blowjob", "blowjobs", "blue waffle", "blumpkin", "boiolas", "bollock", "bollok", "boner", "boobies", "boob", "boobs", "booobs", "boooobs", "booooobs", "booooooobs", "breasts", "buceta", "bugger", "bunny fucker", "bust a load", "busty", "butt", "butt fuck", "butthole", "buttmuch", "buttplug", "c0ck", "c0cksucker", "carpet muncher", "carpetmuncher", "cawk", "chink", "choade", "chota bags", "cipa", "cl1t", "clit", "clit licker", "clitoris", "clits", "clitty litter", "clusterfuck", "cnut", "cock", "cock pocket", "cock snot", "cockface", "cockhead", "cockmunch", "cockmuncher", "cocks", "cocksuck ", "cocksucked ", "cocksucker", "cock-sucker", "cocksucking", "cocksucks ", "cocksuka", "cocksukka", "cok", "cokmuncher", "coksucka", "coon", "cop some wood", "cornhole", "corp whore", "cox", "cum", "cum chugger", "cum dumpster", "cum freak", "cum guzzler", "cumdump", "cummer", "cumming", "cums", "cumshot", "cunilingus", "cunillingus", "cunnilingus", "cunt", "cunt hair", "cuntbag", "cuntlick ", "cuntlicker ", "cuntlicking ", "cunts", "cuntsicle", "cunt-struck", "cut rope", "cyalis", "cyberfuc", "cyberfuck ", "cyberfucked ", "cyberfucker", "cyberfuckers", "cyberfucking ", "damn", "dick hole", "dick shy", "dickhead", "dildo", "dildos", "dink", "dinks", "dirsa", "dirty Sanchez", "dlck", "dog-fucker", "doggie style", "doggiestyle", "doggin", "dogging", "donkeyribber", "doosh", "duche", "dyke", "eat a dick", "eat hair pie", "ejaculate", "ejaculated", "ejaculates ", "ejaculating ", "ejaculatings", "ejaculation", "ejakulate", "erotic", "f u c k", "f u c k e r", "f_u_c_k", "f4nny", "facial", "fag", "fagging", "faggitt", "faggot", "faggs", "fagot", "fagots", "fags", "fanny", "fannyflaps", "fannyfucker", "fanyy", "fatass", "fcuk", "fcuker", "fcuking", "feck", "fecker", "felching", "fellate", "fellatio", "fingerfuck ", "fingerfucked ", "fingerfucker ", "fingerfuckers", "fingerfucking ", "fingerfucks ", "fist fuck", "fistfuck", "fistfucked ", "fistfucker ", "fistfuckers ", "fistfucking ", "fistfuckings ", "fistfucks ", "flange", "flog the log", "fook", "fooker", "fuck hole", "fuck puppet", "fuck trophy", "fuck yo mama", "fuck", "fucka", "fuck-ass", "fuck-bitch", "fucked", "fucker", "fuckers", "fuckhead", "fuckheads", "fuckin", "fucking", "fuckings", "fuckingshitmotherfucker", "fuckme ", "fuckmeat", "fucks", "fucktoy", "fuckwhit", "fuckwit", "fudge packer", "fudgepacker", "fuk", "fuker", "fukker", "fukkin", "fuks", "fukwhit", "fukwit", "fux", "fux0r", "gangbang", "gangbang", "gang-bang", "gangbanged ", "gangbangs ", "gassy ass", "gaylord", "gaysex", "goatse", "hawk", "ham flap", "hardcoresex ", "heshe", "hoar", "hoare", "hoer", "homo", "homoerotic", "hore", "horniest", "horny", "hotsex", "how to kill", "how to murdep", "jackoff", "jack-off ", "japs", "jerk", "jerk-off ", "jism", "jiz ", "jizm ", "jizz", "kawk", "kinky Jesus", "knob", "knob end", "knobead", "knobed", "knobend", "knobend", "knobhead", "knobjocky", "knobjokey", "kock", "kondum", "kondums", "kum", "kummer", "kumming", "kums", "kunilingus", "kwif", "l3i+ch", "l3itch", "labia", "lust", "lusting", "m0f0", "m0fo", "m45terbate", "ma5terb8", "ma5terbate", "mafugly", "masochist", "masterb8", "masterbat*", "masterbat3", "masterbate", "master-bate", "masterbation", "masterbations", "masturbate", "mof0", "mofo", "mo-fo", "mothafuck", "mothafucka", "mothafuckas", "mothafuckaz", "mothafucked ", "mothafucker", "mothafuckers", "mothafuckin", "mothafucking ", "mothafuckings", "mothafucks", "mother fucker", "mother fucker", "motherfuck", "motherfucked", "motherfucker", "motherfuckers", "motherfuckin", "motherfucking", "motherfuckings", "motherfuckka", "motherfucks", "muff", "muff puff", "mutha", "muthafecker", "muthafuckker", "muther", "mutherfucker", "n1gga", "n1gger", "nazi", "need the dick", "nigg3r", "nigg4h", "nigga", "niggah", "niggas", "niggaz", "nigger", "nig ", "niggers", "snicker", "nob jokey", "nobhead", "nobjocky", "nobjokey", "numbnuts", "nut butter", "nutsack", "orgasim ", "orgasims ", "orgasm", "orgasms ", "p0rn", "pecker", "penis", "penisfucker", "phonesex", "phuck", "phuk", "phuked", "phuking", "phukked", "phukking", "phuks", "phuq", "pigfucker", "pimpis", "piss", "pissed", "pisser", "pissers", "pisses ", "pissflaps", "pissin ", "pissing", "pissoff ", "poop", "porn", "porno", "pornography", "pornos", "prick", "pricks ", "pron", "pube", "pusse", "pussi", "pussies", "pussy", "pussy fart", "pussy palace", "pussys ", "queaf", "queer", "rectum", "retard", "rimjaw", "rimming", "s.o.b.", "s_h_i_t", "sadism", "sadist", "sandbar", "sausage queen", "schlong", "screwing", "scroat", "scrote", "scrotum", "semen", "sex", "sh!+", "sh!t", "sh1t", "shag", "shagger", "shaggin", "shagging", "shemale", "shi+", "shit", "shit fucker", "shitdick", "shite", "shited", "shitey", "shitfuck", "shitfull", "shithead", "shiting", "shitings", "shits", "shitted", "shitter", "shitters ", "shitting", "shittings", "shitty ", "skank", "slope", "slut", "slut bucket", "sluts", "smegma", "smut", "snatch", "son-of-a-bitch", "spunk", "t1tt1e5", "t1tties", "teets", "teez", "testical", "testicle", "tit wank", "titfuck", "tits", "titt", "tittie5", "tittiefucker", "titties", "tittyfuck", "tittywank", "titwank", "tosser", "turd", "tw4t", "twat", "twathead", "twatty", "twunt", "twunter", "v14gra", "v1gra", "vagina", "viagra", "vulva", "w00se", "wang", "wank", "wanker", "wanky", "whoar", "whore", "willies", "willy", "wtf", "xrated", "xxx", "sucker", "dumbass", "Kys", "Shooting", "Shoot", "Bomb", "Terrorist", "Terrorism", "Bombed", "Trump", "Maga", "Conservative", "Make america great again", "Far right", "Necrophilia", "Mongoloid", "Furfag", "Cp", "Pedo", "Pedophile", "Pedophilia", "Child predator", "Predatory", "Depression", "Cut myself", "I want to die", "Fuck life", "Redtube", "Loli", "Lolicon", "Cub", "Watermellon", "Fried Chicken" };

    public static int CommonMult = 1;
    public static int RareMult = 2;
    public static int EpicMult = 10;
    public static int LegendaryMult = 40; 


    public static int GetMult(RarityType rarity)
    {
        if (rarity == RarityType.Common)
            return CommonMult;
        else if(rarity == RarityType.Rare)
            return RareMult;
        else if(rarity == RarityType.Epic)
            return EpicMult;
        else
            return LegendaryMult;
    }

    /// <summary>
    /// Loads the defaults from StreamingAssets/config.sample.json, then your values from config.json
    /// in the user data folder (see <see cref="UserData"/>). The first run creates that file with your
    /// details left blank to fill in.
    /// </summary>
    public static void Load()
    {
        string samplePath = Path.Combine(Application.streamingAssetsPath, SampleFileName);
        inst = JsonConvert.DeserializeObject<AppConfig>(File.ReadAllText(samplePath));
        inst.volumes ??= new Dictionary<string, float>();
        _defaultValues = inst.configData.ToDictionary(kv => kv.Key, kv => ToToken(kv.Value.Value));
        _defaultVolumes = new Dictionary<string, float>(inst.volumes);
        _unknownValues.Clear();

        string userPath = UserData.ConfigPath;
        if (File.Exists(userPath))
        {
            UserConfigFile user = JsonConvert.DeserializeObject<UserConfigFile>(File.ReadAllText(userPath)) ?? new UserConfigFile();
            foreach (KeyValuePair<string, object> kv in user.values ?? new Dictionary<string, object>())
            {
                if (inst.configData.TryGetValue(kv.Key, out ConfigItem item))
                    item.Value = kv.Value;
                else
                {
                    _unknownValues[kv.Key] = kv.Value;
                    Debug.LogWarning($"Your config has '{kv.Key}', which this version doesn't use. Keeping it.");
                }
            }
            foreach (KeyValuePair<string, float> kv in user.volumes ?? new Dictionary<string, float>())
                inst.volumes[kv.Key] = kv.Value;
            Debug.Log($"Loaded your config from {userPath}");
        }
        else
        {
            SaveUserConfig();
            Debug.Log($"Created your config at {userPath}. Fill in your Twitch channel and other details there.");
        }

        inst.downloadedEmoteIndexMap = LoadEmoteMap();

        foreach (ConfigItem item in inst.configData.Values)
            item.PropertyChanged += (sender, e) => MarkDirty();
        IsDirty = false;
    }

    /// <summary>
    /// Writes your config: your personal details, plus every setting and volume that differs from the
    /// sample's defaults. Defaults you haven't changed stay out, so new defaults reach you.
    /// </summary>
    public static void SaveUserConfig()
    {
        if (inst == null)
        {
            Debug.LogError("Failed to save your config: it isn't loaded.");
            return;
        }

        UserConfigFile user = new UserConfigFile();
        foreach (KeyValuePair<string, ConfigItem> kv in inst.configData.Where(kv => kv.Value.Personal))
            user.values[kv.Key] = kv.Value.Value;
        foreach (KeyValuePair<string, ConfigItem> kv in inst.configData.Where(kv => !kv.Value.Personal))
        {
            if (!_defaultValues.TryGetValue(kv.Key, out JToken defaultValue) || !JToken.DeepEquals(ToToken(kv.Value.Value), defaultValue))
                user.values[kv.Key] = kv.Value.Value;
        }
        foreach (KeyValuePair<string, object> kv in _unknownValues)
            user.values.TryAdd(kv.Key, kv.Value);
        foreach (KeyValuePair<string, float> kv in inst.volumes)
        {
            if (!_defaultVolumes.TryGetValue(kv.Key, out float defaultVolume) || Math.Abs(defaultVolume - kv.Value) > 0.0001f)
                user.volumes[kv.Key] = kv.Value;
        }

        File.WriteAllText(UserData.ConfigPath, JsonConvert.SerializeObject(user, Formatting.Indented));
        IsDirty = false;
        Debug.Log($"Saved your config to {UserData.ConfigPath}");
    }

    private static JToken ToToken(object value) => value == null ? JValue.CreateNull() : JToken.FromObject(value);

    /// <summary>The emote cache map from the user data folder, or the seed shipped next to the seed sprite sheet.</summary>
    private static Dictionary<string, int> LoadEmoteMap()
    {
        string path = File.Exists(UserData.EmoteMapPath) ? UserData.EmoteMapPath : Path.Combine(Application.streamingAssetsPath, EmoteMapSeedFileName);
        if (!File.Exists(path))
            return new Dictionary<string, int>();
        return JsonConvert.DeserializeObject<Dictionary<string, int>>(File.ReadAllText(path)) ?? new Dictionary<string, int>();
    }

    public static void SaveEmoteMap()
    {
        if (inst == null)
            return;
        File.WriteAllText(UserData.EmoteMapPath, JsonConvert.SerializeObject(inst.downloadedEmoteIndexMap, Formatting.Indented));
    }

    public void SetV(string key, object value)
    {
        if(configData.ContainsKey(key))
            configData[key].Value = value;
        else
        {
            Debug.LogError($"Failed to set {key} config value {value}. Creating new config entry");
            //configData[key] = new ConfigItem { Value = value };
        }
    }
    public string[] GetSArray(string key)
    {
        object value = GetValue(key);

        string[] stringArray = JsonConvert.DeserializeObject<string[]>(value.ToString());

        if (stringArray == null)
            return new string[0];

        return stringArray; 
    }
    public string GetS(string key)
    {
        object value = GetValue(key);
        if (value == null)
            return ""; 

        return value.ToString();
    }

    public int GetI(string key)
    {
        object value = GetValue(key);
        if (value == null)
            return -1;

        if(int.TryParse(value.ToString(), out int result))
            return result;
        else
        {
            Debug.LogError($"Failed to parse int from {key} value {value} from config values. Check config.sample.json in StreamingAssets and your config.json in the user data folder");
            return -1;
        }
    }

    public float GetF(string key)
    {
        object value = GetValue(key);
        if (value == null)
            return -1;

        if (float.TryParse(value.ToString(), out float result))
            return result;
        else
        {
            Debug.LogError($"Failed to parse float from {key} value {value} from config values. Check config.sample.json in StreamingAssets and your config.json in the user data folder");
            return -1;
        }
    }

    public bool GetB(string key)
    {
        object value = GetValue(key);
        if (value == null)
            return false;

        if (bool.TryParse(value.ToString(), out bool result))
            return result;
        else
        {
            Debug.LogError($"Failed to parse bool from {key} value {value} from config values. Check config.sample.json in StreamingAssets and your config.json in the user data folder");
            return false;
        }
    }

    public object GetValue(string key)
    {
        if (configData.TryGetValue(key, out ConfigItem item))
            return item.Value;
        else
        {
            Debug.LogError($"Failed to retreive {key} from config values. Check config.sample.json in StreamingAssets and your config.json in the user data folder");
            return null;
        }
    }

}



