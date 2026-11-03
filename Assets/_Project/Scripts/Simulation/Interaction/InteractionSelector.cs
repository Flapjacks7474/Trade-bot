using System.Collections.Generic;
using System.Numerics;

namespace FFTS.Interaction
{
    public sealed class InteractionCandidate
    {
        public string Id;
        public string Prompt;     // e.g. "Talk to Grandma"
        public string Verb;       // e.g. "talk", "enter", "fight", "record", "shop"
        public Vector3 Position;
        public float Radius = 2f;
        public int Priority;      // story-critical interactions win ties
        public bool Enabled = true;
    }

    /// <summary>
    /// Chooses the single best interaction for the player: must be in range and roughly in front;
    /// among those, priority wins, then the best combination of distance and facing.
    /// </summary>
    public static class InteractionSelector
    {
        public const float MaxAngleDegrees = 75f;

        public static InteractionCandidate SelectBest(Vector3 playerPos, Vector3 playerForward, IEnumerable<InteractionCandidate> candidates)
        {
            InteractionCandidate best = null; float bestScore = float.MinValue;
            var forward = new Vector3(playerForward.X, 0, playerForward.Z);
            if (forward.LengthSquared() < 1e-6f) forward = Vector3.UnitZ;
            forward = Vector3.Normalize(forward);
            float minDot = (float)System.Math.Cos(MaxAngleDegrees * System.Math.PI / 180.0);

            foreach (var c in candidates)
            {
                if (c == null || !c.Enabled) continue;
                var to = c.Position - playerPos; to.Y = 0;
                float dist = to.Length();
                if (dist > c.Radius) continue;
                float dot = dist < 0.25f ? 1f : Vector3.Dot(forward, to / dist);
                if (dot < minDot) continue;
                float score = c.Priority * 1000f + dot * 2f - dist / c.Radius;
                if (score > bestScore) { bestScore = score; best = c; }
            }
            return best;
        }
    }
}
