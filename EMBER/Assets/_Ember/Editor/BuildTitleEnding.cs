using UnityEngine;

namespace Ember.EditorTools
{
    /// <summary>Title diorama (playable: roll into the portal to start) and the dawn ending.</summary>
    public static class BuildTitle
    {
        public const string Name = "Title";
        static Material M(string n) => EmberPaths.Mat(n);

        public static void Run()
        {
            var scene = StageCommon.NewScene();
            var theme = new Theme
            {
                sky = "M_Sky_Title", fogColor = new Color(0.02f, 0.025f, 0.055f), fogDensity = 0.03f,
                ambient = new Color(0.1f, 0.11f, 0.18f), sunColor = new Color(0.5f, 0.6f, 1f), sunIntensity = 0.35f,
                sunEuler = new Vector3(60f, -20f, 0f), profile = "PP_Title", ambience = "amb_crypt", ambienceVolume = 0.25f,
                music = "mus_title", musicVolume = 0.55f, reverb = AudioReverbPreset.Cave
            };
            var refs = StageCommon.Setup(0, "", "", theme, new Vector3(0f, 0.42f, 1.5f), new Vector3(0f, 7f, -11f), 42f, false);
            var rig = refs.cam.transform.parent;
            rig.position = new Vector3(0f, 4.6f, -9.2f);
            rig.rotation = Quaternion.LookRotation(new Vector3(0f, 2.3f, 7f) - rig.position);
            var drift = rig.gameObject.AddComponent<Bob>();
            drift.amplitude = new Vector3(0.35f, 0.12f, 0f);
            drift.period = 16f;

            var kit = new LevelKit(Name, M("M_Floor_Crypt"), M("M_Wall_Crypt"), M("M_StoneTrim"));
            kit.player = refs.player.transform;
            kit.Floor("Floor", -9f, 9f, -6f, 14f);
            kit.Wall("Wall_L", -10f, -9f, -6f, 14f, 0f, 6f);
            kit.Wall("Wall_R", 9f, 10f, -6f, 14f, 0f, 6f);
            kit.Wall("Wall_Back", -10f, 10f, 14f, 15f, 0f, 9f);
            kit.Wall("Wall_Front", -9f, 9f, -7f, -6f, 0f, 0.8f);
            kit.Model("Altar", new Vector3(0f, 0f, 1.5f), 0f, kit.geo, 1f, true);
            var portal = kit.Prefab("ExitPortal", new Vector3(0f, 0f, 9f), 0f, kit.puzzle, "StartPortal");
            portal.GetComponentInChildren<HitAndLoadScene>().loadSceneName = "Stage1";
            kit.FloorText("START", new Vector3(0f, 0f, 5.6f), 4f, new Color(2.4f, 1.4f, 0.5f));
            var logo = kit.Model("Logo", new Vector3(0f, 4.4f, 12.6f), 180f, kit.deco, 2.1f);
            logo.name = "EMBER_Logo";
            LevelKit.Swap(logo, "M_Gold", "M_Logo");
            kit.PointLight(new Vector3(0f, 5.2f, 10.2f), new Color(1f, 0.66f, 0.3f), 4f, 8f);
            kit.SpotLight(new Vector3(0f, 9f, 6f), new Vector3(0f, 4.6f, 12.6f), new Color(1f, 0.75f, 0.45f), 7f, 14f, 50f);
            kit.Beam("Moonbeam", new Vector3(0f, 18f, 1.5f), new Vector3(0f, 0.4f, 1.5f), 0.7f, "M_Beam_Moon", null, new Vector2(0f, 0.04f));
            kit.SpotLight(new Vector3(0f, 14f, 1.5f), new Vector3(0f, 0f, 1.5f), new Color(0.55f, 0.68f, 1f), 7f, 18f, 30f, true);
            kit.Fx("Dust", new Vector3(0f, 3f, 2f), FxKind.Dust, 1.4f);
            kit.Fx("Embers", new Vector3(0f, 1f, 6f), FxKind.Embers, 0.7f);
            foreach (var x in new[] { -6.2f, 6.2f })
            {
                var statue = kit.Prefab("WardenStatue", new Vector3(x, 0f, 9.5f), 180f, kit.deco, "Warden");
                var head = statue.transform.Find("Model/Head");
                if (head != null) head.GetComponent<LookAtTarget>().target = refs.player.transform;
                kit.Lantern("Brazier", new Vector3(x * 0.62f, 0f, 7.2f), true, "Brazier");
                kit.ClassProp("Assets/Session2/Laterns and candles/Prefabs/candlestick.prefab", new Vector3(x * 1.2f, 0f, 2f), 0f, 1f, new Color(1f, 0.6f, 0.25f), 5f, 1.4f);
                kit.Model("Pillar", new Vector3(x * 1.32f, 0f, 12.4f), 0f, kit.deco, 1.4f, true);
                kit.Model("Banner", new Vector3(x * 0.95f, 1.5f, 14f), 180f, kit.deco, 1.3f);
            }
            for (int i = 0; i < 6; i++)
            {
                float a = i * 60f * Mathf.Deg2Rad;
                kit.ClassProp("Assets/Session2/Laterns and candles/Prefabs/" + (i % 2 == 0 ? "Candle1.prefab" : "Candle2.prefab"),
                    new Vector3(Mathf.Cos(a) * 3.2f, 0f, 1.5f + Mathf.Sin(a) * 3.2f), 0f, 1f, new Color(1f, 0.6f, 0.25f), 3.5f, 0.9f);
            }
            StageCommon.Save(scene, Name);
        }
    }

