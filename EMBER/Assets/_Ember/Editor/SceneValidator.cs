using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Ember.EditorTools
{
    /// <summary>
    /// Static checks of every scene: each class Condition/Action component must name an object that
    /// exists (a scene object or a prefab clone "Name(Clone)"), have its target, use clip names that the
    /// target Animation really has, and load scenes that are in the build list.
    /// </summary>
    public static class SceneValidator
    {
        public static int Run(out List<string> report)
        {
            var rep = new List<string>();
            report = rep;
            int errors = 0;
            var buildScenes = new HashSet<string>(EditorBuildSettings.scenes.Select(s => Path.GetFileNameWithoutExtension(s.path)));
            foreach (var name in Pipeline.SceneOrder)
            {
                string path = EmberPaths.Scene(name);
                if (!File.Exists(path)) { rep.Add($"ERROR {name}: scene file missing"); errors++; continue; }
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                var names = new HashSet<string>(Object.FindObjectsByType<Transform>(FindObjectsInactive.Include).Select(t => t.name));
                var cloneNames = new HashSet<string>(Object.FindObjectsByType<HitAndClone>(FindObjectsInactive.Include)
                    .Where(c => c.sourcePrefab != null).Select(c => c.sourcePrefab.name + "(Clone)"));
                int count = 0;
                void Check(Component c, string objectName, bool ok, string what)
                {
                    count++;
                    bool nameOk = names.Contains(objectName) || cloneNames.Contains(objectName);
                    if (!nameOk) { rep.Add($"ERROR {name}/{PathOf(c.transform)} {c.GetType().Name}: objectName '{objectName}' matches nothing"); errors++; }
                    if (!ok) { rep.Add($"ERROR {name}/{PathOf(c.transform)} {c.GetType().Name}: {what}"); errors++; }
                }
                foreach (var c in Object.FindObjectsByType<HitAndShowHide>(FindObjectsInactive.Include))
                    Check(c, c.objectName, c.target != null, "target missing");
                foreach (var c in Object.FindObjectsByType<HitAndPlayAnimation>(FindObjectsInactive.Include))
                {
                    bool ok = c.target != null && (string.IsNullOrEmpty(c.enterAnimName) || c.target.GetClip(c.enterAnimName) != null)
                              && (string.IsNullOrEmpty(c.exitAnimName) || c.target.GetClip(c.exitAnimName) != null);
                    Check(c, c.objectName, ok, $"Animation/clip missing ({c.enterAnimName}/{c.exitAnimName})");
                    if (c.target != null)
                        foreach (var clip in AnimationUtility.GetAnimationClips(c.target.gameObject))
                            if (clip != null && !clip.legacy) { rep.Add($"ERROR {name}: clip {clip.name} is not legacy"); errors++; }
                }
                foreach (var c in Object.FindObjectsByType<HitAndClone>(FindObjectsInactive.Include))
                    Check(c, c.objectName, c.sourcePrefab != null && c.startPosition != null, "sourcePrefab / startPosition missing");
                foreach (var c in Object.FindObjectsByType<HitAndDestroy>(FindObjectsInactive.Include))
                    Check(c, c.objectName, c.destroySelf || c.target != null, "target missing");
                foreach (var c in Object.FindObjectsByType<HitAndSetTransform>(FindObjectsInactive.Include))
                    Check(c, c.objectName, c.onSelf || c.target != null, "target missing");
                foreach (var c in Object.FindObjectsByType<HitAndLoadScene>(FindObjectsInactive.Include))
                    Check(c, c.objectName, buildScenes.Contains(c.loadSceneName), $"scene '{c.loadSceneName}' not in build settings");
                foreach (var l in Object.FindObjectsByType<LookAtTarget>(FindObjectsInactive.Include))
                    if (l.target == null) { rep.Add($"ERROR {name}/{PathOf(l.transform)} LookAtTarget: target missing"); errors++; }
                if (name.StartsWith("Stage") && GameObject.Find("Player") == null) { rep.Add($"ERROR {name}: no Player"); errors++; }
                var player = GameObject.Find("Player");
                if (player != null && player.GetComponentsInChildren<Collider>().Length != 1) { rep.Add($"ERROR {name}: Player must have exactly one collider"); errors++; }
                foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include))
                    foreach (var m in r.sharedMaterials)
                        if (m == null || m.shader == null || m.shader.name == "Hidden/InternalErrorShader")
                        { rep.Add($"WARN {name}/{PathOf(r.transform)}: missing or broken material"); break; }
                rep.Add($"OK   {name}: {count} Condition/Action components checked");
            }
            return errors;
        }

        static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;
    }
}
