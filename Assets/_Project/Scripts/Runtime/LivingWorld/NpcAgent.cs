using FFTS.LivingWorld;
using UnityEngine;

namespace FFTS.Runtime
{
    /// <summary>
    /// Presents one scheduled NPC. Position comes from <see cref="LivingWorldManager"/>; the view glides toward it so NPCs
    /// simulated at lower frequency still move smoothly. Shows a placeholder car while a driving NPC travels.
    /// </summary>
    public sealed class NpcAgent : MonoBehaviour
    {
        public string NpcId;
        public string DisplayName;
        private TextMesh _label;
        private GameObject _car;
        private Renderer _body;
        private string _lastLabel;

        public static NpcAgent Spawn(NpcProfile p, Transform parent)
        {
            var go = Graybox.Capsule(p.NpcId, parent, Vector3.zero, p.Color);
            var agent = go.AddComponent<NpcAgent>();
            agent.NpcId = p.NpcId; agent.DisplayName = p.DisplayName;
            agent._body = go.GetComponent<Renderer>();
            agent._label = Graybox.Label(go.transform, p.DisplayName, Vector3.up * 1.4f, 0.12f, new Color(1f, 0.9f, 0.6f));
            if (p.Travel == TravelMode.Drive)
            {
                agent._car = Graybox.Box("Car", go.transform, go.transform.position, new Vector3(1.9f, 1.1f, 4.2f), p.Color, collider: false);
                agent._car.transform.localPosition = new Vector3(0, -0.45f, 0);
                agent._car.SetActive(false);
            }
            var w = go.AddComponent<WorldInteractable>();
            w.Id = "npc." + p.NpcId; w.Verb = "talk"; w.Prompt = "Talk to " + p.DisplayName; w.Radius = 2.5f; w.Priority = 2;
            w.IsAvailable = () => agent.CanTalk;
            w.OnInteract = () => CoreGameManager.Instance.TalkTo(p.NpcId, p.DisplayName);
            go.AddComponent<NpcPresence>(); // so legacy witness/visibility code paths still see NPCs
            agent.Snap();
            return agent;
        }

        private NpcRuntime Sim => CoreGameManager.Instance.Session.LivingWorld?.Get(NpcId);

        /// <summary>Walkers can be stopped mid-route; drivers can only be spoken to once parked.</summary>
        public bool CanTalk
        {
            get
            {
                var n = Sim; var lw = CoreGameManager.Instance.Session.LivingWorld;
                return n != null && (n.Status != NpcStatus.Traveling || lw.Profile(NpcId).Travel == TravelMode.Walk);
            }
        }

        public void Snap()
        {
            var n = Sim;
            if (n != null) transform.position = new Vector3(n.X, 1f, n.Z);
        }

        private void Update()
        {
            var n = Sim;
            if (n == null) return;
            var lw = CoreGameManager.Instance.Session.LivingWorld;
            var target = new Vector3(n.X, 1f, n.Z);
            float speed = lw.SpeedOf(NpcId) * 1.5f + 0.5f;
            transform.position = Vector3.Distance(transform.position, target) > 30f ? target : Vector3.MoveTowards(transform.position, target, speed * Time.deltaTime);
            if (n.Status == NpcStatus.Traveling) transform.rotation = Quaternion.Euler(0, n.HeadingDeg, 0);

            bool driving = _car != null && n.Status == NpcStatus.Traveling;
            if (_car != null && _car.activeSelf != driving) _car.SetActive(driving);
            if (_body != null) _body.enabled = !driving;

            string text = DisplayName + (n.Status == NpcStatus.Traveling ? " · on the move" : " · " + n.Activity);
            if (text != _lastLabel) { _label.text = text; _lastLabel = text; }
        }
    }
}
