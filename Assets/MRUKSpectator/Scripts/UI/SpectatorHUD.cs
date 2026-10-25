using Meta.XR.MRUtilityKit;
using MRUKSpectator.CameraController;
using MRUKSpectator.Network;
using MRUKSpectator.Visualization;
using UnityEngine;

namespace MRUKSpectator.UI
{
    public class SpectatorHUD : MonoBehaviour
    {
        [Header("Appearance")]
        [SerializeField] private bool _showControlsHint = true;

        private GUIStyle _panelStyle;
        private GUIStyle _headerStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _statusBadgeStyle;
        private bool _stylesInitialized;

        private void InitStyles()
        {
            if (_stylesInitialized) return;

            Texture2D darkBg = MakeTex(2, 2, new Color(0.08f, 0.09f, 0.12f, 0.88f));
            Texture2D buttonNormal = MakeTex(2, 2, new Color(0.18f, 0.22f, 0.28f, 0.95f));
            Texture2D buttonHover = MakeTex(2, 2, new Color(0.25f, 0.35f, 0.50f, 1.0f));

            _panelStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = darkBg },
                padding = new RectOffset(16, 16, 14, 14)
            };

            _headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.9f, 0.95f, 1.0f) }
            };

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                normal = { textColor = new Color(0.8f, 0.82f, 0.85f) }
            };

            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                normal = { background = buttonNormal, textColor = Color.white },
                hover = { background = buttonHover, textColor = Color.white },
                margin = new RectOffset(2, 2, 4, 4),
                padding = new RectOffset(10, 10, 6, 6)
            };

            _statusBadgeStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.2f, 0.85f, 0.4f) }
            };

            _stylesInitialized = true;
        }

        private void OnGUI()
        {
            InitStyles();

            // Main Info & Control Panel (Top Left)
            GUILayout.BeginArea(new Rect(20, 20, 360, 420), _panelStyle);
            {
                GUILayout.Label("MRUK 3D Room Spectator", _headerStyle);
                GUILayout.Space(6);

                // Connection info
                string serverIp = SpectatorServer.Instance != null ? SpectatorServer.Instance.ServerIpAddress : "127.0.0.1";
                int clientCount = SpectatorServer.Instance != null ? SpectatorServer.Instance.ConnectedClientCount : 0;

                GUILayout.Label($"Server IP: {serverIp}:{PacketFramer.DefaultTcpPort}", _labelStyle);

                if (clientCount > 0)
                {
                    _statusBadgeStyle.normal.textColor = new Color(0.2f, 0.9f, 0.4f);
                    GUILayout.Label($"Status: Quest Connected ({clientCount} active)", _statusBadgeStyle);
                }
                else
                {
                    _statusBadgeStyle.normal.textColor = new Color(1.0f, 0.75f, 0.2f);
                    GUILayout.Label("Status: Waiting for Quest (Broadcasting UDP beacon)...", _statusBadgeStyle);
                }

                GUILayout.Space(8);

                // Room info
                bool hasRoom = SpectatorRoomManager.Instance != null && SpectatorRoomManager.Instance.HasRoomLoaded;
                if (hasRoom)
                {
                    var mgr = SpectatorRoomManager.Instance;
                    GUILayout.Label($"Room: Loaded ({mgr.AnchorCount} anchors)", _labelStyle);
                    GUILayout.Label($"Size: {mgr.RoomSize.x:F1}m x {mgr.RoomSize.z:F1}m (H: {mgr.RoomSize.y:F1}m)", _labelStyle);
                }
                else
                {
                    GUILayout.Label("Room: Waiting for room scan from Quest...", _labelStyle);
                }

                // Player Pose
                var player = SpectatorRoomManager.Instance != null ? SpectatorRoomManager.Instance.GetPrimaryPlayer() : null;
                if (player != null)
                {
                    Vector3 pos = player.CurrentPosition;
                    GUILayout.Label($"Player Pos: X: {pos.x:F2}m | Y: {pos.y:F2}m | Z: {pos.z:F2}m", _labelStyle);
                }

                GUILayout.Space(12);
                GUILayout.Box("", GUILayout.Height(1)); // separator
                GUILayout.Space(8);

                // Action Controls
                GUILayout.Label("Camera Views & Options", _headerStyle);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Top-Down (Bird's Eye)", _buttonStyle, GUILayout.Height(30)))
                {
                    OrbitCameraController.Instance?.SetTopDownView();
                }
                if (GUILayout.Button("Isometric 3D", _buttonStyle, GUILayout.Height(30)))
                {
                    OrbitCameraController.Instance?.SetIsometricView();
                }
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Focus Room", _buttonStyle, GUILayout.Height(30)))
                {
                    MRUKRoom currentRoom = Meta.XR.MRUtilityKit.MRUK.Instance?.GetCurrentRoom();
                    if (currentRoom != null && SpectatorRoomManager.Instance != null)
                    {
                        SpectatorRoomManager.Instance.ApplyRoomStyling(currentRoom);
                    }
                }

                bool isFollowing = OrbitCameraController.Instance != null && OrbitCameraController.Instance.IsFollowingPlayer;
                string followText = isFollowing ? "Following Player: ON" : "Follow Player";
                if (GUILayout.Button(followText, _buttonStyle, GUILayout.Height(30)))
                {
                    if (player != null)
                    {
                        OrbitCameraController.Instance?.ToggleFollowPlayer(player.transform);
                    }
                }
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                bool ceilingHidden = SpectatorRoomManager.Instance != null && SpectatorRoomManager.Instance.IsCeilingHidden;
                string ceilingText = ceilingHidden ? "Show Ceiling" : "Hide Ceiling (Dollhouse)";
                if (GUILayout.Button(ceilingText, _buttonStyle, GUILayout.Height(30)))
                {
                    SpectatorRoomManager.Instance?.ToggleCeiling();
                }

                if (GUILayout.Button("Clear Trail", _buttonStyle, GUILayout.Height(30)))
                {
                    player?.ClearTrail();
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndArea();

            // Controls Hint Box (Bottom Right)
            if (_showControlsHint)
            {
                GUILayout.BeginArea(new Rect(Screen.width - 280, Screen.height - 130, 260, 110), _panelStyle);
                {
                    GUILayout.Label("Orbit Controls Legend", _headerStyle);
                    GUILayout.Label("Left Click + Drag: Orbit 3D Room", _labelStyle);
                    GUILayout.Label("Middle Click / Shift+Right: Pan", _labelStyle);
                    GUILayout.Label("Mouse Scroll: Zoom In / Out", _labelStyle);
                }
                GUILayout.EndArea();
            }
        }

        private Texture2D MakeTex(int width, int height, Color col)
        {
            Color[] pix = new Color[width * height];
            for (int i = 0; i < pix.Length; ++i) pix[i] = col;
            Texture2D result = new Texture2D(width, height);
            result.SetPixels(pix);
            result.Apply();
            return result;
        }
    }
}
