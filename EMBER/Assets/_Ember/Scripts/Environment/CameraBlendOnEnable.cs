using UnityEngine;

namespace Ember
{
    /// <summary>Pulls the camera back to a new framing when shown (finale vista).</summary>
    public class CameraBlendOnEnable : MonoBehaviour
    {
        public Vector3 offset = new Vector3(0f, 16f, -26f);
        public float fieldOfView = 55f;
        public float blendTime = 4f;

        void OnEnable()
        {
            if (CameraRig.Instance != null) CameraRig.Instance.BlendTo(offset, fieldOfView, blendTime);
        }
    }
}
