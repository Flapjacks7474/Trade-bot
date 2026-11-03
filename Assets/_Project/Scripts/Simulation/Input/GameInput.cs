using System;
using System.Collections.Generic;

namespace FFTS.Input
{
    /// <summary>Device-independent gameplay actions. Gameplay code only ever sees these.</summary>
    public enum GameAction
    {
        Move, Look,
        LightAttack, HeavyAttack, Kick, Grapple, Block, Dodge, Counter, Finisher, Taunt,
        Interact, Phone, Map, Pause, QuickSave, QuickLoad, Help,
        Accelerate, Brake, Steer, Handbrake, Nitrous
    }

    [Flags]
    public enum ButtonMask : uint
    {
        None = 0,
        LightAttack = 1 << 0, HeavyAttack = 1 << 1, Kick = 1 << 2, Grapple = 1 << 3,
        Block = 1 << 4, Dodge = 1 << 5, Counter = 1 << 6, Finisher = 1 << 7, Taunt = 1 << 8,
        Interact = 1 << 9, Phone = 1 << 10, Map = 1 << 11, Pause = 1 << 12,
        Accelerate = 1 << 13, Brake = 1 << 14, Handbrake = 1 << 15, Nitrous = 1 << 16,
        QuickSave = 1 << 17, QuickLoad = 1 << 18, Help = 1 << 19
    }

    /// <summary>One sampled frame of input. Produced by keyboard, gamepad or touch adapters alike.</summary>
    public struct InputFrame
    {
        public float MoveX, MoveY;   // left stick / WASD, -1..1
        public float LookX, LookY;   // camera delta
        public float Steer;          // -1..1
        public ButtonMask Held;
        public ButtonMask Pressed;   // went down this frame
        public ButtonMask Released;  // went up this frame

        public bool IsHeld(ButtonMask b) => (Held & b) != 0;
        public bool WasPressed(ButtonMask b) => (Pressed & b) != 0;

        /// <summary>Derives Pressed/Released edges from the previous frame's held mask.</summary>
        public static InputFrame WithEdges(InputFrame current, ButtonMask previousHeld)
        {
            current.Pressed = current.Held & ~previousHeld;
            current.Released = previousHeld & ~current.Held;
            return current;
        }
    }

    public interface IInputSource
    {
        InputFrame Sample();
    }

    public sealed class InputBinding
    {
        public GameAction Action;
        public string Path;        // Input System control path, e.g. "<Keyboard>/j"
        public string CompositePart; // for 2D vectors: "up", "down", "left", "right"; otherwise null
        public InputBinding() { }
        public InputBinding(GameAction action, string path, string part = null) { Action = action; Path = path; CompositePart = part; }
    }

    /// <summary>
    /// Default bindings from the design spec, plus player overrides. The overrides are what gets
    /// persisted in settings; defaults stay in code so new actions appear automatically.
    /// Kick is bound to I (not in the original key list; adjacent to J/K/L).
    /// </summary>
    public sealed class InputBindingMap
    {
        private readonly List<InputBinding> _bindings = new List<InputBinding>();
        public IReadOnlyList<InputBinding> Bindings => _bindings;

