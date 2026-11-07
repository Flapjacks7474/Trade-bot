using System;
using System.Collections.Generic;
using System.IO;
using FFTS.Core;
using FFTS.Input;
using FFTS.SaveSystem;
using FFTS.Session;
using FFTS.World;
using UnityEngine;

namespace FFTS.Runtime
{
    /// <summary>
    /// Unity-side owner of the <see cref="GameSession"/>. Loads content, samples input once per frame,
    /// advances game time, routes NPC conversations and handles save/load. Gameplay rules live in the
    /// simulation assembly; this class only bridges them to the engine.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class CoreGameManager : MonoBehaviour
    {
        public static CoreGameManager Instance { get; private set; }

        [SerializeField] private ContentLibrary content;
        [Tooltip("Real seconds per in-game minute while exploring.")]
        [SerializeField] private float realSecondsPerGameMinute = 1f;
        [SerializeField] private long newGameSeed = 20261103;

        public GameSession Session { get; private set; }
        public DistrictLayout Layout { get; private set; }
        public NpcConversationRules Conversations { get; private set; }
        public ValidationReport ContentReport { get; private set; }
        public SaveSlotManager<GameSaveData> Saves { get; private set; }
        public InputFrame Frame { get; private set; }
        public Transform Player { get; set; }

        /// <summary>Raised for short on-screen notifications.</summary>
        public event Action<string> Toast;
        /// <summary>Raised after a save is loaded so world objects can resync.</summary>
        public event Action Reloaded;

        private IInputSource _input;
        private readonly HashSet<object> _locks = new HashSet<object>();
        private float _clockAccumulator;
        private bool _autosavePending;
        public bool ControlsLocked => _locks.Count > 0;

        public FightDirector Fights { get; private set; }
        public DialoguePanel Dialogue { get; private set; }
        public DrivingDirector Driving { get; private set; }
        public RaceDirector Races { get; private set; }
        public GaragePanel Garages { get; private set; }

        private T Ensure<T>() where T : Component
        {
            var c = GetComponent<T>();
            if (c == null) c = gameObject.AddComponent<T>();
            return c;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            ContentReport = new ValidationReport();
            if (content == null) ContentReport.Error("CoreGameManager has no ContentLibrary assigned. Run FFTS > Build Southside Slice Scene.");
            var sources = content != null ? content.ToSources() : new ContentSources();
            var gameContent = GameContentLoader.Load(sources, ContentReport, out var layouts);
            Layout = layouts.Count > 0 ? layouts[0] : new DistrictLayout();
            Conversations = content != null && content.ConversationRules != null
                ? NpcConversationRules.Parse(content.ConversationRules.text, gameContent, ContentReport)
                : new NpcConversationRules(new List<ConversationRule>());
            Session = new GameSession(gameContent, (ulong)newGameSeed, ContentReport);
            Session.Position.X = Layout.SpawnX; Session.Position.Z = Layout.SpawnZ;
            foreach (var e in ContentReport.Errors) Debug.LogError("[FFTS content] " + e);

            Saves = GameSession.CreateSaveManager(new FileSaveStorage(Path.Combine(Application.persistentDataPath, "saves")));
            _input = PlayerInputSource.Create(InputBindingMap.CreateDefault());

            Session.Bus.Subscribe<Core.MissionCompleted>(e => { if (!e.WasReplay) { _autosavePending = true; Notify("Mission complete: " + Title(e.MissionId)); } });
            Session.Bus.Subscribe<Core.MissionStateChanged>(e => { if (e.NewState == "Failed") Notify("Mission failed: " + Title(e.MissionId) + " — retrying from checkpoint"); });
            Session.Bus.Subscribe<Core.ObjectiveCompleted>(e => Notify("Objective complete"));
            Session.Bus.Subscribe<FFTS.Racing.RaceCompleted>(e => { if (!e.Dnf) _autosavePending = true; });
            Session.Bus.Subscribe<FFTS.Vehicles.VehiclePurchased>(e => _autosavePending = true);
            Session.Bus.HandlerFailed += (evt, ex) => Debug.LogException(ex);

            // Explicit checks: '??' bypasses UnityEngine.Object's overloaded null comparison.
            Fights = GetComponent<FightDirector>();
            if (Fights == null) Fights = gameObject.AddComponent<FightDirector>();
            Dialogue = GetComponent<DialoguePanel>();
            if (Dialogue == null) Dialogue = gameObject.AddComponent<DialoguePanel>();
            Driving = Ensure<DrivingDirector>();
            Races = Ensure<RaceDirector>();
            Garages = Ensure<GaragePanel>();
        }

        private void OnDestroy()
        {
            if (_input is IDisposable d) d.Dispose();
            if (Instance == this) Instance = null;
        }

        public string Title(string missionId) => Session.Missions.Definition(missionId)?.Title ?? missionId;

        public void Notify(string message) { Debug.Log("[FFTS] " + message); Toast?.Invoke(message); }

        public void Lock(object owner) => _locks.Add(owner);
        public void Unlock(object owner) => _locks.Remove(owner);

        private void Update()
        {
            Frame = _input.Sample();

            if (!Fights.InFight && !Dialogue.IsOpen && !Garages.IsOpen && !Races.Active)
            {
                _clockAccumulator += Time.deltaTime;
                int minutes = (int)(_clockAccumulator / Mathf.Max(0.01f, realSecondsPerGameMinute));
                if (minutes > 0) { _clockAccumulator -= minutes * realSecondsPerGameMinute; Session.Clock.Advance(minutes); }

                if (Frame.WasPressed(ButtonMask.QuickSave)) SaveTo(SaveSlotManager<GameSaveData>.ManualSlot(0));
                if (Frame.WasPressed(ButtonMask.QuickLoad)) LoadFrom(SaveSlotManager<GameSaveData>.ManualSlot(0));
            }
        }

        private void LateUpdate()
        {
            if (_autosavePending && !Fights.InFight && !Dialogue.IsOpen && !Races.Active)
            {
                _autosavePending = false;
                Driving.Flush();
                CapturePlayerPosition();
                string slot = Saves.Autosave(Session.Capture());
                Notify("Autosaved (" + slot + ")");
            }
        }

        // ------------------------------------------------------------------ talking & triggers

        public void ReportTrigger(string trigger)
        {
            if (string.IsNullOrEmpty(trigger)) return;
            Session.ReportTrigger(trigger);
        }

        public void TalkTo(string npcId, string displayName)
        {
            var rule = Conversations.Choose(Session, npcId);
            if (rule == null) { Notify(displayName + " has nothing to say."); return; }
            if (rule.Bark != null) { Notify(rule.Bark); return; }
            if (rule.FightId != null) { Fights.Begin(rule.FightId, rule.Venue, CurrentLocationId()); return; }
            var runner = Session.StartDialogue(rule.DialogueId);
            if (runner == null) { Notify("Missing dialogue " + rule.DialogueId); return; }
            Dialogue.Open(runner, displayName);
        }

        /// <summary>Location the player is currently inside (or nearest within 30 m).</summary>
        public string CurrentLocationId()
        {
            if (Player == null) return null;
            LocationDefinition best = null; float bestD = 30f;
            foreach (var l in Layout.Locations)
            {
                float d = Vector2.Distance(new Vector2(Player.position.x, Player.position.z), new Vector2(l.X, l.Z)) - Mathf.Max(l.Width, l.Depth) / 2f;
                if (d < bestD) { bestD = d; best = l; }
            }
            return best?.Id;
        }

        public VenueType VenueFor(string locationId)
        {
            var l = locationId != null ? Layout.Find(locationId) : null;
            if (l == null) return VenueType.Street;
            return l.Type == "venue" ? VenueType.Underground : l.Type == "gym" ? VenueType.Sanctioned : VenueType.Street;
        }

        // ------------------------------------------------------------------ persistence

        private void CapturePlayerPosition()
        {
            if (Player == null) return;
            Session.Position.X = Player.position.x; Session.Position.Y = Player.position.y; Session.Position.Z = Player.position.z;
            Session.Position.Yaw = Player.eulerAngles.y;
        }

        public void SaveTo(string slot)
        {
            if (Fights.InFight) { Notify("Can't save during a fight."); return; }
            if (Races.Active) { Notify("Can't save during a race."); return; }
            Driving.Flush();
            CapturePlayerPosition();
            try { Saves.Save(slot, Session.Capture()); Notify("Saved (" + slot + ")"); }
            catch (Exception ex) { Debug.LogException(ex); Notify("Save failed: " + ex.Message); }
        }

        public void LoadFrom(string slot)
        {
            if (Races.Active || Fights.InFight) { Notify("Finish what you're doing first."); return; }
            var r = Saves.Load(slot);
            if (!r.Succeeded) { Notify("Load failed: " + r.Outcome + " " + r.Message); return; }
            Session.Restore(r.Data);
            if (Player != null)
            {
                var cc = Player.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;
                Player.SetPositionAndRotation(new Vector3(r.Data.Position.X, r.Data.Position.Y, r.Data.Position.Z), Quaternion.Euler(0, r.Data.Position.Yaw, 0));
                if (cc != null) cc.enabled = true;
            }
            Reloaded?.Invoke();
            Notify(r.Outcome == LoadOutcome.RecoveredFromBackup ? "Loaded backup (primary save was damaged)" : "Loaded (" + slot + ")");
        }
    }
}
