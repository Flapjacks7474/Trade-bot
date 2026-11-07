using System.Collections.Generic;
using System.Text;
using FFTS.Combat;
using FFTS.Economy;
using FFTS.Input;
using FFTS.Missions;
using FFTS.Reputation;
using UnityEngine;

namespace FFTS.Runtime
{
    /// <summary>
    /// Placeholder HUD (IMGUI): clock, money, reputation, tracked mission, interaction prompt, toasts,
    /// fight bars and a controls overlay (F1). Final UI will be built in uGUI/UI Toolkit with FINESS OS.
    /// </summary>
    public sealed class GameHud : MonoBehaviour
    {
        private readonly List<(string text, float until)> _toasts = new List<(string, float)>();
        private bool _help = true;
        private float _helpAutoHide;
        private InteractionManager _interaction;

        private void Start()
        {
            var gm = CoreGameManager.Instance;
            gm.Toast += t => { _toasts.Add((t, Time.time + 3.5f)); if (_toasts.Count > 5) _toasts.RemoveAt(0); };
            _helpAutoHide = Time.time + 20f;
            Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
        }

        private void Update()
        {
            var gm = CoreGameManager.Instance;
            if (gm.Frame.WasPressed(ButtonMask.Help)) { _help = !_help; _helpAutoHide = float.MaxValue; }
            if (Time.time > _helpAutoHide) { _help = false; _helpAutoHide = float.MaxValue; }
            if (_interaction == null && gm.Player != null) _interaction = gm.Player.GetComponent<InteractionManager>();
            _toasts.RemoveAll(t => t.until < Time.time);
        }

        private void OnGUI()
        {
            var gm = CoreGameManager.Instance;
            if (gm == null) return;
            var s = gm.Session;
            var label = DialoguePanel.RichLabel();

            GUI.Box(new Rect(10, 10, 380, 86), GUIContent.none);
            GUI.Label(new Rect(20, 14, 360, 22), "<b>FIGHT FOR THE SOUTH</b>  ·  " + s.Clock, label);
            GUI.Label(new Rect(20, 36, 360, 22), "Cash " + FinessBank.Format(s.Bank.Balance(FinessBank.Cash)) + "   Bank " + FinessBank.Format(s.Bank.Balance(FinessBank.Checking)), label);
            GUI.Label(new Rect(20, 58, 360, 22), $"Respect {s.Reputation.Get(FaceCardAttribute.Respect)}  Fear {s.Reputation.Get(FaceCardAttribute.Fear)}  Popularity {s.Reputation.Get(PublicImageAttribute.Popularity)}  Ch.{s.Story.Chapter}", label);

            DrawMission(gm, label);

            for (int i = 0; i < _toasts.Count; i++)
                GUI.Label(new Rect(Screen.width / 2f - 300, 14 + i * 24, 600, 24), "<size=15><color=#ffd166>" + _toasts[i].text + "</color></size>", Centered(label));

            if (_interaction != null && _interaction.Current != null && !gm.ControlsLocked)
                GUI.Label(new Rect(Screen.width / 2f - 250, Screen.height - 120, 500, 30), "<size=18>[E] " + _interaction.Current.Prompt + "</size>", Centered(label));

            if (gm.Driving.IsDriving) DrawDriving(gm, label);

            if (gm.Fights.InFight) DrawFight(gm, label);
            if (_help) DrawHelp(label);

            if (!gm.ContentReport.IsValid)
                GUI.Label(new Rect(10, Screen.height - 60, Screen.width - 20, 50), "<color=#ff6b6b>Content errors: " + gm.ContentReport.Errors.Count + " (see Console)</color>", label);
        }

        private static void DrawMission(CoreGameManager gm, GUIStyle label)
        {
            var id = gm.Session.Missions.TrackedMission;
            if (id == null) return;
            var def = gm.Session.Missions.Definition(id); var p = gm.Session.Missions.Progress(id);
            var sb = new StringBuilder("<b>" + def.Title + "</b>\n");
            foreach (var o in def.Objectives)
            {
                if (o.Branch != null && o.Branch != p.ChosenBranch) continue;
                bool done = p.CompletedObjectives.Contains(o.Id);
                sb.Append(done ? "<color=#7bd389>✔ " : "• ").Append(o.Text).Append(o.Optional ? " (optional)" : "").Append(done ? "</color>" : "").Append('\n');
            }
            float h = 30 + def.Objectives.Count * 20;
            GUI.Box(new Rect(Screen.width - 410, 10, 400, h), GUIContent.none);
            GUI.Label(new Rect(Screen.width - 400, 14, 380, h), sb.ToString(), label);
        }

