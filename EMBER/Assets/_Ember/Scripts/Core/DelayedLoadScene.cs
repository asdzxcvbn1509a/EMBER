using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ember
{
    /// <summary>Loads a scene a few seconds after this object is enabled (used by the finale).</summary>
    public class DelayedLoadScene : MonoBehaviour
    {
        public string sceneName = "Ending";
        public float delay = 6f;
        public float fade = 2f;
        public Color fadeColor = Color.white;

        void OnEnable() => StartCoroutine(Run());

        IEnumerator Run()
        {
            yield return new WaitForSeconds(delay);
            var gm = GameManager.Instance;
            if (gm != null)
            {
                if (gm.fader != null) gm.fader.SetColor(fadeColor);
                gm.LoadScene(sceneName, fade);
            }
            else SceneManager.LoadScene(sceneName);
        }
    }
}
