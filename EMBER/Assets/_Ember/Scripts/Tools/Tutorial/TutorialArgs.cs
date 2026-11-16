using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace Ember
{
    public enum TutorialMode { None, Record, Dry, Spike }

    /// <summary>
    /// Options of the tutorial recorder (see TutorialRecorder). Built player: the command line, e.g.
    ///   EMBER.exe -tutorialRecord DIR [-tutorialCaptions captions.json] [-tutorialQuality 92] [-tutorialFrom 2A] [-tutorialTo 2D]
    ///   EMBER.exe -batchmode -nographics -tutorialDry DIR      (no rendering, no files except the timeline)
    ///   EMBER.exe -tutorialSpike DIR                           (150-frame capture test on the title screen)
    /// Editor: the same flags on the Unity command line (batch dry run), or SessionState "EmberTutorialArgs"
    /// written by the menu Ember/Tutorial/..., which survives entering play mode.
    /// </summary>
    public static class TutorialArgs
    {
        public const string SessionKey = "EmberTutorialArgs";
        static string[] cached;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => cached = null;

        static string[] Args
        {
            get
            {
                if (cached != null) return cached;
                var list = new List<string>(Environment.GetCommandLineArgs());
#if UNITY_EDITOR
                string extra = UnityEditor.SessionState.GetString(SessionKey, "");
                if (!string.IsNullOrEmpty(extra)) list.AddRange(Split(extra));
#endif
                cached = list.ToArray();
                return cached;
            }
        }

        /// <summary>Splits on spaces; "double quoted" parts may contain spaces.</summary>
        public static List<string> Split(string s)
        {
            var parts = new List<string>();
            var cur = new StringBuilder();
            bool quoted = false, any = false;
            foreach (char c in s)
            {
                if (c == '"') { quoted = !quoted; any = true; continue; }
                if (c == ' ' && !quoted)
                {
                    if (any) parts.Add(cur.ToString());
                    cur.Clear();
                    any = false;
                    continue;
                }
                cur.Append(c);
                any = true;
            }
            if (any) parts.Add(cur.ToString());
            return parts;
        }

        public static bool Has(string flag) => Array.IndexOf(Args, flag) >= 0;

        public static string Value(string flag)
        {
            var a = Args;
            int i = Array.LastIndexOf(a, flag);
            if (i < 0 || i + 1 >= a.Length || a[i + 1].StartsWith("-tutorial")) return null;
            return a[i + 1];
        }

        public static TutorialMode Mode =>
            Has("-tutorialRecord") ? TutorialMode.Record :
            Has("-tutorialSpike") ? TutorialMode.Spike :
            Has("-tutorialDry") ? TutorialMode.Dry : TutorialMode.None;

        public static string OutDir => Value("-tutorialRecord") ?? Value("-tutorialSpike") ?? Value("-tutorialDry");
        public static string From => Value("-tutorialFrom");
        public static string To => Value("-tutorialTo");
        public static string Captions => Value("-tutorialCaptions");
        public static bool NoAudio => Has("-tutorialNoAudio");
        public static bool NoKeys => Has("-tutorialNoKeys");

        /// <summary>"1920x1080": the take is aborted when the screen has another size.</summary>
        public static string RequireSize => Value("-tutorialRequire");

        public static int Quality => int.TryParse(Value("-tutorialQuality"), out int q) ? Mathf.Clamp(q, 30, 100) : 92;

        public static float MinFreeGB =>
            float.TryParse(Value("-tutorialMinFreeGB"), NumberStyles.Float, CultureInfo.InvariantCulture, out float g) ? g : 3f;
    }
}
