using UnityEngine;

namespace FFTS.Runtime
{
    public enum CameraMode { Explore, Fight, Finisher, Vehicle }

    /// <summary>
    /// Third-person orbit camera with collision, plus fight framing (side-on, both fighters in shot) and a
    /// short finisher shot. Hand-written for the vertical slice; Cinemachine replacement is planned
    /// (TECHNICAL_DECISIONS.md, TD-009) once it can be verified in the editor.
    /// </summary>
    public sealed class CameraRig : MonoBehaviour
    {
        [SerializeField] private float distance = 5.5f;
        [SerializeField] private float height = 1.6f;
        [SerializeField] private float minPitch = -25f, maxPitch = 60f;
        [SerializeField] private float collisionRadius = 0.25f;

        public Transform Target { get; set; }
        public CameraMode Mode { get; private set; } = CameraMode.Explore;

        private float _yaw, _pitch = 15f;
        private Vector3 _fightA, _fightB, _fightAxis;
        private float _finisherUntil;

        private Transform _vehicle;
        private int _vehicleView;

        public void SetExplore() { Mode = CameraMode.Explore; _vehicle = null; }

        /// <summary>Vehicle cameras: 0 chase, 1 hood, 2 cockpit.</summary>
        public void SetVehicle(Transform car, int view) { Mode = CameraMode.Vehicle; _vehicle = car; _vehicleView = view; }

        public void SetFight(Vector3 a, Vector3 b, Vector3 axis)
        {
            Mode = Time.time < _finisherUntil ? CameraMode.Finisher : CameraMode.Fight;
            _fightA = a; _fightB = b; _fightAxis = axis;
        }

        public void PlayFinisherShot(float seconds) { _finisherUntil = Time.time + seconds; Mode = CameraMode.Finisher; }

        private void LateUpdate()
        {
            var gm = CoreGameManager.Instance;
            if (Target == null) return;

            if (Mode == CameraMode.Vehicle && _vehicle != null)
            {
                var t = _vehicle;
                if (_vehicleView == 1) { transform.SetPositionAndRotation(t.position + Vector3.up * 1.35f + t.forward * 0.9f, t.rotation); return; }
                if (_vehicleView == 2) { transform.SetPositionAndRotation(t.position + Vector3.up * 1.15f - t.forward * 0.1f - t.right * 0.35f, t.rotation); return; }
                Vector3 want = t.position - t.forward * 7f + Vector3.up * 2.6f;
                transform.position = Vector3.Lerp(transform.position, want, 1f - Mathf.Exp(-6f * Time.deltaTime));
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(t.position + Vector3.up + t.forward * 3f - transform.position), 1f - Mathf.Exp(-10f * Time.deltaTime));
                _yaw = transform.eulerAngles.y;
                return;
            }

            if (Mode == CameraMode.Explore)
            {
                if (gm != null && !gm.Dialogue.IsOpen)
                {
                    _yaw += gm.Frame.LookX;
                    _pitch = Mathf.Clamp(_pitch - gm.Frame.LookY, minPitch, maxPitch);
                }
                Vector3 pivot = Target.position + Vector3.up * height;
                Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0);
                Vector3 desired = pivot - rot * Vector3.forward * distance;
                float d = distance;
                if (Physics.SphereCast(pivot, collisionRadius, (desired - pivot).normalized, out var hit, distance, ~0, QueryTriggerInteraction.Ignore))
                    d = Mathf.Max(0.5f, hit.distance - 0.1f);
                transform.SetPositionAndRotation(pivot - rot * Vector3.forward * d, rot);
                return;
            }

            Vector3 mid = (_fightA + _fightB) * 0.5f + Vector3.up * 1.2f;
            Vector3 side = Vector3.Cross(Vector3.up, _fightAxis).normalized; // camera sits so +axis is screen-right
            float span = Vector3.Distance(_fightA, _fightB);
            bool finisher = Mode == CameraMode.Finisher && Time.time < _finisherUntil;
            float back = finisher ? 2.6f : Mathf.Clamp(4.5f + span * 0.8f, 4.5f, 10f);
            Vector3 pos = mid - side * back + Vector3.up * (finisher ? -0.3f : 0.8f) + (finisher ? _fightAxis * 1.2f : Vector3.zero);
            transform.position = Vector3.Lerp(transform.position, pos, 1f - Mathf.Exp(-10f * Time.deltaTime));
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(mid - transform.position), 1f - Mathf.Exp(-12f * Time.deltaTime));
            if (!finisher && Mode == CameraMode.Finisher) Mode = CameraMode.Fight;
            _yaw = transform.eulerAngles.y;
        }
    }
}
