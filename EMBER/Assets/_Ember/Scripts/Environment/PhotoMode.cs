using System.Reflection;
using UnityEngine;

namespace Ember
{
    /// <summary>
    /// F toggles a free photo camera using the class script Fly.cs (Session 1). While active, the
    /// ember is frozen and CameraFollow is paused; leaving photo mode lets CameraFollow glide back.
    /// </summary>
    public class PhotoMode : MonoBehaviour
    {
        public bool active;

        CameraFollow follow;
        Fly fly;

        void Awake()
        {
            follow = GetComponent<CameraFollow>();
            fly = GetComponent<Fly>();
            if (fly != null) fly.enabled = false;
        }

        public void Toggle()
        {
            active = !active;
            var gm = GameManager.Instance;
            var player = gm != null ? gm.player : null;
            if (player != null)
            {
                var ball = player.GetComponent<BallController>();
                if (ball != null) ball.enabled = !active;
                var jump = player.GetComponent<CanJumpBuffered>();
                if (jump != null) jump.enabled = !active;
                var rb = player.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    if (active) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
                    rb.isKinematic = active;
                }
            }
            if (follow != null) follow.enabled = !active;
            if (fly != null)
            {
                if (active)
                {
                    // Fly.cs measures mouse movement from a private field; reset it so the view does not jump.
                    var f = typeof(Fly).GetField("lastMouse", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (f != null) f.SetValue(fly, Input.mousePosition);
                }
                fly.enabled = active;
            }
            if (gm != null && gm.hud != null) gm.hud.ShowPhotoHint(active);
        }
    }
}
