using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Ember.EditorTools
{
    /// <summary>
    /// Legacy AnimationClips for the class script HitAndPlayAnimation (it drives the legacy
    /// Animation component, so every clip here is marked legacy). One-shot clips use ClampForever
    /// so doors and bridges hold their final pose.
    /// </summary>
    public static class AnimFactory
    {
        public static void Run()
        {
            EmberPaths.Ensure(EmberPaths.Anims);
            PlateDown(); PlateUp();
            GateOpen(); GateClose();
            ForgeGateOpen(); ForgeGateClose();
            HammerLoop();
            SunRise();
            AssetDatabase.SaveAssets();
        }

        public static AnimationClip Load(string name) => AssetDatabase.LoadAssetAtPath<AnimationClip>($"{EmberPaths.Anims}/{name}.anim");

        public static AnimationClip New(string name, WrapMode wrap) =>
            new AnimationClip { name = name, legacy = true, wrapMode = wrap, frameRate = 60f };

        public static AnimationClip Save(AnimationClip clip)
        {
            EmberPaths.Ensure(EmberPaths.Anims);
            string path = $"{EmberPaths.Anims}/{clip.name}.anim";
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(clip, existing);
                existing.legacy = true;
                EditorUtility.SetDirty(existing);
                return existing;
            }
            AssetDatabase.CreateAsset(clip, path);
            return clip;
        }

        static AnimationCurve Ease(float t0, float v0, float t1, float v1) => AnimationCurve.EaseInOut(t0, v0, t1, v1);

        static AnimationCurve Overshoot(float t0, float v0, float t1, float v1, float amount)
        {
            var c = new AnimationCurve(new Keyframe(t0, v0, 0f, 0f),
                new Keyframe(Mathf.Lerp(t0, t1, 0.72f), v1 + amount * Mathf.Sign(v1 - v0)),
                new Keyframe(t1, v1, 0f, 0f));
            c.SmoothTangents(1, 0f);
            return c;
        }

        public static void Pos(AnimationClip c, string path, Vector3 a, Vector3 b, float t0, float t1, float overshoot = 0f)
        {
            c.SetCurve(path, typeof(Transform), "localPosition.x", Ease(t0, a.x, t1, b.x));
            c.SetCurve(path, typeof(Transform), "localPosition.y", overshoot > 0f ? Overshoot(t0, a.y, t1, b.y, overshoot) : Ease(t0, a.y, t1, b.y));
            c.SetCurve(path, typeof(Transform), "localPosition.z", Ease(t0, a.z, t1, b.z));
        }

        /// <summary>Quaternion curves sampled from an euler function (legacy clips need localRotation.xyzw).</summary>
        public static void Rot(AnimationClip c, string path, float duration, System.Func<float, Vector3> euler, float step = 1f / 30f)
        {
            var kx = new List<Keyframe>(); var ky = new List<Keyframe>(); var kz = new List<Keyframe>(); var kw = new List<Keyframe>();
            Quaternion prev = Quaternion.identity;
            bool first = true;
            for (float t = 0f; t <= duration + 1e-4f; t += step)
            {
                float tt = Mathf.Min(t, duration);
                Quaternion q = Quaternion.Euler(euler(tt));
                if (!first && Quaternion.Dot(prev, q) < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                prev = q; first = false;
                kx.Add(new Keyframe(tt, q.x)); ky.Add(new Keyframe(tt, q.y)); kz.Add(new Keyframe(tt, q.z)); kw.Add(new Keyframe(tt, q.w));
            }
            c.SetCurve(path, typeof(Transform), "localRotation.x", Smooth(kx));
            c.SetCurve(path, typeof(Transform), "localRotation.y", Smooth(ky));
            c.SetCurve(path, typeof(Transform), "localRotation.z", Smooth(kz));
            c.SetCurve(path, typeof(Transform), "localRotation.w", Smooth(kw));
        }

        static AnimationCurve Smooth(List<Keyframe> keys)
        {
            var c = new AnimationCurve(keys.ToArray());
            for (int i = 0; i < c.length; i++) c.SmoothTangents(i, 0f);
            return c;
        }

        public static void Events(AnimationClip c, params (float time, string sfx)[] events)
        {
            var list = new List<AnimationEvent>();
            foreach (var (time, sfx) in events)
                list.Add(new AnimationEvent { time = time, functionName = "PlaySfx", stringParameter = sfx });
            AnimationUtility.SetAnimationEvents(c, list.ToArray());
        }

        // ------------------------------------------------------------------ shared clips
        public static AnimationClip PlateDown()
        {
            var c = New("PlateDown", WrapMode.ClampForever);
            Pos(c, "Disc", Vector3.zero, new Vector3(0f, -0.085f, 0f), 0f, 0.12f);
            Events(c, (0f, "sfx_plate"));
            return Save(c);
        }

        public static AnimationClip PlateUp()
        {
            var c = New("PlateUp", WrapMode.ClampForever);
            Pos(c, "Disc", new Vector3(0f, -0.085f, 0f), Vector3.zero, 0f, 0.25f);
            return Save(c);
        }

        public static AnimationClip GateOpen()
        {
            var c = New("GateOpen", WrapMode.ClampForever);
            Pos(c, "", Vector3.zero, new Vector3(0f, 4.4f, 0f), 0f, 1.8f);
            Events(c, (0f, "sfx_gate"));
            return Save(c);
        }

        public static AnimationClip GateClose()
        {
            var c = New("GateClose", WrapMode.ClampForever);
            Pos(c, "", new Vector3(0f, 4.4f, 0f), Vector3.zero, 0f, 1.1f);
            Events(c, (0f, "sfx_gate"));
            return Save(c);
        }

        // Blender +X imports as Unity -X, so the Blender door "DoorL" (x = -1.15) sits at Unity x = +1.15.
        public static AnimationClip ForgeGateOpen()
        {
            var c = New("ForgeGateOpen", WrapMode.ClampForever);
            Pos(c, "DoorL", new Vector3(1.15f, 0f, 0f), new Vector3(3.35f, 0f, 0f), 0f, 2.2f);
            Pos(c, "DoorR", new Vector3(-1.15f, 0f, 0f), new Vector3(-3.35f, 0f, 0f), 0f, 2.2f);
            Events(c, (0f, "sfx_gate"));
            return Save(c);
        }

        public static AnimationClip ForgeGateClose()
        {
            var c = New("ForgeGateClose", WrapMode.ClampForever);
            Pos(c, "DoorL", new Vector3(3.35f, 0f, 0f), new Vector3(1.15f, 0f, 0f), 0f, 1.6f);
            Pos(c, "DoorR", new Vector3(-3.35f, 0f, 0f), new Vector3(-1.15f, 0f, 0f), 0f, 1.6f);
            Events(c, (0f, "sfx_gate"));
            return Save(c);
        }

        public static AnimationClip HammerLoop()
        {
            var c = New("HammerLoop", WrapMode.Loop);
            const float lift = 1.8f, hold = 0.35f, drop = 0.22f, rest = 0.63f;
            float total = lift + hold + drop + rest;
            Rot(c, "Arm", total, t =>
            {
                float a;
                if (t < lift) a = -38f * Mathf.SmoothStep(0f, 1f, t / lift);
                else if (t < lift + hold) a = -38f;
                else if (t < lift + hold + drop) { float k = (t - lift - hold) / drop; a = -38f * (1f - k * k); }
                else a = 0f;
                return new Vector3(a, 0f, 0f);
            }, 1f / 40f);
            Events(c, (lift + hold + drop, "sfx_hammer"));
            return Save(c);
        }

        public static AnimationClip SunRise()
        {
            var c = New("SunRise", WrapMode.ClampForever);
            Rot(c, "", 8f, t => new Vector3(Mathf.Lerp(2f, 28f, Mathf.SmoothStep(0f, 1f, t / 8f)), 200f, 0f), 1f / 10f);
            return Save(c);
        }

        /// <summary>Planks / stair segments rising one after another (per-instance clip).</summary>
        public static AnimationClip Rise(string name, IList<(string path, Vector3 from, Vector3 to)> parts, float stagger, float each, string sfx)
        {
            var c = New(name, WrapMode.ClampForever);
            for (int i = 0; i < parts.Count; i++)
            {
                var (path, from, to) = parts[i];
                Pos(c, path, from, to, i * stagger, i * stagger + each, 0.12f);
            }
            if (!string.IsNullOrEmpty(sfx)) Events(c, (0f, sfx));
            return Save(c);
        }
    }
}
