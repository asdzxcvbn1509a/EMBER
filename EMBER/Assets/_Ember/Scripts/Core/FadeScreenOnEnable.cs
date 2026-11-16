using UnityEngine;

namespace Ember
{
    /// <summary>
    /// Fades the screen out when this object becomes active. Exit portals reveal it with the
    /// class script HitAndShowHide just before HitAndLoadScene switches the scene.
    /// </summary>
    public class FadeScreenOnEnable : MonoBehaviour
    {
        public float duration = 0.9f;
        public Color color = new Color(1f, 0.93f, 0.8f);

        void OnEnable()
        {
            var gm = GameManager.Instance;
            if (gm == null || gm.fader == null) return;
            gm.fader.SetColor(color);
            gm.fader.FadeOut(duration);
        }

        // HitAndShowHide (cancelOnExit) hides this again if the ember backs out of the portal.
        void OnDisable()
        {
            var gm = GameManager.Instance;
            if (gm != null && gm.fader != null && gm.isActiveAndEnabled && gameObject.scene.isLoaded)
                gm.fader.FadeIn(0.5f);
        }
    }
}
