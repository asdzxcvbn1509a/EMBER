using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Ember
{
    /// <summary>
    /// Per-scene game flow for EMBER: fade-in, checkpoints and respawn, ember shards,
    /// pause, photo mode and faded scene loading. Puzzles themselves are driven by the
    /// class package's Condition/Action scripts (HitAnd*); this only provides the frame.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [Header("Stage")]
        [Tooltip("0 = title, 1..3 = stages, 4 = ending")]
        public int stageNumber;
        public string stageTitle = "";
        public string stageSubtitle = "";
        public int shardsInStage = 3;

        [Header("Ambient light (applied at runtime, no lighting bake needed)")]
        public Color ambient = new Color(0.09f, 0.10f, 0.16f);

        [Header("References (found automatically when empty)")]
        public Transform player;
        public Transform startPoint;
        public ScreenFader fader;
        public Hud hud;

        public const int ShardsTotal = 9;
        static readonly HashSet<string> collected = new HashSet<string>();
        public static float SessionStartTime = -1f;
        public static int ShardsCollected => collected.Count;
        public static bool HasShard(string id) => collected.Contains(id);

        public bool Paused { get; private set; }
        public Transform Checkpoint => checkpoint;

        Transform checkpoint;
        Rigidbody playerBody;
        bool loading, respawning;
        static bool frameRateConfigured;

        void Awake()
        {
            Instance = this;
            if (SessionStartTime < 0f) SessionStartTime = GameClock.Realtime;
            ConfigureFrameRate();
            if (RenderSettings.skybox != null) RenderSettings.skybox = new Material(RenderSettings.skybox);
            SetAmbient(ambient);
            if (player == null)
            {
                var p = GameObject.Find("Player");
                if (p != null) player = p.transform;
            }
            if (player != null) playerBody = player.GetComponent<Rigidbody>();
            if (fader == null) fader = FindAnyObjectByType<ScreenFader>();
            if (hud == null) hud = FindAnyObjectByType<Hud>();
        }

        void Start()
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
            if (stageNumber == 0) ResetSession();
            if (fader != null) { fader.SetColor(Color.black); fader.FadeIn(1.4f); }
            if (hud != null && stageNumber >= 1 && stageNumber <= 3) hud.ShowStageCard(stageTitle, stageSubtitle);
            if (startPoint != null) checkpoint = startPoint;
            else if (player != null)
            {
                var go = new GameObject("StartPoint");
                go.transform.position = player.position;
                checkpoint = go.transform;
            }
            Cursor.visible = stageNumber == 0 || stageNumber == 4;
            Cursor.lockState = CursorLockMode.None;
        }

        void Update()
        {
            if (loading || ExternalInput.Blocked) return;
            bool inStage = stageNumber >= 1 && stageNumber <= 3;
            if (inStage && Input.GetKeyDown(KeyCode.Escape)) SetPaused(!Paused);
            if (Paused) return;
            if (inStage && Input.GetKeyDown(KeyCode.R)) Respawn();
            if (Input.GetKeyDown(KeyCode.F))
            {
                var pm = FindAnyObjectByType<PhotoMode>();
                if (pm != null) pm.Toggle();
            }
        }

        // ---------------------------------------------------------------- frame pacing
        static void ConfigureFrameRate()
        {
            if (frameRateConfigured) return;
            frameRateConfigured = true;
            // CameraFollow (class script) moves in FixedUpdate at 60 Hz, so render at 60 fps too.
            double hz = Screen.currentResolution.refreshRateRatio.value;
            if (hz > 55.0 && hz < 65.0) { QualitySettings.vSyncCount = 1; Application.targetFrameRate = -1; }
            else { QualitySettings.vSyncCount = 0; Application.targetFrameRate = 60; }
        }

        public static void SetAmbient(Color c)
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = c;
            var sh = new SphericalHarmonicsL2();
            sh.AddAmbientLight(c);
            RenderSettings.ambientProbe = sh;
        }

        // ---------------------------------------------------------------- checkpoints
        public void SetCheckpoint(Transform t, bool notify = true)
        {
            if (t == null || checkpoint == t) return;
            checkpoint = t;
            if (notify && hud != null) hud.Toast("CHECKPOINT");
        }

        public void Respawn()
        {
            if (!respawning && player != null && checkpoint != null) StartCoroutine(RespawnRoutine());
        }

        IEnumerator RespawnRoutine()
        {
            respawning = true;
            if (fader != null)
            {
                fader.SetColor(Color.black);
                fader.FadeOut(0.25f);
                yield return new WaitForSeconds(0.25f);
            }
            Vector3 p = checkpoint.position + Vector3.up * 0.6f;
            if (playerBody != null)
            {
                playerBody.linearVelocity = Vector3.zero;
                playerBody.angularVelocity = Vector3.zero;
                playerBody.position = p;
            }
            player.position = p;
            Physics.SyncTransforms();
            var follow = FindAnyObjectByType<CameraFollow>();
            if (follow != null) follow.transform.position = p + follow.offset;
            yield return null;
            if (fader != null) fader.FadeIn(0.45f);
            respawning = false;
        }

        // ---------------------------------------------------------------- shards
        public int ShardsInThisStage
        {
            get
            {
                int n = 0;
                string prefix = "S" + stageNumber + "_";
                foreach (var id in collected) if (id.StartsWith(prefix)) n++;
                return n;
            }
        }

        public void CollectShard(string id)
        {
            if (!collected.Add(id)) return;
            if (hud != null) { hud.RefreshShards(); hud.Toast("EMBER SHARD  " + ShardsInThisStage + " / " + shardsInStage); }
        }

        public static void ResetSession()
        {
            collected.Clear();
            SessionStartTime = GameClock.Realtime;
        }

        // ---------------------------------------------------------------- pause & scenes
        public void SetPaused(bool p)
        {
            Paused = p;
            Time.timeScale = p ? 0f : 1f;
            AudioListener.pause = p;
            Cursor.visible = p || stageNumber == 0 || stageNumber == 4;
            if (hud != null) hud.ShowPause(p);
        }

        public void LoadScene(string sceneName, float fade = 0.9f)
        {
            if (!loading) StartCoroutine(LoadRoutine(sceneName, fade));
        }

        IEnumerator LoadRoutine(string sceneName, float fade)
        {
            loading = true;
            Time.timeScale = 1f;
            AudioListener.pause = false;
            if (fader != null && fade > 0f)
            {
                fader.FadeOut(fade);
                yield return GameClock.WaitRealtime(fade);
            }
            SceneManager.LoadScene(sceneName);
        }
    }
}
