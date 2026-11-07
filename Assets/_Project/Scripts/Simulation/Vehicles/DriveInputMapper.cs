using FFTS.Input;

namespace FFTS.Vehicles
{
    /// <summary>Device-independent input → driving controls. W/S or triggers for throttle/brake, stick/AD to steer.</summary>
    public static class DriveInputMapper
    {
        public static DriveInput Map(InputFrame f, float surfaceGrip)
        {
            float throttle = f.IsHeld(ButtonMask.Accelerate) ? 1f : (f.MoveY > 0 ? f.MoveY : 0f);
            float brake = f.IsHeld(ButtonMask.Brake) ? 1f : (f.MoveY < 0 ? -f.MoveY : 0f);
            return new DriveInput
            {
                Throttle = throttle,
                Brake = brake,
                Steer = f.Steer != 0 ? f.Steer : f.MoveX,
                Handbrake = f.IsHeld(ButtonMask.Handbrake),
                Nitrous = f.IsHeld(ButtonMask.Nitrous),
                SurfaceGrip = surfaceGrip
            };
        }
    }
}
