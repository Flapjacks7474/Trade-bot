using FFTS.Combat;
using UnityEngine;

namespace FFTS.Runtime
{
    /// <summary>
    /// Placeholder fighter presentation: positions the capsule on the fight line, squashes it when down,
    /// extends a "strike" block during active frames and tints by state. Replaced by Animator-driven
    /// characters when animation assets exist (AnimationKey on each move is the hook).
    /// </summary>
    public sealed class FighterView : MonoBehaviour
    {
        private FighterRuntime _f;
        private Renderer _renderer;
        private Color _baseColor;
        private Transform _strike;
        private float _flash;

        public void Bind(FighterRuntime f)
        {
            _f = f;
            _renderer = GetComponent<Renderer>();
            if (_renderer != null) _baseColor = _renderer.material.color;
            if (_strike == null)
            {
                _strike = Graybox.Box("Strike", transform, transform.position, new Vector3(0.25f, 0.25f, 0.8f), "#ffffff", collider: false).transform;
                _strike.SetParent(transform, true);
            }
            _strike.gameObject.SetActive(false);
        }

        public void Unbind()
        {
            _f = null;
            transform.localScale = Vector3.one;
            if (_strike != null) _strike.gameObject.SetActive(false);
            if (_renderer != null) _renderer.material.color = _baseColor;
        }

        public void Flash() => _flash = 0.12f;

        public void Present(Vector3 feet, Vector3 toOpponent, CombatSimulation sim)
        {
            if (_f == null) return;
            bool down = _f.State == CombatState.KnockedDown || _f.State == CombatState.Defeated;
            transform.position = feet + Vector3.up * (down ? 0.35f : 1f);
            toOpponent.y = 0;
            if (toOpponent.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(toOpponent);
            transform.localScale = down ? new Vector3(1.2f, 0.35f, 1.2f) : _f.State == CombatState.Blocking || _f.State == CombatState.BlockStun ? new Vector3(1.1f, 0.9f, 1.1f) : Vector3.one;

            bool striking = _f.InMovePhaseActive || (_f.State == CombatState.Throwing && _f.InMoveStartup == false);
            _strike.gameObject.SetActive(striking);
            if (striking)
            {
                float reach = _f.CurrentMove != null ? _f.CurrentMove.Range : 1f;
                _strike.localPosition = new Vector3(0, _f.CurrentMove != null && _f.CurrentMove.Kind == MoveKind.Kick ? -0.4f : 0.3f, 0.3f + reach * 0.5f);
                _strike.localScale = new Vector3(0.25f, 0.25f, reach);
            }

            if (_renderer != null)
            {
                _flash -= Time.deltaTime;
                _renderer.material.color = _flash > 0 ? Color.white : TintFor(_f.State);
            }
        }

        private Color TintFor(CombatState s)
        {
            switch (s)
            {
                case CombatState.Blocking: case CombatState.BlockStun: return Color.Lerp(_baseColor, new Color(0.3f, 0.5f, 1f), 0.6f);
                case CombatState.HitStun: case CombatState.Stunned: return Color.Lerp(_baseColor, new Color(1f, 0.6f, 0.1f), 0.6f);
                case CombatState.Countering: return Color.Lerp(_baseColor, Color.cyan, 0.6f);
                case CombatState.Grappling: case CombatState.Throwing: return Color.Lerp(_baseColor, new Color(0.6f, 0.2f, 0.8f), 0.6f);
                case CombatState.Dodging: return Color.Lerp(_baseColor, Color.green, 0.5f);
                case CombatState.Finisher: return new Color(1f, 0.84f, 0f);
                case CombatState.Defeated: return Color.Lerp(_baseColor, Color.black, 0.6f);
                default: return _baseColor;
            }
        }
    }
}
