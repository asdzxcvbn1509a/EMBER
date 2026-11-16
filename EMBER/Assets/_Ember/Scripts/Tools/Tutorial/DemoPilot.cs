using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ember
{
    /// <summary>Thrown inside the tutorial script when something did not happen as planned.</summary>
    public class TutorialFail : Exception
    {
        public TutorialFail(string message) : base(message) { }
    }

    /// <summary>A path on the ground (Unity x / z; y is ignored) with a speed limit per leg.</summary>
    public class PilotRoute
    {
        public readonly List<Vector3> points = new List<Vector3>();
        public readonly List<float> speeds = new List<float>();
        /// <summary>Brake to a standstill at the last point (otherwise roll on through it).</summary>
        public bool stop = true;
        /// <summary>Arrival radius in metres.</summary>
        public float tolerance = 0.35f;
        /// <summary>Tighter key hysteresis for narrow beams and edges.</summary>
        public bool precise;
        float[] cumulative;

        public PilotRoute(float speed, bool stop, params Vector3[] pts)
        {
            this.stop = stop;
            foreach (var p in pts) { points.Add(new Vector3(p.x, 0f, p.z)); speeds.Add(speed); }
        }

        public void Prepend(Vector3 p)
        {
            points.Insert(0, new Vector3(p.x, 0f, p.z));
            speeds.Insert(0, speeds.Count > 0 ? speeds[0] : 2f);
            cumulative = null;
        }

        public Vector3 End => points[points.Count - 1];

        public float Length { get { Build(); return cumulative[cumulative.Length - 1]; } }

        void Build()
        {
            if (cumulative != null && cumulative.Length == points.Count) return;
            cumulative = new float[points.Count];
            for (int i = 1; i < points.Count; i++) cumulative[i] = cumulative[i - 1] + Vector3.Distance(points[i - 1], points[i]);
        }

        /// <summary>Arc length of the closest point on the path to p.</summary>
        public float Project(Vector3 p)
        {
            Build();
            p.y = 0f;
            if (points.Count == 1) return 0f;
            float best = float.MaxValue, s = 0f;
            for (int i = 0; i + 1 < points.Count; i++)
            {
                Vector3 a = points[i], b = points[i + 1], ab = b - a;
                float len2 = ab.sqrMagnitude;
                float t = len2 > 1e-6f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2) : 0f;
                float d = (a + ab * t - p).sqrMagnitude;
                if (d < best) { best = d; s = cumulative[i] + Mathf.Sqrt(len2) * t; }
            }
            return s;
        }

        public Vector3 At(float s)
        {
            Build();
            if (s <= 0f) return points[0];
            for (int i = 0; i + 1 < points.Count; i++)
                if (s <= cumulative[i + 1])
                {
                    float seg = cumulative[i + 1] - cumulative[i];
                    return Vector3.Lerp(points[i], points[i + 1], seg > 1e-6f ? (s - cumulative[i]) / seg : 1f);
                }
            return End;
        }

        public float SpeedAt(float s)
        {
            Build();
            for (int i = 0; i + 1 < points.Count; i++) if (s <= cumulative[i + 1]) return speeds[i + 1];
            return speeds[speeds.Count - 1];
        }
    }

    /// <summary>
    /// Drives the ember for the tutorial video the way a player would: it presses virtual keys (DemoInput) and
    /// applies exactly BallController's torque (the class script is switched off, as PhotoMode does), so all
    /// motion is real physics. Route following uses pure pursuit with a braking-distance speed limit; jumps go
    /// through CanJumpBuffered.RequestJump. It also notices warps (teleport rings) and respawns.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class DemoPilot : MonoBehaviour
    {
        public enum Mode { Manual, Route, Track }

        public readonly DemoInput input = new DemoInput();
        public Rigidbody Body { get; private set; }
        public CanJumpBuffered Jumper { get; private set; }
        public Mode CurrentMode { get; private set; }
        public bool Arrived { get; private set; }
        /// <summary>Set when a route cannot be completed (stuck); cleared by the next Drive call.</summary>
        public string Failure { get; private set; }
        public int Warps { get; private set; }
        public int Respawns { get; private set; }
        public int UnexpectedRespawns { get; private set; }
        public bool ExpectRespawn;
        public Action<string> OnTeleport;

        /// <summary>Deceleration (m/s^2) assumed when planning stops; the ball manages about 7 with full reverse torque, the key logic less.</summary>
        public float brake = 3f;
        public float trackGain = 1.6f, trackMax = 1.8f;
        const float Latency = 0.22f, ArriveSpeed = 0.3f, StuckSeconds = 3.5f;

        BallController ball;
        float power = 10f, maxSpeed = 15f, radius = 0.5f;
        PilotRoute route;
        Func<Vector3> trackPos;
        Rigidbody trackBody;
        Vector3 trackPrev, trackVelocity;
        Vector3 lastPos;
        bool hasLast, ownTeleport;
        float stuckTimer, bestProgress;
        AxisState ax, az;

        struct AxisState { public int dir; public float tapUntil, cool; }

        void OnEnable() => SceneManager.sceneLoaded += OnScene;
        void OnDisable() => SceneManager.sceneLoaded -= OnScene;
        void Start() => Acquire();
        void OnScene(Scene s, LoadSceneMode m) => Acquire();

        public void Acquire()
        {
            var p = GameObject.Find("Player");
            Body = p != null ? p.GetComponent<Rigidbody>() : null;
            Jumper = p != null ? p.GetComponent<CanJumpBuffered>() : null;
            ball = p != null ? p.GetComponent<BallController>() : null;
            if (ball != null) { power = ball.power; maxSpeed = ball.maxSpeed; ball.enabled = false; }
            var sphere = p != null ? p.GetComponent<SphereCollider>() : null;
            if (sphere != null) radius = sphere.radius * p.transform.lossyScale.x;
            Stop();
            hasLast = false;
            trackBody = null;
        }

        public Vector3 Position => Body != null ? Body.position : Vector3.zero;
        public Vector3 Velocity => Body != null ? Body.linearVelocity : Vector3.zero;
        public float Speed => Flat(Velocity).magnitude;
        public bool Grounded => Jumper != null && Jumper.IsGrounded;

        /// <summary>Clearly in the air: no ground contact (CanJumpBuffered), not asleep, nothing solid just below.</summary>
        public bool Airborne
        {
            get
            {
                if (Body == null || Grounded || Body.IsSleeping()) return false;
                return !Physics.Raycast(Body.position, Vector3.down, 0.62f, ~0, QueryTriggerInteraction.Ignore);
            }
        }
        public Vector3 TrackVelocity => trackVelocity;

        /// <summary>
        /// The horizontal velocity the ember will roll at once friction has matched its spin to its motion
        /// (solid sphere: 5/7 of the linear velocity + 2/7 of the spin's rolling velocity, relative to a carrying
        /// platform). Hard braking reverses the spin while the ball still slides forward, so steering on this
        /// instead of the raw velocity avoids rolling back; torque changes it linearly (10 N m / 0.7 = 14.3 m/s^2).
        /// </summary>
        public Vector3 SettleVelocity
        {
            get
            {
                if (Body == null) return Vector3.zero;
                Vector3 w = Body.angularVelocity;
                Vector3 floor = CurrentMode == Mode.Track ? Flat(trackVelocity) : Vector3.zero;
                Vector3 spin = new Vector3(-w.z * radius, 0f, w.x * radius);
                return floor + (Flat(Body.linearVelocity) - floor) * (5f / 7f) + spin * (2f / 7f);
            }
        }

        // ---------------------------------------------------------------- commands
        public void Stop()
        {
            CurrentMode = Mode.Manual;
            route = null;
            trackPos = null;
            input.ReleaseMovement();
            ax = az = default;
        }

        /// <summary>Hands the keys to the script (no steering).</summary>
        public void Manual() => Stop();

        public void Drive(PilotRoute r)
        {
            route = r;
            route.Prepend(Position);
            CurrentMode = Mode.Route;
            Arrived = false;
            Failure = null;
            stuckTimer = 0f;
            bestProgress = 0f;
        }

        /// <summary>Holds the ember on a moving target (a slab or a raft), matching its velocity.</summary>
        public void Track(Func<Vector3> target, Rigidbody platform)
        {
            trackPos = target;
            if (platform != trackBody)
            {
                trackBody = platform;
                trackPrev = platform != null ? platform.position : Vector3.zero;
                trackVelocity = Vector3.zero;
            }
            CurrentMode = Mode.Track;
            Arrived = false;
            Failure = null;
        }

        public void Jump()
        {
            input.Tap(DemoInput.Key.Space, 0.16f);
            if (Jumper != null) Jumper.RequestJump();
        }

        /// <summary>Moves the ember (and the camera) instantly; used only while the screen is black.</summary>
        public void Teleport(Vector3 position)
        {
            if (Body == null) return;
            Stop();
            input.ResetAxes();
            ownTeleport = true;
            Body.linearVelocity = Vector3.zero;
            Body.angularVelocity = Vector3.zero;
            Body.position = position;
            Body.transform.position = position;
            Physics.SyncTransforms();
            SnapCamera();
            lastPos = position;
            hasLast = true;
        }

        public void SnapCamera()
        {
            var follow = FindAnyObjectByType<CameraFollow>();
            if (follow == null || Body == null) return;
            Vector3 target = Body.position;
            follow.transform.position = target + follow.offset;
            follow.transform.rotation = Quaternion.LookRotation(target - follow.transform.position, Vector3.up);
        }

        // ---------------------------------------------------------------- frame loop
        void Update()
        {
            if (ball != null && ball.enabled) ball.enabled = false;   // PhotoMode.Toggle turns it back on
            if (Body != null && !Body.isKinematic)
            {
                // torque only spins a ball in the air and the spin throws it sideways on landing: hands off while airborne
                bool airborne = Airborne;
                if (airborne && CurrentMode != Mode.Manual) { input.ReleaseMovement(); ax = az = default; }
                else if (CurrentMode == Mode.Route && route != null) SteerRoute();
                else if (CurrentMode == Mode.Track && trackPos != null) SteerTrack();
            }
            input.Tick(GameClock.UnscaledDeltaTime);
        }

        void FixedUpdate()
        {
            if (Body == null) return;
            DetectTeleport();
            if (trackBody != null)
            {
                trackVelocity = (trackBody.position - trackPrev) / Time.fixedDeltaTime;
                trackPrev = trackBody.position;
            }
            if (Body.isKinematic) return;
            // identical to BallController.FixedUpdate, with the virtual axes
            Body.maxAngularVelocity = maxSpeed;
            float h = input.H, v = input.V;
            if (h != 0f) Body.AddTorque(-Vector3.forward * h * power);
            if (v != 0f) Body.AddTorque(Vector3.right * v * power);
        }

        void DetectTeleport()
        {
            Vector3 p = Body.position;
            if (hasLast && !ownTeleport && (p - lastPos).sqrMagnitude > 2.5f * 2.5f)
            {
                var gm = GameManager.Instance;
                bool atCheckpoint = gm != null && gm.Checkpoint != null && Vector3.Distance(p, gm.Checkpoint.position + Vector3.up * 0.6f) < 1.0f;
                string kind;
                if (atCheckpoint)
                {
                    Respawns++;
                    if (!ExpectRespawn) UnexpectedRespawns++;
                    kind = ExpectRespawn ? "respawn" : "respawn-unplanned";
                    ExpectRespawn = false;
                }
                else
                {
                    Warps++;
                    kind = "warp";
                }
                Stop();
                input.ResetAxes();
                OnTeleport?.Invoke($"{kind} {Fmt(lastPos)} -> {Fmt(p)}");
            }
            ownTeleport = false;
            lastPos = p;
            hasLast = true;
        }

        // ---------------------------------------------------------------- steering
        void SteerRoute()
        {
            Vector3 p = Body.position;
            Vector3 v = SettleVelocity;
            float s = route.Project(p);
            Vector3 end = route.End;
            float distEnd = Flat(end - p).magnitude;
            float remaining = Mathf.Max(0f, route.Length - s);

            if (route.stop && distEnd < route.tolerance)
            {
                // inside the stop radius: brake to a standstill, then hand back the keys
                if (v.magnitude < ArriveSpeed && Speed < ArriveSpeed + 0.1f)
                {
                    Arrived = true;
                    input.ReleaseMovement();
                    CurrentMode = Mode.Manual;
                    return;
                }
                Steer(-v, true);
                return;
            }
            if (!route.stop && distEnd < route.tolerance) Arrived = true;

            float cap = route.SpeedAt(s);
            if (route.stop)
            {
                // past the end (overshoot) the path length left is 0: steer back on the straight distance
                float left = remaining > 0.05f ? Mathf.Min(remaining, distEnd) : distEnd;
                float d = left - 0.08f - v.magnitude * Latency;
                cap = Mathf.Min(cap, Mathf.Sqrt(2f * brake * Mathf.Max(0f, d)));
            }
            float look = Mathf.Clamp(0.6f + 0.4f * v.magnitude, 0.8f, 2.4f);
            Vector3 aim = distEnd < look ? end : route.At(s + look);
            Vector3 dir = Flat(aim - p);
            dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.zero;
            Steer(dir * cap - v, route.precise);

            // stuck: no progress along the path for a while
            if (s > bestProgress + 0.3f) { bestProgress = s; stuckTimer = 0f; }
            else if (!Arrived && distEnd > route.tolerance && route.Length > 0.6f)
            {
                stuckTimer += Time.deltaTime;
                if (stuckTimer > StuckSeconds) Failure = $"stuck at {Fmt(p)} going to {Fmt(end)}";
            }
        }

        void SteerTrack()
        {
            Vector3 p = Body.position;
            Vector3 d = Flat(trackPos() - p);
            Vector3 want = Flat(trackVelocity) + Vector3.ClampMagnitude(d * trackGain, trackMax);
            Steer(want - SettleVelocity, true);
            Arrived = d.magnitude < 0.3f;
        }

        void Steer(Vector3 err, bool precise)
        {
            input.Axis(DemoInput.Key.D, DemoInput.Key.A, Control(err.x, input.H, ref ax, precise));
            input.Axis(DemoInput.Key.W, DemoInput.Key.S, Control(err.z, input.V, ref az, precise));
        }

        /// <summary>
        /// Key choice for one axis from the velocity error (m/s). The Input Manager axis ramps at 3 per second both
        /// ways, so a released key keeps pushing for a while: a large error is held and released early enough that the
        /// ramp-down finishes the job; a small error gets one tap whose length matches it (one frame ~ 0.05 m/s);
        /// after a release the next decision waits until the axis is back at zero.
        /// </summary>
        static int Control(float e, float axis, ref AxisState c, bool precise)
        {
            float now = GameClock.UnscaledTime;
            float big = 0.45f, small = precise ? 0.06f : 0.12f;
            if (c.dir != 0)
            {
                if (c.tapUntil > 0f)                                  // tapping
                {
                    if (now < c.tapUntil) return c.dir;
                    c.dir = 0;
                    c.tapUntil = 0f;
                    c.cool = now + Mathf.Abs(axis) / 3f + 0.02f;
                    return 0;
                }
                // holding: let go when the ramp-down alone will cover the rest (or the error changed sign)
                if (Mathf.Sign(e) != c.dir || Mathf.Abs(e) <= Residual(axis))
                {
                    c.dir = 0;
                    c.cool = now + Mathf.Abs(axis) / 3f + 0.02f;
                }
                return c.dir;
            }
            if (now < c.cool) return 0;
            float ae = Mathf.Abs(e);
            if (ae > big) { c.dir = e > 0f ? 1 : -1; c.tapUntil = 0f; return c.dir; }
            if (ae > small)
            {
                c.dir = e > 0f ? 1 : -1;
                c.tapUntil = now + Mathf.Clamp(Mathf.Sqrt(0.0233f * ae), 0.033f, 0.2f) - 0.001f;
                return c.dir;
            }
            return 0;
        }

        /// <summary>Settle velocity the axis still adds while it ramps down from this value.</summary>
        static float Residual(float axis)
        {
            float u = Mathf.Abs(axis);
            return 2.38f * u * u;   // 14.3 m/s^2 per unit of axis, axis falling at 3 per second
        }

        // ---------------------------------------------------------------- jumps
        /// <summary>
        /// Simulates a jump that starts on the next physics step with the current velocity (gravity, linear damping).
        /// Returns true when the centre comes down to topY + 0.5 inside landHalf around the area centre (which moves at
        /// areaVel) without first hitting the side of the block (blockHalf). landing / time: where and when it comes down.
        /// </summary>
        public bool PredictJump(Vector3 areaCenter, Vector2 blockHalf, Vector2 landHalf, float topY, Vector3 areaVel, out Vector3 landing, out float time)
        {
            const float r = 0.5f;
            float dt = Time.fixedDeltaTime;
            float k = Body.linearDamping;
            Vector3 g = Physics.gravity;
            Vector3 v = Body.linearVelocity;
            Vector3 p = Body.position + v * dt;
            v.y = Jumper != null ? Jumper.jumpVelocity : 6.3f;
            landing = p;
            time = 0f;
            for (int i = 1; i < 240; i++)
            {
                v += g * dt;
                v *= 1f / (1f + k * dt);
                p += v * dt;
                float t = i * dt;
                Vector3 c = areaCenter + areaVel * t;
                float dx = Mathf.Abs(p.x - c.x), dz = Mathf.Abs(p.z - c.z);
                if (v.y < 0f && p.y <= topY + r)
                {
                    landing = p;
                    time = t;
                    return dx <= landHalf.x && dz <= landHalf.y;
                }
                // the ball overlaps the block's edge while its bottom is still below the top: it hits the side
                if (p.y < topY + r - 0.04f && dx < blockHalf.x + r * 0.95f && dz < blockHalf.y + r * 0.95f && (dx > blockHalf.x || dz > blockHalf.y))
                {
                    landing = p;
                    time = t;
                    return false;
                }
            }
            return false;
        }

        // ---------------------------------------------------------------- helpers
        public static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
        public static string Fmt(Vector3 p) => $"({p.x:0.0}, {p.y:0.0}, {p.z:0.0})";
    }
}
