using System;
using System.Collections.Generic;
using FFTS.Core;

namespace FFTS.Relationships
{
    /// <summary>Personality traits, 0-100. Authored per important NPC; background NPCs use archetype presets.</summary>
    public sealed class Personality
    {
        public int Confidence = 50, Temper = 50, Loyalty = 50, Ambition = 50, Trust = 50;
        public int Patience = 50, Intelligence = 50, Sociability = 50, Courage = 50, Composure = 50;
    }

    /// <summary>How one character feels about another. Directional: Dre→Player differs from Rell→Player.</summary>
    public sealed class RelationshipValues
    {
        public int Trust, Respect, Loyalty, Affection, Fear, Resentment, Rivalry, Friendship;

        public RelationshipValues Clone() => (RelationshipValues)MemberwiseClone();

        internal void Add(RelationshipDelta d)
        {
            Trust = Clamp(Trust + d.Trust); Respect = Clamp(Respect + d.Respect); Loyalty = Clamp(Loyalty + d.Loyalty);
            Affection = Clamp(Affection + d.Affection); Fear = Clamp(Fear + d.Fear); Resentment = Clamp(Resentment + d.Resentment);
            Rivalry = Clamp(Rivalry + d.Rivalry); Friendship = Clamp(Friendship + d.Friendship);
        }
        private static int Clamp(int v) => MathUtil.Clamp(v, -100, 100);
    }

    public sealed class RelationshipDelta
    {
        public int Trust, Respect, Loyalty, Affection, Fear, Resentment, Rivalry, Friendship;
    }

    public enum Attitude { Hostile, Intimidated, Wary, Neutral, Warm, Friendly }

    public enum KnowledgeSource { Witnessed, HeardRumor, ToldDirectly }

    /// <summary>A world occurrence that characters may or may not know about.</summary>
    public sealed class WorldEvent
    {
        public string EventId;
        public string EventType;   // e.g. "player.humiliated", "player.helped", "player.won_fight"
        public string ActorId;
        public string TargetId;
        public string LocationId;
        public long GameMinute;
        public List<string> Witnesses = new List<string>();
    }

    public sealed class MemoryRecord
    {
        public string EventId;
        public string EventType;
        public string ActorId;
        public string TargetId;
        public string LocationId;
        public long GameMinute;
        public KnowledgeSource Source;
        public string HeardFrom;
        public float Reliability;  // 1 = saw it; rumours degrade per retelling
        public bool Resolved;
    }

    public sealed class RelationshipEntry { public string NpcId; public string TargetId; public RelationshipValues Values; }

    public sealed class SocialState
    {
        public List<RelationshipEntry> Relationships = new List<RelationshipEntry>();
        public Dictionary<string, List<MemoryRecord>> Memories = new Dictionary<string, List<MemoryRecord>>();
        public List<WorldEvent> Events = new List<WorldEvent>();
    }

    /// <summary>
    /// Owns relationship values, NPC memories and the world-event log. Knowledge spreads only through
    /// witnessing or a rumour from someone who actually knows — NPCs are never omniscient.
    /// </summary>
    public sealed class SocialManager
    {
        public const float RumorDecay = 0.7f;
        public const float MinRumorReliability = 0.15f;

        private readonly EventBus _bus;
        private readonly Dictionary<string, Personality> _personalities = new Dictionary<string, Personality>();
        private readonly Dictionary<string, RelationshipValues> _rel = new Dictionary<string, RelationshipValues>();
        private readonly Dictionary<string, List<MemoryRecord>> _memories = new Dictionary<string, List<MemoryRecord>>();
        private readonly Dictionary<string, WorldEvent> _events = new Dictionary<string, WorldEvent>();

        /// <summary>Valence of event types from the subject's point of view (positive = good for the target).</summary>
        public readonly Dictionary<string, int> EventValence = new Dictionary<string, int>
        {
            ["player.helped"] = 20, ["player.protected"] = 30, ["player.gift"] = 10, ["player.kept_promise"] = 15,
            ["player.won_fight"] = 0, ["player.humiliated"] = -30, ["player.attacked"] = -40, ["player.betrayed"] = -60,
            ["player.lied"] = -20, ["player.broke_promise"] = -25, ["player.backed_down"] = -5, ["player.stood_ground"] = 5
        };

        public SocialManager(EventBus bus) { _bus = bus; }

        private static string Key(string npc, string target) => npc + "→" + target;

        public void SetPersonality(string npcId, Personality p) => _personalities[npcId] = p;
        public Personality GetPersonality(string npcId) => _personalities.TryGetValue(npcId, out var p) ? p : new Personality();

        public RelationshipValues Get(string npcId, string targetId)
            => _rel.TryGetValue(Key(npcId, targetId), out var v) ? v.Clone() : new RelationshipValues();

        /// <summary>Applies a change, scaled by the NPC's personality (temper amplifies resentment, patience dampens it).</summary>
        public void Modify(string npcId, string targetId, RelationshipDelta d, string reason)
        {
            var p = GetPersonality(npcId);
            var scaled = new RelationshipDelta
            {
                Trust = d.Trust, Respect = d.Respect, Loyalty = d.Loyalty, Affection = d.Affection, Friendship = d.Friendship, Rivalry = d.Rivalry,
                Resentment = (int)Math.Round(d.Resentment * (0.6 + p.Temper / 100.0 - (p.Patience - 50) / 200.0)),
                Fear = (int)Math.Round(d.Fear * (1.4 - p.Courage / 100.0))
            };
            if (!_rel.TryGetValue(Key(npcId, targetId), out var v)) _rel[Key(npcId, targetId)] = v = new RelationshipValues();
            v.Add(scaled);
            _bus?.Publish(new RelationshipChanged(npcId, targetId, reason));
        }

