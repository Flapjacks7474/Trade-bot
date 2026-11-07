using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFTS.Core;
using FFTS.Runtime;
using FFTS.Session;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace FFTS.EditorTools
{
    /// <summary>
    /// One-click setup for a fresh checkout: creates the ContentLibrary asset from the JSON data files,
    /// the URP pipeline asset, and the Southside vertical-slice scene. Re-running is safe.
    /// </summary>
    public static class SliceSceneBuilder
    {
        public const string DataRoot = "Assets/_Project/Data";
        public const string LibraryPath = "Assets/_Project/Settings/ContentLibrary.asset";
        public const string ScenePath = "Assets/_Project/Scenes/Southside_Slice.unity";
        public const string UrpAssetPath = "Assets/_Project/Settings/FFTS_URP.asset";

        [MenuItem("FFTS/Build Southside Slice Scene", priority = 0)]
        public static void BuildScene()
        {
            SetupUrp();
            var library = BuildContentLibrary();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var core = new GameObject("FFTS Core");
            var gm = core.AddComponent<CoreGameManager>();
            var so = new SerializedObject(gm);
            so.FindProperty("content").objectReferenceValue = library;
            so.ApplyModifiedPropertiesWithoutUndo();
            core.AddComponent<FightDirector>();
            core.AddComponent<DialoguePanel>();
            core.AddComponent<DrivingDirector>();
            core.AddComponent<RaceDirector>();
            core.AddComponent<GaragePanel>();
            new GameObject("Slice Builder").AddComponent<SliceBuilder>();

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log("[FFTS] Built " + ScenePath + ". Press Play.");
        }

        [MenuItem("FFTS/Rebuild Content Library", priority = 1)]
        public static ContentLibrary BuildContentLibrary()
        {
            AssetDatabase.Refresh();
            var lib = AssetDatabase.LoadAssetAtPath<ContentLibrary>(LibraryPath);
            if (lib == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LibraryPath));
                lib = ScriptableObject.CreateInstance<ContentLibrary>();
                AssetDatabase.CreateAsset(lib, LibraryPath);
            }
            lib.Missions = Load("Missions");
            lib.Dialogues = Load("Dialogue").Where(t => !t.name.StartsWith("npc_conversations")).ToArray();
            lib.Styles = Load("Moves");
            lib.Fighters = Load("Characters").Where(t => t.name.StartsWith("fighters")).ToArray();
            lib.Layouts = Load("World").Where(t => t.name.EndsWith("_layout")).ToArray();
            lib.Vehicles = Load("Vehicles");
            lib.Races = Load("Racing");
            lib.NpcProfiles = Load("Characters").Where(t => t.name.StartsWith("npc_profiles")).ToArray();
            lib.ConversationRules = AssetDatabase.LoadAssetAtPath<TextAsset>(DataRoot + "/Dialogue/npc_conversations.json");
            lib.Districts = AssetDatabase.LoadAssetAtPath<TextAsset>(DataRoot + "/World/districts.json");
            lib.Crews = AssetDatabase.LoadAssetAtPath<TextAsset>(DataRoot + "/Crews/crews.json");
            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();
            return lib;
        }

        private static TextAsset[] Load(string folder)
        {
            var guids = AssetDatabase.FindAssets("t:TextAsset", new[] { DataRoot + "/" + folder });
            return guids.Select(g => AssetDatabase.LoadAssetAtPath<TextAsset>(AssetDatabase.GUIDToAssetPath(g)))
                        .Where(t => t != null).OrderBy(t => t.name).ToArray();
        }

        [MenuItem("FFTS/Validate Content", priority = 2)]
        public static bool ValidateContent()
        {
            var lib = BuildContentLibrary();
            var report = new ValidationReport();
            var content = GameContentLoader.Load(lib.ToSources(), report, out _);
            if (lib.ConversationRules != null) NpcConversationRules.Parse(lib.ConversationRules.text, content, report);
            if (lib.Crews != null) FFTS.Crews.CrewFile.Parse(lib.Crews.text, report);
            new GameSession(content, 1, report);
            if (report.IsValid) Debug.Log("[FFTS] Content valid.");
            else foreach (var e in report.Errors) Debug.LogError("[FFTS content] " + e);
            return report.IsValid;
        }

        [MenuItem("FFTS/Setup URP", priority = 20)]
        public static void SetupUrp()
        {
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(UrpAssetPath);
            if (asset == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(UrpAssetPath));
                var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, UrpAssetPath.Replace(".asset", "_Renderer.asset"));
                asset = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(asset, UrpAssetPath);
            }
            GraphicsSettings.defaultRenderPipeline = asset;
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = asset;
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[FFTS] URP pipeline asset assigned.");
        }
    }

    /// <summary>Batch-mode entry points, e.g. Unity -batchmode -quit -projectPath . -executeMethod FFTS.EditorTools.BuildScripts.BuildWindows</summary>
    public static class BuildScripts
    {
        public static void BuildWindows()
        {
            if (!SliceSceneBuilder.ValidateContent()) { EditorApplication.Exit(2); return; }
            if (!File.Exists(SliceSceneBuilder.ScenePath)) SliceSceneBuilder.BuildScene();
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { SliceSceneBuilder.ScenePath },
                locationPathName = "Builds/Windows/FightForTheSouth.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            });
            Debug.Log("[FFTS] Build result: " + report.summary.result + " (" + report.summary.totalSize + " bytes)");
            EditorApplication.Exit(report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
        }
    }
}
