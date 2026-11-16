using System.Collections;
using UnityEngine;

namespace Ember
{
    /// <summary>
    /// Sits next to the class script CameraFollow on the camera rig. It only changes
    /// CameraFollow.offset and the child camera's field of view (camera zones), and shakes the
    /// child camera locally so CameraFollow's own smoothing is never disturbed.
    /// </summary>
    [RequireComponent(typeof(CameraFollow))]
    public class CameraRig : MonoBehaviour
    {
        public static CameraRig Instance { get; private set; }
        public Camera cam;

        CameraFollow follow;
        Coroutine blend;
        Vector3 camRest;

        void Awake()
        {
            Instance = this;
            follow = GetComponent<CameraFollow>();
            if (cam == null) cam = GetComponentInChildren<Camera>();
            if (cam != null) camRest = cam.transform.localPosition;
        }

        public void BlendTo(Vector3 offset, float fieldOfView, float time)
        {
            if (blend != null) StopCoroutine(blend);
            blend = StartCoroutine(Blend(offset, fieldOfView, Mathf.Max(0.05f, time)));
        }

        IEnumerator Blend(Vector3 offset, float fov, float time)
        {
            Vector3 o0 = follow.offset;
            float f0 = cam != null ? cam.fieldOfView : fov;
            for (float t = 0f; t < time; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / time);
                follow.offset = Vector3.Lerp(o0, offset, k);
                if (cam != null) cam.fieldOfView = Mathf.Lerp(f0, fov, k);
                yield return null;
            }
            follow.offset = offset;
            if (cam != null) cam.fieldOfView = fov;
            blend = null;
        }

        public void Shake(float amplitude, float duration)
        {
            if (cam != null) StartCoroutine(ShakeRoutine(amplitude, duration));
        }

        IEnumerator ShakeRoutine(float amplitude, float duration)
        {
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                float k = 1f - t / duration;
                cam.transform.localPosition = camRest + Random.insideUnitSphere * amplitude * k * k;
                yield return null;
            }
            cam.transform.localPosition = camRest;
        }
    }
}
