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
    /// uGUI-based Spectator HUD with modern styling, connection badges,
    /// live room/player stats, and interactive view buttons.
    /// </summary>
    public class SpectatorCanvasHUD : MonoBehaviour
    {
        [Header("Status & Info References")]
        [SerializeField] private Text _statusText;
        [SerializeField] private Image _statusDot;
        [SerializeField] private Text _serverIpText;
        [SerializeField] private Text _roomInfoText;
        [SerializeField] private Text _playerPosText;

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

        private static readonly Color ConnectedColor = new Color(0.2f, 0.9f, 0.45f, 1f);
        private static readonly Color WaitingColor = new Color(1f, 0.72f, 0.2f, 1f);
        private static readonly Color ButtonActiveBg = new Color(0.14f, 0.45f, 0.65f, 1f);
        private static readonly Color ButtonNormalBg = new Color(0.16f, 0.19f, 0.26f, 1f);

        private Image _imgFollowPlayer;

        private void Awake()
        {
            if (_btnFollowPlayer != null)
            {
                _imgFollowPlayer = _btnFollowPlayer.GetComponent<Image>();
            }

            SetupButtonListeners();
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

            if (clientCount > 0)
            {
                if (_statusText != null)
                {
                    _statusText.text = $"Quest Connected ({clientCount})";
                    _statusText.color = ConnectedColor;
                }
                if (_statusDot != null)
                {
                    _statusDot.color = ConnectedColor;
                }
            }
            else
            {
                if (_statusText != null)
                {
                    _statusText.text = "Waiting for Quest (LAN Beacon)...";
                    _statusText.color = WaitingColor;
                }
                if (_statusDot != null)
                {
                    _statusDot.color = WaitingColor;
                }
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
                _imgFollowPlayer.color = isFollowing ? ButtonActiveBg : ButtonNormalBg;
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
