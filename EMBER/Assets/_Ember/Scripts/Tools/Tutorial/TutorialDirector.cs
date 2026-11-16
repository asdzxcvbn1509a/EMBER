using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ember
{
    /// <summary>
    /// Plays the tutorial script (TutorialScript.cs) in the running game while TutorialRecorder captures it.
    /// Building blocks used by the script:
    ///  * captions:  Say(id) / Unsay() / Hold() - ids and texts are in ArtSource/Tutorial/captions.json; the video edit
    ///               draws them later, the take only records when each one is on screen (and holds it long enough to read)
    ///  * the ember: Go / Pass (routes), JumpOnto (predicted jumps), Ride (moving platforms), keys via DemoInput
    ///  * menus:     CursorTo / Click with the overlay cursor
    ///  * dips:      Dip(...) fades the game's own Fader to black, does off-camera work (teleports, Solve(step) like
    ///               the AutoPilot), then fades back in; the timeline marks the black hold for the sound mix
    ///  * checks:    Expect(step) evaluates the AutoPilot checks of a StageSolution step
    /// Every segment runs under a guard: a failure is logged, the game is put back in a playable state and the next
    /// segment runs. The exit code is the number of failed segments (0 = a clean take).
    /// </summary>
    public partial class TutorialDirector : MonoBehaviour
    {
        class Segment
        {
            public string id, scene;
            public Vector3? start;
            public string[] requires = new string[0];
            public Func<IEnumerator> body;
            public float budget = 90f;
        }

        [Serializable] class CaptionEntry { public string id, text, kind, pos; public float hold; }
        [Serializable] class CaptionFile { public CaptionEntry[] captions; }

        TutorialRecorder rec;
        TutorialTimeline tl;
        DemoPilot pilot;
        DemoInput input;
        TutorialOverlay overlay;
        readonly Dictionary<string, float> holds = new Dictionary<string, float>();
        string caption;
        int failures;
        float sceneStart;

        // ---------------------------------------------------------------- run
        IEnumerator Start()
        {
            rec = GetComponent<TutorialRecorder>();
            tl = rec.Timeline;
            pilot = gameObject.AddComponent<DemoPilot>();
            input = pilot.input;
            overlay = gameObject.AddComponent<TutorialOverlay>();
            overlay.input = input;
            TutorialTimeline.KeysMask = () => input.Mask;
            TutorialTimeline.CursorState = overlay.State;
            tl.data.keysRect = TutorialOverlay.KeysRect;
            input.Changed += (k, d) => tl.Key(k.ToString(), d);
            pilot.OnTeleport = s => tl.Event(s.StartsWith("warp") ? "warp" : s.StartsWith("respawn-unplanned") ? "respawn-unplanned" : "respawn", s);
            SceneManager.sceneLoaded += OnSceneLoaded;
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
            LoadCaptions();

            var script = Script();
            int from = 0, to = script.Count - 1;
            if (!string.IsNullOrEmpty(TutorialArgs.From)) from = Mathf.Max(0, script.FindIndex(s => s.id == TutorialArgs.From));
            if (!string.IsNullOrEmpty(TutorialArgs.To)) { int i = script.FindIndex(s => s.id == TutorialArgs.To); if (i >= 0) to = i; }
            tl.Log($"script: {script.Count} segments, running {script[from].id}..{script[to].id}");
            yield return null;
            yield return Guarded(Sequence(script, from, to), e => { failures++; tl.Warn("director: " + e); });
            string summary = $"{failures} failed segment(s), {pilot.UnexpectedRespawns} unplanned respawn(s), {pilot.Respawns} respawn(s), {pilot.Warps} warp(s)";
            tl.Log(summary);
            rec.Finish(failures, summary);
        }

        void OnDestroy() => SceneManager.sceneLoaded -= OnSceneLoaded;

        IEnumerator Sequence(List<Segment> list, int from, int to)
        {
            if (from > 0)
            {
                string err = null;
                yield return Guarded(FastForward(list[from]), e => err = Describe(e));
                if (err != null) { failures++; tl.Warn("fast-forward failed: " + err); Recover(); }
            }
            for (int i = from; i <= to && !rec.Finished; i++)
            {
                var s = list[i];
                tl.SegmentBegin(s.id, SceneManager.GetActiveScene().name);
                int unplanned = pilot.UnexpectedRespawns;
                string err = null;
                yield return Guarded(s.body(), e => err = Describe(e), s.budget);
                if (err == null && pilot.UnexpectedRespawns > unplanned) err = "unplanned respawn during the segment";
                if (err == null) tl.SegmentEnd("PASS");
                else
                {
                    failures++;
                    tl.SegmentEnd("FAIL", 0, err);
                    Recover();
                    // put the game where the next segment expects it, so one failure does not hide the rest
                    if (i < to && !rec.Finished)
                    {
                        string ffErr = null;
                        yield return Guarded(FastForward(list[i + 1]), e => ffErr = Describe(e));
                        if (ffErr != null) { tl.Warn("fast-forward failed: " + ffErr); Recover(); }
                    }
                }
            }
        }

        static string Describe(Exception e) => e is TutorialFail ? e.Message : e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace;

        /// <summary>Runs a coroutine tree inline (nested IEnumerators included) and catches exceptions at any depth.</summary>
        IEnumerator Guarded(IEnumerator root, Action<Exception> onError, float budget = 0f)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(root);
            float start = GameClock.UnscaledTime;
            while (stack.Count > 0)
            {
                if (rec.Finished) yield break;
                object current;
                try
                {
                    if (budget > 0f && GameClock.UnscaledTime - start > budget) throw new TutorialFail($"over the time budget ({budget:0} s)");
                    var top = stack.Peek();
                    if (!top.MoveNext()) { stack.Pop(); continue; }
                    current = top.Current;
                }
                catch (Exception e)
                {
                    onError(e);
                    yield break;
                }
                if (current is IEnumerator nested) { stack.Push(nested); continue; }
                yield return current;
            }
        }

        /// <summary>After a failed segment: leave the game in a normal, visible, unpaused state.</summary>
        void Recover()
        {
            Time.timeScale = 1f;
            var gm = GameManager.Instance;
            if (gm != null && gm.Paused) gm.SetPaused(false);
            var photo = FindAnyObjectByType<PhotoMode>();
            if (photo != null && photo.active) photo.Toggle();
            pilot.Stop();
            pilot.ExpectRespawn = false;
            Unsay();
            overlay.ShowCursor(false);
            tl.HoldEnd();
            if (gm != null && gm.fader != null && gm.fader.GetComponent<CanvasGroup>().alpha > 0.5f) gm.fader.FadeIn(0.45f);
        }

        /// <summary>-tutorialFrom: load the segment's scene, solve its prerequisites off-camera, place the ember.</summary>
        IEnumerator FastForward(Segment s)
        {
            tl.Log("fast-forward to " + s.id);
            if (SceneManager.GetActiveScene().name != s.scene)
            {
                SceneManager.LoadScene(s.scene);
                yield return WaitScene(s.scene, 15f);
            }
            yield return Wait(0.3f);
            Time.timeScale = 4f;
            foreach (var label in s.requires) yield return Solve(label);
            Time.timeScale = 1f;
            if (s.start.HasValue)
            {
                pilot.Teleport(s.start.Value);
                NearestCheckpoint();
            }
            yield return Wait(0.6f);
        }

        void NearestCheckpoint()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;
            Transform best = null;
            float bestZ = float.MinValue, z = pilot.Position.z + 1.5f;
            foreach (var cp in FindObjectsByType<Checkpoint>(FindObjectsSortMode.None))
            {
                var t = cp.spawnPoint != null ? cp.spawnPoint : cp.transform;
                if (t.position.z <= z && t.position.z > bestZ) { best = t; bestZ = t.position.z; }
            }
            if (best != null) gm.SetCheckpoint(best, false);
        }

        void OnSceneLoaded(Scene s, LoadSceneMode m)
        {
            sceneStart = GameClock.UnscaledTime;
            tl.Event("scene", s.name);
            var sol = FindAnyObjectByType<StageSolution>();
            if (sol == null) return;
            foreach (var st in sol.steps)
                foreach (var c in st.checks)
                    if (c.target != null)
                    {
                        c.startPosition = c.target.transform.position;
                        c.startRotation = c.target.transform.rotation;
                    }
        }

        // ---------------------------------------------------------------- waiting
        IEnumerator Wait(float seconds)
        {
            float end = GameClock.UnscaledTime + seconds;
            while (GameClock.UnscaledTime < end) yield return null;
        }

        /// <summary>Waits game time (follows Time.timeScale; physics-driven events).</summary>
        IEnumerator WaitGame(float seconds)
        {
            float end = Time.time + seconds;
            while (Time.time < end) yield return null;
        }

        IEnumerator Until(Func<bool> condition, float timeout, string what)
        {
            float end = GameClock.UnscaledTime + timeout;
            while (!condition())
            {
                if (GameClock.UnscaledTime > end) throw new TutorialFail("timeout: " + what);
                yield return null;
            }
        }

        /// <summary>Waits until this many seconds have passed since the current scene loaded.</summary>
        IEnumerator SceneTime(float seconds)
        {
            while (GameClock.UnscaledTime - sceneStart < seconds) yield return null;
        }

        IEnumerator WaitScene(string scene, float timeout)
        {
            yield return Until(() => SceneManager.GetActiveScene().name == scene, timeout, "scene " + scene);
            yield return null;
            yield return null;
        }

        // ---------------------------------------------------------------- captions
        void LoadCaptions()
        {
            var candidates = new List<string>();
            if (!string.IsNullOrEmpty(TutorialArgs.Captions)) candidates.Add(TutorialArgs.Captions);
            candidates.Add(Path.Combine(Application.dataPath, "..", "..", "ArtSource", "Tutorial", "captions.json"));        // editor
            candidates.Add(Path.Combine(Application.dataPath, "..", "..", "..", "ArtSource", "Tutorial", "captions.json"));  // Build/EMBER player
            foreach (var path in candidates)
            {
                if (!File.Exists(path)) continue;
                var file = JsonUtility.FromJson<CaptionFile>(File.ReadAllText(path));
                if (file?.captions == null) continue;
                foreach (var c in file.captions) holds[c.id] = c.hold > 0f ? c.hold : AutoHold(c.text);
                tl.Log($"captions: {holds.Count} from {Path.GetFullPath(path)}");
                return;
            }
            tl.Warn("captions.json not found: default hold times");
        }

        /// <summary>Reading time: about 16 Thai characters per second (combining marks and spaces not counted) plus 0.7 s.</summary>
        static float AutoHold(string text)
        {
            if (string.IsNullOrEmpty(text)) return 3f;
            int n = 0;
            foreach (char ch in text)
            {
                if (ch == '[' || ch == '{' || ch == ']' || ch == '}') continue;
                if (char.IsWhiteSpace(ch)) continue;
                if (ch == 'ั' || (ch >= 'ิ' && ch <= 'ฺ') || (ch >= '็' && ch <= '๎')) continue;
                n++;
            }
            return Mathf.Max(2.3f, n / 16f + 0.7f);
        }

        float HoldOf(string id) => holds.TryGetValue(id, out float h) ? h : 3.5f;

        void Say(string id)
        {
            Unsay();
            caption = id;
            tl.CaptionOn(id);
            if (!holds.ContainsKey(id)) tl.Warn("caption id not in captions.json: " + id);
        }

        void Unsay()
        {
            if (caption == null) return;
            tl.CaptionOff(caption);
            caption = null;
        }

        /// <summary>Waits until the current caption has been readable for its hold time.</summary>
        IEnumerator Hold()
        {
            string id = caption;
            if (id == null) yield break;
            float need = HoldOf(id);
            while (caption == id && tl.CaptionAge(id) < need) yield return null;
        }

        IEnumerator SayHold(string id)
        {
            Say(id);
            yield return Hold();
        }

        // ---------------------------------------------------------------- overlay
        void Keys(bool on) => overlay.ShowKeys(on && !TutorialArgs.NoKeys);

        IEnumerator CursorTo(string path, float seconds, float fx = -1f, float fy = 0.5f)
        {
            if (fx < 0f)
            {
                var go = GameObject.Find(path);
                var fx2 = go != null ? go.GetComponent<MenuButtonFX>() : null;
                fx = fx2 == null || fx2.side == MenuButtonFX.Side.Center ? 0.5f : fx2.side == MenuButtonFX.Side.Left ? 0.28f : 0.72f;
            }
            if (!overlay.CanvasPointOf(path, fx, fy, out var pos)) throw new TutorialFail("no UI element " + path);
            yield return overlay.MoveTo(pos, seconds);
            yield return Wait(0.06f);
        }

        IEnumerator Click(float after = 0.3f)
        {
            yield return Wait(0.12f);
            string name = overlay.Hovered != null ? overlay.Hovered.name : "(nothing)";
            if (!overlay.Click()) throw new TutorialFail("click with nothing under the cursor");
            tl.Event("click", name);
            yield return Wait(after);
        }

        // ---------------------------------------------------------------- the ember
        IEnumerator Drive(PilotRoute r, float timeout = 0f)
        {
            string scene = SceneManager.GetActiveScene().name;
            int warps = pilot.Warps, respawns = pilot.Respawns;
            pilot.Drive(r);
            float slowest = float.MaxValue;
            foreach (var s in r.speeds) slowest = Mathf.Min(slowest, s);
            float limit = timeout > 0f ? timeout : 6f + 2f * r.Length / Mathf.Max(0.6f, slowest);
            float end = GameClock.UnscaledTime + limit;
            while (!pilot.Arrived)
            {
                if (SceneManager.GetActiveScene().name != scene) yield break;   // a portal loaded the next scene
                if (pilot.Warps != warps) yield break;                          // a ring moved the ember
                if (pilot.Respawns != respawns) throw new TutorialFail($"fell (respawned) on the way to {DemoPilot.Fmt(r.End)}");
                if (pilot.Failure != null) throw new TutorialFail(pilot.Failure);
                if (GameClock.UnscaledTime > end) throw new TutorialFail($"timeout ({limit:0} s) on the way to {DemoPilot.Fmt(r.End)} at {DemoPilot.Fmt(pilot.Position)}");
                yield return null;
            }
        }

        static Vector3 V(float x, float z) => new Vector3(x, 0f, z);

        /// <summary>Rolls along the points and stops at the last one.</summary>
        IEnumerator Go(float speed, params Vector3[] pts) => Drive(new PilotRoute(speed, true, pts));

        /// <summary>Like Go, with tight key timing (narrow beams, edges).</summary>
        IEnumerator Careful(float speed, params Vector3[] pts) => Drive(new PilotRoute(speed, true, pts) { precise = true, tolerance = 0.25f });

        /// <summary>Brakes to a standstill where the ember is (small tops: the shard pillar).</summary>
        IEnumerator Settle() => Drive(new PilotRoute(0.5f, true, pilot.Position) { precise = true, tolerance = 0.7f }, 4f);

        /// <summary>Rolls through the points without stopping (into rings and portals).</summary>
        IEnumerator Pass(float speed, params Vector3[] pts) => Drive(new PilotRoute(speed, false, pts) { tolerance = 0.5f });

        /// <summary>A planned fall: rolls along the points until the kill zone respawns the ember at the checkpoint.</summary>
        IEnumerator FallFrom(float speed, params Vector3[] pts)
        {
            int before = pilot.Respawns;
            pilot.ExpectRespawn = true;
            pilot.Drive(new PilotRoute(speed, false, pts) { tolerance = 0.2f });
            yield return Until(() => pilot.Respawns > before, 8f, "planned fall");
            pilot.Stop();
            yield return Wait(0.5f);
        }

        IEnumerator PressR()
        {
            int before = pilot.Respawns;
            pilot.ExpectRespawn = true;
            pilot.Stop();
            input.Tap(DemoInput.Key.R);
            GameManager.Instance.Respawn();
            yield return Until(() => pilot.Respawns > before, 3f, "R respawn");
            yield return Wait(0.5f);
        }

        /// <summary>
        /// Rolls along the approach and jumps as soon as the predicted landing is inside landHalf of the target area
        /// (centre / velocity may move: rafts). Fails when no jump window comes or the ember lands outside the block.
        /// </summary>
        IEnumerator JumpOnto(string what, Func<Vector3> center, Vector2 blockHalf, Vector2 landHalf, float top, Func<Vector3> velocity,
            PilotRoute approach, float timeout = 6f, Func<bool> abort = null, float airBrake = 0f)
        {
            pilot.Drive(approach);
            Vector3 heading = DemoPilot.Flat(approach.End - pilot.Position).normalized;
            float end = GameClock.UnscaledTime + timeout;
            string last = "";
            int frames = 0;
            while (true)
            {
                if (GameClock.UnscaledTime > end) { pilot.Stop(); throw new TutorialFail("no jump window: " + what + " | " + last); }
                if (abort != null && abort()) { pilot.Stop(); throw new TutorialFail("jump window missed: " + what + " | " + last); }
                if (pilot.Failure != null) throw new TutorialFail(pilot.Failure + " (" + what + ")");
                Vector3 vel = velocity != null ? velocity() : Vector3.zero;
                bool ok = pilot.PredictJump(center(), blockHalf, landHalf, top, vel, out var land, out float t);
                last = $"at {DemoPilot.Fmt(pilot.Position)} v {DemoPilot.Fmt(pilot.Velocity)} grounded {pilot.Grounded} -> lands {DemoPilot.Fmt(land)} {(ok ? "inside" : "outside")}";
                if (frames++ % 6 == 0) tl.Log("jump " + what + ": " + last);
                if (pilot.Grounded && ok)
                {
                    pilot.Jump();
                    tl.Event("jump", $"{what}: landing {DemoPilot.Fmt(land)} in {t:0.00} s");
                    break;
                }
                if (approach.stop && pilot.Arrived) throw new TutorialFail("approach ended without a jump: " + what);
                yield return null;
            }
            yield return Until(() => !pilot.Grounded, 0.6f, "take-off: " + what);
            if (airBrake > 0f)
            {
                // reverse spin in the air (torque only spins a flying ball): it grips and slows down on landing
                pilot.Manual();
                bool alongX = Mathf.Abs(heading.x) > Mathf.Abs(heading.z);
                var key = alongX ? (heading.x > 0f ? DemoInput.Key.A : DemoInput.Key.D) : (heading.z > 0f ? DemoInput.Key.S : DemoInput.Key.W);
                input.Set(key, true);
                yield return Wait(airBrake);
                input.Set(key, false);
            }
            yield return Until(() => pilot.Grounded, 3f, "landing: " + what);
            Vector3 c = center(), p = pilot.Position;
            if (Mathf.Abs(p.x - c.x) > blockHalf.x || Mathf.Abs(p.z - c.z) > blockHalf.y || p.y < top + 0.25f)
                throw new TutorialFail($"missed {what}: landed at {DemoPilot.Fmt(p)}");
            tl.Event("landed", $"{what} at {DemoPilot.Fmt(p)}");
        }

        /// <summary>Holds the ember on a moving platform (offset from its centre) until the condition holds.</summary>
        IEnumerator Ride(Rigidbody platform, Vector3 offset, Func<bool> until, float timeout, string what)
        {
            pilot.Track(() => platform.position + offset, platform);
            yield return Until(() => platform != null && until(), timeout, what);
        }

        // ---------------------------------------------------------------- off-camera work
        /// <summary>
        /// Fades to black with the game's Fader, runs the work (timeScale = fast), snaps the camera and fades back in.
        /// card: a caption shown over the black screen for its hold time.
        /// </summary>
        IEnumerator Dip(Func<IEnumerator> work, string reason, float fast = 1f, string card = null)
        {
            var gm = GameManager.Instance;
            Unsay();
            if (gm != null && gm.fader != null) { gm.fader.SetColor(Color.black); gm.fader.FadeOut(0.35f); }
            yield return Wait(0.4f);
            tl.HoldBegin(reason);
            if (card != null) Say(card);
            Time.timeScale = fast;
            yield return work();
            Time.timeScale = 1f;
            pilot.SnapCamera();
            yield return Wait(0.2f);
            if (card != null) { yield return Hold(); Unsay(); yield return Wait(0.25f); }
            pilot.SnapCamera();
            tl.HoldEnd();
            if (gm != null && gm.fader != null) gm.fader.FadeIn(0.45f);
            yield return Wait(0.35f);
        }

        IEnumerator TeleportTo(Vector3 position, float settle = 0.3f)
        {
            pilot.Teleport(position);
            yield return WaitGame(settle);
            pilot.SnapCamera();
        }

        SolutionStep FindStep(string label)
        {
            var sol = FindAnyObjectByType<StageSolution>();
            return sol != null ? sol.steps.Find(s => s.label == label) : null;
        }

        /// <summary>AutoPilot-style: teleport onto the step's trigger and wait (used only while the screen is black).</summary>
        IEnumerator Solve(string label)
        {
            var step = FindStep(label);
            if (step == null) throw new TutorialFail("no StageSolution step " + label);
            if (step.moveTo != null) pilot.Teleport(step.moveTo.position + Vector3.up * 0.7f);
            float start = Time.time;
            while (Time.time < start + step.wait)
            {
                if (pilot.Body != null && Time.time < start + step.wait * 0.5f)
                    pilot.Body.linearVelocity = new Vector3(0f, pilot.Body.linearVelocity.y, 0f);   // parked on the trigger
                yield return null;
            }
            Check(label);
        }

        /// <summary>Evaluates a StageSolution step's checks now; returns true when all pass (results go to the timeline).</summary>
        bool Check(string label, bool log = true)
        {
            var step = FindStep(label);
            if (step == null) { if (log) tl.Check(CurrentSegment, label, "step missing", false); return false; }
            bool all = true;
            foreach (var c in step.checks)
            {
                bool ok = AutoPilot.Evaluate(c);
                all &= ok;
                if (log) tl.Check(CurrentSegment, label, AutoPilot.Describe(c), ok);
            }
            return all;
        }

        /// <summary>Waits until every check of the step passes (fails the segment after the timeout).</summary>
        IEnumerator Expect(string label, float timeout = 4f)
        {
            float end = GameClock.UnscaledTime + timeout;
            while (!Check(label, false))
            {
                if (GameClock.UnscaledTime > end) { Check(label); throw new TutorialFail("expected result missing: " + label); }
                yield return null;
            }
            Check(label);
        }

        string CurrentSegment => tl.data.segments.Count > 0 ? tl.data.segments[tl.data.segments.Count - 1].id : "";

        static bool AnyNamed(string name)
        {
            foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None)) if (t.name == name) return true;
            return false;
        }

        static T Find<T>(string name) where T : Component
        {
            var go = GameObject.Find(name);
            if (go == null) throw new TutorialFail("object not found: " + name);
            var c = go.GetComponent<T>();
            if (c == null) throw new TutorialFail($"{name} has no {typeof(T).Name}");
            return c;
        }
    }
}
