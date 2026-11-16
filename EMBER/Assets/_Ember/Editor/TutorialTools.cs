using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Ember.EditorTools
{
    /// <summary>
    /// Editor side of the tutorial recorder (Scripts/Tools/Tutorial). The video itself is recorded from the built
    /// player (Tools/run_tutorial.sh); here the script can be tried in play mode without capturing anything:
    ///   menu Ember/Tutorial/Dry Run           whole script, timeline in Build/Tutorial/dry_editor
    ///   batch  -executeMethod Ember.EditorTools.TutorialTools.BatchDryRun -tutorialDry DIR [-tutorialFrom ID] [-tutorialTo ID]
    /// The recorder exits the editor with the number of failed segments.
    /// </summary>
    public static class TutorialTools
    {
        static string Root => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));

        [MenuItem("Ember/Tutorial/Dry Run (play mode)", priority = 30)]
        public static void DryRun() => Start($"-tutorialDry \"{Path.Combine(Root, "Build", "Tutorial", "dry_editor")}\"");

        [MenuItem("Ember/Tutorial/Dry Run Stage II (play mode)", priority = 31)]
        public static void DryRunStage2() => Start($"-tutorialDry \"{Path.Combine(Root, "Build", "Tutorial", "dry_editor")}\" -tutorialFrom 2A -tutorialTo 2D");

        [MenuItem("Ember/Tutorial/Dry Run Stage III (play mode)", priority = 32)]
        public static void DryRunStage3() => Start($"-tutorialDry \"{Path.Combine(Root, "Build", "Tutorial", "dry_editor")}\" -tutorialFrom 3A");

        static void Start(string args)
        {
            if (EditorApplication.isPlaying) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            SessionState.SetString("EmberTutorialArgs", args);
            EditorSceneManager.OpenScene(EmberPaths.Scene("Title"));
            EditorApplication.EnterPlaymode();
        }

        /// <summary>Batch: the -tutorial* flags are on the Unity command line, so the recorder finds them itself.</summary>
        public static void BatchDryRun()
        {
            try
            {
                SessionState.EraseString("EmberTutorialArgs");
                EditorSceneManager.OpenScene(EmberPaths.Scene("Title"));
                EditorApplication.EnterPlaymode();
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }
    }
}
