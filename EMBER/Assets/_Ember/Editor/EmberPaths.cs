using System.IO;
using UnityEditor;
using UnityEngine;

namespace Ember.EditorTools
{
    public static class EmberPaths
    {
        public const string Root = "Assets/_Ember";
        public const string Models = Root + "/Art/Models";
        public const string Textures = Root + "/Art/Textures";
        public const string UI = Root + "/Art/UI";
        public const string Materials = Root + "/Materials";
        public const string Prefabs = Root + "/Prefabs";
        public const string Anims = Root + "/Animations";
        public const string Audio = Root + "/Audio";
        public const string Settings = Root + "/Settings";
        public const string Scenes = Root + "/Scenes";
        public const string Meshes = Root + "/Generated/Meshes";
        public const string Fonts = Root + "/Fonts";
        public const string Shaders = Root + "/Shaders";
        public const string Physics = Root + "/Physics";
        public const string ClassS3 = "Assets/Downloads/Session3";
        public const string ClassS2 = "Assets/Session2";

        public static void Ensure(string folder)
        {
            folder = folder.Replace('\\', '/').TrimEnd('/');
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            Ensure(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        public static string Scene(string name) => $"{Scenes}/{name}.unity";

        public static T Load<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(path);

        public static GameObject Model(string name) => Load<GameObject>($"{Models}/{name}.fbx");
        public static GameObject Prefab(string name) => Load<GameObject>($"{Prefabs}/{name}.prefab");
        public static Material Mat(string name) => Load<Material>($"{Materials}/{name}.mat");
        public static AudioClip Clip(string name) => Load<AudioClip>($"{Audio}/{name}.wav");
        public static Texture2D Tex(string path) => Load<Texture2D>(path);
        public static Texture2D GenTex(string name) => Load<Texture2D>($"{Textures}/{name}.png");
    }
}
