using System.IO;
using UnityEngine;

/// <summary>
/// Where the game keeps your settings and player data: the per-user app data folder
/// (%USERPROFILE%\AppData\LocalLow\ChaosLeague\Chaos League on Windows). It's outside the repo and
/// the build folder, so commits can't include it and rebuilds can't overwrite it, and the Editor and
/// builds share it.
/// </summary>
public static class UserData
{
    /// <summary>Used instead of the real folder when set, so tests don't touch your data.</summary>
    public static string FolderOverride { get; set; }

    /// <summary>The value after a command-line flag such as -datadir, or null.</summary>
    public static string CommandLineValue(string flag)
    {
        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], flag, System.StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }

    /// <summary>The folder in use: a test override, then -datadir on the command line (e.g. a portable install), then the per-user folder.</summary>
    public static string Folder
    {
        get
        {
            string folder = !string.IsNullOrEmpty(FolderOverride) ? FolderOverride
                : CommandLineValue("-datadir") ?? Application.persistentDataPath;
            Directory.CreateDirectory(folder);
            return folder;
        }
    }

    /// <summary>Your values, on top of the defaults in StreamingAssets/config.sample.json.</summary>
    public static string ConfigPath => Path.Combine(Folder, "config.json");
    public static string DatabaseBackupsFolder => Path.Combine(Folder, "DatabaseBackups");
    public static string EmoteSheetPath => Path.Combine(Folder, "dynamic_sprite_sheet_4096.png");
    public static string EmoteMapPath => Path.Combine(Folder, "emote_index_map.json");

    public static string DatabasePath(string tableName) => Path.Combine(Folder, $"{tableName}.db");
}
