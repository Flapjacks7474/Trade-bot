using System.Collections.Generic;
using FFTS.Relationships;
using FFTS.Reputation;

namespace FFTS.Missions
{
    public enum MissionCategory { Main, Side, Career, Dynamic, Hidden }
    public enum MissionStatus { Locked, Available, Active, Completed, Failed }
    public enum FailureRecovery { RestartFromCheckpoint, RestartMission, FailPermanently }
    public enum RewardType { Money, Reputation, Relationship, Flag, Contact, Item }

    /// <summary>One reward or consequence. Rewards can be negative — choices have costs.</summary>
    public sealed class RewardDefinition
    {
        public RewardType Type;
        public long AmountCents;           // Money
        public string Target;              // npc id (Relationship), flag (Flag), contact id, item id
        public int Value = 1;              // Flag value / item count
        public RelationshipDelta Relationship;
        public Dictionary<FaceCardAttribute, int> FaceCard;
        public Dictionary<PublicImageAttribute, int> PublicImage;
        public string Memo;
    }

    public sealed class ObjectiveDefinition
    {
        public string Id;
        public string Text;
        public bool Optional;
        public string Trigger;     // world trigger that completes it, e.g. "location.southside.grandma_house"
        public bool Checkpoint;    // kept after a failure when recovery is RestartFromCheckpoint
        public string Branch;      // only required/allowed when this branch is chosen
    }

    public sealed class BranchDefinition
    {
        public string Id;
        public string Text;
        public string Approach;    // Fight, Talk, Negotiate, StandGround, Pay, WalkAway, Investigate...
        public List<RewardDefinition> Effects = new List<RewardDefinition>();
        public List<string> Unlocks = new List<string>();
    }

    public sealed class PrerequisiteDefinition
    {
        public List<string> MissionsCompleted = new List<string>();
        public Dictionary<string, int> FlagsAtLeast = new Dictionary<string, int>();
        public List<string> FlagsAbsent = new List<string>();
        public int MinChapter = 1;
    }

    public sealed class MissionDefinition
    {
        public string Id;
        public string Title;
        public int Chapter;
        public int Order;
        public MissionCategory Category;
        public string Cause;
        public string Purpose;
        public string Summary;
        public string GiverNpc;
        public string LocationId;
        public PrerequisiteDefinition Prerequisites = new PrerequisiteDefinition();
        public List<ObjectiveDefinition> Objectives = new List<ObjectiveDefinition>();
        public List<BranchDefinition> Branches = new List<BranchDefinition>();
        public bool BranchRequired;
        public List<RewardDefinition> Rewards = new List<RewardDefinition>();
        public List<string> FailureConditions = new List<string>(); // triggers that fail the mission, e.g. "fight.lost.fighter.dre"
        public FailureRecovery Recovery = FailureRecovery.RestartFromCheckpoint;
        public List<string> Unlocks = new List<string>();
        public int AdvanceToChapter;   // 0 = no change
        public bool Replayable = true;
    }

    public sealed class MissionFile
    {
        public List<MissionDefinition> Missions = new List<MissionDefinition>();
    }

    public sealed class MissionProgress
    {
        public string MissionId;
        public MissionStatus Status;
        public List<string> CompletedObjectives = new List<string>();
        public string ChosenBranch;
        public int Attempts;
        public bool IsReplay;
        public List<string> ReplayObjectives = new List<string>();
        public string ReplayBranch;
        public string LastFailure;
    }

    public sealed class MissionsState
    {
        public List<MissionProgress> Progress = new List<MissionProgress>();
        public List<string> GrantedRewardKeys = new List<string>();
        public string TrackedMission;
    }
}