        private static void DrawDriving(CoreGameManager gm, GUIStyle label)
        {
            var car = gm.Driving.Current; var d = car.Dyn;
            GUI.Box(new Rect(Screen.width - 260, Screen.height - 130, 250, 110), GUIContent.none);
            string nos = d.Stats.NitrousCapacitySeconds > 0 ? $"  NOS {d.NitrousRemaining / d.Stats.NitrousCapacitySeconds:P0}" : "";
            GUI.Label(new Rect(Screen.width - 250, Screen.height - 126, 230, 104),
                $"<size=26><b>{Mathf.Abs(d.SpeedKph):0}</b></size> km/h\n{car.DisplayName}\nDamage {d.Damage:0}%{nos}\nView: {(gm.Driving.CameraView == 0 ? "Chase" : gm.Driving.CameraView == 1 ? "Hood" : "Cockpit")} (C)", label);
            if (gm.Driving.Prompt != null)
                GUI.Label(new Rect(Screen.width / 2f - 300, Screen.height - 120, 600, 30), "<size=18>" + gm.Driving.Prompt + "</size>", Centered(label));

            var race = gm.Races;
            if (!race.Active) return;
            var s = race.Session;
            GUI.Box(new Rect(Screen.width / 2f - 170, 110, 340, 64), GUIContent.none);
            GUI.Label(new Rect(Screen.width / 2f - 160, 114, 320, 60),
                $"<b>{s.Definition.DisplayName}</b>\nPosition {s.PositionOf(s.Player)}/{s.Racers.Count} · Checkpoint {System.Math.Min(s.Player.NextCheckpoint + 1, s.CheckpointCount)}/{s.CheckpointCount} · {s.Elapsed:0.0}s", Centered(label));
            if (race.Banner != null)
                GUI.Label(new Rect(Screen.width / 2f - 300, Screen.height / 2f - 60, 600, 80), "<size=48><b>" + race.Banner + "</b></size>", Centered(label));
        }

        private static void DrawFight(CoreGameManager gm, GUIStyle label)
        {
            var sim = gm.Fights.Sim;
            Bars(new Rect(20, 110, 360, 60), "You", sim.A, label);
            Bars(new Rect(Screen.width - 380, 110 + 140, 360, 60), gm.Fights.OpponentName, sim.B, label);
            string fin = gm.Fights.FinisherStatus == FinisherDenial.None ? "<color=#ffd166><b>FINISHER READY — press U</b></color>" : "";
            GUI.Label(new Rect(Screen.width / 2f - 300, 120, 600, 26), "<size=16>" + fin + "</size>", Centered(label));
            GUI.Label(new Rect(Screen.width / 2f - 300, 146, 600, 26), gm.Fights.LastEventText ?? "", Centered(label));
            if (sim.IsOver)
                GUI.Label(new Rect(Screen.width / 2f - 300, Screen.height / 2f - 30, 600, 60), "<size=32><b>" + (sim.Result.WinnerId == FightDirector.PlayerFighterId ? "YOU WIN" : sim.Result.Method == FightEndMethod.Draw ? "DRAW" : "DEFEATED") + "</b></size>", Centered(label));
        }

        private static void Bars(Rect r, string name, FighterRuntime f, GUIStyle label)
        {
            GUI.Box(new Rect(r.x - 6, r.y - 4, r.width + 12, r.height + 26), GUIContent.none);
            GUI.Label(new Rect(r.x, r.y, r.width, 20), "<b>" + name + "</b>  <size=11>" + f.State + "</size>", label);
            Bar(new Rect(r.x, r.y + 22, r.width, 12), f.HealthFraction, new Color(0.85f, 0.2f, 0.2f));
            Bar(new Rect(r.x, r.y + 38, r.width, 8), f.Stamina / f.MaxStamina, new Color(0.3f, 0.75f, 0.35f));
            Bar(new Rect(r.x, r.y + 50, r.width, 8), f.Momentum / 100f, new Color(1f, 0.8f, 0.2f));
        }

        private static void Bar(Rect r, float t, Color c)
        {
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, false, 0, new Color(0, 0, 0, 0.5f), 0, 0);
            GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(t), r.height), Texture2D.whiteTexture, ScaleMode.StretchToFill, false, 0, c, 0, 0);
        }

        private static void DrawHelp(GUIStyle label)
        {
            const string text = "<b>Controls</b> (F1 toggles)\nWASD move · Mouse camera · Shift sprint · E interact\n" +
                                "J light · K heavy · I kick · L grapple · Space block (tap just before a hit = parry)\n" +
                                "Shift dodge · O counter stance · U finisher · T taunt\nIn a clinch: J knee · K throw · L wall slam (near wall) · mash L/Shift to escape\n" +
                                "Driving: W/S throttle/brake · A/D steer · Space handbrake · Shift nitrous · C camera · E get out\n" +
                                "F5 quicksave · F9 quickload · 1-9 dialogue choices";
            GUI.Box(new Rect(10, Screen.height - 170, 640, 140), GUIContent.none);
            GUI.Label(new Rect(20, Screen.height - 166, 620, 136), text, label);
        }

        private static GUIStyle _centered;
        private static GUIStyle Centered(GUIStyle baseStyle)
        {
            if (_centered == null) _centered = new GUIStyle(baseStyle) { alignment = TextAnchor.MiddleCenter };
            return _centered;
        }
    }
}
