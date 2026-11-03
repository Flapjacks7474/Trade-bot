namespace FFTS.Core
{
    // Cross-system events. Payloads are immutable after construction.

    public sealed class TimeSegmentChanged : IGameEvent
    {
        public readonly TimeSegment Previous; public readonly TimeSegment Current; public readonly int Day;
        public TimeSegmentChanged(TimeSegment previous, TimeSegment current, int day) { Previous = previous; Current = current; Day = day; }
    }

    public sealed class DayChanged : IGameEvent
    {
        public readonly int Day;
        public DayChanged(int day) { Day = day; }
    }

    public sealed class TransactionPosted : IGameEvent
    {
        public readonly string TransactionId; public readonly string Kind; public readonly string FromAccount; public readonly string ToAccount; public readonly long AmountCents;
        public TransactionPosted(string transactionId, string kind, string from, string to, long amountCents)
        { TransactionId = transactionId; Kind = kind; FromAccount = from; ToAccount = to; AmountCents = amountCents; }
    }

    public sealed class ReputationChanged : IGameEvent
    {
        public readonly string Source; public readonly string Attribute; public readonly int OldValue; public readonly int NewValue;
        public ReputationChanged(string source, string attribute, int oldValue, int newValue) { Source = source; Attribute = attribute; OldValue = oldValue; NewValue = newValue; }
    }

    public sealed class RelationshipChanged : IGameEvent
    {
        public readonly string NpcId; public readonly string TargetId; public readonly string Reason;
        public RelationshipChanged(string npcId, string targetId, string reason) { NpcId = npcId; TargetId = targetId; Reason = reason; }
    }

    public sealed class StoryFlagChanged : IGameEvent
    {
        public readonly string Flag; public readonly int OldValue; public readonly int NewValue;
        public StoryFlagChanged(string flag, int oldValue, int newValue) { Flag = flag; OldValue = oldValue; NewValue = newValue; }
    }

    public sealed class MissionStateChanged : IGameEvent
    {
        public readonly string MissionId; public readonly string OldState; public readonly string NewState;
        public MissionStateChanged(string missionId, string oldState, string newState) { MissionId = missionId; OldState = oldState; NewState = newState; }
    }

    public sealed class MissionCompleted : IGameEvent
    {
        public readonly string MissionId; public readonly string BranchId; public readonly bool WasReplay;
        public MissionCompleted(string missionId, string branchId, bool wasReplay) { MissionId = missionId; BranchId = branchId; WasReplay = wasReplay; }
    }

    public sealed class ObjectiveCompleted : IGameEvent
    {
        public readonly string MissionId; public readonly string ObjectiveId;
        public ObjectiveCompleted(string missionId, string objectiveId) { MissionId = missionId; ObjectiveId = objectiveId; }
    }

    public sealed class WorldEventRecorded : IGameEvent
    {
        public readonly string EventId; public readonly string EventType; public readonly string ActorId; public readonly string TargetId; public readonly string LocationId;
        public WorldEventRecorded(string eventId, string eventType, string actorId, string targetId, string locationId)
        { EventId = eventId; EventType = eventType; ActorId = actorId; TargetId = targetId; LocationId = locationId; }
    }

    public sealed class FightCompleted : IGameEvent
    {
        public readonly string FightId; public readonly string WinnerId; public readonly string LoserId; public readonly string Method; public readonly string VenueId;
        public FightCompleted(string fightId, string winnerId, string loserId, string method, string venueId)
        { FightId = fightId; WinnerId = winnerId; LoserId = loserId; Method = method; VenueId = venueId; }
    }

    public sealed class GameSaved : IGameEvent
    {
        public readonly string Slot;
        public GameSaved(string slot) { Slot = slot; }
    }

    public sealed class GameLoaded : IGameEvent
    {
        public readonly string Slot; public readonly bool RecoveredFromBackup;
        public GameLoaded(string slot, bool recovered) { Slot = slot; RecoveredFromBackup = recovered; }
    }
}
