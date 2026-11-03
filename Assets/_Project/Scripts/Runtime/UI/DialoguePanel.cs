using FFTS.Dialogue;
using UnityEngine;

namespace FFTS.Runtime
{
    /// <summary>Placeholder dialogue UI (IMGUI). Options by mouse or number keys. uGUI/UI Toolkit version: KNOWN_LIMITATIONS.md.</summary>
    public sealed class DialoguePanel : MonoBehaviour
    {
        private DialogueRunner _runner;
        private string _speakerFallback;
        public bool IsOpen => _runner != null;

        public void Open(DialogueRunner runner, string speaker)
        {
            _runner = runner; _speakerFallback = speaker;
            CoreGameManager.Instance.Lock(this);
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        }

        private void Close()
        {
            _runner = null;
            CoreGameManager.Instance.Unlock(this);
            Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
        }

        private void Choose(DialogueOption o)
        {
            var outcome = _runner.Choose(o.Id);
            if (outcome == null) return;
            if (outcome.Ended || _runner.IsFinished) Close();
            if (outcome.StartFight != null)
            {
                var gm = CoreGameManager.Instance;
                gm.Fights.Begin(outcome.StartFight, null, gm.CurrentLocationId());
            }
        }

        private void OnGUI()
        {
            if (_runner == null || _runner.Current == null) return;
            var options = _runner.AvailableOptions();
            float w = Mathf.Min(900, Screen.width - 40), h = 120 + options.Count * 34;
            var rect = new Rect((Screen.width - w) / 2, Screen.height - h - 20, w, h);
            GUI.Box(rect, GUIContent.none);
            GUILayout.BeginArea(new Rect(rect.x + 16, rect.y + 12, rect.width - 32, rect.height - 24));
            GUILayout.Label("<b><size=18>" + (_runner.Current.Speaker ?? _speakerFallback) + "</size></b>", RichLabel());
            GUILayout.Label("<size=16>" + _runner.CurrentLine + "</size>", RichLabel());
            GUILayout.Space(8);
            for (int i = 0; i < options.Count; i++)
            {
                var o = options[i];
                string tone = o.Tone == DialogueTone.Neutral ? "" : "  <i>[" + o.Tone + "]</i>";
                if (GUILayout.Button(new GUIContent("<size=15>" + (i + 1) + ". " + o.Text + tone + "</size>"), RichButton(), GUILayout.Height(30))) { Choose(o); break; }
            }
            GUILayout.EndArea();

            var e = Event.current;
            if (e != null && e.type == EventType.KeyDown && e.keyCode >= KeyCode.Alpha1 && e.keyCode <= KeyCode.Alpha9)
            {
                int idx = e.keyCode - KeyCode.Alpha1;
                if (idx < options.Count) { Choose(options[idx]); e.Use(); }
            }
        }

        private static GUIStyle _label, _button;
        internal static GUIStyle RichLabel() { if (_label == null) _label = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true }; return _label; }
        internal static GUIStyle RichButton() { if (_button == null) _button = new GUIStyle(GUI.skin.button) { richText = true, alignment = TextAnchor.MiddleLeft }; return _button; }
    }
}
