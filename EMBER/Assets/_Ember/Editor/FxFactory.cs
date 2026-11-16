using UnityEngine;

namespace Ember.EditorTools
{
    public enum FxKind { Flame, BigFire, Sparks, Embers, Dust, Smoke, Glow, Burst, Trail, Motes, Steam, CrystalSparkle }

    /// <summary>Particle systems used across EMBER (Unity's built-in Particle System, configured in code).</summary>
    public static class FxFactory
    {
        static Gradient Grad(params (float t, Color c, float a)[] keys)
        {
            var g = new Gradient();
            var ck = new GradientColorKey[keys.Length];
            var ak = new GradientAlphaKey[keys.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                ck[i] = new GradientColorKey(keys[i].c, keys[i].t);
                ak[i] = new GradientAlphaKey(keys[i].a, keys[i].t);
            }
            g.SetKeys(ck, ak);
            return g;
        }

        static AnimationCurve Curve(params float[] kv)
        {
            var c = new AnimationCurve();
            for (int i = 0; i + 1 < kv.Length; i += 2) c.AddKey(kv[i], kv[i + 1]);
            return c;
        }

        public static ParticleSystem Create(Transform parent, string name, Vector3 localPos, FxKind kind, float scale = 1f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var ps = go.AddComponent<ParticleSystem>();
            var r = go.GetComponent<ParticleSystemRenderer>();
            var main = ps.main;
            var em = ps.emission;
            var sh = ps.shape;
            var col = ps.colorOverLifetime;
            var sol = ps.sizeOverLifetime;
            var vel = ps.velocityOverLifetime;
            var noise = ps.noise;
            main.loop = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            Color amber = new Color(1f, 0.62f, 0.25f), orange = new Color(1f, 0.38f, 0.08f), hot = new Color(1f, 0.85f, 0.5f);

            switch (kind)
            {
                case FxKind.Flame:
                    go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                    main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.65f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 0.7f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.18f * scale, 0.32f * scale);
                    main.maxParticles = 60;
                    em.rateOverTime = 26f;
                    sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 8f; sh.radius = 0.05f * scale;
                    col.enabled = true; col.color = Grad((0f, hot, 0f), (0.15f, amber, 1f), (0.7f, orange, 0.6f), (1f, orange, 0f));
                    sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, 0.6f, 0.3f, 1f, 1f, 0.1f));
                    r.sharedMaterial = EmberPaths.Mat("M_Particle_Fire");
                    break;
                case FxKind.BigFire:
                    go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                    main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.45f * scale, 0.8f * scale);
                    main.maxParticles = 150;
                    em.rateOverTime = 55f;
                    sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 12f; sh.radius = 0.35f * scale;
                    col.enabled = true; col.color = Grad((0f, hot, 0f), (0.12f, amber, 1f), (0.6f, orange, 0.7f), (1f, new Color(0.6f, 0.15f, 0.05f), 0f));
                    sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, 0.5f, 0.25f, 1f, 1f, 0.2f));
                    noise.enabled = true; noise.strength = 0.4f; noise.frequency = 1.2f;
                    r.sharedMaterial = EmberPaths.Mat("M_Particle_Fire");
                    break;
                case FxKind.Sparks:
                    go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                    main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.8f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.8f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.04f * scale, 0.09f * scale);
                    main.gravityModifier = -0.05f;
                    main.maxParticles = 120;
                    em.rateOverTime = 14f;
                    sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 25f; sh.radius = 0.2f * scale;
                    col.enabled = true; col.color = Grad((0f, hot, 1f), (0.6f, amber, 1f), (1f, orange, 0f));
                    noise.enabled = true; noise.strength = 0.6f; noise.frequency = 0.8f;
                    r.sharedMaterial = EmberPaths.Mat("M_Particle_Spark");
                    break;
                case FxKind.Embers:
                    main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 8f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.4f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.1f);
                    main.gravityModifier = -0.02f;
                    main.maxParticles = 300;
                    em.rateOverTime = 14f * scale;
                    sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(14f, 2f, 14f) * scale;
                    col.enabled = true; col.color = Grad((0f, amber, 0f), (0.2f, amber, 1f), (0.8f, orange, 0.8f), (1f, orange, 0f));
                    noise.enabled = true; noise.strength = 0.5f; noise.frequency = 0.3f;
                    r.sharedMaterial = EmberPaths.Mat("M_Particle_Spark");
                    break;
                case FxKind.Dust:
                    main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 10f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.1f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);
                    main.maxParticles = 200;
                    em.rateOverTime = 10f * scale;
                    sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(3f, 6f, 3f) * scale;
                    col.enabled = true; col.color = Grad((0f, Color.white, 0f), (0.3f, Color.white, 0.8f), (0.7f, Color.white, 0.8f), (1f, Color.white, 0f));
                    noise.enabled = true; noise.strength = 0.15f; noise.frequency = 0.2f;
                    r.sharedMaterial = EmberPaths.Mat("M_Particle_Dust");
                    break;
                case FxKind.Motes:
                    main.startLifetime = new ParticleSystem.MinMaxCurve(2f, 4f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.5f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.12f);
                    main.gravityModifier = -0.06f;
                    main.maxParticles = 100;
                    em.rateOverTime = 12f;
                    sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 1.8f * scale; sh.rotation = new Vector3(90f, 0f, 0f);
                    col.enabled = true; col.color = Grad((0f, hot, 0f), (0.3f, amber, 1f), (1f, amber, 0f));
                    r.sharedMaterial = EmberPaths.Mat("M_Particle_Spark");
                    break;
                case FxKind.Smoke:
                    go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                    main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 7f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
                    main.startSize = new ParticleSystem.MinMaxCurve(1.5f * scale, 3f * scale);
                    main.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
                    main.maxParticles = 80;
                    em.rateOverTime = 4f;
                    sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 15f; sh.radius = 0.8f * scale;
                    col.enabled = true; col.color = Grad((0f, Color.white, 0f), (0.2f, Color.white, 0.6f), (1f, Color.white, 0f));
                    sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, 0.5f, 1f, 1.6f));
                    r.sharedMaterial = EmberPaths.Mat("M_Particle_Smoke");
                    break;
                case FxKind.Steam:
                    go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                    main.startLifetime = new ParticleSystem.MinMaxCurve(2f, 3.5f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.8f * scale, 1.6f * scale);
                    main.maxParticles = 60;
                    em.rateOverTime = 6f;
                    sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(6f, 6f, 0.1f) * scale;
                    col.enabled = true; col.color = Grad((0f, orange, 0f), (0.3f, amber, 0.25f), (1f, orange, 0f));
                    sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, 0.6f, 1f, 1.5f));
                    r.sharedMaterial = EmberPaths.Mat("M_Particle_Fire");
                    break;
                case FxKind.Glow:
                    main.startLifetime = 1f;
                    main.startSpeed = 0f;
                    main.startSize = 1.6f * scale;
                    main.maxParticles = 1;
                    main.simulationSpace = ParticleSystemSimulationSpace.Local;
                    em.rateOverTime = 0f;
                    em.SetBursts(new[] { new ParticleSystem.Burst(0f, 1, 1, 0, 1f) });
                    sh.enabled = false;
                    main.startLifetime = 100000f;
                    main.duration = 1f;
                    main.loop = true;
                    main.startColor = new Color(1f, 0.6f, 0.25f, 0.55f);
                    r.sharedMaterial = EmberPaths.Mat("M_Particle_Glow");
                    break;
                case FxKind.Burst:
                    main.loop = false;
                    main.playOnAwake = true;
                    main.duration = 1f;
                    main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.4f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(2f * scale, 5f * scale);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.16f);
                    main.gravityModifier = 0.4f;
                    main.maxParticles = 120;
                    em.rateOverTime = 0f;
                    em.SetBursts(new[] { new ParticleSystem.Burst(0f, 60) });
                    sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.3f * scale;
                    col.enabled = true; col.color = Grad((0f, hot, 1f), (0.7f, amber, 1f), (1f, orange, 0f));
                    r.sharedMaterial = EmberPaths.Mat("M_Particle_Spark");
                    break;
                case FxKind.CrystalSparkle:
                    main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.25f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.12f);
                    main.maxParticles = 40;
                    em.rateOverTime = 8f;
                    sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.5f * scale;
                    col.enabled = true; col.color = Grad((0f, Color.white, 0f), (0.3f, Color.white, 1f), (1f, Color.white, 0f));
                    r.sharedMaterial = EmberPaths.Mat("M_Particle_Crystal");
                    break;
                case FxKind.Trail:
                    main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.3f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.5f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.13f);
                    main.gravityModifier = -0.12f;
                    main.maxParticles = 200;
                    em.rateOverTime = 6f;
                    sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.4f;
                    col.enabled = true; col.color = Grad((0f, hot, 1f), (0.5f, amber, 0.9f), (1f, orange, 0f));
                    noise.enabled = true; noise.strength = 0.35f; noise.frequency = 1f;
                    r.sharedMaterial = EmberPaths.Mat("M_Particle_Spark");
                    break;
            }
            return ps;
        }
    }
}
