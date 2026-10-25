using System;
using Meta.XR.MRUtilityKit;
using MRUKSpectator.CameraController;
using MRUKSpectator.Network;
using MRUKSpectator.Visualization;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MRUKSpectator.UI
{
    /// <summary>
    /// uGUI-based Spectator HUD with live SpectatorTheme palette synchronization,
    /// connection badges, live room/player stats, and interactive view buttons.
    /// Runs in Edit Mode via ExecuteAlways so theme edits reflect live.
    /// </summary>
    [ExecuteAlways]
    public class SpectatorCanvasHUD : MonoBehaviour
    {
        [Header("Theme Palette")]
        [SerializeField] private SpectatorTheme _theme;

        [Header("Status & Info References")]
        [SerializeField] private Text _statusText;
        [SerializeField] private Image _statusDot;
        [SerializeField] private Text _serverIpText;
        [SerializeField] private Text _roomInfoText;
        [SerializeField] private Text _playerPosText;

        [Header("Typography References")]
        [SerializeField] private Text _titleText;
        [SerializeField] private Text _controlsHeaderText;
        [SerializeField] private Text _legendTitleText;
        [SerializeField] private Text[] _legendBodyTexts;

        [Header("Button References")]
        [SerializeField] private Button _btnTopDown;
        [SerializeField] private Button _btnIsometric;
        [SerializeField] private Button _btnFocusRoom;
        [SerializeField] private Button _btnFollowPlayer;
        [SerializeField] private Text _txtFollowPlayer;
        [SerializeField] private Button _btnToggleCeiling;
        [SerializeField] private Text _txtToggleCeiling;
        [SerializeField] private Button _btnClearTrail;
        [SerializeField] private Button _btnToggleHUD;

        [Header("Panels")]
        [SerializeField] private GameObject _mainCard;
        [SerializeField] private GameObject _legendCard;
        [SerializeField] private GameObject _toggleBtnContainer;
        [SerializeField] private Image _dividerImage;

        private Image _imgFollowPlayer;

        public SpectatorTheme Theme
        {
            get => _theme;
            set
            {
                if (_theme != null) _theme.OnThemeChanged -= ApplyTheme;
                _theme = value;
                if (_theme != null) _theme.OnThemeChanged += ApplyTheme;
                ApplyTheme();
            }
        }

        private void Awake()
        {
            if (_btnFollowPlayer != null)
            {
                _imgFollowPlayer = _btnFollowPlayer.GetComponent<Image>();
            }

            if (Application.isPlaying)
            {
                SetupButtonListeners();
            }
        }

        private void OnEnable()
        {
            if (_theme != null)
            {
                _theme.OnThemeChanged -= ApplyTheme;
                _theme.OnThemeChanged += ApplyTheme;
            }

            ApplyTheme();
        }

        private void OnDisable()
        {
            if (_theme != null)
            {
                _theme.OnThemeChanged -= ApplyTheme;
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            ApplyTheme();
        }
#endif

        [ContextMenu("Apply Theme Palette Now")]
        public void ApplyTheme()
        {
            if (_theme == null) return;

            // 1. Scene Camera Background
            Camera cam = Camera.main;
            if (cam != null)
            {
                cam.backgroundColor = _theme.cameraBackground;
#if UNITY_EDITOR
                if (!Application.isPlaying) EditorUtility.SetDirty(cam);
#endif
            }

            // 2. Panels and Cards
            ApplyCardStyle(_mainCard);
            ApplyCardStyle(_legendCard);
            ApplyCardStyle(_toggleBtnContainer);

            if (_dividerImage != null)
            {
                _dividerImage.color = _theme.dividerColor;
#if UNITY_EDITOR
                if (!Application.isPlaying) EditorUtility.SetDirty(_dividerImage);
#endif
            }

            // 3. Typography
            ApplyTextColor(_titleText, _theme.textPrimary);
            ApplyTextColor(_controlsHeaderText, _theme.textPrimary);
            ApplyTextColor(_legendTitleText, _theme.textPrimary);

            ApplyTextColor(_serverIpText, _theme.textSecondary);
            ApplyTextColor(_roomInfoText, _theme.textSecondary);
            ApplyTextColor(_playerPosText, _theme.textSecondary);

            if (_legendBodyTexts != null)
            {
                foreach (var t in _legendBodyTexts)
                {
                    ApplyTextColor(t, _theme.textSecondary);
                }
            }

            // 4. Buttons
            ApplyButtonStyle(_btnTopDown);
            ApplyButtonStyle(_btnIsometric);
            ApplyButtonStyle(_btnFocusRoom);
            ApplyButtonStyle(_btnFollowPlayer);
            ApplyButtonStyle(_btnToggleCeiling);
            ApplyButtonStyle(_btnClearTrail);
            ApplyButtonStyle(_btnToggleHUD);
        }

        private void ApplyCardStyle(GameObject cardGo)
        {
            if (cardGo == null || _theme == null) return;

            Image img = cardGo.GetComponent<Image>();
            if (img != null)
            {
                img.color = _theme.cardBackground;
#if UNITY_EDITOR
                if (!Application.isPlaying) EditorUtility.SetDirty(img);
#endif
            }

            Outline outline = cardGo.GetComponent<Outline>();
            if (outline != null)
            {
                outline.effectColor = _theme.cardBorder;
#if UNITY_EDITOR
                if (!Application.isPlaying) EditorUtility.SetDirty(outline);
#endif
            }
        }

        private void ApplyButtonStyle(Button btn)
        {
            if (btn == null || _theme == null) return;

            // In uGUI ColorTint mode, targetGraphic.color multiplies with normalColor.
            // Setting targetGraphic.color to White ensures button colors match the exact hex palette.
            Image img = btn.GetComponent<Image>();
            if (img != null)
            {
                img.color = Color.white;
#if UNITY_EDITOR
                if (!Application.isPlaying) EditorUtility.SetDirty(img);
#endif
            }

            Outline outline = btn.GetComponent<Outline>();
            if (outline != null)
            {
                outline.effectColor = _theme.buttonBorder;
#if UNITY_EDITOR
                if (!Application.isPlaying) EditorUtility.SetDirty(outline);
#endif
            }

            var colors = btn.colors;
            colors.colorMultiplier = 1f;
            colors.normalColor = _theme.buttonNormal;
            colors.highlightedColor = _theme.buttonHover;
            colors.pressedColor = _theme.buttonPressed;
            colors.selectedColor = _theme.buttonHover;
            btn.colors = colors;

            Text txt = btn.GetComponentInChildren<Text>();
            if (txt != null)
            {
                txt.color = _theme.buttonText;
#if UNITY_EDITOR
                if (!Application.isPlaying) EditorUtility.SetDirty(txt);
#endif
            }

#if UNITY_EDITOR
            if (!Application.isPlaying) EditorUtility.SetDirty(btn);
#endif
        }

        private void ApplyTextColor(Text text, Color color)
        {
            if (text == null) return;
            text.color = color;
#if UNITY_EDITOR
            if (!Application.isPlaying) EditorUtility.SetDirty(text);
#endif
        }

        private void SetupButtonListeners()
        {
            if (_btnTopDown != null)
                _btnTopDown.onClick.AddListener(OnTopDownClicked);

            if (_btnIsometric != null)
                _btnIsometric.onClick.AddListener(OnIsometricClicked);

            if (_btnFocusRoom != null)
                _btnFocusRoom.onClick.AddListener(OnFocusRoomClicked);

            if (_btnFollowPlayer != null)
                _btnFollowPlayer.onClick.AddListener(OnFollowPlayerClicked);

            if (_btnToggleCeiling != null)
                _btnToggleCeiling.onClick.AddListener(OnToggleCeilingClicked);

            if (_btnClearTrail != null)
                _btnClearTrail.onClick.AddListener(OnClearTrailClicked);

            if (_btnToggleHUD != null)
                _btnToggleHUD.onClick.AddListener(OnToggleHUDClicked);
        }

        private void Update()
        {
            if (!Application.isPlaying) return;

            UpdateConnectionStatus();
            UpdateRoomInfo();
            UpdatePlayerInfo();
            UpdateButtonStates();
        }

        private void UpdateConnectionStatus()
        {
            string serverIp = SpectatorServer.Instance != null ? SpectatorServer.Instance.ServerIpAddress : "127.0.0.1";
            int clientCount = SpectatorServer.Instance != null ? SpectatorServer.Instance.ConnectedClientCount : 0;

            if (_serverIpText != null)
            {
                _serverIpText.text = $"Server IP: {serverIp}:{PacketFramer.DefaultTcpPort}";
            }

            Color statusColor = clientCount > 0
                ? (_theme != null ? _theme.statusConnected : new Color(0.2f, 0.9f, 0.45f))
                : (_theme != null ? _theme.statusWaiting : new Color(1f, 0.72f, 0.2f));

            if (_statusText != null)
            {
                _statusText.text = clientCount > 0 ? $"Quest Connected ({clientCount})" : "Waiting for Quest (LAN Beacon)...";
                _statusText.color = statusColor;
            }

            if (_statusDot != null)
            {
                _statusDot.color = statusColor;
            }
        }

        private void UpdateRoomInfo()
        {
            if (_roomInfoText == null) return;

            bool hasRoom = SpectatorRoomManager.Instance != null && SpectatorRoomManager.Instance.HasRoomLoaded;
            if (hasRoom)
            {
                var mgr = SpectatorRoomManager.Instance;
                _roomInfoText.text = $"Room: {mgr.AnchorCount} Anchors | {mgr.RoomSize.x:F1}m x {mgr.RoomSize.z:F1}m (H: {mgr.RoomSize.y:F1}m)";
            }
            else
            {
                _roomInfoText.text = "Room: Waiting for scan from Quest...";
            }
        }

        private void UpdatePlayerInfo()
        {
            if (_playerPosText == null) return;

            var player = SpectatorRoomManager.Instance != null ? SpectatorRoomManager.Instance.GetPrimaryPlayer() : null;
            if (player != null)
            {
                Vector3 pos = player.CurrentPosition;
                _playerPosText.text = $"Player: X: {pos.x:F2}m | Y: {pos.y:F2}m | Z: {pos.z:F2}m";
            }
            else
            {
                _playerPosText.text = "Player: Not detected";
            }
        }

        private void UpdateButtonStates()
        {
            bool isFollowing = OrbitCameraController.Instance != null && OrbitCameraController.Instance.IsFollowingPlayer;
            if (_txtFollowPlayer != null)
            {
                _txtFollowPlayer.text = isFollowing ? "Following Player: ON" : "Follow Player";
            }
            if (_imgFollowPlayer != null)
            {
                Color activeCol = _theme != null ? _theme.buttonActiveState : new Color(0.14f, 0.45f, 0.65f, 1f);
                Color normalCol = _theme != null ? _theme.buttonNormal : new Color(0.16f, 0.19f, 0.26f, 1f);
                _imgFollowPlayer.color = isFollowing ? activeCol : normalCol;
            }

            bool ceilingHidden = SpectatorRoomManager.Instance != null && SpectatorRoomManager.Instance.IsCeilingHidden;
            if (_txtToggleCeiling != null)
            {
                _txtToggleCeiling.text = ceilingHidden ? "Show Ceiling" : "Hide Ceiling";
            }
        }

        public void OnTopDownClicked()
        {
            OrbitCameraController.Instance?.SetTopDownView();
        }

        public void OnIsometricClicked()
        {
            OrbitCameraController.Instance?.SetIsometricView();
        }

        public void OnFocusRoomClicked()
        {
            MRUKRoom currentRoom = MRUK.Instance?.GetCurrentRoom();
            if (currentRoom != null && SpectatorRoomManager.Instance != null)
            {
                SpectatorRoomManager.Instance.ApplyRoomStyling(currentRoom);
            }
        }

        public void OnFollowPlayerClicked()
        {
            var player = SpectatorRoomManager.Instance != null ? SpectatorRoomManager.Instance.GetPrimaryPlayer() : null;
            if (player != null)
            {
                OrbitCameraController.Instance?.ToggleFollowPlayer(player.transform);
            }
        }

        public void OnToggleCeilingClicked()
        {
            SpectatorRoomManager.Instance?.ToggleCeiling();
        }

        public void OnClearTrailClicked()
        {
            var player = SpectatorRoomManager.Instance != null ? SpectatorRoomManager.Instance.GetPrimaryPlayer() : null;
            player?.ClearTrail();
        }

        public void OnToggleHUDClicked()
        {
            if (_mainCard != null)
            {
                bool active = !_mainCard.activeSelf;
                _mainCard.SetActive(active);
                if (_legendCard != null) _legendCard.SetActive(active);
            }
        }
    }
}
