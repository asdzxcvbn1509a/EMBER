using System.Collections;
using UnityEngine;

namespace Ember
{
    /// <summary>
    /// Unscaled and real time for UI fades, menu effects, scene-load waits and the play timer.
    /// In normal play these return exactly Time.unscaledDeltaTime / unscaledTime / realtimeSinceStartup.
    /// While a frame-locked recording runs (Time.captureDeltaTime > 0, set by the tutorial recorder) they
    /// advance by one capture step per rendered frame instead of by wall-clock time, so a fade lasts the
    /// same number of video frames however long each frame takes to render and save.
    /// </summary>
    public static class GameClock
    {
        static int frame0 = -1;
        static double unscaled0, real0;
        static float step;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => frame0 = -1;   // statics survive editor play sessions (domain reload is off)

        /// <summary>True while Time.captureDeltaTime locks the frame rate (recording or dry run).</summary>
        public static bool Locked
        {
            get
            {
                float dt = Time.captureDeltaTime;
                if (dt <= 0f) { frame0 = -1; return false; }
                if (frame0 < 0 || dt != step)
                {
                    unscaled0 = Time.unscaledTimeAsDouble;
                    real0 = Time.realtimeSinceStartupAsDouble;
                    frame0 = Time.frameCount;
                    step = dt;
                }
                return true;
            }
        }

        public static float UnscaledDeltaTime => Locked ? step : Time.unscaledDeltaTime;
        public static float UnscaledTime => Locked ? (float)(unscaled0 + (Time.frameCount - frame0) * (double)step) : Time.unscaledTime;
        public static float Realtime => Locked ? (float)(real0 + (Time.frameCount - frame0) * (double)step) : Time.realtimeSinceStartup;

        /// <summary>Like WaitForSecondsRealtime, but counts captured frames while recording.</summary>
        public static IEnumerator WaitRealtime(float seconds)
        {
            float end = Realtime + seconds;
            while (Realtime < end) yield return null;
        }
    }
}
