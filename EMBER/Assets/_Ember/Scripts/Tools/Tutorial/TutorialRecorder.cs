using System;
using System.Collections;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Ember
{
    /// <summary>
    /// Frame-locked recorder for the gameplay tutorial video (ArtSource/Tutorial turns its output into the MP4).
    /// Started only by the command-line flags in TutorialArgs, so normal play is untouched.
    ///  * Time.captureDeltaTime = 2 physics steps: the game advances exactly 1/30 s per rendered frame however
    ///    long a frame takes, and GameClock makes UI fades and scene-load waits follow the same clock.
    ///  * Record / Spike: every frame the finished back buffer (game + overlay UI) is read with
    ///    ScreenCapture.CaptureScreenshotAsTexture and encoded to JPEG on worker threads (frames/f_NNNNNN.jpg),
    ///    and the game's own mix is taken from AudioRenderer in sync with the frames (game.wav, 32-bit float).
    ///  * Dry: nothing is captured; the director runs as fast as the CPU allows and only the timeline is written.
    /// A controller component drives the game: TutorialDirector (Record, Dry) or TutorialSpike (Spike).
    /// </summary>
    [DefaultExecutionOrder(32000)]
    public class TutorialRecorder : MonoBehaviour
    {
        public static TutorialRecorder Instance { get; private set; }

        public TutorialMode Mode { get; private set; }
        /// <summary>Index of the frame being produced (file f_{Frame + 1}.jpg).</summary>
        public int Frame { get; private set; }
        public float Step { get; private set; }
        public string OutDir { get; private set; }
        public TutorialTimeline Timeline { get; private set; }
        public bool Capturing => Mode == TutorialMode.Record || Mode == TutorialMode.Spike;
        public bool Finished { get; private set; }
        /// <summary>Spike test: also encode the frame with this index on the main thread (main_NNNNNN.jpg).</summary>
        public int MainThreadEncodeFrame = -1;
        /// <summary>Sample frames the audio renderer delivered for the last captured frame (frames.tsv column "aud").</summary>
        public int LastAudioSamples { get; private set; }

        // video
        string framesDir;
        int quality;
        BlockingCollection<Job> jobs;
        Thread[] workers;
        readonly ConcurrentBag<byte[]> pool = new ConcurrentBag<byte[]>();
        volatile string workerError;
        int written;
        // audio
        WavWriter wav;
        int channels;
        bool audioOn;
        // run state
        bool stopped;
        float wallStart;
        const float MaxWallSeconds = 50f * 60f;

        struct Job
        {
            public byte[] data;
            public GraphicsFormat format;
            public int width, height;
            public string path;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var mode = TutorialArgs.Mode;
            if (mode == TutorialMode.None || Instance != null) return;
            var go = new GameObject("TutorialRecorder");
            DontDestroyOnLoad(go);
            var rec = go.AddComponent<TutorialRecorder>();
            rec.Begin(mode);
            if (rec.Finished) return;
            var controller = Type.GetType(mode == TutorialMode.Spike ? "Ember.TutorialSpike" : "Ember.TutorialDirector");
            if (controller == null) { rec.Abort(5, "controller component missing"); return; }
            go.AddComponent(controller);
        }

        void Awake() => Instance = this;

        void Begin(TutorialMode mode)
        {
            Mode = mode;
            string dir = TutorialArgs.OutDir;
            if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Application.persistentDataPath, "tutorial_" + mode.ToString().ToLowerInvariant());
            OutDir = Path.GetFullPath(dir);
            Directory.CreateDirectory(OutDir);
            Step = 2f * Time.fixedDeltaTime;   // 30 fps: a whole number of 60 Hz physics steps per frame
            Time.captureDeltaTime = Step;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            UnityEngine.Random.InitState(20261116);
            ExternalInput.Blocked = true;
            wallStart = Time.realtimeSinceStartup;
            Timeline = new TutorialTimeline(this, OutDir);
            Timeline.Log($"mode {mode}, out {OutDir}, step {Step:0.000000}, screen {Screen.width}x{Screen.height}, " +
                         $"batch {Application.isBatchMode}, graphics {SystemInfo.graphicsDeviceType}, unity {Application.unityVersion}");
            if (!Capturing) return;

            float freeGB = FreeGB(OutDir);
            Timeline.Log($"free disk {freeGB:0.0} GB (need {TutorialArgs.MinFreeGB:0.0})");
            if (freeGB >= 0f && freeGB < TutorialArgs.MinFreeGB) { Abort(6, $"only {freeGB:0.0} GB free"); return; }

            framesDir = Path.Combine(OutDir, "frames");
            if (Directory.Exists(framesDir))
                foreach (var f in Directory.GetFiles(framesDir, "*.jpg")) File.Delete(f);
            Directory.CreateDirectory(framesDir);
            quality = TutorialArgs.Quality;
            Timeline.data.quality = quality;
            jobs = new BlockingCollection<Job>(12);
            int n = Mathf.Clamp(SystemInfo.processorCount - 3, 2, 6);
            workers = new Thread[n];
            for (int i = 0; i < n; i++)
            {
                workers[i] = new Thread(Work) { IsBackground = true, Name = "TutorialJpeg" + i };
                workers[i].Start();
            }
            if (!TutorialArgs.NoAudio)
            {
                var cfg = AudioSettings.GetConfiguration();
                channels = SpeakerChannels(cfg.speakerMode);
                int rate = AudioSettings.outputSampleRate;
                audioOn = AudioRenderer.Start();
                if (audioOn)
                {
                    wav = new WavWriter(Path.Combine(OutDir, "game.wav"), rate, channels);
                    Timeline.data.audioFile = "game.wav";
                    Timeline.data.audioRate = rate;
                    Timeline.data.audioChannels = channels;
                }
                Timeline.Log($"audio renderer {(audioOn ? "on" : "FAILED")}: {rate} Hz, {channels} ch ({cfg.speakerMode}), dsp buffer {cfg.dspBufferSize}");
            }
            StartCoroutine(CaptureLoop());
        }

        static int SpeakerChannels(AudioSpeakerMode m)
        {
            switch (m)
            {
                case AudioSpeakerMode.Mono: return 1;
                case AudioSpeakerMode.Quad: return 4;
                case AudioSpeakerMode.Surround: return 5;
                case AudioSpeakerMode.Mode5point1: return 6;
                case AudioSpeakerMode.Mode7point1: return 8;
                default: return 2;
            }
        }

        static float FreeGB(string path)
        {
            try { return new DriveInfo(Path.GetPathRoot(path)).AvailableFreeSpace / (1024f * 1024f * 1024f); }
            catch { return -1f; }
        }

        // ---------------------------------------------------------------- per frame
        IEnumerator CaptureLoop()
        {
            var eof = new WaitForEndOfFrame();
            while (!stopped)
            {
                yield return eof;
                if (stopped) break;
                CaptureAudio();
                CaptureVideo();
                EndFrame();
            }
        }

        void LateUpdate()
        {
            // without capture there is no WaitForEndOfFrame in batch mode: count frames here
            if (!Capturing && !stopped) EndFrame();
        }

        void EndFrame()
        {
            Timeline.Row();
            Frame++;
        }

        void Update()
        {
            if (stopped) return;
            if (workerError != null) Abort(4, "JPEG worker failed: " + workerError);
            else if (Time.realtimeSinceStartup - wallStart > MaxWallSeconds) Abort(3, "wall-clock limit reached");
        }

        void CaptureAudio()
        {
            LastAudioSamples = 0;
            if (!audioOn) return;
            int n = AudioRenderer.GetSampleCountForCaptureFrame();
            LastAudioSamples = n;
            if (n <= 0) { Timeline.data.audioEmptyFrames++; return; }
            var buf = new NativeArray<float>(n * channels, Allocator.Temp);
            if (AudioRenderer.Render(buf)) wav.Write(buf, n * channels);
            else { wav.WriteSilence(n); Timeline.data.audioEmptyFrames++; }
            buf.Dispose();
        }

        void CaptureVideo()
        {
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            if (tex == null) { Timeline.Warn("no screenshot for frame " + Frame); return; }
            if (Timeline.data.width == 0)
            {
                Timeline.data.width = tex.width;
                Timeline.data.height = tex.height;
                Timeline.Log($"capture {tex.width}x{tex.height} {tex.graphicsFormat}, quality {quality}, {workers.Length} JPEG workers");
            }
            if (tex.width != Timeline.data.width || tex.height != Timeline.data.height)
            {
                Destroy(tex);
                Abort(8, $"screen size changed to {tex.width}x{tex.height} during the take");
                return;
            }
            string need = TutorialArgs.RequireSize;
            if (Frame == 2 && !string.IsNullOrEmpty(need) && need != $"{tex.width}x{tex.height}")
            {
                Destroy(tex);
                Abort(8, $"screen is {tex.width}x{tex.height}, the take needs {need}");
                return;
            }
            var raw = tex.GetRawTextureData<byte>();
            if (!pool.TryTake(out var buf) || buf.Length != raw.Length) buf = new byte[raw.Length];
            NativeArray<byte>.Copy(raw, buf, raw.Length);
            var job = new Job
            {
                data = buf, format = tex.graphicsFormat, width = tex.width, height = tex.height,
                path = Path.Combine(framesDir, $"f_{Frame + 1:D6}.jpg")
            };
            if (Frame == MainThreadEncodeFrame)
            {
                var jpg = ImageConversion.EncodeArrayToJPG(buf, job.format, (uint)job.width, (uint)job.height, 0, quality);
                File.WriteAllBytes(Path.Combine(OutDir, $"main_{Frame + 1:D6}.jpg"), jpg);
            }
            Destroy(tex);
            // waits while the workers are behind (the game waits too, so the video does not care)
            while (!jobs.TryAdd(job, 250))
                if (workerError != null) { Abort(4, "JPEG worker failed: " + workerError); return; }
        }

        void Work()
        {
            try
            {
                foreach (var j in jobs.GetConsumingEnumerable())
                {
                    var jpg = ImageConversion.EncodeArrayToJPG(j.data, j.format, (uint)j.width, (uint)j.height, 0, quality);
                    File.WriteAllBytes(j.path, jpg);
                    pool.Add(j.data);
                    Interlocked.Increment(ref written);
                }
            }
            catch (Exception e)
            {
                workerError = e.GetType().Name + ": " + e.Message;
            }
        }

        // ---------------------------------------------------------------- end
        /// <summary>Ends the take: flushes frames and audio, writes timeline.json and quits with the exit code.</summary>
        public void Finish(int code, string reason)
        {
            if (Finished) return;
            Finished = true;
            stopped = true;
            if (jobs != null)
            {
                jobs.CompleteAdding();
                foreach (var t in workers) t.Join();
            }
            if (audioOn)
            {
                AudioRenderer.Stop();
                Timeline.data.audioSampleFrames = wav.SampleFrames;
                wav.Dispose();
            }
            Timeline.data.partial = !string.IsNullOrEmpty(TutorialArgs.From) || !string.IsNullOrEmpty(TutorialArgs.To);
            if (Capturing) Timeline.Log($"{written} JPEG frame(s) written");
            Timeline.Finish($"{code} {reason}", Time.realtimeSinceStartup - wallStart);
            Timeline.Save(OutDir);
            Time.captureDeltaTime = 0f;
            ExternalInput.Blocked = false;
#if UNITY_EDITOR
            UnityEditor.SessionState.EraseString(TutorialArgs.SessionKey);
            UnityEditor.EditorApplication.isPlaying = false;
            if (Application.isBatchMode) UnityEditor.EditorApplication.Exit(code);
#else
            Application.Quit(code);
#endif
        }

        public void Abort(int code, string reason)
        {
            if (Timeline != null) Timeline.Warn("ABORT " + reason);
            Debug.LogError("[Tutorial] ABORT " + reason);
            Finish(code, reason);
        }

        void OnApplicationQuit()
        {
            if (!Finished && Timeline != null) Finish(7, "application quit before the end");
        }
    }
}
