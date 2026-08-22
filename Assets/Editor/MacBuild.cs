using System;
using System.IO;
using System.Linq;
using MajdataViewX.Managers;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEditor.Build.Reporting;

public static class MacBuild
{
    private const string OutputPath = "Builds/macOS/MajdataViewX.app";
    private const string BuildProfilePath = "Assets/Settings/Build Profiles/macOS-arm64.asset";

    public static void Verify()
    {
        if (!EditorBuildSettings.scenes.Any(scene => scene.enabled))
            throw new InvalidOperationException("No enabled scene is configured for the macOS build.");

        if (AssetDatabase.LoadAssetAtPath<BuildProfile>(BuildProfilePath) is null)
            throw new InvalidOperationException("The Apple Silicon macOS build profile is missing.");

        var bass = AssetImporter.GetAtPath("Assets/Plugins/macOS/libbass.dylib") as PluginImporter;
        if (bass is null || !bass.GetCompatibleWithPlatform(BuildTarget.StandaloneOSX))
            throw new InvalidOperationException("libbass.dylib is not enabled for macOS builds.");

        var recorder = AssetImporter.GetAtPath("Assets/Plugins/x86_64/RenderingOut.dll") as PluginImporter;
        if (recorder is null || recorder.GetCompatibleWithPlatform(BuildTarget.StandaloneOSX))
            throw new InvalidOperationException("The Windows-only recorder must be excluded from macOS builds.");

        if (ScreenRecorder.IsSupported)
            throw new InvalidOperationException("Video recording must be disabled in the macOS player.");
    }

    [MenuItem("Build/macOS")]
    public static void Build()
    {
        Verify();
        Directory.CreateDirectory(Path.GetDirectoryName(OutputPath)!);

        var report = BuildPipeline.BuildPlayer(new BuildPlayerWithProfileOptions
        {
            buildProfile = AssetDatabase.LoadAssetAtPath<BuildProfile>(BuildProfilePath),
            locationPathName = OutputPath,
            options = BuildOptions.None
        });

        if (report.summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException($"macOS build failed: {report.summary.result}");
    }
}
