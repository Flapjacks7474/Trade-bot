using System;
using System.Collections.Generic;
using FFTS.Input;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace FFTS.Runtime
{
    /// <summary>
    /// Builds an <see cref="IInputSource"/> for the active input backend. With the Input System package
    /// (the project default) actions are created in code from <see cref="InputBindingMap"/>, so rebinding
    /// data stays engine-independent. A legacy-input fallback keeps the project usable if the package is
    /// missing or the player setting is changed.
    /// </summary>
    public static class PlayerInputSource
    {
        public static IInputSource Create(InputBindingMap bindings)
        {
#if ENABLE_INPUT_SYSTEM
            return new InputSystemSource(bindings);
#else
            Debug.LogWarning("[FFTS] Input System not enabled; using legacy keyboard fallback (no rebinding, no gamepad).");
            return new LegacyKeyboardSource();
#endif
        }
    }

#if ENABLE_INPUT_SYSTEM
    public sealed class InputSystemSource : IInputSource, IDisposable
    {
        private const float MouseSensitivity = 0.12f;
        private const float StickDegreesPerSecond = 160f;

        private readonly InputActionMap _map = new InputActionMap("Gameplay");
        private readonly Dictionary<GameAction, InputAction> _buttons = new Dictionary<GameAction, InputAction>();
        private readonly InputAction _move, _lookPointer, _lookStick;
        private ButtonMask _previous;

        public InputSystemSource(InputBindingMap bindings)
        {
            _move = _map.AddAction("Move", InputActionType.Value, expectedControlType: "Vector2");
            _lookPointer = _map.AddAction("LookPointer", InputActionType.Value, expectedControlType: "Vector2");
            _lookStick = _map.AddAction("LookStick", InputActionType.Value, expectedControlType: "Vector2");

            var composite = new Dictionary<string, string>();
            foreach (var b in bindings.Bindings)
            {
                switch (b.Action)
                {
                    case GameAction.Move:
                        if (b.CompositePart != null) composite[b.CompositePart] = b.Path; else _move.AddBinding(b.Path);
                        break;
                    case GameAction.Look:
                        (b.Path.StartsWith("<Mouse>") || b.Path.StartsWith("<Pointer>") ? _lookPointer : _lookStick).AddBinding(b.Path);
                        break;
                    case GameAction.Steer:
                        break; // driving milestone
                    default:
                        if (!_buttons.TryGetValue(b.Action, out var act))
                            _buttons[b.Action] = act = _map.AddAction(b.Action.ToString(), InputActionType.Button);
                        act.AddBinding(b.Path);
                        break;
                }
            }
            if (composite.Count > 0)
            {
                var c = _move.AddCompositeBinding("2DVector");
                foreach (var kv in composite) c.With(char.ToUpperInvariant(kv.Key[0]) + kv.Key.Substring(1), kv.Value);
            }
            _map.Enable();
        }

        public InputFrame Sample()
        {
            var f = new InputFrame();
            var mv = _move.ReadValue<Vector2>();
            mv = Vector2.ClampMagnitude(mv, 1f);
            f.MoveX = mv.x; f.MoveY = mv.y;
            var look = _lookPointer.ReadValue<Vector2>() * MouseSensitivity + _lookStick.ReadValue<Vector2>() * StickDegreesPerSecond * Time.unscaledDeltaTime;
            f.LookX = look.x; f.LookY = look.y;
            foreach (var kv in _buttons)
                if (kv.Value.IsPressed()) f.Held |= InputBindingMap.MaskFor(kv.Key);
            f = InputFrame.WithEdges(f, _previous);
            _previous = f.Held;
            return f;
        }

        public void Dispose() { _map.Disable(); _map.Dispose(); }
    }
#endif

    /// <summary>Fallback for projects without the Input System package. Fixed PC bindings only.</summary>
    public sealed class LegacyKeyboardSource : IInputSource
    {
        private static readonly (KeyCode key, ButtonMask mask)[] Keys =
        {
            (KeyCode.J, ButtonMask.LightAttack), (KeyCode.K, ButtonMask.HeavyAttack), (KeyCode.I, ButtonMask.Kick), (KeyCode.L, ButtonMask.Grapple),
            (KeyCode.Space, ButtonMask.Block), (KeyCode.LeftShift, ButtonMask.Dodge), (KeyCode.O, ButtonMask.Counter), (KeyCode.U, ButtonMask.Finisher),
            (KeyCode.T, ButtonMask.Taunt), (KeyCode.E, ButtonMask.Interact), (KeyCode.Tab, ButtonMask.Phone), (KeyCode.M, ButtonMask.Map),
            (KeyCode.Escape, ButtonMask.Pause), (KeyCode.F5, ButtonMask.QuickSave), (KeyCode.F9, ButtonMask.QuickLoad), (KeyCode.F1, ButtonMask.Help)
        };
        private ButtonMask _previous;

        public InputFrame Sample()
        {
            var f = new InputFrame
            {
                MoveX = (UnityEngine.Input.GetKey(KeyCode.D) ? 1 : 0) - (UnityEngine.Input.GetKey(KeyCode.A) ? 1 : 0),
                MoveY = (UnityEngine.Input.GetKey(KeyCode.W) ? 1 : 0) - (UnityEngine.Input.GetKey(KeyCode.S) ? 1 : 0),
                LookX = UnityEngine.Input.GetAxisRaw("Mouse X") * 2f,
                LookY = UnityEngine.Input.GetAxisRaw("Mouse Y") * 2f
            };
            foreach (var (key, mask) in Keys) if (UnityEngine.Input.GetKey(key)) f.Held |= mask;
            f = InputFrame.WithEdges(f, _previous);
            _previous = f.Held;
            return f;
        }
    }
}
