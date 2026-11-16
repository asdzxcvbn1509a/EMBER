using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ember
{
    [Serializable] public class TlSegment { public string id, scene, result = "RUN", note = ""; public int start, end = -1, retries; }
    [Serializable] public class TlCaption { public string id; public int on, off = -1; }
    [Serializable] public class TlHold { public string reason; public int start, end = -1; }
    [Serializable] public class TlKey { public int f; public string k; public int d; }
    [Serializable] public class TlEvent { public int f; public string t, v; }
    [Serializable] public class TlCheck { public int f; public string seg, label, what; public bool pass; }

    /// <summary>Everything the post-production scripts (ArtSource/Tutorial) need to know about a take.</summary>
    [Serializable]
    public class TimelineData
    {
        public int version = 1;
        public string mode;
        public string started, finished, exit = "";
        public int fps = 30;
        public float step;
        public int width, height, frames, quality;
        public string framePattern = "frames/f_{0:D6}.jpg (frame index + 1)";
        public string audioFile = "";
        public int audioRate, audioChannels;
        public long audioSampleFrames;
        public int audioEmptyFrames;
        public float wallSeconds, captureFps;
        public bool partial;
        /// <summary>On-screen key panel in canvas pixels (1920x1080 reference, origin bottom-left): x0, y0, x1, y1.</summary>
        public float[] keysRect = new float[0];
        public List<TlSegment> segments = new List<TlSegment>();
        public List<TlCaption> captions = new List<TlCaption>();
        public List<TlHold> holds = new List<TlHold>();
        public List<TlKey> keys = new List<TlKey>();
        public List<TlEvent> events = new List<TlEvent>();
        public List<TlCheck> checks = new List<TlCheck>();
        public List<string> warnings = new List<string>();
    }

    /// <summary>Collects the take's timeline (written as timeline.json), one row of state per frame (frames.tsv) and a log.</summary>
    public class TutorialTimeline
    {
        /// <summary>Bitmask of the on-screen keys (DemoInput.Key order).</summary>
        public static Func<int> KeysMask;
        /// <summary>Fake cursor: x, y in canvas pixels, z = 1 when visible.</summary>
        public static Func<Vector3> CursorState;

        public readonly TimelineData data = new TimelineData();
        readonly TutorialRecorder rec;
        readonly StreamWriter rows, log;
        readonly Dictionary<string, TlCaption> openCaptions = new Dictionary<string, TlCaption>();
        TlHold openHold;
        TlSegment openSegment;
        string rowScene;
        CanvasGroup faderGroup;
        Transform ball;
        Rigidbody ballBody;

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public TutorialTimeline(TutorialRecorder rec, string dir)
        {
            this.rec = rec;
            data.mode = rec.Mode.ToString();
            data.started = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv);
            data.step = rec.Step;
            data.fps = Mathf.RoundToInt(1f / rec.Step);
            rows = new StreamWriter(Path.Combine(dir, "frames.tsv"), false, new UTF8Encoding(false));
            rows.WriteLine("f\tscene\tfade\tfr\tfg\tfb\tcard\thint\ttoast\tpause\tphoto\tkeys\tcur\tcx\tcy\tbx\tby\tbz\tspeed\tts\taud");
            log = new StreamWriter(Path.Combine(dir, "log.txt"), false, new UTF8Encoding(false)) { AutoFlush = true };
        }

        int F => rec.Frame;

        public void Log(string s)
        {
            Debug.Log("[Tutorial] " + s);
            log.WriteLine($"{F,6}  {s}");
        }

        public void Warn(string s)
        {
            data.warnings.Add($"f{F}: {s}");
            Log("WARNING " + s);
        }

        public void Event(string type, string value)
        {
            data.events.Add(new TlEvent { f = F, t = type, v = value ?? "" });
            Log($"event {type} {value}");
        }

        public void Key(string key, bool down) => data.keys.Add(new TlKey { f = F, k = key, d = down ? 1 : 0 });

        public void Check(string seg, string label, string what, bool pass)
        {
            data.checks.Add(new TlCheck { f = F, seg = seg, label = label, what = what, pass = pass });
            Log($"{(pass ? "PASS" : "FAIL")}  {seg} / {label} :: {what}");
        }

        // ---------------------------------------------------------------- captions
        public void CaptionOn(string id)
        {
            if (openCaptions.ContainsKey(id)) return;
            var c = new TlCaption { id = id, on = F };
            data.captions.Add(c);
            openCaptions[id] = c;
            Log("caption on  " + id);
        }

        public void CaptionOff(string id)
        {
            if (!openCaptions.TryGetValue(id, out var c)) return;
            c.off = F;
            openCaptions.Remove(id);
            Log("caption off " + id);
        }

        public bool CaptionVisible(string id) => openCaptions.ContainsKey(id);

        /// <summary>Seconds the caption has been on screen (0 when it is not shown).</summary>
        public float CaptionAge(string id) => openCaptions.TryGetValue(id, out var c) ? (F - c.on) * rec.Step : 0f;

        public IEnumerable<string> OpenCaptions => new List<string>(openCaptions.Keys);

        // ---------------------------------------------------------------- holds (black screen) and segments
        public void HoldBegin(string reason)
        {
            if (openHold != null) return;
            openHold = new TlHold { start = F, reason = reason };
            data.holds.Add(openHold);
        }

        public void HoldEnd()
        {
            if (openHold == null) return;
            openHold.end = F;
            openHold = null;
        }

        public void SegmentBegin(string id, string scene)
        {
            SegmentEnd("PASS");
            openSegment = new TlSegment { id = id, scene = scene, start = F };
            data.segments.Add(openSegment);
            Log($"== segment {id} ({scene})");
        }

        public void SegmentEnd(string result, int retries = 0, string note = "")
        {
            if (openSegment == null) return;
            openSegment.end = F;
            openSegment.result = result;
            openSegment.retries = retries;
            openSegment.note = note ?? "";
            Log($"== segment {openSegment.id} {result} {(openSegment.end - openSegment.start) * rec.Step:0.0}s {note}");
            openSegment = null;
        }

        // ---------------------------------------------------------------- per-frame state
        public void Row()
        {
            string scene = SceneManager.GetActiveScene().name;
            var gm = GameManager.Instance;
            if (scene != rowScene || ball == null)
            {
                rowScene = scene;
                faderGroup = gm != null && gm.fader != null ? gm.fader.GetComponent<CanvasGroup>() : null;
                var p = GameObject.Find("Player");
                ball = p != null ? p.transform : null;
                ballBody = p != null ? p.GetComponent<Rigidbody>() : null;
            }
            float fade = faderGroup != null ? faderGroup.alpha : 0f;
            Color fc = gm != null && gm.fader != null && gm.fader.image != null ? gm.fader.image.color : Color.black;
            var hud = gm != null ? gm.hud : null;
            float card = hud != null && hud.stageCard != null && hud.stageCard.gameObject.activeInHierarchy ? hud.stageCard.alpha : 0f;
            float hint = hud != null && hud.controlsHint != null ? hud.controlsHint.alpha : 0f;
            float toast = hud != null && hud.toastGroup != null ? hud.toastGroup.alpha : 0f;
            int pause = hud != null && hud.pausePanel != null && hud.pausePanel.activeInHierarchy ? 1 : 0;
            int photo = hud != null && hud.photoHint != null && hud.photoHint.activeInHierarchy ? 1 : 0;
            int keys = KeysMask != null ? KeysMask() : 0;
            Vector3 cur = CursorState != null ? CursorState() : Vector3.zero;
            Vector3 b = ball != null ? ball.position : Vector3.zero;
            float speed = ballBody != null ? ballBody.linearVelocity.magnitude : 0f;
            rows.WriteLine(string.Format(Inv, "{0}\t{1}\t{2:0.###}\t{3:0.##}\t{4:0.##}\t{5:0.##}\t{6:0.###}\t{7:0.###}\t{8:0.###}\t{9}\t{10}\t{11}\t{12}\t{13:0.#}\t{14:0.#}\t{15:0.###}\t{16:0.###}\t{17:0.###}\t{18:0.###}\t{19:0.##}\t{20}",
                F, scene, fade, fc.r, fc.g, fc.b, card, hint, toast, pause, photo, keys, cur.z > 0.5f ? 1 : 0, cur.x, cur.y, b.x, b.y, b.z, speed, Time.timeScale, rec.LastAudioSamples));
        }

        public void Finish(string exit, float wallSeconds)
        {
            foreach (var id in new List<string>(openCaptions.Keys)) CaptionOff(id);
            HoldEnd();
            SegmentEnd(openSegment != null ? "ABORTED" : "PASS");
            data.frames = F;
            data.exit = exit ?? "";
            data.finished = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv);
            data.wallSeconds = wallSeconds;
            data.captureFps = wallSeconds > 0f ? F / wallSeconds : 0f;
            rows.Flush();
            rows.Dispose();
            Log($"finished: {F} frames, {wallSeconds:0.0} s wall, {data.captureFps:0.0} frames/s, exit {exit}");
            log.Dispose();
        }

        public void Save(string dir) => File.WriteAllText(Path.Combine(dir, "timeline.json"), JsonUtility.ToJson(data, true), new UTF8Encoding(false));
    }
}