    public static class BuildEnding
    {
        public const string Name = "Ending";
        static Material M(string n) => EmberPaths.Mat(n);

        public static void Run()
        {
            var scene = StageCommon.NewScene();
            var theme = new Theme
            {
                sky = "M_Sky_Ending", fogColor = new Color(0.95f, 0.76f, 0.68f), fogDensity = 0.0035f,
                ambient = new Color(0.46f, 0.4f, 0.42f), sunColor = new Color(1f, 0.86f, 0.68f), sunIntensity = 2.2f,
                sunEuler = new Vector3(24f, 200f, 0f), profile = "PP_Ending", ambience = "amb_sky", ambienceVolume = 0.35f,
                music = "mus_ending", musicVolume = 0.6f, reverb = AudioReverbPreset.Mountains
            };
            var refs = StageCommon.Setup(4, "", "", theme, new Vector3(8f, 7.25f, 118.6f), new Vector3(0f, 7f, -11f), 45f, false);
            // the ember rests in front of the rekindled beacon
            var body = refs.player.GetComponent<Rigidbody>();
            body.isKinematic = true;
            refs.player.GetComponent<BallController>().enabled = false;
            refs.player.GetComponent<Ember.CanJumpBuffered>().enabled = false;
            var rig = refs.cam.transform.parent;
            rig.position = new Vector3(-6f, 10f, 100f);
            rig.rotation = Quaternion.LookRotation(new Vector3(9f, 13f, 124f) - rig.position);
            var drift = rig.gameObject.AddComponent<Bob>();
            drift.amplitude = new Vector3(1.2f, 0.4f, 0.6f);
            drift.period = 22f;

            var kit = new LevelKit(Name, M("M_Floor_Sky"), M("M_Limestone"), M("M_LimestoneTrim"));
            var sea = kit.Plane("CloudSea", new Vector3(0f, -14f, 120f), new Vector2(420f, 420f), M("M_CloudSea"));
            sea.AddComponent<ScrollingUVs>().uvAnimationRate = new Vector2(0.004f, 0.002f);
            kit.Disc("Hub", new Vector3(8f, 4f, 92f), 10f, 1.2f);
            kit.Floor("Beacon_Terrace", 1f, 15f, 114f, 128f, 7f);
            for (int k = 0; k < 3; k++)
                kit.Model("Stairs", new Vector3(8f, 4f + k, 102f + 4f * k), 0f, kit.deco).transform.localScale = new Vector3(1f, 1f, 1.6f);
            kit.Model("Beacon", new Vector3(8f, 7f, 124f), 0f, kit.geo, 1f);
            kit.Model("Altar", new Vector3(8f, 7f, 118.6f), 0f, kit.geo, 0.8f);
            var fire = kit.Fx("BeaconFire", new Vector3(8f, 18.8f, 124f), FxKind.BigFire, 3.2f);
            kit.Fx("BeaconSparks", new Vector3(8f, 19.4f, 124f), FxKind.Sparks, 3f);
            kit.PointLight(new Vector3(8f, 20.4f, 124f), new Color(1f, 0.7f, 0.4f), 8f, 70f, true);
            foreach (var (x, z) in new[] { (-12f, 105.8f), (30f, 97.6f), (-14f, 87.4f) })
            {
                kit.Lantern("Lantern", new Vector3(x, x < 0f && z < 90f ? 10f : 4f, z), true, "LanternDawn");
                kit.Beam("Beam", new Vector3(x, (x < 0f && z < 90f ? 13.4f : 7.4f), z), new Vector3(8f, 18.4f, 124f), 0.22f, "M_Beam_Gold");
            }
            kit.Model("Island_M", new Vector3(-12f, 4f, 92f), 0f, kit.geo);
            kit.Model("Island_M", new Vector3(28f, 4f, 92f), 0f, kit.geo);
            kit.Model("Island_S", new Vector3(-14f, 10f, 85.5f), 0f, kit.geo);
            kit.Model("Island_S", new Vector3(-12f, 4f, 103.6f), 0f, kit.geo);
            kit.Lantern("Terrace_Lantern_L", new Vector3(3f, 7f, 116f), true, "LanternDawn");
            kit.Lantern("Terrace_Lantern_R", new Vector3(13f, 7f, 116f), true, "LanternDawn");
            var rnd = new System.Random(11);
            for (int i = 0; i < 14; i++)
            {
                var p = new Vector3((float)rnd.NextDouble() * 140f - 60f, -8f + (float)rnd.NextDouble() * 10f, 60f + (float)rnd.NextDouble() * 120f);
                var c = kit.Model("CloudPuff", p, (float)rnd.NextDouble() * 360f, kit.deco, 2f + (float)rnd.NextDouble() * 3f);
                c.AddComponent<Bob>().amplitude = new Vector3(0.8f, 0.3f, 0f);
            }
            kit.Fx("Embers", new Vector3(8f, 12f, 118f), FxKind.Embers, 1.4f);
            StageCommon.Save(scene, Name);
        }
    }
}
