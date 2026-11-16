using System.Collections.Generic;
using UnityEngine;

namespace Ember
{
    [System.Serializable]
    public class SolutionCheck
    {
        public enum Kind { Active, Inactive, Destroyed, Moved, AnimationPlayed, AnyCloneNamed, NoCloneNamed, PlayerNear, Rotated }
        public Kind kind;
        public GameObject target;
        public string text;
        [HideInInspector] public Vector3 startPosition;
        [HideInInspector] public Quaternion startRotation;
    }

    [System.Serializable]
    public class SolutionStep
    {
        public string label;
        [Tooltip("Where the ember is teleported (on top of a plate / trigger). Empty = stay.")]
        public Transform moveTo;
        public float wait = 1.5f;
        public bool expectSceneChange;
        public List<SolutionCheck> checks = new List<SolutionCheck>();
    }

    /// <summary>
    /// The intended solution of a stage as data. AutoPilot replays it to prove that every
    /// Condition/Action link (object names, targets, clip names, scene names) is wired correctly.
    /// </summary>
    public class StageSolution : MonoBehaviour
    {
        public string nextScene;
        public List<SolutionStep> steps = new List<SolutionStep>();
    }
}