        public static InputBindingMap CreateDefault()
        {
            var m = new InputBindingMap();
            m.Add(GameAction.Move, "<Keyboard>/w", "up"); m.Add(GameAction.Move, "<Keyboard>/s", "down");
            m.Add(GameAction.Move, "<Keyboard>/a", "left"); m.Add(GameAction.Move, "<Keyboard>/d", "right");
            m.Add(GameAction.Move, "<Gamepad>/leftStick");
            m.Add(GameAction.Look, "<Mouse>/delta"); m.Add(GameAction.Look, "<Gamepad>/rightStick");
            m.Add(GameAction.LightAttack, "<Keyboard>/j"); m.Add(GameAction.LightAttack, "<Gamepad>/buttonWest");
            m.Add(GameAction.HeavyAttack, "<Keyboard>/k"); m.Add(GameAction.HeavyAttack, "<Gamepad>/buttonNorth");
            m.Add(GameAction.Kick, "<Keyboard>/i"); m.Add(GameAction.Kick, "<Gamepad>/buttonEast");
            m.Add(GameAction.Grapple, "<Keyboard>/l"); m.Add(GameAction.Grapple, "<Gamepad>/rightShoulder");
            m.Add(GameAction.Block, "<Keyboard>/space"); m.Add(GameAction.Block, "<Gamepad>/leftTrigger");
            m.Add(GameAction.Dodge, "<Keyboard>/leftShift"); m.Add(GameAction.Dodge, "<Gamepad>/buttonSouth");
            m.Add(GameAction.Counter, "<Keyboard>/o"); m.Add(GameAction.Counter, "<Gamepad>/leftShoulder");
            m.Add(GameAction.Finisher, "<Keyboard>/u"); m.Add(GameAction.Finisher, "<Gamepad>/rightTrigger");
            m.Add(GameAction.Taunt, "<Keyboard>/t"); m.Add(GameAction.Taunt, "<Gamepad>/dpad/up");
            m.Add(GameAction.Interact, "<Keyboard>/e"); m.Add(GameAction.Interact, "<Gamepad>/dpad/right");
            m.Add(GameAction.Phone, "<Keyboard>/tab"); m.Add(GameAction.Phone, "<Gamepad>/select");
            m.Add(GameAction.Map, "<Keyboard>/m"); m.Add(GameAction.Map, "<Gamepad>/dpad/down");
            m.Add(GameAction.Pause, "<Keyboard>/escape"); m.Add(GameAction.Pause, "<Gamepad>/start");
            m.Add(GameAction.QuickSave, "<Keyboard>/f5"); m.Add(GameAction.QuickLoad, "<Keyboard>/f9"); m.Add(GameAction.Help, "<Keyboard>/f1");
            m.Add(GameAction.Accelerate, "<Keyboard>/w"); m.Add(GameAction.Accelerate, "<Gamepad>/rightTrigger");
            m.Add(GameAction.Brake, "<Keyboard>/s"); m.Add(GameAction.Brake, "<Gamepad>/leftTrigger");
            m.Add(GameAction.Handbrake, "<Keyboard>/space"); m.Add(GameAction.Handbrake, "<Gamepad>/buttonSouth");
            m.Add(GameAction.Nitrous, "<Keyboard>/leftShift"); m.Add(GameAction.Nitrous, "<Gamepad>/buttonWest");
            return m;
        }

        public void Add(GameAction action, string path, string part = null) => _bindings.Add(new InputBinding(action, path, part));

        /// <summary>Replaces every binding of an action on the same device ("&lt;Keyboard&gt;", "&lt;Gamepad&gt;").</summary>
        public void Rebind(GameAction action, string newPath, string part = null)
        {
            string device = DeviceOf(newPath);
            _bindings.RemoveAll(b => b.Action == action && DeviceOf(b.Path) == device && b.CompositePart == part);
            _bindings.Add(new InputBinding(action, newPath, part));
        }

        /// <summary>Returns actions (in the same input context) that share a control path, for rebinding UI warnings.</summary>
        public List<string> FindConflicts(bool onFoot)
        {
            var conflicts = new List<string>();
            var seen = new Dictionary<string, GameAction>();
            foreach (var b in _bindings)
            {
                if (IsDrivingAction(b.Action) == onFoot) continue;
                if (seen.TryGetValue(b.Path, out var other) && other != b.Action)
                    conflicts.Add($"{b.Path}: {other} / {b.Action}");
                else seen[b.Path] = b.Action;
            }
            return conflicts;
        }

        /// <summary>Maps a button action to its mask bit; Move/Look/Steer return None.</summary>
        public static ButtonMask MaskFor(GameAction a)
        {
            switch (a)
            {
                case GameAction.LightAttack: return ButtonMask.LightAttack; case GameAction.HeavyAttack: return ButtonMask.HeavyAttack;
                case GameAction.Kick: return ButtonMask.Kick; case GameAction.Grapple: return ButtonMask.Grapple;
                case GameAction.Block: return ButtonMask.Block; case GameAction.Dodge: return ButtonMask.Dodge;
                case GameAction.Counter: return ButtonMask.Counter; case GameAction.Finisher: return ButtonMask.Finisher;
                case GameAction.Taunt: return ButtonMask.Taunt; case GameAction.Interact: return ButtonMask.Interact;
                case GameAction.Phone: return ButtonMask.Phone; case GameAction.Map: return ButtonMask.Map; case GameAction.Pause: return ButtonMask.Pause;
                case GameAction.QuickSave: return ButtonMask.QuickSave; case GameAction.QuickLoad: return ButtonMask.QuickLoad; case GameAction.Help: return ButtonMask.Help;
                case GameAction.Accelerate: return ButtonMask.Accelerate; case GameAction.Brake: return ButtonMask.Brake;
                case GameAction.Handbrake: return ButtonMask.Handbrake; case GameAction.Nitrous: return ButtonMask.Nitrous;
                default: return ButtonMask.None;
            }
        }

        public static bool IsDrivingAction(GameAction a) =>
            a == GameAction.Accelerate || a == GameAction.Brake || a == GameAction.Steer || a == GameAction.Handbrake || a == GameAction.Nitrous;

        private static string DeviceOf(string path)
        {
            int end = path.IndexOf('>');
            return end > 0 ? path.Substring(0, end + 1) : path;
        }
    }
}
