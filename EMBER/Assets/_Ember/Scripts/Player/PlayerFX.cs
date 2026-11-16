using UnityEngine;

namespace Ember
{
    /// <summary>Rolling sound, impact sounds, spark trail and the breathing ember light.</summary>
    [RequireComponent(typeof(Rigidbody))]
    public class PlayerFX : MonoBehaviour
    {
        public AudioSource rollSource;
        public AudioSource sfxSource;
        public AudioClip jumpClip, landClip, impactClip;
        public ParticleSystem trail;
        public Light emberLight;
        public float lightBase = 2.4f;

        Rigidbody body;
        CanJumpBuffered jump;
        float lastImpact;

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            jump = GetComponent<CanJumpBuffered>();
            if (jump != null) jump.Jumped += () => Play(jumpClip, 0.55f);
        }

        void Update()
        {
            float speed = body.linearVelocity.magnitude;
            bool grounded = jump == null || jump.IsGrounded;
            float k = Mathf.Clamp01(speed / 7f);
            if (rollSource != null)
            {
                float target = grounded && !body.isKinematic ? k * 0.55f : 0f;
                rollSource.volume = Mathf.MoveTowards(rollSource.volume, target, Time.deltaTime * 3f);
                rollSource.pitch = 0.75f + k * 0.6f;
            }
            if (trail != null)
            {
                var em = trail.emission;
                em.rateOverTimeMultiplier = 3f + speed * 7f;
            }
            if (emberLight != null)
            {
                float t = Time.time;
                emberLight.intensity = lightBase * (1f + 0.08f * Mathf.Sin(t * 7.3f) + 0.05f * Mathf.Sin(t * 13.1f));
            }
        }

        void OnCollisionEnter(Collision c)
        {
            float v = c.relativeVelocity.magnitude;
            if (v > 3f && Time.time - lastImpact > 0.12f)
            {
                lastImpact = Time.time;
                Play(v > 7f ? impactClip : landClip, Mathf.Clamp01(v / 10f));
            }
        }

        void Play(AudioClip clip, float volume)
        {
            if (clip != null && sfxSource != null) sfxSource.PlayOneShot(clip, volume);
        }
    }
}
