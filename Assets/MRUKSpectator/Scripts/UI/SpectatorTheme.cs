using System;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MRUKSpectator.UI
{
    /// <summary>
    /// Centralized Color Palette for the Spectator UI and Scene.
    /// Modify colors here in the Inspector to recolor every UI element and camera in real time.
    /// </summary>
    [CreateAssetMenu(fileName = "SpectatorTheme", menuName = "MRUK Spectator/Theme Palette", order = 1)]
    public class SpectatorTheme : ScriptableObject
    {
        [Header("Scene Background")]
        [Tooltip("The clear/background color of the Main Camera")]
        public Color cameraBackground = new Color(0.06f, 0.07f, 0.09f, 1f);

        [Header("UI Panels & Cards")]
        [Tooltip("Background color and opacity of UI cards (MainCard, LegendCard)")]
        public Color cardBackground = new Color(0.06f, 0.08f, 0.12f, 0.90f);
        [Tooltip("Outline/border color around UI cards")]
        public Color cardBorder = new Color(0.18f, 0.40f, 0.65f, 0.35f);
        [Tooltip("Divider line color between sections")]
        public Color dividerColor = new Color(0.25f, 0.35f, 0.45f, 0.35f);

        [Header("Buttons")]
        [Tooltip("Normal idle background color of buttons")]
        public Color buttonNormal = new Color(0.15f, 0.19f, 0.26f, 0.95f);
        [Tooltip("Hover/highlighted background color of buttons")]
        public Color buttonHover = new Color(0.20f, 0.38f, 0.58f, 1.0f);
        [Tooltip("Pressed background color of buttons")]
        public Color buttonPressed = new Color(0.11f, 0.25f, 0.40f, 1.0f);
        [Tooltip("Active toggle button state (e.g. Following Player: ON)")]
        public Color buttonActiveState = new Color(0.14f, 0.45f, 0.65f, 1f);
        [Tooltip("Button text color")]
        public Color buttonText = Color.white;
        [Tooltip("Button border outline")]
        public Color buttonBorder = new Color(0.25f, 0.45f, 0.70f, 0.30f);

        [Header("Typography")]
        [Tooltip("Title and header text color")]
        public Color textPrimary = new Color(0.92f, 0.96f, 1f, 1f);
        [Tooltip("Body and coordinate labels text color")]
        public Color textSecondary = new Color(0.76f, 0.80f, 0.86f, 1f);

        [Header("Status Indicators")]
        [Tooltip("Status badge color when Quest is connected")]
        public Color statusConnected = new Color(0.20f, 0.90f, 0.45f, 1f);
        [Tooltip("Status badge color when waiting for Quest connection")]
        public Color statusWaiting = new Color(1.00f, 0.72f, 0.20f, 1f);

        public event Action OnThemeChanged;

        private void OnValidate()
        {
            OnThemeChanged?.Invoke();

#if UNITY_EDITOR
            // Automatically push live changes to any open SpectatorCanvas in the Editor scene
            var hud = UnityEngine.Object.FindAnyObjectByType<SpectatorCanvasHUD>();
            if (hud != null && (hud.Theme == this || hud.Theme == null))
            {
                hud.ApplyTheme();
            }
#endif
        }

        public void NotifyChanged()
        {
            OnThemeChanged?.Invoke();
        }
    }
}
