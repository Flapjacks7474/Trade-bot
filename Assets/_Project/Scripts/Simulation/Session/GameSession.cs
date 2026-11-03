using System;
using System.Collections.Generic;
using FFTS.Combat;
using FFTS.Core;
using FFTS.Dialogue;
using FFTS.Economy;
using FFTS.Missions;
using FFTS.Relationships;
using FFTS.Reputation;
using FFTS.SaveSystem;
using FFTS.Story;

namespace FFTS.Session
{
    public sealed class PlayerIdentity
    {
        public string Name = "Player";
        public string StageName = "";
        public Dictionary<string, string> Appearance = new Dictionary<string, string>
        {
            ["hair"] = "hair.low_taper_fade", ["facial_hair"] = "facial.clean", ["outfit"] = "outfit.southside_basic"
        };
        public List<string> Contacts = new List<string>();
        public Dictionary<string, int> Inventory = new Dictionary<string, int>();
    }

    public sealed class WorldPosition { public float X, Y, Z, Yaw; public string DistrictId = "district.southside"; }

    /// <summary>Root save payload. Bump <see cref="GameSession.SaveSchemaVersion"/> and add a migration on any breaking change.</summary>
    public sealed class GameSaveData
    {
        public string GameVersion;
        public string SaveLabel;
        public PlayerIdentity Player = new PlayerIdentity();
        public WorldPosition Position = new WorldPosition();
        public long ClockMinutes;
        public ulong RngState;
        public BankState Bank = new BankState();
        public ReputationState Reputation = new ReputationState();
        public SocialState Social = new SocialState();
        public StoryState Story = new StoryState();
        public MissionsState Missions = new MissionsState();
        public int FightCounter;
    }

    /// <summary>Static content the session needs, loaded once by the platform layer.</summary>
    public sealed class GameContent
    {
        public List<MissionDefinition> Missions = new List<MissionDefinition>();
        public Dictionary<string, DialogueDefinition> Dialogues = new Dictionary<string, DialogueDefinition>();
        public CombatDatabase Combat;
    }

    /// <summary>
    /// The single owner of live game state. Systems communicate through the event bus; the session wires
    /// cross-system rules (rewards, fight consequences, dialogue effects) and owns save capture/restore.
    /// Engine-independent: the Unity layer drives it and renders it.
    /// </summary>
    public sealed class GameSession : IRewardSink, IDialogueContext
    {
        public const int SaveSchemaVersion = 1;
        public const string GameVersion = "0.1.0-foundation";
        public const string PlayerId = "player";
        public const long StartingCashCents = 4000; // $40.00

        public EventBus Bus { get; } = new EventBus();
        public GameClock Clock { get; }
        public DeterministicRandom Random { get; }
        public FinessBank Bank { get; private set; }
        public ReputationManager Reputation { get; }
        public SocialManager Social { get; }
        public StoryManager Story { get; }
        public MissionManager Missions { get; }
        public GameContent Content { get; }
        public PlayerIdentity Player { get; private set; } = new PlayerIdentity();
        public WorldPosition Position { get; private set; } = new WorldPosition();
        private int _fightCounter;

        public GameSession(GameContent content, ulong seed, ValidationReport report)
        {
            Content = content ?? new GameContent();
            Clock = new GameClock(Bus);
            Random = new DeterministicRandom(seed);
            Bank = FinessBank.CreateStarter(Bus, () => Clock.TotalMinutes, StartingCashCents);
            Reputation = new ReputationManager(Bus);
            Social = new SocialManager(Bus);
            Story = new StoryManager(Bus);
            Missions = new MissionManager(Bus, Story, this);
            Missions.Load(Content.Missions, report ?? new ValidationReport());
            Bus.Subscribe<StoryFlagChanged>(_ => Missions.RefreshAvailability());
            PumpMissions();
        }

        /// <summary>
        /// Starts newly available main/side missions and retries failed ones that allow recovery.
        /// Called after anything that can change mission state; never from inside a mission event handler.
        /// </summary>
        public void PumpMissions()
        {
            for (int guard = 0; guard < 8; guard++)
            {
                bool changed = false;
                foreach (var d in Missions.ByStatus(MissionStatus.Available))
                    if (d.Category == MissionCategory.Main || d.Category == MissionCategory.Side)
                        changed |= Missions.Start(d.Id) == MissionActionResult.Ok;
                foreach (var d in Missions.ByStatus(MissionStatus.Failed))
                    if (d.Recovery != FailureRecovery.FailPermanently)
                        changed |= Missions.Start(d.Id) == MissionActionResult.Ok;
                if (!changed) break;
            }
            if (Missions.TrackedMission == null)
                foreach (var d in Missions.ByStatus(MissionStatus.Active))
                    if (d.Category == MissionCategory.Main) { Missions.TrackedMission = d.Id; break; }
        }

        // ---------------------------------------------------------------- triggers & fights

        public int ReportTrigger(string trigger)
        {
            int n = Missions.ReportTrigger(trigger);
            PumpMissions();
            return n;
        }
        void IDialogueContext.ReportTrigger(string trigger) => ReportTrigger(trigger);

