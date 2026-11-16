using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace Ember
{
    /// <summary>
    /// Automated wiring test. Started with the command-line flag -autopilot (built player) or from
    /// the editor menu Ember/Verify/Run AutoPilot. For every stage it replays StageSolution: teleports
    /// the ember onto each plate in order, waits, then checks the expected results.
    /// </summary>
    public class AutoPilot : MonoBehaviour
    {
        public static readonly string[] Stages = { "Stage1", "Stage2", "Stage3" };
        const string EditorFlag = "EmberAutoPilot";

        readonly List<string> lines = new List<string>();
        int failures, passes;
        string shotsDir; // -autopilotShots <dir>: also save real in-game screenshots (overlay UI, no editor tricks)

        static bool Requested
        {
            get
            {
#if UNITY_EDITOR
                if (UnityEditor.SessionState.GetBool(EditorFlag, false)) return true;
#endif
                return Environment.GetCommandLineArgs().Contains("-autopilot");
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Requested || FindAnyObjectByType<AutoPilot>() != null) return;
            var go = new GameObject("AutoPilot");
            DontDestroyOnLoad(go);
            go.AddComponent<AutoPilot>();
        }

        IEnumerator Start()
        {
            Log("EMBER AutoPilot  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));
            var args = Environment.GetCommandLineArgs();
            int shots = Array.IndexOf(args, "-autopilotShots");
            if (shots >= 0 && shots + 1 < args.Length)
            {
                shotsDir = args[shots + 1];
                Directory.CreateDirectory(shotsDir);
            }
            if (shotsDir != null && SceneManager.GetActiveScene().name == "Title") yield return MenuShots();
            foreach (var stage in Stages) yield return RunStage(stage);
            Log($"RESULT  passed={passes}  failed={failures}");
            if (shotsDir != null && SceneManager.GetActiveScene().name == "Ending")
            {
                yield return new WaitForSecondsRealtime(24f);   // the team names are on screen about 22-29 s in
                yield return Shot("Ending_Credits.png");
                yield return HoverShot("UI/Buttons/Menu", "Ending_Hover.png");
            }
            Finish();
        }

        // ---------------------------------------------------------------- screenshots (not counted as tests)
        IEnumerator MenuShots()
        {
            yield return new WaitForSecondsRealtime(2f);       // title fade-in
            yield return HoverShot("UI/Menu/NewGame", "Title_Hover.png");
            var select = GameObject.Find("UI/Menu/Select");
            if (select != null) select.GetComponent<UIButtonAction>().Run();
            yield return new WaitForSecondsRealtime(0.3f);
            yield return HoverShot("UI/StageSelect/S2", "StageSelect_Hover.png");
        }

        IEnumerator PauseShot()
        {
            var gm = GameManager.Instance;
            if (gm == null) yield break;
            gm.SetPaused(true);
            yield return new WaitForSecondsRealtime(0.3f);
            yield return HoverShot("UI/PausePanel/Resume", "Pause_Hover.png");
            gm.SetPaused(false);
        }

        /// <summary>Hovers a menu button the way the mouse does (pointer-enter event), saves a screenshot, then leaves it.</summary>
        IEnumerator HoverShot(string path, string file)
        {
            var go = GameObject.Find(path);
            if (go == null || EventSystem.current == null) { Log("shot skipped, not found: " + path); yield break; }
            var pointer = new PointerEventData(EventSystem.current);
            ExecuteEvents.Execute(go, pointer, ExecuteEvents.pointerEnterHandler);
            yield return new WaitForSecondsRealtime(0.8f);
            yield return Shot(file);
            ExecuteEvents.Execute(go, pointer, ExecuteEvents.pointerExitHandler);
        }

        IEnumerator Shot(string file)
        {
            ScreenCapture.CaptureScreenshot(Path.Combine(shotsDir, file));
            yield return null;
            yield return new WaitForSecondsRealtime(1f);
        }

        IEnumerator RunStage(string stage)
        {
            Log("== " + stage);
            SceneManager.LoadScene(stage);
            yield return null;
            yield return new WaitForSeconds(2f);
            if (shotsDir != null && stage == Stages[0]) yield return PauseShot();
            var solution = FindAnyObjectByType<StageSolution>();
            var player = GameObject.Find("Player");
            if (solution == null || player == null) { Fail(stage, "setup", "StageSolution or Player missing"); yield break; }
            var body = player.GetComponent<Rigidbody>();

            foreach (var step in solution.steps)
                foreach (var c in step.checks)
                    if (c.target != null)
                    {
                        c.startPosition = c.target.transform.position;
                        c.startRotation = c.target.transform.rotation;
                    }

            foreach (var step in solution.steps)
            {
                string startScene = SceneManager.GetActiveScene().name;
                if (step.moveTo != null)
                {
                    Vector3 p = step.moveTo.position + Vector3.up * 0.7f;
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                    body.position = p;
                    player.transform.position = p;
                    Physics.SyncTransforms();
                }
                float t = 0f;
                float limit = step.expectSceneChange ? step.wait + 6f : step.wait;
                while (t < limit)
                {
                    t += Time.deltaTime;
                    if (SceneManager.GetActiveScene().name != startScene) break;
                    if (body != null && step.moveTo != null && t < step.wait * 0.5f)
                    {
                        // keep the ember parked on the trigger while things happen
                        body.linearVelocity = new Vector3(0f, body.linearVelocity.y, 0f);
                    }
                    yield return null;
                }
                if (step.expectSceneChange)
                {
                    bool changed = SceneManager.GetActiveScene().name != startScene;
                    Report(stage, step.label, changed, "next scene loaded (" + solution.nextScene + ")");
                    yield break;
                }
                foreach (var c in step.checks) Report(stage, step.label, Evaluate(c), Describe(c));
            }
        }

        public static bool Evaluate(SolutionCheck c)
        {
            switch (c.kind)
            {
                case SolutionCheck.Kind.Active: return c.target != null && c.target.activeInHierarchy;
                case SolutionCheck.Kind.Inactive: return c.target != null && !c.target.activeInHierarchy;
                case SolutionCheck.Kind.Destroyed: return c.target == null;
                case SolutionCheck.Kind.Moved: return c.target != null && Vector3.Distance(c.target.transform.position, c.startPosition) > 0.25f;
                case SolutionCheck.Kind.AnimationPlayed:
                    if (c.target == null) return false;
                    var anim = c.target.GetComponent<Animation>();
                    if (anim == null) return false;
                    var state = anim[c.text];
                    return state != null && (anim.IsPlaying(c.text) || state.time > 0f || state.normalizedTime > 0f || Vector3.Distance(c.target.transform.position, c.startPosition) > 0.2f);
                case SolutionCheck.Kind.AnyCloneNamed:
                    return FindObjectsByType<Transform>().Any(tr => tr.name == c.text);
                case SolutionCheck.Kind.NoCloneNamed:
                    return !FindObjectsByType<Transform>().Any(tr => tr.name == c.text);
                case SolutionCheck.Kind.PlayerNear:
                    var p = GameObject.Find("Player");
                    return p != null && c.target != null && Vector3.Distance(p.transform.position, c.target.transform.position) < 3f;
                case SolutionCheck.Kind.Rotated:
                    return c.target != null && Quaternion.Angle(c.target.transform.rotation, c.startRotation) > 10f;
            }
            return false;
        }

        public static string Describe(SolutionCheck c)
        {
            string n = c.target != null ? c.target.name : (c.kind == SolutionCheck.Kind.Destroyed ? "(destroyed)" : "(none)");
            return c.kind + " " + n + (string.IsNullOrEmpty(c.text) ? "" : " [" + c.text + "]");
        }

        void Report(string stage, string step, bool ok, string what)
        {
            if (ok) passes++; else failures++;
            Log($"{(ok ? "PASS" : "FAIL")}  {stage} / {step} :: {what}");
        }

        void Fail(string stage, string step, string what) => Report(stage, step, false, what);

        void Log(string s)
        {
            lines.Add(s);
            Debug.Log("[AutoPilot] " + s);
        }

        void Finish()
        {
            string path = null;
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-autopilotLog");
            if (i >= 0 && i + 1 < args.Length) path = args[i + 1];
            if (string.IsNullOrEmpty(path)) path = Path.Combine(Application.persistentDataPath, "autopilot.txt");
            try { File.WriteAllLines(path, lines); } catch (Exception e) { Debug.LogWarning(e.Message); }
            int code = failures > 0 ? 1 : 0;
#if UNITY_EDITOR
            UnityEditor.SessionState.EraseBool(EditorFlag);
            string editorLog = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Logs", "autopilot_editor.txt"));
            try { File.WriteAllLines(editorLog, lines); } catch { }
            UnityEditor.EditorApplication.isPlaying = false;
            if (Application.isBatchMode) UnityEditor.EditorApplication.Exit(code);
#else
            Application.Quit(code);
#endif
        }
    }
}
