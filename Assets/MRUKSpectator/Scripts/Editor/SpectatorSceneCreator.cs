using System.Collections.Generic;
using System.IO;
using Meta.XR.MRUtilityKit;
using MRUKSpectator.CameraController;
using MRUKSpectator.Markers;
using MRUKSpectator.Network;
using MRUKSpectator.UI;
using MRUKSpectator.Visualization;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace MRUKSpectator.Editor
{
    [InitializeOnLoad]
    public static class SpectatorSceneCreator
    {
        private const string PcScenePath = "Assets/MRUKSpectator/Scenes/SpectatorPC.unity";
        private const string QuestScenePath = "Assets/MRUKSpectator/Scenes/SpectatorQuest.unity";
        private const string MrukPrefabGuid = "c2e00db6d2dfa42ac8d821db8d493a44";

        static SpectatorSceneCreator()
        {
            EditorApplication.delayCall += EnsureScenesExist;
        }

        private static void EnsureScenesExist()
        {
            if (!File.Exists(PcScenePath))
            {
                CreateSpectatorPCScene();
            }
            if (!File.Exists(QuestScenePath))
            {
                CreateSpectatorQuestScene();
            }
        }

        [MenuItem("Tools/MRUK Spectator/1. Create Spectator PC Scene", priority = 1)]
        [MenuItem("MRUK Spectator/1. Create Spectator PC Scene", priority = 1)]
        public static void CreateSpectatorPCScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 1. Directional Light
            GameObject lightGo = new GameObject("Directional Light");
            Light light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            // 2. Camera with Orbit Controller
            GameObject camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            Camera cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.06f, 0.07f, 0.09f, 1f);
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 100f;
            camGo.AddComponent<UniversalAdditionalCameraData>();
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<OrbitCameraController>();

            // 3. MRUK System
            string mrukPath = AssetDatabase.GUIDToAssetPath(MrukPrefabGuid);
            GameObject mrukPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(mrukPath);
            GameObject mrukGo;
            if (mrukPrefab != null)
            {
                mrukGo = (GameObject)PrefabUtility.InstantiatePrefab(mrukPrefab);
            }
            else
            {
                mrukGo = new GameObject("MRUK");
                mrukGo.AddComponent<MRUK>();
            }


            // 4. Spectator Managers
            GameObject mgrGo = new GameObject("SpectatorManager");
            mgrGo.AddComponent<SpectatorServer>();
            mgrGo.AddComponent<SpectatorRoomManager>();
            mgrGo.AddComponent<SpectatorMarkerManager>();

            // Save scene
            Directory.CreateDirectory(Path.GetDirectoryName(PcScenePath));
            EditorSceneManager.SaveScene(scene, PcScenePath);

            // 5. Build modern uGUI Canvas HUD
            SpectatorUIBuilder.BuildOrUpgradeCanvas();

            AddSceneToBuildSettings(PcScenePath);
            Debug.Log($"[SpectatorSceneCreator] Successfully created PC Spectator scene with uGUI: {PcScenePath}");
        }

        [MenuItem("Tools/MRUK Spectator/2. Create Spectator Quest Scene", priority = 2)]
        [MenuItem("MRUK Spectator/2. Create Spectator Quest Scene", priority = 2)]
        public static void CreateSpectatorQuestScene()
        {
            // Clone from basic MRUKBase scene which already has working OVRCameraRig and MRUK
            string baseScenePath = "Assets/MRUKSamples/Basic/MRUKBase.unity";
            if (!File.Exists(baseScenePath))
            {
                Debug.LogError($"[SpectatorSceneCreator] Base scene not found: {baseScenePath}");
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(QuestScenePath));
            AssetDatabase.CopyAsset(baseScenePath, QuestScenePath);
            AssetDatabase.Refresh();

            var scene = EditorSceneManager.OpenScene(QuestScenePath, OpenSceneMode.Single);

            // Add QuestSender & SpectatorMarkerManager if not present
            QuestSender sender = Object.FindAnyObjectByType<QuestSender>();
            if (sender == null)
            {
                GameObject senderGo = new GameObject("QuestSender");
                senderGo.AddComponent<QuestSender>();
                senderGo.AddComponent<SpectatorMarkerManager>();
            }
            else if (sender.GetComponent<SpectatorMarkerManager>() == null)
            {
                sender.gameObject.AddComponent<SpectatorMarkerManager>();
            }

            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            AddSceneToBuildSettings(QuestScenePath);
            Debug.Log($"[SpectatorSceneCreator] Successfully created Quest Spectator scene: {QuestScenePath}");
        }

        private static void AddSceneToBuildSettings(string scenePath)
        {
            List<EditorBuildSettingsScene> scenes = new(EditorBuildSettings.scenes);
            foreach (var s in scenes)
            {
                if (s.path == scenePath) return; // already added
            }
            scenes.Add(new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
