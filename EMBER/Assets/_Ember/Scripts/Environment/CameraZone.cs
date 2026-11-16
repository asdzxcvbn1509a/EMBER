using UnityEngine;

namespace Ember
{
    /// <summary>Trigger volume that reframes the camera: close in narrow corridors, wide for vistas.</summary>
    public class CameraZone : MonoBehaviour
    {
        public Vector3 offset = new Vector3(0f, 7.5f, -11f);
        public float fieldOfView = 50f;
        public float blendTime = 1.4f;

        void OnTriggerEnter(Collider other)
        {
            var rb = other.attachedRigidbody;
            if (rb != null && rb.name == "Player" && CameraRig.Instance != null)
                CameraRig.Instance.BlendTo(offset, fieldOfView, blendTime);
        }

        void OnDrawGizmos()
        {
            var box = GetComponent<BoxCollider>();
            if (box == null) return;
            Gizmos.color = new Color(0.3f, 0.7f, 1f, 0.15f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(box.center, box.size);
        }
    }
}
