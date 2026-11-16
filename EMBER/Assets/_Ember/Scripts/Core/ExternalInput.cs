using UnityEngine;

namespace Ember
{
    /// <summary>
    /// Set by tools that drive the game themselves (the tutorial recorder) so the real keyboard cannot
    /// interfere: GameManager ignores Esc / R / F and CanJumpBuffered ignores Space while it is true.
    /// </summary>
    public static class ExternalInput
    {
        public static bool Blocked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Blocked = false;
    }
}
