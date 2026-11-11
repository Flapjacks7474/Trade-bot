using FFTS.Session;
using UnityEngine;

namespace FFTS.Runtime
{
    /// <summary>
    /// ScriptableObject listing every JSON content file. Content stays as text so the same files are
    /// validated by the engine-independent test suite. (Addressables migration: see TECHNICAL_DECISIONS.md.)
    /// </summary>
    [CreateAssetMenu(menuName = "FFTS/Content Library", fileName = "ContentLibrary")]
    public sealed class ContentLibrary : ScriptableObject
    {
        public TextAsset[] Missions;
        public TextAsset[] Dialogues;
        public TextAsset[] Styles;
        public TextAsset[] Fighters;
        public TextAsset[] Layouts;
        public TextAsset[] Vehicles;
        public TextAsset[] Races;
        public TextAsset[] NpcProfiles;
        public TextAsset[] Appearance;
        public TextAsset ConversationRules;
        public TextAsset Districts;
        public TextAsset Crews;

        public ContentSources ToSources()
        {
            var s = new ContentSources();
            Add(s.Missions, Missions); Add(s.Dialogues, Dialogues); Add(s.Styles, Styles); Add(s.Fighters, Fighters); Add(s.Layouts, Layouts);
            Add(s.Vehicles, Vehicles); Add(s.Races, Races); Add(s.NpcProfiles, NpcProfiles);
            Add(s.Appearance, Appearance);
            return s;
        }

        private static void Add(System.Collections.Generic.List<string> list, TextAsset[] assets)
        {
            if (assets == null) return;
            foreach (var a in assets) if (a != null) list.Add(a.text);
        }
    }
}
