using System.Collections.Generic;
using FFTS.Combat;
using FFTS.Core;
using FFTS.Input;
using FFTS.Session;
using UnityEngine;

namespace FFTS.Runtime
{
    /// <summary>
    /// Runs a <see cref="CombatSimulation"/> at a fixed 60 Hz inside the open world and presents it.
    /// Input presses are latched between ticks so none are lost or duplicated at any frame rate.
    /// </summary>
    public sealed class FightDirector : MonoBehaviour
    {
        public const string PlayerFighterId = "fighter.player";
        public const float WitnessRadius = 25f;

        public bool InFight => Sim != null;
        public CombatSimulation Sim { get; private set; }
        public FighterAI Ai { get; private set; }
        public string OpponentName { get; private set; }
        public string LastEventText { get; private set; }
        public FinisherDenial FinisherStatus => Sim != null ? Sim.CanFinish(Sim.A) : FinisherDenial.NoFinisherMove;

        private string _opponentId, _locationId;
        private VenueType _venue;
        private Vector3 _origin, _axis;
        private FighterView _playerView, _opponentView;
        private FighterAnimator _playerAnim, _opponentAnim;   // present only once the rigged character prefabs exist
        private GameObject _opponentGo;
        private float _accumulator, _endTimer;
        private ButtonMask _latchedPresses;
        private readonly List<string> _witnesses = new List<string>();

        public void Begin(string opponentFighterId, VenueType? venueOverride, string locationId)
        {
            var gm = CoreGameManager.Instance;
            if (InFight || gm.Player == null) return;
            var db = gm.Session.Content.Combat;
            var playerDef = db.GetFighter(PlayerFighterId); var oppDef = db.GetFighter(opponentFighterId);
            if (playerDef == null || oppDef == null) { gm.Notify("Missing fighter data for " + opponentFighterId); return; }

            _opponentId = opponentFighterId; _locationId = locationId;
            _venue = venueOverride ?? gm.VenueFor(locationId);
            OpponentName = oppDef.DisplayName;

            _origin = gm.Player.position; _origin.y = 0;
            _axis = gm.Player.forward; _axis.y = 0;
            _axis = _axis.sqrMagnitude < 0.01f ? Vector3.forward : _axis.normalized;

            // The player fights with the styles they have learned and equipped (hybrid included when unlocked).
            var a = new FighterRuntime(playerDef, gm.Session.Styles != null ? gm.Session.Styles.BuildMoveset() : db.BuildMoveset(playerDef), -1.1f);
            var b = new FighterRuntime(oppDef, db.BuildMoveset(oppDef), 1.1f);
            Sim = new CombatSimulation(new CombatConfig(), a, b, gm.Session.NextFightId());
            Ai = new FighterAI(b, oppDef.AiStyle, oppDef.AiDifficulty, new DeterministicRandom(gm.Session.Random.State ^ (ulong)gm.Session.Clock.TotalMinutes));

            CollectWitnesses(gm);
            gm.Lock(this);
            var cc = gm.Player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            _playerView = gm.Player.gameObject.GetComponent<FighterView>();
            if (_playerView == null) _playerView = gm.Player.gameObject.AddComponent<FighterView>();
            _playerView.Bind(a);
            _opponentGo = Graybox.Capsule("Opponent " + oppDef.DisplayName, null, _origin + _axis * 1.1f, "#8a8a8a");
            _opponentView = _opponentGo.AddComponent<FighterView>();
            _opponentView.Bind(b);
            _playerAnim = gm.Player.GetComponentInChildren<FighterAnimator>();
            if (_playerAnim != null) _playerAnim.Bind(a);
            _opponentAnim = _opponentGo.GetComponentInChildren<FighterAnimator>();
            if (_opponentAnim != null) _opponentAnim.Bind(b);
            _accumulator = 0; _endTimer = 0; _latchedPresses = ButtonMask.None;
            gm.Notify("FIGHT: " + OpponentName + " (" + _venue + ")");
        }

        private void CollectWitnesses(CoreGameManager gm)
        {
            _witnesses.Clear();
            var lw = gm.Session.LivingWorld;
            if (lw != null)
            {
                foreach (var id in lw.NpcsNear(gm.Player.position.x, gm.Player.position.z, WitnessRadius))
                    if (id != GameSession.NpcIdForFighter(_opponentId)) _witnesses.Add(id);
                return;
            }
            foreach (var n in Object.FindObjectsByType<NpcPresence>(FindObjectsSortMode.None))
            {
                if (!n.gameObject.activeInHierarchy) continue;
                if (Vector3.Distance(n.transform.position, gm.Player.position) > WitnessRadius) continue;
                string npc = n.Placement.NpcId;
                if (npc != GameSession.NpcIdForFighter(_opponentId) && !_witnesses.Contains(npc)) _witnesses.Add(npc);
            }
        }

