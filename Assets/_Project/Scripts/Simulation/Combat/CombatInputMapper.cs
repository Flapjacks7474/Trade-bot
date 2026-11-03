using FFTS.Input;

namespace FFTS.Combat
{
    /// <summary>
    /// Converts device-independent input frames into fight commands. On keyboard there is no
    /// dedicated counter key requirement: tapping Block just before a hit lands is a parry-counter.
    /// The explicit Counter action (mobile button / O / LB) enters the counter stance.
    /// </summary>
    public static class CombatInputMapper
    {
        public static FighterInput Map(InputFrame frame, float towardOpponentSign)
        {
            var input = new FighterInput
            {
                Move = frame.MoveX * towardOpponentSign,
                BlockHeld = frame.IsHeld(ButtonMask.Block),
                Command = CombatCommand.None
            };
            // Priority when several buttons go down in the same frame.
            if (frame.WasPressed(ButtonMask.Finisher)) input.Command = CombatCommand.Finisher;
            else if (frame.WasPressed(ButtonMask.Counter)) input.Command = CombatCommand.Counter;
            else if (frame.WasPressed(ButtonMask.Dodge)) input.Command = CombatCommand.Dodge;
            else if (frame.WasPressed(ButtonMask.Grapple)) input.Command = CombatCommand.Grapple;
            else if (frame.WasPressed(ButtonMask.HeavyAttack)) input.Command = CombatCommand.Heavy;
            else if (frame.WasPressed(ButtonMask.Kick)) input.Command = CombatCommand.Kick;
            else if (frame.WasPressed(ButtonMask.LightAttack)) input.Command = CombatCommand.Light;
            else if (frame.WasPressed(ButtonMask.Taunt)) input.Command = CombatCommand.Taunt;
            return input;
        }
    }
}
