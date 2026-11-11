using System.Collections.Generic;
using FFTS.Combat;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace FFTS.Runtime
{
    /// <summary>
    /// Clip library for the fight rig. Fill it with the clips imported from FFTS_FightRig.fbx (Humanoid): each clip
    /// keeps the Blender action name, which is the move's AnimationKey (e.g. "anim.boxing.jab").
    /// </summary>
    [CreateAssetMenu(menuName = "FFTS/Fight Animation Library", fileName = "FightAnimationLibrary")]
    public sealed class FightAnimationLibrary : ScriptableObject
    {
        public AnimationClip[] Clips;
        private Dictionary<string, AnimationClip> _byName;

        public AnimationClip Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (_byName == null)
            {
                _byName = new Dictionary<string, AnimationClip>();
                if (Clips != null) foreach (var c in Clips) if (c != null) _byName[c.name] = c;
            }
            return _byName.TryGetValue(key, out var clip) ? clip : null;
        }
    }

    /// <summary>
    /// Drives a Humanoid character from the combat simulation with no Animator Controller: the simulation's state and
    /// move tick pick the clip and its exact time (the clips are authored at 60 fps, one frame per simulation tick, with
    /// the hit on frame Startup). A short cross-fade smooths changes of clip; time within a move is never interpolated,
    /// so the impact frame on screen is the tick the simulation resolves the hit.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public sealed class FighterAnimator : MonoBehaviour
    {
        public FightAnimationLibrary Library;
        [Tooltip("Seconds to blend between clips. Kept short so strikes stay sharp.")]
        public float BlendSeconds = 0.06f;
        public const float TicksPerSecond = 60f;

        private FighterRuntime _f;
        private PlayableGraph _graph;
        private AnimationMixerPlayable _mixer;
        private AnimationClipPlayable _current, _previous;
        private string _currentKey;
        private float _blend;

        public string CurrentClip => _currentKey;

        public void Bind(FighterRuntime fighter)
        {
            _f = fighter;
            if (!_graph.IsValid())
            {
                _graph = PlayableGraph.Create(name + ".Fight");
                _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var output = AnimationPlayableOutput.Create(_graph, "Fight", GetComponent<Animator>());
                _mixer = AnimationMixerPlayable.Create(_graph, 2);
                output.SetSourcePlayable(_mixer);
            }
            _currentKey = null;
        }

        public void Unbind()
        {
            _f = null;
            if (_graph.IsValid()) _graph.Destroy();
        }

        private void OnDestroy() => Unbind();

        /// <summary>Call once per rendered frame after the simulation stepped.</summary>
        public void Present(float deltaTime)
        {
            if (_f == null || Library == null || !_graph.IsValid()) return;
            string key; float time; bool loop;
            Choose(out key, out time, out loop, deltaTime);
            var clip = Library.Get(key);
            if (clip == null) { key = StanceKey("stance_idle"); clip = Library.Get(key); }
            if (clip == null) return;
            if (key != _currentKey)
            {
                if (_previous.IsValid()) { _graph.Disconnect(_mixer, 1); _previous.Destroy(); }
                if (_current.IsValid()) { _graph.Disconnect(_mixer, 0); _previous = _current; _graph.Connect(_previous, 0, _mixer, 1); }
                _current = AnimationClipPlayable.Create(_graph, clip);
                _current.SetApplyFootIK(true);
                _graph.Connect(_current, 0, _mixer, 0);
                _currentKey = key; _blend = 0f;
            }
            _blend = BlendSeconds <= 0 ? 1f : Mathf.Min(1f, _blend + deltaTime / BlendSeconds);
            _mixer.SetInputWeight(0, _blend);
            _mixer.SetInputWeight(1, _previous.IsValid() ? 1f - _blend : 0f);
            _current.SetTime(loop ? Mathf.Repeat(time, clip.length) : Mathf.Min(time, clip.length));
            _graph.Evaluate(0f);
        }

        private string StanceKey(string clip)
        {
            var idle = _f.Stance.IdleAnimation;   // "anim.<style>.stance_idle"
            if (string.IsNullOrEmpty(idle)) return "anim.street." + clip;
            return idle.Substring(0, idle.LastIndexOf('.') + 1) + clip;
        }

        private void Choose(out string key, out float time, out bool loop, float dt)
        {
            loop = false;
            switch (_f.State)
            {
                case CombatState.Attacking: case CombatState.Combo: case CombatState.Throwing: case CombatState.Finisher:
                    key = _f.CurrentMove != null ? _f.CurrentMove.AnimationKey : StanceKey("stance_idle");
                    // MoveTick runs at the fighter's speed-adjusted startup; rescale so the authored impact frame lines up.
                    float authored = _f.CurrentMove != null ? _f.CurrentMove.Startup : 1f;
                    float scale = _f.MoveStartup > 0 ? authored / _f.MoveStartup : 1f;
                    float tick = _f.MoveTick <= _f.MoveStartup ? _f.MoveTick * scale : authored + (_f.MoveTick - _f.MoveStartup);
                    time = tick / TicksPerSecond;
                    return;
                case CombatState.Blocking: key = StanceKey("stance_guard"); time = Mathf.Min(_f.StateTicks, 20) / TicksPerSecond; return;
                case CombatState.BlockStun: key = "anim.react.block_impact"; break;
                case CombatState.HitStun: key = _f.StateDuration > 22 ? "anim.react.hit_head_heavy" : "anim.react.hit_head_light"; break;
                case CombatState.Stunned: key = "anim.react.stunned"; break;
                case CombatState.KnockedDown: key = "anim.react.knockdown"; break;
                case CombatState.GettingUp: key = "anim.react.get_up"; break;
                case CombatState.Dodging: key = _f.Stance.Defense == "Slip and roll" || _f.Stance.Defense == "Shoulder roll" ? "anim.react.slip" : "anim.react.dodge_back"; break;
                case CombatState.Grappled: key = "anim.react.grappled"; loop = true; break;
                case CombatState.FinisherVictim: key = "anim.react.stunned"; break;
                case CombatState.Defeated: key = "anim.react.defeated"; break;
                case CombatState.Moving: key = StanceKey("shuffle_forward"); loop = true; break;
                case CombatState.Grappling: key = StanceKey("stance_guard"); time = 0.3f; return;
                default: key = StanceKey("stance_idle"); loop = true; break;
            }
            time = _f.StateTicks / TicksPerSecond;
        }
    }
}
