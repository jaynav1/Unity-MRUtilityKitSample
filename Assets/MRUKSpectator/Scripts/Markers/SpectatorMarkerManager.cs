using System;
using System.Collections.Generic;
using System.Text;
using Meta.XR.MRUtilityKit;
using MRUKSpectator.Network;
using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MRUKSpectator.Markers
{
    /// <summary>
    /// Coordinates spatial marker placement on PC and synchronizes them across the LAN
    /// to all connected Meta Quest headsets.
    /// </summary>
    public class SpectatorMarkerManager : MonoBehaviour
    {
        public static SpectatorMarkerManager Instance { get; private set; }

        [Header("Marker Settings")]
        [SerializeField] private GameObject _customMarkerPrefab;
        [SerializeField] private Color _defaultMarkerColor = new Color(0.143f, 0.690f, 0.868f, 1f); // MRUK Cyan
        [SerializeField] private string _labelPrefix = "Marker";
        [SerializeField] private LayerMask _raycastLayers = ~0;

        [Header("Interaction State")]
        [SerializeField] private bool _placementModeActive = false;

        private readonly Dictionary<string, NetworkMarker> _markers = new();
        private int _markerIndexCounter = 1;

        public bool IsPlacementModeActive
        {
            get => _placementModeActive;
            set
            {
                _placementModeActive = value;
                OnPlacementModeChanged?.Invoke(value);
            }
        }

        public Color ActiveColor { get; set; }
        public int MarkerCount => _markers.Count;
        public IReadOnlyCollection<NetworkMarker> Markers => _markers.Values;

        public event Action<NetworkMarker> OnMarkerSpawned;
        public event Action<string> OnMarkerDeleted;
        public event Action OnMarkersCleared;
        public event Action<bool> OnPlacementModeChanged;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            ActiveColor = _defaultMarkerColor;
        }

        private void Start()
        {
            // Subscribe to PC Spectator Server events (if running on PC)
            if (SpectatorServer.Instance != null)
            {
                SpectatorServer.Instance.OnClientConnectedEvent += HandleClientConnected;
                SpectatorServer.Instance.OnMarkerSpawnRequested += HandleRemoteSpawn;
                SpectatorServer.Instance.OnMarkerDeleteRequested += HandleRemoteDelete;
                SpectatorServer.Instance.OnMarkersClearRequested += HandleRemoteClear;
            }

            // Subscribe to Quest Sender events (if running on Quest)
            if (QuestSender.Instance != null)
            {
                QuestSender.Instance.OnMarkerSpawnReceived += HandleRemoteSpawn;
                QuestSender.Instance.OnMarkerDeleteReceived += HandleRemoteDelete;
                QuestSender.Instance.OnMarkersClearReceived += HandleRemoteClear;
            }
        }

        private void OnDestroy()
        {
            if (SpectatorServer.Instance != null)
            {
                SpectatorServer.Instance.OnClientConnectedEvent -= HandleClientConnected;
                SpectatorServer.Instance.OnMarkerSpawnRequested -= HandleRemoteSpawn;
                SpectatorServer.Instance.OnMarkerDeleteRequested -= HandleRemoteDelete;
                SpectatorServer.Instance.OnMarkersClearRequested -= HandleRemoteClear;
            }

            if (QuestSender.Instance != null)
            {
                QuestSender.Instance.OnMarkerSpawnReceived -= HandleRemoteSpawn;
                QuestSender.Instance.OnMarkerDeleteReceived -= HandleRemoteDelete;
                QuestSender.Instance.OnMarkersClearReceived -= HandleRemoteClear;
            }
        }

        private void Update()
        {
            HandlePCInput();
        }

        private void HandlePCInput()
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            // Don't interact through UI elements
            bool isOverUI = false;
            try
            {
                if (EventSystem.current != null)
                {
                    isOverUI = EventSystem.current.IsPointerOverGameObject();
                }
            }
            catch (Exception)
            {
                // In case input modules are transitioning
            }
            if (isOverUI) return;

            bool placeModifier = false;
            bool leftClickDown = false;
            bool rightClickDown = false;
            Vector2 mouseScreenPos = Vector2.zero;
            bool hasPointerPos = false;

#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            var kb = Keyboard.current;
            if (mouse != null)
            {
                leftClickDown = mouse.leftButton.wasPressedThisFrame;
                rightClickDown = mouse.rightButton.wasPressedThisFrame;
                mouseScreenPos = mouse.position.ReadValue();
                hasPointerPos = true;
            }
            if (kb != null)
            {
                placeModifier = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            try
            {
                if (!hasPointerPos)
                {
                    mouseScreenPos = Input.mousePosition;
                    hasPointerPos = true;
                }
                if (!placeModifier)
                {
                    placeModifier = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                }
                if (!leftClickDown)
                {
                    leftClickDown = Input.GetMouseButtonDown(0);
                }
                if (!rightClickDown)
                {
                    rightClickDown = Input.GetMouseButtonDown(1);
                }
            }
            catch (InvalidOperationException)
            {
                // In case legacy input is disabled in Player Settings
            }
#endif

            if (!hasPointerPos) return;

            // Do not interact with markers if whiteboard is being grabbed
            if (MRUKSpectator.Whiteboard.WhiteboardDraggable.AnyWhiteboardGrabbed) return;

            // Left Click: Place Marker (if Placement Mode is on, or Shift is held)
            if ((_placementModeActive || placeModifier) && leftClickDown)
            {
                Ray ray = cam.ScreenPointToRay(mouseScreenPos);
                if (Physics.Raycast(ray, out RaycastHit hit, 100f, _raycastLayers))
                {
                    // Check if clicked an existing marker or whiteboard to avoid accidental placement
                    if (hit.collider.GetComponentInParent<NetworkMarker>() == null &&
                        hit.collider.GetComponentInParent<MRUKSpectator.Whiteboard.SpectatorWhiteboard>() == null &&
                        hit.collider.GetComponentInParent<MRUKSpectator.Whiteboard.WhiteboardDraggable>() == null)
                    {
                        SpawnMarkerAtWorldHit(hit);
                    }
                }
            }

            // Right Click: Add marker on face, or Delete marker if clicked directly on an existing marker
            if (rightClickDown)
            {
                Ray ray = cam.ScreenPointToRay(mouseScreenPos);
                if (Physics.Raycast(ray, out RaycastHit hit, 100f, _raycastLayers))
                {
                    NetworkMarker marker = hit.collider.GetComponentInParent<NetworkMarker>();
                    if (marker != null)
                    {
                        DeleteMarker(marker.MarkerId, broadcast: true);
                    }
                    else if (hit.collider.GetComponentInParent<MRUKSpectator.Whiteboard.SpectatorWhiteboard>() == null &&
                             hit.collider.GetComponentInParent<MRUKSpectator.Whiteboard.WhiteboardDraggable>() == null)
                    {
                        SpawnMarkerAtWorldHit(hit);
                    }
                }
            }
        }

        public void SpawnMarkerAtWorldHit(RaycastHit hit)
        {
            MRUKRoom room = MRUK.Instance != null ? MRUK.Instance.GetCurrentRoom() : null;
            if (room == null && MRUK.Instance != null && MRUK.Instance.Rooms.Count > 0)
            {
                room = MRUK.Instance.Rooms[0];
            }

            // Offset 2mm along the surface normal so the puck rests flush against the face
            Vector3 spawnWorldPos = hit.point + hit.normal * 0.002f;
            Vector3 localPos = room != null ? room.transform.InverseTransformPoint(spawnWorldPos) : spawnWorldPos;

            // Orient marker's local +Y (up) along the surface normal
            Quaternion worldRot = Quaternion.FromToRotation(Vector3.up, hit.normal);
            Quaternion localRot = room != null ? Quaternion.Inverse(room.transform.rotation) * worldRot : worldRot;

            // Strictly normalize rotation quaternion
            float sqr = localRot.x * localRot.x + localRot.y * localRot.y + localRot.z * localRot.z + localRot.w * localRot.w;
            if (sqr > 0.0001f && !float.IsNaN(sqr))
            {
                float inv = 1.0f / Mathf.Sqrt(sqr);
                localRot = new Quaternion(localRot.x * inv, localRot.y * inv, localRot.z * inv, localRot.w * inv);
            }
            else
            {
                localRot = Quaternion.identity;
            }

            string id = Guid.NewGuid().ToString("N").Substring(0, 8);
            string label = $"{_labelPrefix} {_markerIndexCounter++}";

            MarkerPacket packet = new MarkerPacket
            {
                markerId = id,
                localPosition = localPos,
                localRotation = localRot,
                color = ActiveColor,
                label = label,
                scale = 1.0f
            };

            // Spawn locally
            SpawnMarkerFromPacket(packet);

            // Broadcast to connected Quests
            if (SpectatorServer.Instance != null && SpectatorServer.Instance.IsRunning)
            {
                byte[] payload = MarkerPacket.Serialize(packet);
                SpectatorServer.Instance.BroadcastPacket(PacketType.SpawnMarker, payload);
            }
        }

        public NetworkMarker SpawnMarkerFromPacket(MarkerPacket packet)
        {
            if (_markers.TryGetValue(packet.markerId, out var existing))
            {
                existing.Initialize(packet);
                return existing;
            }

            GameObject go;
            if (_customMarkerPrefab != null)
            {
                go = Instantiate(_customMarkerPrefab);
            }
            else
            {
                go = new GameObject($"Marker_{packet.label}_{packet.markerId}");
            }

            NetworkMarker marker = go.GetComponent<NetworkMarker>();
            if (marker == null)
            {
                marker = go.AddComponent<NetworkMarker>();
            }

            marker.Initialize(packet);
            _markers[packet.markerId] = marker;

            Debug.Log($"[SpectatorMarkerManager] Spawned marker '{packet.label}' (ID: {packet.markerId}) at room-local {packet.localPosition}");
            OnMarkerSpawned?.Invoke(marker);
            return marker;
        }

        public void DeleteMarker(string markerId, bool broadcast = true)
        {
            if (_markers.TryGetValue(markerId, out var marker))
            {
                _markers.Remove(markerId);
                if (marker != null)
                {
                    Destroy(marker.gameObject);
                }

                Debug.Log($"[SpectatorMarkerManager] Deleted marker ID: {markerId}");
                OnMarkerDeleted?.Invoke(markerId);

                if (broadcast && SpectatorServer.Instance != null && SpectatorServer.Instance.IsRunning)
                {
                    byte[] payload = Encoding.UTF8.GetBytes(markerId);
                    SpectatorServer.Instance.BroadcastPacket(PacketType.DeleteMarker, payload);
                }
            }
        }

        public void ClearAllMarkers(bool broadcast = true)
        {
            foreach (var marker in _markers.Values)
            {
                if (marker != null)
                {
                    Destroy(marker.gameObject);
                }
            }
            _markers.Clear();
            _markerIndexCounter = 1;

            Debug.Log("[SpectatorMarkerManager] Cleared all markers.");
            OnMarkersCleared?.Invoke();

            if (broadcast && SpectatorServer.Instance != null && SpectatorServer.Instance.IsRunning)
            {
                SpectatorServer.Instance.BroadcastPacket(PacketType.ClearMarkers, new byte[0]);
            }
        }

        private void HandleRemoteSpawn(MarkerPacket packet)
        {
            SpawnMarkerFromPacket(packet);
        }

        private void HandleRemoteDelete(string markerId)
        {
            DeleteMarker(markerId, broadcast: false);
        }

        private void HandleRemoteClear()
        {
            ClearAllMarkers(broadcast: false);
        }

        private void HandleClientConnected(string clientEndpoint)
        {
            // Sync all existing markers to the newly connected headset
            if (SpectatorServer.Instance == null || !SpectatorServer.Instance.IsRunning) return;

            Debug.Log($"[SpectatorMarkerManager] Late-join sync: Sending {_markers.Count} markers to {clientEndpoint}...");
            foreach (var marker in _markers.Values)
            {
                if (marker != null)
                {
                    byte[] payload = MarkerPacket.Serialize(marker.ToPacket());
                    SpectatorServer.Instance.BroadcastPacket(PacketType.SpawnMarker, payload);
                }
            }
        }
    }
}
