using System;
using FFTS.Core;

namespace FFTS.Vehicles
{
    public struct DriveInput
    {
        public float Throttle;   // 0..1
        public float Brake;      // 0..1 (also reverse when stopped)
        public float Steer;      // -1 (left) .. 1 (right)
        public bool Handbrake;
        public bool Nitrous;
        public float SurfaceGrip; // 0 = use 1 (asphalt). Dirt ≈ 0.7, grass ≈ 0.55
    }

    /// <summary>
    /// Deterministic arcade-leaning vehicle model on the ground plane. Heading 0 = +Z, 90° = +X.
    /// Speed is signed along the heading; a drift angle separates the travel direction from the nose so
    /// handbrake turns and power slides exist without a full tyre model. Physics-engine collisions are
    /// reported back through <see cref="ApplyImpact"/>.
    /// </summary>
    public sealed class VehicleDynamics
    {
        public const float ReverseMaxSpeed = 7f;
        public const float WreckedDamage = 100f;

        private VehicleStats _base;
        private HandlingPreset _preset;
        public VehicleStats Stats => _base;
        public HandlingPreset Preset => _preset;

        public float X, Z;
        public float HeadingDeg;
        public float Speed;           // m/s, signed
        public float DriftDeg;        // travel direction = heading - drift
        public float Damage;          // 0..100
        public float NitrousRemaining;
        public bool NitrousActive { get; private set; }
        public double DistanceMeters { get; private set; }
        public bool IsWrecked => Damage >= WreckedDamage;

        public VehicleDynamics(VehicleStats stats, HandlingPreset preset, float damage = 0)
        {
            _base = stats; _preset = preset; Damage = MathUtil.Clamp(damage, 0, 100);
            NitrousRemaining = stats.NitrousCapacitySeconds;
        }

        public void SetPreset(HandlingPreset p) => _preset = p;

        // Preset tuning: assists, not stat changes.
        private float GripMul => _preset == HandlingPreset.Arcade ? 1.25f : _preset == HandlingPreset.Expert ? 0.9f : 1f;
        private float SteerMul => _preset == HandlingPreset.Arcade ? 1.2f : 1f;
        private float BrakeMul => _preset == HandlingPreset.Arcade ? 1.2f : 1f;
        private float DriftRecovery => _preset == HandlingPreset.Arcade ? 8f : _preset == HandlingPreset.Expert ? 3.5f : 5f;

        public float DamageAccelFactor => Damage >= 90 ? 0.5f : Damage >= 60 ? 0.75f : 1f;
        public float EffectiveMaxSpeed => _base.MaxSpeed * (Damage >= 90 ? 0.8f : 1f) * (NitrousActive ? 1.15f : 1f);
        public float SpeedKph => Speed * 3.6f;

        public void Step(DriveInput input, float dt)
        {
            if (dt <= 0) return;
            float surface = input.SurfaceGrip <= 0 ? 1f : MathUtil.Clamp(input.SurfaceGrip, 0.2f, 1.2f);
            float throttle = IsWrecked ? 0 : MathUtil.Clamp01(input.Throttle);
            float brake = MathUtil.Clamp01(input.Brake);
            float steer = MathUtil.Clamp(input.Steer, -1f, 1f);

            NitrousActive = input.Nitrous && !IsWrecked && NitrousRemaining > 0 && throttle > 0.1f && Speed > 1f;
            if (NitrousActive) NitrousRemaining = Math.Max(0, NitrousRemaining - dt);

            // Longitudinal
            float max = EffectiveMaxSpeed;
            if (throttle > 0 && Speed >= -0.1f)
            {
                float ratio = MathUtil.Clamp01(Speed / max);
                float launch = Speed < 8f ? MathUtil.Clamp(_base.Traction * surface, 0.3f, 1.2f) : 1f;
                float accel = _base.Acceleration * DamageAccelFactor * launch * (1f - ratio * ratio) + (NitrousActive ? _base.Acceleration * 0.6f : 0f);
                Speed += throttle * accel * dt;
            }
            if (brake > 0)
            {
                if (Speed > 0.05f) Speed = Math.Max(0, Speed - brake * _base.Braking * BrakeMul * surface * dt);
                else if (Speed > -ReverseMaxSpeed) Speed = Math.Max(-ReverseMaxSpeed, Speed - brake * _base.Acceleration * 0.5f * dt);
            }
            if (throttle > 0 && Speed < -0.1f) Speed = Math.Min(0, Speed + _base.Braking * dt); // throttle while reversing = brake
            // Rolling resistance + aero drag
            float drag = (0.25f + 0.0005f * Speed * Speed) * dt;
            Speed = Math.Abs(Speed) <= drag ? (throttle > 0 || brake > 0 ? Speed : 0) : Speed - Math.Sign(Speed) * drag;
            if (Speed > max) Speed = Math.Max(max, Speed - 6f * dt); // nitrous ended or damage: bleed down, don't snap

            // Lateral
            float grip = _base.Grip * GripMul * surface * (input.Handbrake ? 0.35f : 1f);
            float abs = Math.Abs(Speed);
            float speedFactor = MathUtil.Clamp01(abs / 6f) * (1f - 0.45f * MathUtil.Clamp01(abs / _base.MaxSpeed));
            float yaw = steer * _base.SteeringDegPerSec * SteerMul * speedFactor * Math.Sign(Speed == 0 ? 1 : Speed);
            if (input.Handbrake && abs > 4f) { yaw *= 1.35f; Speed -= Math.Sign(Speed) * Math.Min(abs, 3.5f * dt); }
            HeadingDeg = Normalize(HeadingDeg + yaw * dt);

            // Low grip lets the nose rotate faster than the travel direction follows.
            float targetDrift = yaw * 0.35f / MathUtil.Clamp(grip, 0.25f, 2f) * MathUtil.Clamp01(abs / 12f);
            targetDrift = MathUtil.Clamp(targetDrift, -45f, 45f);
            float relax = DriftRecovery * MathUtil.Clamp(grip, 0.2f, 2f) * dt;
            DriftDeg += (targetDrift - DriftDeg) * MathUtil.Clamp01(relax);
            if (Math.Abs(DriftDeg) > 8f) Speed -= Math.Sign(Speed) * Math.Min(Math.Abs(Speed), Math.Abs(DriftDeg) * 0.04f * dt); // scrub

            double travel = (HeadingDeg - DriftDeg) * Math.PI / 180.0;
            float dx = (float)Math.Sin(travel) * Speed * dt, dz = (float)Math.Cos(travel) * Speed * dt;
            X += dx; Z += dz;
            DistanceMeters += Math.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>
        /// A collision reported by the physics layer. <paramref name="closingSpeed"/> is the speed component
        /// into the obstacle (m/s). Returns damage added.
        /// </summary>
        public float ApplyImpact(float closingSpeed)
        {
            closingSpeed = Math.Abs(closingSpeed);
            if (closingSpeed < 1.5f) { Speed *= 0.9f; return 0; }
            float dmg = closingSpeed * 1.4f * (1.25f - _base.Durability / 133f);
            Damage = Math.Min(100, Damage + dmg);
            Speed *= closingSpeed > 12 ? 0.15f : 0.4f;
            DriftDeg *= 0.5f;
            return dmg;
        }

        public void Repair() { Damage = 0; }
        public void RefillNitrous() { NitrousRemaining = _base.NitrousCapacitySeconds; }

        public static float Normalize(float deg)
        {
            deg %= 360f;
            if (deg > 180f) deg -= 360f;
            if (deg <= -180f) deg += 360f;
            return deg;
        }
    }
}
