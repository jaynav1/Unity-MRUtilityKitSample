using System;
using System.Collections;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Meta.XR.MRUtilityKit;
using Meta.XR.EnvironmentDepth;
using UnityEngine;

namespace MRUKSpectator.Network
{
    public enum PlayerRole : byte
    {
        Host = 0,
        Guest = 1
    }

    public class QuestSender : MonoBehaviour
    {
        [Header("Connection Settings")]
        [Tooltip("If empty, auto-discovery via UDP beacon is used.")]
        [SerializeField] private string _manualPcIp = "";
        [SerializeField] private int _tcpPort = PacketFramer.DefaultTcpPort;
        [SerializeField] private int _udpDiscoveryPort = PacketFramer.DefaultUdpDiscoveryPort;
        [SerializeField] private float _poseSendFrequency = 60f; // Hz

        [Header("Tracking Reference")]
        [SerializeField] private Transform _headTransform;
        [SerializeField] private string _playerId = "Player_1";
        [SerializeField] private PlayerRole _role = PlayerRole.Host;
        [SerializeField] private float _standaloneTimeout = 4.0f;

        public static QuestSender Instance { get; private set; }

        public bool IsConnected => _tcpClient != null && _tcpClient.Connected;
        public string TargetServerIp => _resolvedPcIp;
        public string StatusMessage { get; private set; } = "Initializing...";
        public PlayerRole Role => _role;
        public string PlayerId => _playerId;

        public event Action<MarkerPacket> OnMarkerSpawnReceived;
        public event Action<string> OnMarkerDeleteReceived;
        public event Action OnMarkersClearReceived;
        public event Action<WhiteboardLinePacket> OnWhiteboardLineReceived;
        public event Action OnWhiteboardClearReceived;
        public event Action<WhiteboardPosePacket> OnWhiteboardPoseReceived;

        private string _resolvedPcIp = "";
        private TcpClient _tcpClient;
        private NetworkStream _networkStream;
        private Thread _discoveryThread;
        private Thread _networkWorkerThread;
        private volatile bool _shouldStop;
        private float _lastPoseTime;
        private string _cachedRoomJson = "";
        private SpaceSharingDataPacket? _cachedSpaceSharingPacket;

        private bool _hasRoleAssigned;
        private bool _hasLoadedSharedSpace;
        private bool _hasTriggeredHostLoad;
        private Color32 _playerColor = new Color32(0, 229, 255, 255);

        private readonly ConcurrentQueue<AssignPlayerPacket> _assignPlayerQueue = new();
        private readonly ConcurrentQueue<SpaceSharingDataPacket> _spaceSharingQueue = new();
        private readonly ConcurrentQueue<string> _fallbackRoomJsonQueue = new();
        private readonly ConcurrentQueue<MarkerPacket> _spawnMarkerQueue = new();
        private readonly ConcurrentQueue<string> _deleteMarkerQueue = new();
        private readonly ConcurrentQueue<bool> _clearMarkersQueue = new();
        private readonly ConcurrentQueue<WhiteboardLinePacket> _whiteboardLineQueue = new();
        private readonly ConcurrentQueue<bool> _whiteboardClearQueue = new();
        private readonly ConcurrentQueue<WhiteboardPosePacket> _whiteboardPoseQueue = new();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            // Ensure MRUK does not auto-prompt for scene capture before role assignment
            if (MRUK.Instance != null && MRUK.Instance.SceneSettings != null)
            {
                MRUK.Instance.SceneSettings.LoadSceneOnStartup = false;
            }

            if (!TryGetComponent<MRUKSpectator.Markers.SpectatorMarkerManager>(out _))
            {
                gameObject.AddComponent<MRUKSpectator.Markers.SpectatorMarkerManager>();
            }
        }

