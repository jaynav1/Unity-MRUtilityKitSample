using System;
using Meta.XR.MRUtilityKit;
using MRUKSpectator.CameraController;
using MRUKSpectator.Network;
using MRUKSpectator.Visualization;
using UnityEngine;
using UnityEngine.UI;

namespace MRUKSpectator.UI
{
    /// <summary>
    /// uGUI-based Spectator HUD with centralized SpectatorTheme palette support,
    /// connection badges, live room/player stats, and interactive view buttons.
    /// </summary>
    public class SpectatorCanvasHUD : MonoBehaviour
    {
        [Header("Theme Palette")]
        [Tooltip("Enable only if you want the SpectatorTheme asset to override your manual Inspector colors. Keep false to preserve your own UI edits.")]
        [SerializeField] private bool _usePaletteTheme = false;
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

        public bool UsePaletteTheme
        {
            get => _usePaletteTheme;
            set
            {
                _usePaletteTheme = value;
                if (_usePaletteTheme) ApplyTheme();
            }
        }

        public SpectatorTheme Theme
        {
            get => _theme;
            set
            {
                if (_theme != null) _theme.OnThemeChanged -= ApplyTheme;
                _theme = value;
                if (_theme != null && _usePaletteTheme) _theme.OnThemeChanged += ApplyTheme;
                if (_usePaletteTheme) ApplyTheme();
            }
        }

        private void Awake()
        {
            if (_btnFollowPlayer != null)
            {
                _imgFollowPlayer = _btnFollowPlayer.GetComponent<Image>();
            }

            SetupButtonListeners();
        }

        private void OnEnable()
        {
            if (_usePaletteTheme && _theme != null)
            {
                _theme.OnThemeChanged += ApplyTheme;
                ApplyTheme();
            }
        }

        private void OnDisable()
        {
            if (_theme != null)
            {
                _theme.OnThemeChanged -= ApplyTheme;
            }
        }

        public void ApplyTheme()
        {
            if (!_usePaletteTheme || _theme == null) return;

            // 1. Scene Camera Background
            Camera cam = Camera.main;
            if (cam != null)
            {
                cam.backgroundColor = _theme.cameraBackground;
            }

            // 2. Panels and Cards
            ApplyCardStyle(_mainCard);
            ApplyCardStyle(_legendCard);
            ApplyCardStyle(_toggleBtnContainer);

            if (_dividerImage != null)
            {
                _dividerImage.color = _theme.dividerColor;
            }

            // 3. Typography
            if (_titleText != null) _titleText.color = _theme.textPrimary;
            if (_controlsHeaderText != null) _controlsHeaderText.color = _theme.textPrimary;
            if (_legendTitleText != null) _legendTitleText.color = _theme.textPrimary;

            if (_serverIpText != null) _serverIpText.color = _theme.textSecondary;
            if (_roomInfoText != null) _roomInfoText.color = _theme.textSecondary;
            if (_playerPosText != null) _playerPosText.color = _theme.textSecondary;

            if (_legendBodyTexts != null)
            {
                foreach (var t in _legendBodyTexts)
                {
                    if (t != null) t.color = _theme.textSecondary;
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
            if (img != null) img.color = _theme.cardBackground;

            Outline outline = cardGo.GetComponent<Outline>();
            if (outline != null) outline.effectColor = _theme.cardBorder;
        }

        private void ApplyButtonStyle(Button btn)
        {
            if (btn == null || _theme == null) return;

            Image img = btn.GetComponent<Image>();
            if (img != null) img.color = _theme.buttonNormal;

            Outline outline = btn.GetComponent<Outline>();
            if (outline != null) outline.effectColor = _theme.buttonBorder;

            var colors = btn.colors;
            colors.normalColor = _theme.buttonNormal;
            colors.highlightedColor = _theme.buttonHover;
            colors.pressedColor = _theme.buttonPressed;
            colors.selectedColor = _theme.buttonHover;
            btn.colors = colors;

            Text txt = btn.GetComponentInChildren<Text>();
            if (txt != null) txt.color = _theme.buttonText;
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
