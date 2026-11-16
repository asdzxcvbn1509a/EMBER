using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Ember.EditorTools
{
    /// <summary>
    /// One-click (or batch-mode) pipeline for EMBER. Menu: Ember/...
    /// Batch: Unity.exe -batchmode -quit -projectPath EMBER -executeMethod Ember.EditorTools.Pipeline.BatchAll
    /// </summary>
    public static class Pipeline
    {
        public static readonly string[] SceneOrder = { "Title", "Stage1", "Stage2", "Stage3", "Ending" };
        static string LogDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Logs"));

        [MenuItem("Ember/1. Generate Assets (textures, materials, clips, prefabs)", priority = 1)]
        public static void GenerateAssets()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            TextureFactory.Run();
            MaterialFactory.Run();
            AnimFactory.Run();
            VolumeFactory.Run();
            UIFactory.Font();
            ConfigurePlayer();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            PrefabFactory.Run();
            AssetDatabase.SaveAssets();
            Debug.Log("[Ember] assets generated");
        }

        [MenuItem("Ember/2. Build All Scenes", priority = 2)]
        public static void BuildScenes()
        {
            BuildTitle.Run();
            BuildStage1.Run();
            BuildStage2.Run();
            BuildStage3.Run();
            BuildEnding.Run();
            EditorBuildSettings.scenes = SceneOrder.Select(n => new EditorBuildSettingsScene(EmberPaths.Scene(n), true)).ToArray();
            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(EmberPaths.Scene("Title"));
            Debug.Log("[Ember] scenes built");
        }

        [MenuItem("Ember/3. Validate Scenes", priority = 3)]
        static void MenuValidate() => Validate();

        public static int Validate()
        {
            int errors = SceneValidator.Run(out var report);
            Directory.CreateDirectory(LogDir);
            File.WriteAllLines(Path.Combine(LogDir, "validator.txt"), report);
            foreach (var line in report) Debug.Log("[Validator] " + line);
            Debug.Log($"[Ember] validation finished with {errors} error(s)");
            return errors;
        }

        [MenuItem("Ember/4. Capture Level Maps and Screenshots", priority = 4)]
        public static void Capture() => CaptureTool.Run();

        [MenuItem("Ember/5. Build Windows Player", priority = 5)]
        static void MenuBuildPlayer() => BuildPlayer();

        public static bool BuildPlayer()
        {
            string exe = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Build", "EMBER", "EMBER.exe"));
            Directory.CreateDirectory(Path.GetDirectoryName(exe));
            var opts = new BuildPlayerOptions
            {
                scenes = SceneOrder.Select(EmberPaths.Scene).ToArray(),
                locationPathName = exe,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None
            };
            var report = BuildPipeline.BuildPlayer(opts);
            Debug.Log($"[Ember] build {report.summary.result}: {report.summary.totalSize / (1024 * 1024)} MB, {report.summary.totalErrors} errors -> {exe}");
            return report.summary.result == BuildResult.Succeeded;
        }

        /// <summary>Scene data for the Blender trailer (ArtSource/Trailer). Read only: nothing in the project is saved.</summary>
        [MenuItem("Ember/6. Export Scenes for Trailer", priority = 6)]
        public static void ExportTrailer() => TrailerExport.Run();

        [MenuItem("Ember/Verify/Run AutoPilot (play mode)", priority = 20)]
        public static void RunAutoPilot()
        {
            SessionState.SetBool("EmberAutoPilot", true);
            EditorSceneManager.OpenScene(EmberPaths.Scene("Stage1"));
            EditorApplication.EnterPlaymode();
        }

        static void ConfigurePlayer()
        {
            PlayerSettings.companyName = "EMBER Studio";
            PlayerSettings.productName = "EMBER";
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.runInBackground = true;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            var icon = EmberPaths.GenTex("T_Icon");
            if (icon != null) PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
        }

        // ------------------------------------------------------------------ batch entry points
        static void Guard(Action a)
        {
            try { a(); }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        public static void BatchGenerate() => Guard(GenerateAssets);
        public static void BatchScenes() => Guard(BuildScenes);
        public static void BatchValidate() => Guard(() => { if (Validate() > 0) EditorApplication.Exit(2); });
        public static void BatchCapture() => Guard(Capture);
        public static void BatchBuild() => Guard(() => { if (!BuildPlayer()) EditorApplication.Exit(3); });
        public static void BatchAutoPilot() => Guard(RunAutoPilot);
        public static void BatchTrailerExport() => Guard(ExportTrailer);

        // Template packages EMBER does not use. The deprecated IAP / Analytics packages ship DLLs that crash
        // Unity's API updater ("Argument null or empty"), which shows red errors on a fresh import.
        static readonly string[] UnusedPackages =
            { "com.unity.purchasing", "com.unity.analytics", "com.unity.collab-proxy", "com.unity.multiplayer.center", "com.unity.xr.legacyinputhelpers" };

        /// <summary>Removes the unused template packages through the Package Manager API (run without -quit).</summary>
        public static void BatchTrimPackages()
        {
            var request = UnityEditor.PackageManager.Client.AddAndRemove(null, UnusedPackages);
            EditorApplication.update += Poll;
            void Poll()
            {
                if (!request.IsCompleted) return;
                EditorApplication.update -= Poll;
                bool ok = request.Status == UnityEditor.PackageManager.StatusCode.Success;
                if (ok) Debug.Log("[Ember] removed unused template packages: " + string.Join(", ", UnusedPackages));
                else Debug.LogError("[Ember] package removal failed: " + request.Error?.message);
                EditorApplication.Exit(ok ? 0 : 4);
            }
        }

        public static void BatchAll() => Guard(() =>
        {
            GenerateAssets();
            BuildScenes();
            if (Validate() > 0) EditorApplication.Exit(2);
        });
    }
}
