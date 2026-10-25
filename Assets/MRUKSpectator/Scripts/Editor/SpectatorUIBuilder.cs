using System;
using System.IO;
using MRUKSpectator.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace MRUKSpectator.Editor
{
    public static class SpectatorUIBuilder
    {
        private const string PcScenePath = "Assets/MRUKSpectator/Scenes/SpectatorPC.unity";

        private static readonly Color DarkBgColor = new Color(0.06f, 0.08f, 0.12f, 0.90f);
        private static readonly Color CardBorderColor = new Color(0.18f, 0.40f, 0.65f, 0.35f);
        private static readonly Color ButtonNormalColor = new Color(0.15f, 0.19f, 0.26f, 0.95f);
        private static readonly Color ButtonHoverColor = new Color(0.20f, 0.38f, 0.58f, 1.0f);
        private static readonly Color ButtonPressedColor = new Color(0.11f, 0.25f, 0.40f, 1.0f);

        [MenuItem("Tools/MRUK Spectator/Upgrade to uGUI Canvas HUD", priority = 3)]
        [MenuItem("MRUK Spectator/Upgrade to uGUI Canvas HUD", priority = 3)]
        public static void BuildOrUpgradeCanvas()
        {
            var activeScene = EditorSceneManager.GetActiveScene();
            if (activeScene.path != PcScenePath)
            {
                if (File.Exists(PcScenePath))
                {
                    activeScene = EditorSceneManager.OpenScene(PcScenePath, OpenSceneMode.Single);
                }
            }

            // Remove legacy SpectatorHUD if present on SpectatorManager
            var legacyHud = UnityEngine.Object.FindAnyObjectByType<SpectatorHUD>();
            if (legacyHud != null)
            {
                UnityEngine.Object.DestroyImmediate(legacyHud);
            }

            // Remove existing Canvas if upgrading
            var existingCanvas = GameObject.Find("SpectatorCanvas");
            if (existingCanvas != null)
            {
                UnityEngine.Object.DestroyImmediate(existingCanvas);
            }

            // 1. Ensure EventSystem exists
            EnsureEventSystem();

            // 2. Create Canvas GameObject
            GameObject canvasGo = new GameObject("SpectatorCanvas");
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            canvasGo.AddComponent<GraphicRaycaster>();
            SpectatorCanvasHUD hud = canvasGo.AddComponent<SpectatorCanvasHUD>();

            // 3. Build Main Card (Top-Left)
            GameObject mainCard = CreatePanel(canvasGo.transform, "MainCard",
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(24f, -24f), new Vector2(390f, 470f), DarkBgColor, CardBorderColor);

            var mainVlg = mainCard.AddComponent<VerticalLayoutGroup>();
            mainVlg.padding = new RectOffset(20, 20, 18, 18);
            mainVlg.spacing = 8;
            mainVlg.childControlWidth = true;
            mainVlg.childControlHeight = false;
            mainVlg.childForceExpandWidth = true;
            mainVlg.childForceExpandHeight = false;

            // Title
            CreateText(mainCard.transform, "Title", "MRUK 3D Room Spectator", 18, FontStyle.Bold, new Color(0.92f, 0.96f, 1f), 32);

            // Status Pill (Row)
            GameObject statusRow = CreateHorizontalRow(mainCard.transform, "StatusRow", 8, 26);
            Image statusDot = CreateImage(statusRow.transform, "StatusDot", new Vector2(12f, 12f), new Color(1f, 0.72f, 0.2f, 1f));
            var dotLayout = statusDot.gameObject.AddComponent<LayoutElement>();
            dotLayout.preferredWidth = 12;
            dotLayout.preferredHeight = 12;
            dotLayout.flexibleWidth = 0;

            Text statusText = CreateText(statusRow.transform, "StatusText", "Waiting for Quest (LAN Beacon)...", 13, FontStyle.Bold, new Color(1f, 0.72f, 0.2f), 24);

            // Network and Room Information Labels
            Text serverIpText = CreateText(mainCard.transform, "ServerIpText", "Server IP: 127.0.0.1:9050", 12, FontStyle.Normal, new Color(0.76f, 0.80f, 0.86f), 20);
            Text roomInfoText = CreateText(mainCard.transform, "RoomInfoText", "Room: Waiting for scan from Quest...", 12, FontStyle.Normal, new Color(0.76f, 0.80f, 0.86f), 20);
            Text playerPosText = CreateText(mainCard.transform, "PlayerPosText", "Player: Not detected", 12, FontStyle.Normal, new Color(0.76f, 0.80f, 0.86f), 20);

            // Divider Line
            CreateDivider(mainCard.transform, "Divider");

            // Section Subtitle
            CreateText(mainCard.transform, "ControlsHeader", "Camera & Room Controls", 13, FontStyle.Bold, new Color(0.88f, 0.92f, 0.98f), 22);

            // Buttons: Row 1
            GameObject btnRow1 = CreateHorizontalRow(mainCard.transform, "BtnRow1", 8, 36);
            Button btnTopDown = CreateButton(btnRow1.transform, "BtnTopDown", "Top-Down (Bird's Eye)", 12, out _);
            Button btnIsometric = CreateButton(btnRow1.transform, "BtnIsometric", "Isometric 3D", 12, out _);

            // Buttons: Row 2
            GameObject btnRow2 = CreateHorizontalRow(mainCard.transform, "BtnRow2", 8, 36);
            Button btnFocusRoom = CreateButton(btnRow2.transform, "BtnFocusRoom", "Focus Room", 12, out _);
            Button btnFollowPlayer = CreateButton(btnRow2.transform, "BtnFollowPlayer", "Follow Player", 12, out Text txtFollowPlayer);

            // Buttons: Row 3
            GameObject btnRow3 = CreateHorizontalRow(mainCard.transform, "BtnRow3", 8, 36);
            Button btnToggleCeiling = CreateButton(btnRow3.transform, "BtnToggleCeiling", "Hide Ceiling", 12, out Text txtToggleCeiling);
            Button btnClearTrail = CreateButton(btnRow3.transform, "BtnClearTrail", "Clear Trail", 12, out _);

            // 4. Build Controls Legend (Bottom-Right)
            GameObject legendCard = CreatePanel(canvasGo.transform, "LegendCard",
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-24f, 24f), new Vector2(300f, 130f), DarkBgColor, CardBorderColor);

            var legendVlg = legendCard.AddComponent<VerticalLayoutGroup>();
            legendVlg.padding = new RectOffset(16, 16, 14, 14);
            legendVlg.spacing = 4;
            legendVlg.childControlWidth = true;
            legendVlg.childControlHeight = false;
            legendVlg.childForceExpandWidth = true;

            CreateText(legendCard.transform, "LegendTitle", "Orbit Controls Legend", 13, FontStyle.Bold, new Color(0.92f, 0.96f, 1f), 20);
            CreateText(legendCard.transform, "Legend1", "• Left Click + Drag: Orbit 3D Room", 11, FontStyle.Normal, new Color(0.76f, 0.80f, 0.86f), 18);
            CreateText(legendCard.transform, "Legend2", "• Middle / Shift+Right: Pan Camera", 11, FontStyle.Normal, new Color(0.76f, 0.80f, 0.86f), 18);
            CreateText(legendCard.transform, "Legend3", "• Mouse Scroll: Zoom In / Out", 11, FontStyle.Normal, new Color(0.76f, 0.80f, 0.86f), 18);

            // 5. Mini HUD Toggle Button (Top-Right)
            GameObject toggleGo = CreatePanel(canvasGo.transform, "ToggleBtnContainer",
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-24f, -24f), new Vector2(110f, 36f), DarkBgColor, CardBorderColor);

            Button btnToggleHUD = CreateButton(toggleGo.transform, "BtnToggleHUD", "Toggle UI", 12, out _);
            RectTransform btnToggleRt = btnToggleHUD.GetComponent<RectTransform>();
            btnToggleRt.anchorMin = Vector2.zero;
            btnToggleRt.anchorMax = Vector2.one;
            btnToggleRt.sizeDelta = Vector2.zero;
            btnToggleRt.anchoredPosition = Vector2.zero;

            // 6. Connect serialized fields on SpectatorCanvasHUD
            var so = new SerializedObject(hud);
            so.FindProperty("_statusText").objectReferenceValue = statusText;
            so.FindProperty("_statusDot").objectReferenceValue = statusDot;
            so.FindProperty("_serverIpText").objectReferenceValue = serverIpText;
            so.FindProperty("_roomInfoText").objectReferenceValue = roomInfoText;
            so.FindProperty("_playerPosText").objectReferenceValue = playerPosText;
            so.FindProperty("_btnTopDown").objectReferenceValue = btnTopDown;
            so.FindProperty("_btnIsometric").objectReferenceValue = btnIsometric;
            so.FindProperty("_btnFocusRoom").objectReferenceValue = btnFocusRoom;
            so.FindProperty("_btnFollowPlayer").objectReferenceValue = btnFollowPlayer;
            so.FindProperty("_txtFollowPlayer").objectReferenceValue = txtFollowPlayer;
            so.FindProperty("_btnToggleCeiling").objectReferenceValue = btnToggleCeiling;
            so.FindProperty("_txtToggleCeiling").objectReferenceValue = txtToggleCeiling;
            so.FindProperty("_btnClearTrail").objectReferenceValue = btnClearTrail;
            so.FindProperty("_btnToggleHUD").objectReferenceValue = btnToggleHUD;
            so.FindProperty("_mainCard").objectReferenceValue = mainCard;
            so.FindProperty("_legendCard").objectReferenceValue = legendCard;
            so.ApplyModifiedProperties();

            // Save Scene
            EditorSceneManager.SaveScene(activeScene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[SpectatorUIBuilder] Successfully created uGUI Spectator Canvas HUD!");
        }

        private static void EnsureEventSystem()
        {
            var es = UnityEngine.Object.FindAnyObjectByType<EventSystem>();
            if (es == null)
            {
                GameObject esGo = new GameObject("EventSystem");
                es = esGo.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
                esGo.AddComponent<InputSystemUIInputModule>();
#else
                esGo.AddComponent<StandaloneInputModule>();
#endif
            }
        }

        private static GameObject CreatePanel(Transform parent, string name,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 anchoredPos, Vector2 size, Color bgColor, Color borderColor)
        {
            GameObject panel = new GameObject(name);
            panel.transform.SetParent(parent, false);

            RectTransform rt = panel.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;

            Image img = panel.AddComponent<Image>();
            img.color = bgColor;

            Outline outline = panel.AddComponent<Outline>();
            outline.effectColor = borderColor;
            outline.effectDistance = new Vector2(1.5f, -1.5f);

            return panel;
        }

        private static GameObject CreateHorizontalRow(Transform parent, string name, float spacing, float height)
        {
            GameObject row = new GameObject(name);
            row.transform.SetParent(parent, false);

            RectTransform rt = row.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0f, height);

            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = spacing;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;

            var le = row.AddComponent<LayoutElement>();
            le.preferredHeight = height;

            return row;
        }

        private static Text CreateText(Transform parent, string name, string content, int fontSize, FontStyle style, Color color, float preferredHeight)
        {
            GameObject textGo = new GameObject(name);
            textGo.transform.SetParent(parent, false);

            Text text = textGo.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (text.font == null) text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.text = content;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = color;
            text.alignment = TextAnchor.MiddleLeft;
            text.raycastTarget = false;

            if (preferredHeight > 0)
            {
                var le = textGo.AddComponent<LayoutElement>();
                le.preferredHeight = preferredHeight;
            }

            return text;
        }

        private static Image CreateImage(Transform parent, string name, Vector2 size, Color color)
        {
            GameObject imgGo = new GameObject(name);
            imgGo.transform.SetParent(parent, false);

            RectTransform rt = imgGo.AddComponent<RectTransform>();
            rt.sizeDelta = size;

            Image img = imgGo.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;

            return img;
        }

        private static void CreateDivider(Transform parent, string name)
        {
            GameObject div = new GameObject(name);
            div.transform.SetParent(parent, false);

            Image img = div.AddComponent<Image>();
            img.color = new Color(0.25f, 0.35f, 0.45f, 0.35f);
            img.raycastTarget = false;

            var le = div.AddComponent<LayoutElement>();
            le.preferredHeight = 1;
        }

        private static Button CreateButton(Transform parent, string name, string label, int fontSize, out Text buttonText)
        {
            GameObject btnGo = new GameObject(name);
            btnGo.transform.SetParent(parent, false);

            Image img = btnGo.AddComponent<Image>();
            img.color = ButtonNormalColor;

            Outline outline = btnGo.AddComponent<Outline>();
            outline.effectColor = new Color(0.25f, 0.45f, 0.70f, 0.30f);
            outline.effectDistance = new Vector2(1f, -1f);

            Button btn = btnGo.AddComponent<Button>();
            var colors = btn.colors;
            colors.normalColor = ButtonNormalColor;
            colors.highlightedColor = ButtonHoverColor;
            colors.pressedColor = ButtonPressedColor;
            colors.selectedColor = ButtonHoverColor;
            colors.fadeDuration = 0.1f;
            btn.colors = colors;

            GameObject textGo = new GameObject("Text");
            textGo.transform.SetParent(btnGo.transform, false);

            RectTransform textRt = textGo.AddComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.sizeDelta = Vector2.zero;
            textRt.anchoredPosition = Vector2.zero;

            buttonText = textGo.AddComponent<Text>();
            buttonText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (buttonText.font == null) buttonText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            buttonText.text = label;
            buttonText.fontSize = fontSize;
            buttonText.fontStyle = FontStyle.Bold;
            buttonText.color = Color.white;
            buttonText.alignment = TextAnchor.MiddleCenter;
            buttonText.raycastTarget = false;

            return btn;
        }
    }
}
