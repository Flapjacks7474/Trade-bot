using System;
using System.Collections.Generic;
using FFTS.Interaction;
using UnityEngine;

namespace FFTS.Runtime
{
    /// <summary>Something the player can use with Interact. Selection rules live in <see cref="InteractionSelector"/>.</summary>
    public sealed class WorldInteractable : MonoBehaviour
    {
        public static readonly List<WorldInteractable> All = new List<WorldInteractable>();

        public string Id;
        public string Prompt;
        public string Verb;
        public float Radius = 2.5f;
        public int Priority;
        public Action OnInteract;
        public Func<bool> IsAvailable;

        private readonly InteractionCandidate _candidate = new InteractionCandidate();

        private void OnEnable() => All.Add(this);
        private void OnDisable() => All.Remove(this);

        public InteractionCandidate Candidate
        {
            get
            {
                var p = transform.position;
                _candidate.Id = Id; _candidate.Prompt = Prompt; _candidate.Verb = Verb; _candidate.Radius = Radius; _candidate.Priority = Priority;
                _candidate.Position = new System.Numerics.Vector3(p.x, p.y, p.z);
                _candidate.Enabled = isActiveAndEnabled && (IsAvailable == null || IsAvailable());
                return _candidate;
            }
        }
    }
}
