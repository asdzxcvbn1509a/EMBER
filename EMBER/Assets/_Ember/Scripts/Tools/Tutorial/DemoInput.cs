using System;
using UnityEngine;

namespace Ember
{
    /// <summary>
    /// Virtual keyboard of the tutorial pilot. The key states feed the on-screen key panel and the timeline;
    /// H / V replicate Input.GetAxis("Horizontal" / "Vertical") of the project's Input Manager
    /// (keys only: gravity 3, sensitivity 3, snap on, dead zone 0.001), so the ember gets exactly the
    /// torque a player holding the same keys would give it.
    /// </summary>
    public class DemoInput
    {
        public enum Key { W, A, S, D, Space, R, Esc, F }
        public const int Count = 8;

        readonly bool[] down = new bool[Count];
        readonly float[] releaseAt = new float[Count];
        float h, v;

        public event Action<Key, bool> Changed;

        public float H => Mathf.Abs(h) < 0.001f ? 0f : h;
        public float V => Mathf.Abs(v) < 0.001f ? 0f : v;
        public bool IsDown(Key k) => down[(int)k];

        public int Mask
        {
            get
            {
                int m = 0;
                for (int i = 0; i < Count; i++) if (down[i]) m |= 1 << i;
                return m;
            }
        }

        public void Set(Key k, bool d)
        {
            int i = (int)k;
            releaseAt[i] = 0f;
            if (down[i] == d) return;
            down[i] = d;
            Changed?.Invoke(k, d);
        }

        /// <summary>Press, then release after a short time (Space, R, Esc, F).</summary>
        public void Tap(Key k, float seconds = 0.18f)
        {
            Set(k, true);
            releaseAt[(int)k] = GameClock.UnscaledTime + seconds;
        }

        /// <summary>One axis from a direction: +1 presses pos, -1 presses neg, 0 releases both.</summary>
        public void Axis(Key pos, Key neg, int dir)
        {
            Set(pos, dir > 0);
            Set(neg, dir < 0);
        }

        public void ReleaseMovement()
        {
            Set(Key.W, false);
            Set(Key.A, false);
            Set(Key.S, false);
            Set(Key.D, false);
        }

        /// <summary>Axes straight to zero (after a teleport or respawn: hands off the keys, no torque carried over).</summary>
        public void ResetAxes() => h = v = 0f;

        public void ReleaseAll()
        {
            for (int i = 0; i < Count; i++) Set((Key)i, false);
            h = v = 0f;
        }

        /// <summary>Once per frame: timed releases, then the axis ramps.</summary>
        public void Tick(float dt)
        {
            float now = GameClock.UnscaledTime;
            for (int i = 0; i < Count; i++)
                if (releaseAt[i] > 0f && now >= releaseAt[i]) Set((Key)i, false);
            h = Ramp(h, down[(int)Key.D], down[(int)Key.A], dt);
            v = Ramp(v, down[(int)Key.W], down[(int)Key.S], dt);
        }

        static float Ramp(float value, bool pos, bool neg, float dt)
        {
            float target = (pos ? 1f : 0f) - (neg ? 1f : 0f);
            if (target != 0f)
            {
                if (value != 0f && Mathf.Sign(target) != Mathf.Sign(value)) value = 0f;   // snap
                return Mathf.MoveTowards(value, target, 3f * dt);                           // sensitivity
            }
            return Mathf.MoveTowards(value, 0f, 3f * dt);                                   // gravity
        }
    }
}