        /// <summary>Applies everything a finished fight changes: reputation, opponent memory, witnesses, missions.</summary>
        public void OnFightCompleted(FightResult result, string opponentId, VenueType venue, string locationId, IEnumerable<string> witnesses)
        {
            // A draw counts as "not won" for consequences.
            bool won = result.WinnerId != null && result.WinnerId != opponentId;
            string fightKey = result.FightId ?? ("fight." + (++_fightCounter));
            Reputation.Apply(FightConsequences.ReputationFor(venue, won, result.Method), fightKey);

            string npc = NpcIdForFighter(opponentId);
            Social.Modify(npc, PlayerId, FightConsequences.OpponentDelta(won, result.Method), fightKey);

            var evt = new WorldEvent
            {
                EventId = "event." + fightKey, EventType = won ? "player.won_fight" : "player.lost_fight",
                ActorId = won ? PlayerId : npc, TargetId = won ? npc : PlayerId, LocationId = locationId, GameMinute = Clock.TotalMinutes
            };
            evt.Witnesses.Add(npc);
            if (witnesses != null) evt.Witnesses.AddRange(witnesses);
            Social.Record(evt);

            Bus.Publish(new FightCompleted(fightKey, won ? PlayerId : opponentId, won ? opponentId : PlayerId, result.Method.ToString(), locationId));
            ReportTrigger((won ? "fight.won." : "fight.lost.") + opponentId);
        }

        /// <summary>Fighter definitions are "fighter.x"; the matching social identity is "npc.x".</summary>
        public static string NpcIdForFighter(string fighterId) => fighterId.StartsWith("fighter.") ? "npc." + fighterId.Substring(8) : fighterId;

        public string NextFightId() => "fight." + (++_fightCounter);

        // ---------------------------------------------------------------- rewards

        public void Grant(string key, RewardDefinition r)
        {
            switch (r.Type)
            {
                case RewardType.Money:
                    if (r.AmountCents > 0)
                        Bank.Post(new TransactionRequest { IdempotencyKey = key, Kind = TransactionKind.Reward, To = FinessBank.Cash, AmountCents = r.AmountCents, Memo = r.Memo });
                    else
                        Bank.Pay(key, -r.AmountCents, TransactionKind.Bill, r.Memo);
                    break;
                case RewardType.Reputation:
                    var change = new ReputationChange();
                    if (r.FaceCard != null) foreach (var kv in r.FaceCard) change.Face(kv.Key, kv.Value);
                    if (r.PublicImage != null) foreach (var kv in r.PublicImage) change.Image(kv.Key, kv.Value);
                    Reputation.Apply(change, key);
                    break;
                case RewardType.Relationship:
                    Social.Modify(r.Target, PlayerId, r.Relationship, key);
                    break;
                case RewardType.Flag:
                    Story.TrySet(r.Target, r.Value);
                    break;
                case RewardType.Contact:
                    if (!Player.Contacts.Contains(r.Target)) Player.Contacts.Add(r.Target);
                    break;
                case RewardType.Item:
                    Player.Inventory.TryGetValue(r.Target, out var n);
                    Player.Inventory[r.Target] = n + r.Value;
                    break;
            }
        }

        // ---------------------------------------------------------------- dialogue context

        private readonly HashSet<string> _dialogueEffectKeys = new HashSet<string>();

        public DialogueRunner StartDialogue(string dialogueId)
            => Content.Dialogues.TryGetValue(dialogueId, out var d) ? new DialogueRunner(d, this) : null;

        public bool HasFlag(string flag) => Story.Has(flag);
        public RelationshipValues Relationship(string npcId) => Social.Get(npcId, PlayerId);
        public Attitude AttitudeOf(string npcId) => Social.EvaluateAttitude(npcId, PlayerId);
        public void ApplyEffect(string key, RewardDefinition effect)
        {
            // Dialogue effect keys share the mission reward ledger so they persist across saves.
            if (Missions.RewardGranted(key) || !_dialogueEffectKeys.Add(key)) return;
            Grant(key, effect);
        }
        public bool ChooseMissionBranch(string missionId, string branchId)
        {
            bool ok = Missions.ChooseBranch(missionId, branchId) == MissionActionResult.Ok;
            PumpMissions();
            return ok;
        }

        // ---------------------------------------------------------------- persistence

        public GameSaveData Capture(string label = null)
        {
            var m = Missions.Capture();
            foreach (var k in _dialogueEffectKeys) if (!m.GrantedRewardKeys.Contains(k)) m.GrantedRewardKeys.Add(k);
            return new GameSaveData
            {
                GameVersion = GameVersion, SaveLabel = label ?? Clock.ToString(),
                Player = Player, Position = Position, ClockMinutes = Clock.TotalMinutes, RngState = Random.State,
                Bank = Bank.Capture(), Reputation = Reputation.Capture(), Social = Social.Capture(), Story = Story.Capture(),
                Missions = m, FightCounter = _fightCounter
            };
        }

        public void Restore(GameSaveData s)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            Player = s.Player ?? new PlayerIdentity();
            Position = s.Position ?? new WorldPosition();
            Clock.Restore(s.ClockMinutes);
            Random.Restore(s.RngState);
            Bank.Restore(s.Bank);
            Reputation.Restore(s.Reputation);
            Social.Restore(s.Social);
            Story.Restore(s.Story);   // story before missions: availability depends on chapter/flags
            Missions.Restore(s.Missions);
            PumpMissions();
            _dialogueEffectKeys.Clear();
            foreach (var k in s.Missions.GrantedRewardKeys) if (k.StartsWith("dialogue.")) _dialogueEffectKeys.Add(k);
            _fightCounter = s.FightCounter;
        }

        public static SaveSlotManager<GameSaveData> CreateSaveManager(ISaveStorage storage)
            => new SaveSlotManager<GameSaveData>(storage, new SaveEnvelopeCodec(SaveSchemaVersion));
    }
}
