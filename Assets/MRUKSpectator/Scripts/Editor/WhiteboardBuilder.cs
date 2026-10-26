using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using MRUKSpectator.Whiteboard;

namespace MRUKSpectator.Editor
{
    public static class WhiteboardBuilder
    {
        public const string PrefabPath = "Assets/MRUKSpectator/Prefabs/Whiteboard.prefab";
        public const string QuestScenePath = "Assets/MRUKSpectator/Scenes/SpectatorQuest.unity";
        public const string PcScenePath = "Assets/MRUKSpectator/Scenes/SpectatorPC.unity";

        [MenuItem("Tools/MRUK Spectator/Create or Update Whiteboard Prefab", priority = 20)]
        [MenuItem("MRUK Spectator/Create or Update Whiteboard Prefab", priority = 20)]
        public static GameObject CreateOrUpdateWhiteboardPrefab()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));

            // Create a Quad for the whiteboard surface
            GameObject wbGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            wbGo.name = "Whiteboard";
            wbGo.transform.localScale = new Vector3(1.6f, 0.9f, 1.0f); // 16:9 realistic board size

            // Ensure BoxCollider is properly set
            if (!wbGo.TryGetComponent<BoxCollider>(out var boxCollider))
            {
                boxCollider = wbGo.AddComponent<BoxCollider>();
            }
            boxCollider.size = new Vector3(1f, 1f, 0.04f);

            // Add Whiteboard scripts
            wbGo.AddComponent<SpectatorWhiteboard>();
            wbGo.AddComponent<WhiteboardPointerInput>();
            wbGo.AddComponent<WhiteboardDraggable>();

            // Assign standard URP material
            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit") ??
                           Shader.Find("Universal Render Pipeline/Lit") ??
                           Shader.Find("Standard");

            if (unlit != null)
            {
                var mat = new Material(unlit)
                {
                    name = "Whiteboard_SurfaceMat"
                };
                wbGo.GetComponent<MeshRenderer>().sharedMaterial = mat;
            }

            // Save as Prefab
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(wbGo, PrefabPath);
            Object.DestroyImmediate(wbGo);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[WhiteboardBuilder] Successfully built Whiteboard prefab at: {PrefabPath}");
            return prefab;
        }

        [MenuItem("Tools/MRUK Spectator/Add Whiteboard to Quest Scene", priority = 21)]
        [MenuItem("MRUK Spectator/Add Whiteboard to Quest Scene", priority = 21)]
        public static void AddWhiteboardToQuestScene()
        {
            if (!File.Exists(QuestScenePath))
            {
                Debug.LogError($"[WhiteboardBuilder] Quest scene not found at: {QuestScenePath}");
                return;
            }

            var scene = EditorSceneManager.OpenScene(QuestScenePath, OpenSceneMode.Single);

            // Ensure prefab exists
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                prefab = CreateOrUpdateWhiteboardPrefab();
            }

            SpectatorWhiteboard existing = Object.FindAnyObjectByType<SpectatorWhiteboard>();
            if (existing == null)
            {
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                instance.name = "Whiteboard";
                // Position 1.8 meters in front of the origin at chest/eye level (1.3m high), facing the user
                instance.transform.position = new Vector3(0f, 1.3f, 1.8f);
                instance.transform.rotation = Quaternion.Euler(0f, 180f, 0f);

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
                Debug.Log($"[WhiteboardBuilder] Added Whiteboard to Quest scene at {instance.transform.position}");
            }
            else
            {
                Debug.Log("[WhiteboardBuilder] Whiteboard already present in Quest scene.");
            }
        }

        [MenuItem("Tools/MRUK Spectator/Add Whiteboard to PC Scene", priority = 22)]
        [MenuItem("MRUK Spectator/Add Whiteboard to PC Scene", priority = 22)]
        public static void AddWhiteboardToPcScene()
        {
            if (!File.Exists(PcScenePath))
            {
                Debug.LogError($"[WhiteboardBuilder] PC scene not found at: {PcScenePath}");
                return;
            }

            var scene = EditorSceneManager.OpenScene(PcScenePath, OpenSceneMode.Single);

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                prefab = CreateOrUpdateWhiteboardPrefab();
            }

            SpectatorWhiteboard existing = Object.FindAnyObjectByType<SpectatorWhiteboard>();
            if (existing == null)
            {
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                instance.name = "Whiteboard";
                instance.transform.position = new Vector3(0f, 1.3f, 1.8f);
                instance.transform.rotation = Quaternion.Euler(0f, 180f, 0f);

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
                Debug.Log($"[WhiteboardBuilder] Added Whiteboard to PC scene at {instance.transform.position}");
            }
            else
            {
                Debug.Log("[WhiteboardBuilder] Whiteboard already present in PC scene.");
            }
        }
    }

    [InitializeOnLoad]
    public static class WhiteboardAutoSetup
    {
        private const string SetupFlagKey = "MRUK_Whiteboard_AutoSetup_v1";

        static WhiteboardAutoSetup()
        {
            EditorApplication.delayCall += CheckAndSetup;
        }

        private static void CheckAndSetup()
        {
            if (SessionState.GetBool(SetupFlagKey, false)) return;
            SessionState.SetBool(SetupFlagKey, true);

            try
            {
                WhiteboardBuilder.CreateOrUpdateWhiteboardPrefab();
                WhiteboardBuilder.AddWhiteboardToQuestScene();
                WhiteboardBuilder.AddWhiteboardToPcScene();
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[WhiteboardAutoSetup] Note during auto setup: {ex.Message}");
            }
        }
    }
}
