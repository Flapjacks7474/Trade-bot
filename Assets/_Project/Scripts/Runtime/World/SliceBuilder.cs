using System.Collections.Generic;
using FFTS.Session;
using FFTS.World;
using UnityEngine;

namespace FFTS.Runtime
{
    /// <summary>
    /// Builds the Southside vertical slice at runtime from southside_layout.json: ground, roads, graybox
    /// buildings with labels, location volumes, interaction markers, NPCs, the player and the camera.
    /// Building from data keeps the layout testable and lets designers iterate without scene merges.
    /// </summary>
    public sealed class SliceBuilder : MonoBehaviour
    {
        private readonly List<NpcPresence> _npcs = new List<NpcPresence>();

        private void Start()
        {
            var gm = CoreGameManager.Instance;
            if (gm == null) { Debug.LogError("[FFTS] SliceBuilder needs a CoreGameManager in the scene."); return; }
            var layout = gm.Layout;
            var root = new GameObject("Southside (generated)").transform;

            EnsureLight();
            var ground = Graybox.Box("Ground", root, new Vector3(0, -0.5f, 0), new Vector3(layout.SizeX, 1, layout.SizeZ), "#3d4a3a");
            ground.isStatic = true;
            foreach (var r in layout.Roads) BuildRoad(root, r);
            foreach (var l in layout.Locations) BuildLocation(root, l);
            var lw = gm.Session.LivingWorld;
            foreach (var n in layout.Npcs)
                if (lw == null || lw.Profile(n.NpcId) == null) BuildNpc(root, layout, n); // static fallback for unscheduled NPCs
            if (lw != null) foreach (var p in lw.Profiles) NpcAgent.Spawn(p, root);
            BuildGaragesAndRaces(root, gm, layout);

            var player = BuildPlayer(new Vector3(gm.Session.Position.X, 0.1f, gm.Session.Position.Z));
            gm.Player = player.transform;
            var tracker = player.AddComponent<LocationTracker>();
            tracker.Locations = layout.Locations;

            gm.Reloaded += RefreshNpcs;
            gm.Session.Bus.Subscribe<Core.ObjectiveCompleted>(_ => RefreshNpcs());
            gm.Session.Bus.Subscribe<Core.MissionStateChanged>(_ => RefreshNpcs());
            RefreshNpcs();
            gameObject.AddComponent<GameHud>();
        }

        private void RefreshNpcs()
        {
            var s = CoreGameManager.Instance.Session;
            foreach (var n in _npcs) if (n != null) n.gameObject.SetActive(NpcConversationRules.IsVisible(s, n.Placement));
        }

        private static void EnsureLight()
        {
            if (Object.FindFirstObjectByType<Light>() != null) return;
            var go = new GameObject("Sun");
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.color = new Color(1f, 0.95f, 0.85f);
            go.transform.rotation = Quaternion.Euler(50, -30, 0);
            light.shadows = LightShadows.Soft;
        }

        private static void BuildRoad(Transform root, RoadDefinition r)
        {
            var a = new Vector3(r.X1, 0, r.Z1); var b = new Vector3(r.X2, 0, r.Z2);
            var go = Graybox.Box(r.Id, root, (a + b) / 2f + Vector3.up * 0.01f, new Vector3(r.Width, 0.02f, Vector3.Distance(a, b)), "#2b2b2b", collider: false);
            go.transform.rotation = Quaternion.LookRotation(b - a);
            go.isStatic = true;
        }

        private void BuildLocation(Transform root, LocationDefinition l)
        {
            var group = new GameObject(l.Id).transform;
            group.SetParent(root, false);
            var center = new Vector3(l.X, 0, l.Z);
            var b = Graybox.Box("Building", group, center + Vector3.up * (l.Height / 2f), new Vector3(l.Width, l.Height, l.Depth), l.Color);
            b.isStatic = true;
            Graybox.Label(group, l.DisplayName, center + Vector3.up * (l.Height + 1.5f), 0.3f);

            foreach (var ia in l.Interactions)
            {
                var pos = center + new Vector3(ia.OffsetX, 0, ia.OffsetZ);
                var marker = Graybox.Box(ia.Id, group, pos + Vector3.up * 0.5f, new Vector3(0.5f, 1f, 0.5f), "#f2c14e", collider: false);
                var w = marker.AddComponent<WorldInteractable>();
                w.Id = ia.Id; w.Prompt = ia.Prompt; w.Verb = ia.Verb; w.Priority = ia.Priority; w.Radius = 2.5f;
                string trigger = ia.Trigger;
                string verb = ia.Verb;
                w.IsAvailable = () => verb == "look" || verb == "rest" || CoreGameManager.Instance.Session.Missions.WantsTrigger(trigger);
                w.OnInteract = () => UseInteraction(verb, trigger, ia.Prompt);
                marker.AddComponent<MarkerVisibility>().Interactable = w;
            }
        }