        private void Start()
        {
            if (_headTransform == null)
            {
                Camera cam = Camera.main;
                if (cam != null) _headTransform = cam.transform;
            }

            if (MRUK.Instance != null)
            {
                MRUK.Instance.RegisterSceneLoadedCallback(OnMRUKSceneLoaded);
            }

            // Suppress Guardian boundary in mixed reality so the user can roam freely
            if (OVRManager.instance != null)
            {
                OVRManager.instance.shouldBoundaryVisibilityBeSuppressed = true;
            }

            ConfigureHeadsetEffectsAndOcclusion();

            // Check for persistent config override or PlayerPrefs
            if (string.IsNullOrEmpty(_manualPcIp))
            {
                if (PlayerPrefs.HasKey("SpectatorServerIp"))
                {
                    _manualPcIp = PlayerPrefs.GetString("SpectatorServerIp");
                }

                string configPath = Path.Combine(Application.persistentDataPath, "spectator_server.txt");
                if (File.Exists(configPath))
                {
                    try
                    {
                        string fileIp = File.ReadAllText(configPath).Trim();
                        if (!string.IsNullOrEmpty(fileIp))
                        {
                            _manualPcIp = fileIp;
                            Debug.Log($"[QuestSender] Loaded manual PC IP from {configPath}: {_manualPcIp}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[QuestSender] Failed reading spectator_server.txt: {ex.Message}");
                    }
                }
            }

            if (!string.IsNullOrEmpty(_manualPcIp))
            {
                _resolvedPcIp = _manualPcIp;
                StartClientThread();
            }
            else
            {
                StartDiscoveryThread();
            }

            StartCoroutine(CheckStandaloneFallback());
        }

        private IEnumerator CheckStandaloneFallback()
        {
            yield return new WaitForSeconds(_standaloneTimeout);

            // If standalone (no PC connected and no role assigned), assume Host and load local room
            if (!_hasRoleAssigned && !IsConnected)
            {
                Debug.Log("[QuestSender] Standalone mode timeout: assuming Host and loading device room...");
                _role = PlayerRole.Host;
                _playerId = "Player_1";
                LoadLocalRoomAsHost();
            }
        }

        private void LoadLocalRoomAsHost()
        {
            if (_hasTriggeredHostLoad) return;
            _hasTriggeredHostLoad = true;

            if (MRUK.Instance != null && MRUK.Instance.GetCurrentRoom() == null)
            {
                StatusMessage = "Loading local room (Host)...";
                _ = LoadSceneFromDeviceAsync();
            }
        }

        private async Task LoadSceneFromDeviceAsync()
        {
            try
            {
                Debug.Log("[QuestSender] Host calling LoadSceneFromDevice(requestSceneCaptureIfNoDataFound: true)...");
                var result = await MRUK.Instance.LoadSceneFromDevice(requestSceneCaptureIfNoDataFound: true);
                Debug.Log($"[QuestSender] LoadSceneFromDevice finished with result: {result}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[QuestSender] LoadSceneFromDevice error: {ex.Message}");
            }
        }

        private void OnMRUKSceneLoaded()
        {
            Debug.Log($"[QuestSender] MRUK Scene Loaded! Role: {_role}");
            ConfigureHeadsetEffectsAndOcclusion();
            if (_role == PlayerRole.Host)
            {
                ShareAndSendHostRoom();
            }
        }

        private async void ShareAndSendHostRoom()
        {
            try
            {
                if (MRUK.Instance == null) return;
                MRUKRoom room = MRUK.Instance.GetCurrentRoom();
                if (room == null && MRUK.Instance.Rooms.Count > 0)
                {
                    room = MRUK.Instance.Rooms[0];
                }
                if (room == null)
                {
                    Debug.LogWarning("[QuestSender] No room available after MRUK scene load.");
                    return;
                }

                StatusMessage = "Sharing room to Meta Cloud...";
                Guid groupUuid = Guid.NewGuid();
                bool shareSucceeded = false;

                // 1. Meta Space Sharing via Cloud Anchors
                try
                {
                    var shareResult = await MRUK.Instance.ShareRoomsAsync(new[] { room }, groupUuid);
                    shareSucceeded = shareResult.Success;
                    Debug.Log($"[QuestSender] ShareRoomsAsync completed: Success={shareSucceeded}, Status={shareResult.Status}");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[QuestSender] ShareRoomsAsync exception: {ex.Message}");
                }

                // 2. Prepare Alignment Pose
                MRUKAnchor floorAnchor = room.FloorAnchors != null && room.FloorAnchors.Count > 0 ? room.FloorAnchors[0] : null;
                Pose floorPose = floorAnchor != null
                    ? new Pose(floorAnchor.transform.position, floorAnchor.transform.rotation)
                    : new Pose(room.transform.position, room.transform.rotation);

                SpaceSharingDataPacket sharePacket = new SpaceSharingDataPacket
                {
                    groupUuid = groupUuid.ToString(),
                    alignmentRoomUuid = room.Anchor.Uuid.ToString(),
                    floorPosition = floorPose.position,
                    floorRotation = floorPose.rotation
                };
                _cachedSpaceSharingPacket = sharePacket;

                // 3. Transmit SpaceSharingDataPacket to PC (which relays it to guest headsets)
                if (IsConnected)
                {
                    SendPacket(PacketType.SpaceSharingData, SpaceSharingDataPacket.Serialize(sharePacket));
                }

                // 4. Save and transmit Room JSON for PC Spectator rendering
                _cachedRoomJson = MRUK.Instance.SaveSceneToJsonString(false, (System.Collections.Generic.List<MRUKRoom>)null);
                Debug.Log($"[QuestSender] Host serialized room JSON ({_cachedRoomJson.Length} bytes)");

                if (IsConnected)
                {
                    SendPacket(PacketType.RoomData, Encoding.UTF8.GetBytes(_cachedRoomJson));
                    StatusMessage = shareSucceeded ? "Room Shared & Synced with PC!" : "Room Synced (Cloud Share fallback ready)";
                }
                else
                {
                    StatusMessage = "Room cached, waiting for PC connection...";
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[QuestSender] Error in ShareAndSendHostRoom: {ex.Message}");
            }
        }

        private void Update()
        {
            // Process packets dispatched to the Unity main thread
            while (_assignPlayerQueue.TryDequeue(out var assign))
            {
                HandleAssignPlayer(assign);
            }

            while (_spaceSharingQueue.TryDequeue(out var shareData))
            {
                HandleSpaceSharingReceived(shareData);
            }

            while (_fallbackRoomJsonQueue.TryDequeue(out var fallbackJson))
            {
                HandleFallbackRoomJson(fallbackJson);
            }

            while (_spawnMarkerQueue.TryDequeue(out var marker))
            {
                OnMarkerSpawnReceived?.Invoke(marker);
            }

            while (_deleteMarkerQueue.TryDequeue(out var markerId))
            {
                OnMarkerDeleteReceived?.Invoke(markerId);
            }

            while (_clearMarkersQueue.TryDequeue(out _))
            {
                OnMarkersClearReceived?.Invoke();
            }

            while (_whiteboardLineQueue.TryDequeue(out var line))
            {
                OnWhiteboardLineReceived?.Invoke(line);
            }

            while (_whiteboardClearQueue.TryDequeue(out _))
            {
                OnWhiteboardClearReceived?.Invoke();
            }

            while (_whiteboardPoseQueue.TryDequeue(out var pose))
            {
                OnWhiteboardPoseReceived?.Invoke(pose);
            }

            // Stream continuous 60Hz pose to PC
            if (!IsConnected || _headTransform == null) return;

            float interval = 1f / Mathf.Max(1f, _poseSendFrequency);
            if (Time.time - _lastPoseTime >= interval)
            {
                _lastPoseTime = Time.time;
                SendCurrentPose();
            }
        }

        private void HandleAssignPlayer(AssignPlayerPacket packet)
        {
            _hasRoleAssigned = true;
            _playerId = packet.playerId;
            _role = (PlayerRole)packet.role;
            _playerColor = packet.playerColor;

            Debug.Log($"[QuestSender] Role assigned by PC: {_role} (Player ID: {_playerId})");

            if (_role == PlayerRole.Host)
            {
                StatusMessage = $"Host ({_playerId}): Setting up space...";
                LoadLocalRoomAsHost();
            }
            else
            {
                StatusMessage = $"Guest ({_playerId}): Awaiting space from Host...";
                // Guest never calls LoadSceneFromDevice, preventing the room scan prompt!
            }
        }

        private async void HandleSpaceSharingReceived(SpaceSharingDataPacket shareData)
        {
            if (_role != PlayerRole.Guest || _hasLoadedSharedSpace) return;
            if (string.IsNullOrEmpty(shareData.groupUuid) || string.IsNullOrEmpty(shareData.alignmentRoomUuid)) return;

            _hasLoadedSharedSpace = true;
            StatusMessage = "Localizing in Host Space (Meta Colocation)...";
            Debug.Log($"[QuestSender] Guest loading shared room (Group UUID: {shareData.groupUuid}, Alignment UUID: {shareData.alignmentRoomUuid})...");

            try
            {
                Guid groupUuid = new Guid(shareData.groupUuid);
                Guid alignmentRoomUuid = new Guid(shareData.alignmentRoomUuid);
                Pose floorPose = new Pose(shareData.floorPosition, shareData.floorRotation);

                // Loads shared anchors from Meta Cloud and aligns local coordinates to Host with ZERO room scan dialog!
                var loadResult = await MRUK.Instance.LoadSceneFromSharedRooms(groupUuid, (alignmentRoomUuid, floorPose));
                if (loadResult == MRUK.LoadDeviceResult.Success)
                {
                    StatusMessage = "Colocated in Host Space!";
                    Debug.Log("[QuestSender] Successfully joined and localized shared room!");
                    ConfigureHeadsetEffectsAndOcclusion();
                }
                else
                {
                    Debug.LogWarning($"[QuestSender] LoadSceneFromSharedRooms returned {loadResult}. Awaiting fallback...");
                    _hasLoadedSharedSpace = false;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[QuestSender] Error loading shared room: {ex.Message}");
                _hasLoadedSharedSpace = false;
            }
        }

        private async void HandleFallbackRoomJson(string json)
        {
            // Only use JSON fallback if guest hasn't loaded any room yet
            if (_role != PlayerRole.Guest || _hasLoadedSharedSpace) return;
            if (MRUK.Instance == null || MRUK.Instance.GetCurrentRoom() != null) return;
            if (string.IsNullOrEmpty(json) || json.Length < 50) return;

            try
            {
                Debug.Log("[QuestSender] Guest loading space from Host JSON fallback...");
                await MRUK.Instance.LoadSceneFromJsonString(json);
                _hasLoadedSharedSpace = true;
                StatusMessage = "Space Loaded (JSON Fallback)";
                ConfigureHeadsetEffectsAndOcclusion();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[QuestSender] Fallback room JSON failed: {ex.Message}");
            }
        }

        private void SendCurrentPose()
        {
            try
            {
                MRUKRoom currentRoom = MRUK.Instance != null ? MRUK.Instance.GetCurrentRoom() : null;
                Transform roomTransform = currentRoom != null ? currentRoom.transform : null;

                Vector3 pos = roomTransform != null
                    ? roomTransform.InverseTransformPoint(_headTransform.position)
                    : _headTransform.position;

                Quaternion rot = roomTransform != null
                    ? Quaternion.Inverse(roomTransform.rotation) * _headTransform.rotation
                    : _headTransform.rotation;

                PosePacket pose = new PosePacket
                {
                    playerId = _playerId,
                    position = pos,
                    rotation = rot,
                    timestamp = Time.time
                };

                byte[] payload = PosePacket.Serialize(pose);
                SendPacket(PacketType.PoseData, payload);
            }
            catch (Exception)
            {
                // Network will handle reconnect on failure
            }
        }

        private void SendPacket(PacketType type, byte[] payload)
        {
            if (_networkStream == null || !IsConnected) return;

            try
            {
                byte[] framed = PacketFramer.WrapPacket(type, payload);
                _networkStream.Write(framed, 0, framed.Length);
            }
            catch (Exception)
            {
                Disconnect();
            }
        }

        private void StartDiscoveryThread()
        {
            StatusMessage = "Listening for PC broadcast...";
            _discoveryThread = new Thread(ListenForBeacon) { IsBackground = true };
            _discoveryThread.Start();
        }

        private void ListenForBeacon()
        {
            using (UdpClient udp = new UdpClient())
            {
                udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                udp.Client.ReceiveTimeout = 2500;
                udp.Client.Bind(new IPEndPoint(IPAddress.Any, _udpDiscoveryPort));

                IPEndPoint remoteEp = new IPEndPoint(IPAddress.Any, 0);

                while (!_shouldStop && string.IsNullOrEmpty(_resolvedPcIp))
                {
                    try
                    {
                        byte[] data = udp.Receive(ref remoteEp);
                        string msg = Encoding.UTF8.GetString(data);

                        if (msg.StartsWith(PacketFramer.DiscoveryBeaconMessage))
                        {
                            _resolvedPcIp = remoteEp.Address.ToString();
                            Debug.Log($"[QuestSender] Discovered PC at {_resolvedPcIp}");
                            break;
                        }
                    }
                    catch (SocketException)
                    {
                        // Normal receive timeout, continue listening
                    }
                    catch (Exception)
                    {
                        if (_shouldStop) break;
                    }
                }
            }

            if (!string.IsNullOrEmpty(_resolvedPcIp) && !_shouldStop)
            {
                StartClientThread();
            }
        }

        private void StartClientThread()
        {
            _networkWorkerThread = new Thread(ConnectAndMaintain) { IsBackground = true };
            _networkWorkerThread.Start();
        }

        private void ConnectAndMaintain()
        {
            while (!_shouldStop)
            {
                if (!IsConnected)
                {
                    try
                    {
                        StatusMessage = $"Connecting to PC ({_resolvedPcIp}:{_tcpPort})...";
                        _tcpClient = new TcpClient();
                        _tcpClient.NoDelay = true;
                        _tcpClient.Connect(_resolvedPcIp, _tcpPort);
                        _networkStream = _tcpClient.GetStream();

                        StatusMessage = "Connected to PC!";
                        Debug.Log("[QuestSender] Connected to PC Spectator Server!");

                        // If Host already prepared space sharing and room, send immediately
                        if (_role == PlayerRole.Host)
                        {
                            if (_cachedSpaceSharingPacket.HasValue)
                            {
                                SendPacket(PacketType.SpaceSharingData, SpaceSharingDataPacket.Serialize(_cachedSpaceSharingPacket.Value));
                            }

                            if (!string.IsNullOrEmpty(_cachedRoomJson))
                            {
                                SendPacket(PacketType.RoomData, Encoding.UTF8.GetBytes(_cachedRoomJson));
                            }
                        }

                        // Read incoming packets from PC
                        ReadIncomingPackets(_networkStream);
                    }
                    catch (Exception)
                    {
                        Disconnect();
                        Thread.Sleep(2000); // Retry interval
                        continue;
                    }
                }

                Thread.Sleep(100);
            }
        }

        private void ReadIncomingPackets(NetworkStream stream)
        {
            while (!_shouldStop && IsConnected && stream != null)
            {
                try
                {
                    if (PacketFramer.TryReadPacket(stream, out PacketType type, out byte[] payload))
                    {
                        switch (type)
                        {
                            case PacketType.AssignPlayer:
                                AssignPlayerPacket assignPacket = AssignPlayerPacket.Deserialize(payload);
                                _assignPlayerQueue.Enqueue(assignPacket);
                                break;

                            case PacketType.SpaceSharingData:
                                SpaceSharingDataPacket shareData = SpaceSharingDataPacket.Deserialize(payload);
                                _spaceSharingQueue.Enqueue(shareData);
                                break;

                            case PacketType.RoomData:
                                string roomJson = Encoding.UTF8.GetString(payload);
                                _fallbackRoomJsonQueue.Enqueue(roomJson);
                                break;

                            case PacketType.SpawnMarker:
                                MarkerPacket marker = MarkerPacket.Deserialize(payload);
                                _spawnMarkerQueue.Enqueue(marker);
                                break;

                            case PacketType.DeleteMarker:
                                string delId = Encoding.UTF8.GetString(payload);
                                _deleteMarkerQueue.Enqueue(delId);
                                break;

                            case PacketType.ClearMarkers:
                                _clearMarkersQueue.Enqueue(true);
                                break;

                            case PacketType.WhiteboardDrawLine:
                                WhiteboardLinePacket wbLine = WhiteboardLinePacket.Deserialize(payload);
                                _whiteboardLineQueue.Enqueue(wbLine);
                                break;

                            case PacketType.WhiteboardClear:
                                _whiteboardClearQueue.Enqueue(true);
                                break;

                            case PacketType.WhiteboardPose:
                                WhiteboardPosePacket wbPose = WhiteboardPosePacket.Deserialize(payload);
                                _whiteboardPoseQueue.Enqueue(wbPose);
                                break;
                        }
                    }
                    else
                    {
                        break;
                    }
                }
                catch (Exception)
                {
                    break;
                }
            }

            Disconnect();
        }

        private void Disconnect()
        {
            try { _networkStream?.Close(); } catch { }
            try { _tcpClient?.Close(); } catch { }
            _networkStream = null;
            _tcpClient = null;
            StatusMessage = "Disconnected. Reconnecting...";
        }

        public void SendWhiteboardLine(WhiteboardLinePacket line)
        {
            SendPacket(PacketType.WhiteboardDrawLine, WhiteboardLinePacket.Serialize(line));
        }

        public void SendWhiteboardClear()
        {
            SendPacket(PacketType.WhiteboardClear, Array.Empty<byte>());
        }

        public void SendWhiteboardPose(WhiteboardPosePacket pose)
        {
            SendPacket(PacketType.WhiteboardPose, WhiteboardPosePacket.Serialize(pose));
        }

        public void ConfigureHeadsetEffectsAndOcclusion()
        {
            // 1. Ensure EnvironmentDepthManager is active for real-time dynamic depth occlusion (hands, people, moving obstacles)
#if UNITY_ANDROID || UNITY_EDITOR
            try
            {
                var depthMgr = FindAnyObjectByType<EnvironmentDepthManager>();
                if (depthMgr == null)
                {
                    var depthGo = new GameObject("EnvironmentDepthManager");
                    depthMgr = depthGo.AddComponent<EnvironmentDepthManager>();
                    depthMgr.OcclusionShadersMode = OcclusionShadersMode.SoftOcclusion;
                    depthMgr.RemoveHands = false; // Keep real hands in depth buffer so hands occlude virtual walls
                    Debug.Log("[QuestSender] Enabled EnvironmentDepthManager for real-world dynamic occlusion.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[QuestSender] EnvironmentDepthManager initialization note: {ex.Message}");
            }
#endif

            // 2. Filter EffectMesh so ONLY walls and ceiling are rendered (no object outlines for furniture)
            var effectMeshes = FindObjectsByType<EffectMesh>();
            foreach (var em in effectMeshes)
            {
                if (em.gameObject.name == "FurnitureOccluder") continue;

                em.Labels = MRUKAnchor.SceneLabels.WALL_FACE |
                            MRUKAnchor.SceneLabels.CEILING |
                            MRUKAnchor.SceneLabels.INNER_WALL_FACE |
                            MRUKAnchor.SceneLabels.INVISIBLE_WALL_FACE;
                em.CutHoles = MRUKAnchor.SceneLabels.DOOR_FRAME | MRUKAnchor.SceneLabels.WINDOW_FRAME;
            }

            // 3. Create or ensure an InvisibleOccluder EffectMesh for scanned furniture
            // This ensures static real-world objects (tables, couches, desks, beds, etc.) write to the depth buffer at RenderQueue 2000
            // and physically block virtual wall lines that pass behind them.
            Transform occluderTransform = transform.Find("FurnitureOccluder");
            if (occluderTransform == null)
            {
                GameObject occluderGo = new GameObject("FurnitureOccluder");
                occluderGo.transform.SetParent(transform, false);
                EffectMesh occluderMesh = occluderGo.AddComponent<EffectMesh>();
                occluderMesh.SpawnOnStart = MRUK.RoomFilter.AllRooms;
                occluderMesh.Labels = MRUKAnchor.SceneLabels.TABLE |
                                      MRUKAnchor.SceneLabels.COUCH |
                                      MRUKAnchor.SceneLabels.STORAGE |
                                      MRUKAnchor.SceneLabels.BED |
                                      MRUKAnchor.SceneLabels.SCREEN |
                                      MRUKAnchor.SceneLabels.LAMP |
                                      MRUKAnchor.SceneLabels.PLANT |
                                      MRUKAnchor.SceneLabels.OTHER;

                Shader occluderShader = Shader.Find("Meta/MRUK/MixedReality/InvisibleOccluder");
                if (occluderShader != null)
                {
                    Material occluderMat = new Material(occluderShader);
                    occluderMat.renderQueue = 2000;
                    occluderMesh.MeshMaterial = occluderMat;
                }
            }
        }

        private void OnDestroy()
        {
            _shouldStop = true;
            Disconnect();
            try { _discoveryThread?.Abort(); } catch { }
            try { _networkWorkerThread?.Abort(); } catch { }
        }
    }
}
