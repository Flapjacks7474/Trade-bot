using System.Collections.Generic;
using FFTS.Input;
using FFTS.Interaction;
using UnityEngine;

namespace FFTS.Runtime
{
    /// <summary>Picks the best nearby interactable each frame and fires it on Interact.</summary>
    public sealed class InteractionManager : MonoBehaviour
    {
        public WorldInteractable Current { get; private set; }
        private readonly List<InteractionCandidate> _candidates = new List<InteractionCandidate>();
        private readonly Dictionary<InteractionCandidate, WorldInteractable> _lookup = new Dictionary<InteractionCandidate, WorldInteractable>();

        private void Update()
        {
            var gm = CoreGameManager.Instance;
            Current = null;
            if (gm == null || gm.ControlsLocked) return;

            _candidates.Clear(); _lookup.Clear();
            foreach (var w in WorldInteractable.All)
            {
                if (w == null) continue;
                var c = w.Candidate;
                _candidates.Add(c); _lookup[c] = w;
            }
            var p = transform.position; var f = transform.forward;
            var best = InteractionSelector.SelectBest(new System.Numerics.Vector3(p.x, p.y, p.z), new System.Numerics.Vector3(f.x, f.y, f.z), _candidates);
            if (best != null) Current = _lookup[best];
            if (Current != null && gm.Frame.WasPressed(ButtonMask.Interact)) Current.OnInteract?.Invoke();
        }
    }
}
