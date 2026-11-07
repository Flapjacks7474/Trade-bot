using System;
using FFTS.Vehicles;
using UnityEngine;

namespace FFTS.Runtime
{
    /// <summary>
    /// Placeholder car presentation plus world collision. The simulation (<see cref="VehicleDynamics"/>) decides where
    /// the car wants to go; this component sweeps a box toward that point and, on contact, stops the car there and
    /// reports the closing speed back as an impact. Kinematic by design: no Rigidbody tuning needed for the slice.
    /// </summary>
    public sealed class VehicleView : MonoBehaviour
    {
        public static readonly Vector3 HalfExtents = new Vector3(0.95f, 0.5f, 2.2f);

        public VehicleDynamics Dyn { get; private set; }
        public string OwnershipId { get; private set; }
        public string RacerId { get; set; }
        public string DisplayName { get; private set; }
        private Transform _wheelsFront;

        public static VehicleView Spawn(string name, VehicleDynamics dyn, string ownershipId, string colorHex)
        {
            var root = new GameObject("Vehicle " + name);
            var body = Graybox.Box("Body", root.transform, Vector3.up * 0.7f, new Vector3(1.9f, 0.8f, 4.4f), colorHex, collider: false);
            body.transform.localPosition = Vector3.up * 0.7f;
            var cabin = Graybox.Box("Cabin", root.transform, Vector3.zero, new Vector3(1.6f, 0.6f, 2.0f), "#1d1d1d", collider: false);
            cabin.transform.localPosition = new Vector3(0, 1.35f, -0.3f);
            var front = new GameObject("FrontAxle").transform; front.SetParent(root.transform, false); front.localPosition = new Vector3(0, 0.35f, 1.4f);
            foreach (var side in new[] { -1f, 1f })
            {
                var wf = Graybox.Box("Wheel", front, Vector3.zero, new Vector3(0.3f, 0.7f, 0.7f), "#111111", collider: false);
                wf.transform.localPosition = new Vector3(side * 0.95f, 0, 0);
                var wr = Graybox.Box("Wheel", root.transform, Vector3.zero, new Vector3(0.3f, 0.7f, 0.7f), "#111111", collider: false);
                wr.transform.localPosition = new Vector3(side * 0.95f, 0.35f, -1.4f);
            }
            var col = root.AddComponent<BoxCollider>();
            col.center = Vector3.up * 0.9f; col.size = HalfExtents * 2f;
            var v = root.AddComponent<VehicleView>();
            v.Dyn = dyn; v.OwnershipId = ownershipId; v.DisplayName = name; v._wheelsFront = front;
            v.Teleport();
            return v;
        }

        /// <summary>Snap the transform to the simulation (spawns, race grid) without sweeping.</summary>
        public void Teleport()
        {
            transform.SetPositionAndRotation(new Vector3(Dyn.X, 0, Dyn.Z), Quaternion.Euler(0, Dyn.HeadingDeg, 0));
        }

        /// <summary>Moves toward the simulated position, resolving collisions. <paramref name="onImpact"/> gets the closing speed.</summary>
        public void SyncFromSim(Action<float> onImpact)
        {
            var from = transform.position;
            var to = new Vector3(Dyn.X, 0, Dyn.Z);
            var delta = to - from; delta.y = 0;
            var rot = Quaternion.Euler(0, Dyn.HeadingDeg, 0);
            float dist = delta.magnitude;
            if (dist > 0.0005f)
            {
                var dir = delta / dist;
                var hits = Physics.BoxCastAll(from + Vector3.up * 0.9f, HalfExtents * 0.98f, dir, rot, dist, ~0, QueryTriggerInteraction.Ignore);
                float nearest = float.MaxValue; RaycastHit best = default;
                foreach (var h in hits)
                {
                    if (h.collider.transform.IsChildOf(transform) || h.collider.name == "Ground") continue;
                    if (h.distance < nearest) { nearest = h.distance; best = h; }
                }
                if (nearest < float.MaxValue)
                {
                    float travel = Mathf.Max(0, nearest - 0.05f);
                    to = from + dir * travel;
                    float closing = Mathf.Abs(Dyn.Speed) * Mathf.Max(0, Vector3.Dot(dir, -best.normal));
                    Dyn.X = to.x; Dyn.Z = to.z;
                    onImpact?.Invoke(closing);
                }
            }
            transform.SetPositionAndRotation(to, rot);
            if (_wheelsFront != null) _wheelsFront.localRotation = Quaternion.Euler(0, Mathf.Clamp(Dyn.DriftDeg * -0.5f, -30, 30), 0);
        }
    }
}