        private static void BuildGaragesAndRaces(Transform root, CoreGameManager gm, DistrictLayout layout)
        {
            foreach (var g in gm.Session.Garage.Database.Garages)
            {
                var l = layout.Find(g.LocationId);
                if (l == null) continue;
                var pos = new Vector3(l.X + l.Width / 2f - 1.5f, 0, l.Z - l.Depth / 2f - 2.5f);
                var marker = Graybox.Box("garage." + g.Id, root, pos + Vector3.up * 0.6f, new Vector3(0.8f, 1.2f, 0.8f), "#4cc9f0", collider: false);
                Graybox.Label(marker.transform, g.DisplayName, Vector3.up * 1.4f, 0.08f, new Color(0.6f, 0.9f, 1f));
                var w = marker.AddComponent<WorldInteractable>();
                w.Id = "garage." + g.Id; w.Verb = "garage"; w.Prompt = "Open " + g.DisplayName; w.Radius = 3f; w.Priority = 1;
                string id = g.Id;
                w.OnInteract = () => CoreGameManager.Instance.Garages.Open(id);
                // Parking spot outline where retrieved cars appear.
                Graybox.Box("parking." + g.Id, root, new Vector3(g.SpawnX, 0.03f, g.SpawnZ), new Vector3(3f, 0.02f, 5.5f), "#4cc9f0", collider: false)
                    .transform.rotation = Quaternion.Euler(0, g.SpawnHeadingDeg, 0);
            }
            foreach (var r in gm.Session.Racing.Races.Races)
            {
                var l = layout.Find(r.LocationId);
                if (l == null) continue;
                var flag = Graybox.Box("race." + r.Id, root, new Vector3(l.X, 3f, l.Z + l.Depth / 2f + 1f), new Vector3(0.4f, 6f, 0.4f), "#ef476f", collider: false);
                Graybox.Label(flag.transform, "RACE: " + r.DisplayName + "\n(drive here and press E)", Vector3.up * 3.6f, 0.08f, new Color(1f, 0.7f, 0.75f));
            }
        }

        private static void UseInteraction(string verb, string trigger, string prompt)
        {
            var gm = CoreGameManager.Instance;
            if (verb == "rest")
            {
                var clock = gm.Session.Clock;
                int minutesToMorning = (24 * 60 - clock.MinuteOfDay + 8 * 60) % (24 * 60);
                if (minutesToMorning == 0) minutesToMorning = 24 * 60;
                clock.Advance(minutesToMorning);
                gm.Notify("You slept until morning. " + clock);
            }
            else if (verb == "look") gm.Notify(prompt.Replace("Look at", "You look at"));
            gm.ReportTrigger(trigger);
        }

        private void BuildNpc(Transform root, DistrictLayout layout, NpcPlacement n)
        {
            var l = layout.Find(n.LocationId);
            var pos = new Vector3(l.X + n.OffsetX, 0, l.Z + n.OffsetZ);
            var go = Graybox.Capsule(n.PlacementId, root, pos, n.Color);
            Graybox.Label(go.transform, n.DisplayName, Vector3.up * 1.4f, 0.12f, new Color(1f, 0.9f, 0.6f));
            var presence = go.AddComponent<NpcPresence>();
            presence.Placement = n;
            var w = go.AddComponent<WorldInteractable>();
            w.Id = n.PlacementId; w.Verb = "talk"; w.Prompt = "Talk to " + n.DisplayName; w.Radius = 2.5f; w.Priority = 2;
            string npcId = n.NpcId, name = n.DisplayName;
            w.OnInteract = () => CoreGameManager.Instance.TalkTo(npcId, name);
            _npcs.Add(presence);
        }

        private static GameObject BuildPlayer(Vector3 spawn)
        {
            var player = Graybox.Capsule("Player", null, spawn, "#e0e0e0");
            Object.Destroy(player.GetComponent<CapsuleCollider>());
            var cc = player.AddComponent<CharacterController>();
            cc.height = 2f; cc.radius = 0.4f; cc.center = Vector3.zero;
            var nose = Graybox.Box("Facing", player.transform, player.transform.position + new Vector3(0, 0.5f, 0.45f), new Vector3(0.2f, 0.2f, 0.3f), "#d62828", collider: false);
            nose.transform.SetParent(player.transform, true);
            var motor = player.AddComponent<PlayerMotor>();
            player.AddComponent<InteractionManager>();

            var cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = camGo.AddComponent<Camera>();
                camGo.AddComponent<AudioListener>();
            }
            var rig = cam.GetComponent<CameraRig>();
            if (rig == null) rig = cam.gameObject.AddComponent<CameraRig>();
            rig.Target = player.transform;
            motor.CameraTransform = cam.transform;
            return player;
        }
    }

    public sealed class NpcPresence : MonoBehaviour
    {
        public NpcPlacement Placement;
    }

    /// <summary>Hides interaction markers that currently do nothing.</summary>
    public sealed class MarkerVisibility : MonoBehaviour
    {
        public WorldInteractable Interactable;
        private Renderer _r;
        private void Awake() { _r = GetComponent<Renderer>(); }
        private void Update()
        {
            if (_r == null || Interactable == null) return;
            bool on = Interactable.IsAvailable == null || Interactable.IsAvailable();
            _r.enabled = on;
            transform.Rotate(0, 90 * Time.deltaTime, 0);
        }
    }

    /// <summary>Reports location enter triggers using simple bounds checks (no physics dependency).</summary>
    public sealed class LocationTracker : MonoBehaviour
    {
        public List<LocationDefinition> Locations;
        public const float Margin = 4f;
        private readonly HashSet<string> _inside = new HashSet<string>();

        private void Update()
        {
            var gm = CoreGameManager.Instance;
            if (gm == null || Locations == null) return;
            var p = transform.position;
            foreach (var l in Locations)
            {
                bool inside = Mathf.Abs(p.x - l.X) <= l.Width / 2f + Margin && Mathf.Abs(p.z - l.Z) <= l.Depth / 2f + Margin;
                if (inside && _inside.Add(l.Id))
                {
                    gm.Notify(l.DisplayName);
                    gm.ReportTrigger(l.EnterTrigger);
                }
                else if (!inside) _inside.Remove(l.Id);
            }
        }
    }
}
