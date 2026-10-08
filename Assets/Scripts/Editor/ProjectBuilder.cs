using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds the game. Builds hold no personal data: your config, player database and emote cache
/// live in the user data folder (see UserData), so a build is safe to share.
/// </summary>
public class ProjectBuilder : EditorWindow
{
    // Builds go to the same place every time, so Windows keeps per-app settings for the exe
    // (like its output device in the volume mixer): %USERPROFILE%\ChaosLeague\ChaosLeague.exe
    private static readonly string DefaultBuildPath = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
    private const string DefaultProjectName = "ChaosLeague";

    private string BuildPath = DefaultBuildPath;
    private string ProjectName = DefaultProjectName;
    private bool DevelopmentBuild;

    [MenuItem("Tools/Project Builder")]
    public static void ShowWindow()
    {
        GetWindow<ProjectBuilder>("Project Builder");
    }

    /// <summary>Release build to the default folder, for command-line builds: -executeMethod ProjectBuilder.BuildDefault</summary>
    public static void BuildDefault()
    {
        BuildProject(DefaultBuildPath, DefaultProjectName, development: false);
    }

    private void OnGUI()
    {
        GUILayout.Label("Build Settings", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        BuildPath = EditorGUILayout.TextField("Build Path", BuildPath);
        if (GUILayout.Button("Browse", GUILayout.Width(100)))
        {
            string selectedPath = EditorUtility.OpenFolderPanel("Select Build Folder", BuildPath, "");
            if (!string.IsNullOrEmpty(selectedPath))
            {
                BuildPath = selectedPath;
            }
        }
        EditorGUILayout.EndHorizontal();

        ProjectName = EditorGUILayout.TextField("Build Name", ProjectName);

        //Development builds show a "Development Build" watermark (on stream too) and run slower
        DevelopmentBuild = EditorGUILayout.Toggle("Development Build", DevelopmentBuild);

        if (GUILayout.Button("Build"))
        {
            BuildProject(BuildPath, ProjectName, DevelopmentBuild);
        }
    }

    private static void BuildProject(string buildPath, string projectName, bool development)
    {
        string buildFolderPath = Path.Combine(buildPath, projectName);

        BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions();
        buildPlayerOptions.scenes = new[] { "Assets/Scenes/MainScene.unity" };
        buildPlayerOptions.locationPathName = Path.Combine(buildFolderPath, $"{projectName}.exe");
        buildPlayerOptions.target = BuildTarget.StandaloneWindows64;
        if (development)
            buildPlayerOptions.options = BuildOptions.Development;

        BuildPipeline.BuildPlayer(buildPlayerOptions);
        Debug.Log("Build completed: " + buildFolderPath);
    }
}