        // ------------------------------------------------------------------ knowledge

        public void Record(WorldEvent e)
        {
            Ids.Require(e.EventId, "world event");
            if (_events.ContainsKey(e.EventId)) return; // idempotent
            _events[e.EventId] = e;
            foreach (var w in e.Witnesses)
                AddMemory(w, new MemoryRecord
                {
                    EventId = e.EventId, EventType = e.EventType, ActorId = e.ActorId, TargetId = e.TargetId, LocationId = e.LocationId,
                    GameMinute = e.GameMinute, Source = KnowledgeSource.Witnessed, Reliability = 1f
                });
            _bus?.Publish(new WorldEventRecorded(e.EventId, e.EventType, e.ActorId, e.TargetId, e.LocationId));
        }

        public bool Knows(string npcId, string eventId) => FindMemory(npcId, eventId) != null;

        public MemoryRecord FindMemory(string npcId, string eventId)
        {
            if (!_memories.TryGetValue(npcId, out var list)) return null;
            foreach (var m in list) if (m.EventId == eventId) return m;
            return null;
        }

        public IReadOnlyList<MemoryRecord> MemoriesOf(string npcId)
            => _memories.TryGetValue(npcId, out var l) ? (IReadOnlyList<MemoryRecord>)l : Array.Empty<MemoryRecord>();

        /// <summary>
        /// Passes on what the teller knows. Fails if the teller does not know the event, if the
        /// listener already knows it at least as reliably, or if the story has degraded past belief.
        /// </summary>
        public bool ShareRumor(string tellerId, string listenerId, string eventId)
        {
            var source = FindMemory(tellerId, eventId);
            if (source == null || tellerId == listenerId) return false;
            float reliability = source.Reliability * RumorDecay;
            if (reliability < MinRumorReliability) return false;
            var existing = FindMemory(listenerId, eventId);
            if (existing != null && existing.Reliability >= reliability) return false;
            if (existing != null) _memories[listenerId].Remove(existing);
            AddMemory(listenerId, new MemoryRecord
            {
                EventId = source.EventId, EventType = source.EventType, ActorId = source.ActorId, TargetId = source.TargetId,
                LocationId = source.LocationId, GameMinute = source.GameMinute,
                Source = KnowledgeSource.HeardRumor, HeardFrom = tellerId, Reliability = reliability
            });
            return true;
        }

        public bool Resolve(string npcId, string eventId)
        {
            var m = FindMemory(npcId, eventId);
            if (m == null || m.Resolved) return false;
            m.Resolved = true;
            return true;
        }

        private void AddMemory(string npcId, MemoryRecord m)
        {
            if (!_memories.TryGetValue(npcId, out var list)) _memories[npcId] = list = new List<MemoryRecord>();
            list.Add(m);
        }

        // ------------------------------------------------------------------ evaluation

        /// <summary>
        /// Combines relationship values, unresolved memories about the target and personality into an
        /// attitude. Two NPCs with identical values can still react differently: a brave NPC who fears
        /// you turns hostile, a timid one becomes intimidated.
        /// </summary>
        public Attitude EvaluateAttitude(string npcId, string targetId)
        {
            var v = Get(npcId, targetId);
            var p = GetPersonality(npcId);
            double score = v.Trust + v.Friendship + v.Respect * 0.5 + v.Affection * 0.5 + v.Loyalty * 0.3
                           - v.Resentment - v.Rivalry * 0.5;
            double temper = 0.5 + p.Temper / 100.0;
            foreach (var m in MemoriesOf(npcId))
            {
                if (m.Resolved || m.ActorId != targetId) continue;
                if (!EventValence.TryGetValue(m.EventType, out var valence)) continue;
                bool aboutMe = m.TargetId == npcId;
                double weight = (aboutMe ? 1.0 : 0.4) * m.Reliability;
                score += valence < 0 ? valence * weight * temper : valence * weight;
            }

            if (v.Fear >= 40)
                return p.Courage >= 60 ? Attitude.Hostile : Attitude.Intimidated;
            if (score <= -60) return Attitude.Hostile;
            if (score <= -15) return Attitude.Wary;
            if (score < 20) return Attitude.Neutral;
            if (score < 60) return Attitude.Warm;
            return Attitude.Friendly;
        }

        // ------------------------------------------------------------------ persistence

        public SocialState Capture()
        {
            var s = new SocialState();
            foreach (var kv in _rel)
            {
                var parts = kv.Key.Split('→');
                s.Relationships.Add(new RelationshipEntry { NpcId = parts[0], TargetId = parts[1], Values = kv.Value.Clone() });
            }
            foreach (var kv in _memories) s.Memories[kv.Key] = new List<MemoryRecord>(kv.Value);
            s.Events.AddRange(_events.Values);
            return s;
        }

        public void Restore(SocialState s)
        {
            _rel.Clear(); _memories.Clear(); _events.Clear();
            foreach (var r in s.Relationships) _rel[Key(r.NpcId, r.TargetId)] = r.Values.Clone();
            foreach (var kv in s.Memories) _memories[kv.Key] = new List<MemoryRecord>(kv.Value);
            foreach (var e in s.Events) _events[e.EventId] = e;
        }
    }
}