        private void Update()
        {
            if (!InFight) return;
            var gm = CoreGameManager.Instance;
            var frame = gm.Frame;
            _latchedPresses |= frame.Pressed;

            if (Sim.IsOver)
            {
                _endTimer += Time.deltaTime;
                if (_endTimer > 2.5f) End(gm);
                UpdateViews(gm);
                return;
            }

            _accumulator += Mathf.Min(Time.deltaTime, 0.1f);
            float dt = Sim.Config.Dt;
            while (_accumulator >= dt && !Sim.IsOver)
            {
                _accumulator -= dt;
                var tickFrame = frame; tickFrame.Pressed = _latchedPresses; _latchedPresses = ButtonMask.None;
                float toward = Mathf.Sign(Sim.B.X - Sim.A.X);
                var playerInput = CombatInputMapper.Map(tickFrame, toward);
                Sim.Step(playerInput, Ai.Think(Sim));
                foreach (var e in Sim.Events) HandleEvent(gm, e);
            }
            UpdateViews(gm);
        }

        private void HandleEvent(CoreGameManager gm, CombatEvent e)
        {
            bool byPlayer = e.ActorId == PlayerFighterId;
            switch (e.Type)
            {
                case CombatEventType.Blocked:
                    if (e.TargetId == PlayerFighterId) gm.ReportTrigger("combat.tutorial.blocked");
                    break;
                case CombatEventType.Hit:
                    if (byPlayer && e.Detail != null && e.Detail.StartsWith("combo ") && int.TryParse(e.Detail.Substring(6), out var n) && n >= 3)
                        gm.ReportTrigger("combat.tutorial.combo");
                    (e.TargetId == PlayerFighterId ? _playerView : _opponentView)?.Flash();
                    break;
                case CombatEventType.Thrown:
                    if (byPlayer) gm.ReportTrigger("combat.tutorial.thrown");
                    break;
                case CombatEventType.FinisherStarted:
                    Rig()?.PlayFinisherShot(1.4f);
                    break;
                case CombatEventType.FinisherDenied:
                    if (byPlayer) LastEventText = "Finisher not ready: " + e.Detail;
                    return;
            }
            if (e.Type != CombatEventType.MoveStarted) LastEventText = e.Type + (e.MoveId != null ? " · " + e.MoveId.Replace("street.", "") : "");
        }

        /// <summary>Returns a real reference or a true C# null (safe for '?.'; avoids Unity fake-null objects).</summary>
        private static CameraRig Rig()
        {
            var cam = Camera.main;
            if (cam == null) return null;
            var rig = cam.GetComponent<CameraRig>();
            return rig != null ? rig : null;
        }

        private void UpdateViews(CoreGameManager gm)
        {
            Vector3 pa = _origin + _axis * Sim.A.X, pb = _origin + _axis * Sim.B.X;
            _playerView.Present(pa, pb - pa, Sim);
            _opponentView.Present(pb, pa - pb, Sim);
            if (_playerAnim != null) _playerAnim.Present(Time.deltaTime);
            if (_opponentAnim != null) _opponentAnim.Present(Time.deltaTime);
            Rig()?.SetFight(pa, pb, _axis);
        }

        private void End(CoreGameManager gm)
        {
            var result = Sim.Result;
            var player = gm.Player;
            _playerView.Unbind();
            if (_playerAnim != null) _playerAnim.Unbind();
            if (_opponentAnim != null) _opponentAnim.Unbind();
            if (_opponentGo != null) Destroy(_opponentGo);
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = true;
            Rig()?.SetExplore();
            Sim = null; Ai = null;
            gm.Unlock(this);

            bool won = result.WinnerId == PlayerFighterId;
            gm.Notify(won ? "You beat " + OpponentName + " (" + result.Method + ")" : result.Method == FightEndMethod.Draw ? "Draw." : OpponentName + " beat you.");
            gm.Session.OnFightCompleted(result, _opponentId, _venue, _locationId, _witnesses);
        }
    }
}
