using FFTS.Input;
using UnityEngine;

namespace FFTS.Runtime
{
    /// <summary>Camera-relative third-person locomotion on a CharacterController. Sprint = Dodge held while exploring.</summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerMotor : MonoBehaviour
    {
        [SerializeField] private float walkSpeed = 4.5f;
        [SerializeField] private float sprintSpeed = 7.5f;
        [SerializeField] private float turnSpeedDegrees = 720f;
        [SerializeField] private float gravity = -20f;

        private CharacterController _cc;
        private float _verticalVelocity;
        public Transform CameraTransform { get; set; }

        private void Awake() { _cc = GetComponent<CharacterController>(); }

        private void Update()
        {
            var gm = CoreGameManager.Instance;
            if (gm == null || !_cc.enabled) return;

            Vector3 move = Vector3.zero;
            if (!gm.ControlsLocked)
            {
                var f = gm.Frame;
                Vector3 fwd = CameraTransform != null ? CameraTransform.forward : Vector3.forward;
                fwd.y = 0; fwd = fwd.sqrMagnitude > 0.001f ? fwd.normalized : Vector3.forward;
                Vector3 right = new Vector3(fwd.z, 0, -fwd.x);
                move = fwd * f.MoveY + right * f.MoveX;
                if (move.sqrMagnitude > 1f) move.Normalize();
                float speed = f.IsHeld(ButtonMask.Dodge) ? sprintSpeed : walkSpeed;
                move *= speed;
                if (move.sqrMagnitude > 0.01f)
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(move), turnSpeedDegrees * Time.deltaTime);
            }

            _verticalVelocity = _cc.isGrounded ? -1f : _verticalVelocity + gravity * Time.deltaTime;
            _cc.Move((move + Vector3.up * _verticalVelocity) * Time.deltaTime);
        }
    }
}
