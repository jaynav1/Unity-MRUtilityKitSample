using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace MRUKSpectator.Network
{
    public class SpectatorServer : MonoBehaviour
    {
        public static SpectatorServer Instance { get; private set; }

        [Header("Network Settings")]
        [SerializeField] private int _tcpPort = PacketFramer.DefaultTcpPort;
        [SerializeField] private int _udpDiscoveryPort = PacketFramer.DefaultUdpDiscoveryPort;
        [SerializeField] private bool _autoBroadcastDiscovery = true;

        public event Action<string> OnRoomJsonReceived;
        public event Action<PosePacket> OnPoseReceived;
        public event Action<SpaceSharingDataPacket> OnSpaceSharingDataReceived;
        public event Action<string> OnClientConnectedEvent;
        public event Action<string> OnClientDisconnectedEvent;
        public event Action<MarkerPacket> OnMarkerSpawnRequested;
        public event Action<string> OnMarkerDeleteRequested;
        public event Action OnMarkersClearRequested;
        public event Action<WhiteboardLinePacket> OnWhiteboardLineReceived;
        public event Action OnWhiteboardClearReceived;
        public event Action<WhiteboardPosePacket> OnWhiteboardPoseReceived;

        public bool IsRunning { get; private set; }
        public int ConnectedClientCount => _clientThreads.Count;
        public string ServerIpAddress { get; private set; } = "127.0.0.1";

        private TcpListener _tcpListener;
        private Thread _listenerThread;
        private Thread _beaconThread;
        private volatile bool _shouldStop;

        private string _hostEndpoint;
        private int _guestCounter = 1;
        private SpaceSharingDataPacket? _cachedSpaceSharingPacket;
        private string _cachedRoomJson;

        private readonly ConcurrentDictionary<string, TcpClient> _clients = new();
        private readonly ConcurrentDictionary<string, Thread> _clientThreads = new();
        private readonly ConcurrentDictionary<string, string> _clientPlayerIds = new();
        private readonly ConcurrentQueue<string> _roomJsonQueue = new();
        private readonly ConcurrentQueue<PosePacket> _poseQueue = new();
        private readonly ConcurrentQueue<SpaceSharingDataPacket> _spaceSharingQueue = new();
        private readonly ConcurrentQueue<string> _connectedEvents = new();
        private readonly ConcurrentQueue<string> _disconnectedEvents = new();
        private readonly ConcurrentQueue<MarkerPacket> _spawnMarkerQueue = new();
        private readonly ConcurrentQueue<string> _deleteMarkerQueue = new();
        private readonly ConcurrentQueue<bool> _clearMarkersQueue = new();
        private readonly ConcurrentQueue<WhiteboardLinePacket> _whiteboardLineQueue = new();
        private readonly ConcurrentQueue<bool> _whiteboardClearQueue = new();
        private readonly ConcurrentQueue<WhiteboardPosePacket> _whiteboardPoseQueue = new();

        private static readonly Color32[] PlayerColorPalette = new Color32[]
        {
            new Color32(0, 229, 255, 255),   // Cyan (Player 1 - Host)
            new Color32(255, 145, 0, 255),   // Orange (Player 2 - Guest 1)
            new Color32(213, 0, 249, 255),   // Magenta (Player 3 - Guest 2)
            new Color32(0, 230, 118, 255),   // Spring Green (Player 4 - Guest 3)
            new Color32(255, 214, 0, 255),   // Amber (Player 5)
            new Color32(255, 64, 129, 255)   // Pink (Player 6)
        };

        private static Color32 GetPlayerColor(int playerNumber)
        {
            int index = Mathf.Max(0, playerNumber - 1) % PlayerColorPalette.Length;
            return PlayerColorPalette[index];
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            ServerIpAddress = GetLocalIPAddress();
        }

        private void Start()
        {
            StartServer();
        }

        public void StartServer()
        {
            if (IsRunning) return;

            _shouldStop = false;
            try
            {
                _tcpListener = new TcpListener(IPAddress.Any, _tcpPort);
                _tcpListener.Start();
                IsRunning = true;

                _listenerThread = new Thread(ListenForClients) { IsBackground = true };
                _listenerThread.Start();

                if (_autoBroadcastDiscovery)
                {
                    _beaconThread = new Thread(BroadcastBeacon) { IsBackground = true };
                    _beaconThread.Start();
                }

                Debug.Log($"[SpectatorServer] Server started on IP {ServerIpAddress}:{_tcpPort}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SpectatorServer] Failed to start server: {ex.Message}");
            }
        }

        public void StopServer()
        {
            _shouldStop = true;
            IsRunning = false;

            try { _tcpListener?.Stop(); } catch { }

            foreach (var client in _clients.Values)
            {
                try { client.Close(); } catch { }
            }
            _clients.Clear();
            _clientThreads.Clear();
            _clientPlayerIds.Clear();
            _hostEndpoint = null;

            Debug.Log("[SpectatorServer] Server stopped.");
        }

        private void Update()
        {
            while (_connectedEvents.TryDequeue(out var clientInfo))
            {
                OnClientConnectedEvent?.Invoke(clientInfo);
            }

            while (_disconnectedEvents.TryDequeue(out var clientInfo))
            {
                OnClientDisconnectedEvent?.Invoke(clientInfo);
            }

            while (_roomJsonQueue.TryDequeue(out var roomJson))
            {
                OnRoomJsonReceived?.Invoke(roomJson);
            }

            while (_poseQueue.TryDequeue(out var pose))
            {
                OnPoseReceived?.Invoke(pose);
            }

            while (_spaceSharingQueue.TryDequeue(out var shareData))
            {
                OnSpaceSharingDataReceived?.Invoke(shareData);
            }

            while (_spawnMarkerQueue.TryDequeue(out var marker))
            {
                OnMarkerSpawnRequested?.Invoke(marker);
            }

            while (_deleteMarkerQueue.TryDequeue(out var markerId))
            {
                OnMarkerDeleteRequested?.Invoke(markerId);
            }

            while (_clearMarkersQueue.TryDequeue(out _))
            {
                OnMarkersClearRequested?.Invoke();
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
        }

        private void ListenForClients()
        {
            while (!_shouldStop)
            {
                try
                {
                    TcpClient client = _tcpListener.AcceptTcpClient();
                    string endpoint = client.Client.RemoteEndPoint?.ToString() ?? "Unknown";

                    _clients[endpoint] = client;
                    _connectedEvents.Enqueue(endpoint);

                    Thread clientThread = new Thread(() => HandleClient(endpoint, client)) { IsBackground = true };
                    _clientThreads[endpoint] = clientThread;
                    clientThread.Start();
                }
                catch (SocketException)
                {
                    if (_shouldStop) break;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[SpectatorServer] Client accept error: {ex.Message}");
                }
            }
        }

        private void HandleClient(string endpoint, TcpClient client)
        {
            Debug.Log($"[SpectatorServer] Client connected from {endpoint}");
            using (NetworkStream stream = client.GetStream())
            {
                // Send immediate ACK
                byte[] ack = PacketFramer.WrapPacket(PacketType.ConnectionAck, Encoding.UTF8.GetBytes("Welcome"));
                stream.Write(ack, 0, ack.Length);

                // Assign role and unique player ID
                string assignedPlayerId;
                byte assignedRole;
                Color32 assignedColor;

                if (string.IsNullOrEmpty(_hostEndpoint) || _hostEndpoint == endpoint)
                {
                    _hostEndpoint = endpoint;
                    assignedPlayerId = "Player_1";
                    assignedRole = 0; // Host
                    assignedColor = GetPlayerColor(1);
                    Debug.Log($"[SpectatorServer] Client {endpoint} assigned as HOST ({assignedPlayerId})");
                }
                else
                {
                    _guestCounter++;
                    assignedPlayerId = $"Player_{_guestCounter}";
                    assignedRole = 1; // Guest
                    assignedColor = GetPlayerColor(_guestCounter);
                    Debug.Log($"[SpectatorServer] Client {endpoint} assigned as GUEST ({assignedPlayerId})");
                }

                _clientPlayerIds[endpoint] = assignedPlayerId;

                // Send AssignPlayer packet
                AssignPlayerPacket assignPacket = new AssignPlayerPacket
                {
                    playerId = assignedPlayerId,
                    playerColor = assignedColor,
                    role = assignedRole
                };
                byte[] assignFramed = PacketFramer.WrapPacket(PacketType.AssignPlayer, AssignPlayerPacket.Serialize(assignPacket));
                stream.Write(assignFramed, 0, assignFramed.Length);

                // If this is a Guest and we already have cached SpaceSharingData, send it immediately
                if (assignedRole == 1 && _cachedSpaceSharingPacket.HasValue)
                {
                    byte[] shareFramed = PacketFramer.WrapPacket(PacketType.SpaceSharingData, SpaceSharingDataPacket.Serialize(_cachedSpaceSharingPacket.Value));
                    stream.Write(shareFramed, 0, shareFramed.Length);
                    Debug.Log($"[SpectatorServer] Forwarded cached SpaceSharingData to guest {endpoint}");
                }

                // If cached room JSON is available, send to guest as fallback
                if (assignedRole == 1 && !string.IsNullOrEmpty(_cachedRoomJson))
                {
                    byte[] roomFramed = PacketFramer.WrapPacket(PacketType.RoomData, Encoding.UTF8.GetBytes(_cachedRoomJson));
                    stream.Write(roomFramed, 0, roomFramed.Length);
                }

                while (!_shouldStop && client.Connected)
                {
                    try
                    {
                        if (PacketFramer.TryReadPacket(stream, out PacketType type, out byte[] payload))
                        {
                            switch (type)
                            {
                                case PacketType.SpaceSharingData:
                                    SpaceSharingDataPacket shareData = SpaceSharingDataPacket.Deserialize(payload);
                                    _cachedSpaceSharingPacket = shareData;
                                    _spaceSharingQueue.Enqueue(shareData);
                                    Debug.Log($"[SpectatorServer] Received SpaceSharingData (Group UUID: {shareData.groupUuid}). Forwarding to guests...");
                                    BroadcastPacketExcept(endpoint, PacketType.SpaceSharingData, payload);
                                    break;

                                case PacketType.RoomData:
                                    string json = Encoding.UTF8.GetString(payload);
                                    if (_hostEndpoint == endpoint || string.IsNullOrEmpty(_cachedRoomJson))
                                    {
                                        _cachedRoomJson = json;
                                        _roomJsonQueue.Enqueue(json);
                                        BroadcastPacketExcept(endpoint, PacketType.RoomData, payload);
                                    }
                                    break;

                                case PacketType.PoseData:
                                    PosePacket pose = PosePacket.Deserialize(payload);
                                    // Ensure pose is tagged with the assigned player ID
                                    if (string.IsNullOrEmpty(pose.playerId) || pose.playerId == "Player_1")
                                    {
                                        if (_clientPlayerIds.TryGetValue(endpoint, out var actualId))
                                        {
                                            pose.playerId = actualId;
                                        }
                                    }
                                    _poseQueue.Enqueue(pose);
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
                                    BroadcastPacketExcept(endpoint, PacketType.WhiteboardDrawLine, payload);
                                    break;

                                case PacketType.WhiteboardClear:
                                    _whiteboardClearQueue.Enqueue(true);
                                    BroadcastPacketExcept(endpoint, PacketType.WhiteboardClear, payload);
                                    break;

                                case PacketType.WhiteboardPose:
                                    WhiteboardPosePacket wbPose = WhiteboardPosePacket.Deserialize(payload);
                                    _whiteboardPoseQueue.Enqueue(wbPose);
                                    BroadcastPacketExcept(endpoint, PacketType.WhiteboardPose, payload);
                                    break;

                                case PacketType.Ping:
                                    // Keepalive
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
            }

            _clients.TryRemove(endpoint, out _);
            _clientThreads.TryRemove(endpoint, out _);
            _clientPlayerIds.TryRemove(endpoint, out _);
            if (_hostEndpoint == endpoint)
            {
                _hostEndpoint = null;
            }
            _disconnectedEvents.Enqueue(endpoint);
            Debug.Log($"[SpectatorServer] Client disconnected: {endpoint}");
        }

        public void SendPacketToClient(TcpClient client, PacketType type, byte[] payload)
        {
            if (client == null || !client.Connected) return;
            try
            {
                byte[] framed = PacketFramer.WrapPacket(type, payload);
                var stream = client.GetStream();
                stream.Write(framed, 0, framed.Length);
            }
            catch { }
        }

        public void BroadcastPacket(PacketType type, byte[] payload)
        {
            byte[] framed = PacketFramer.WrapPacket(type, payload);
            foreach (var client in _clients.Values)
            {
                try
                {
                    if (client != null && client.Connected)
                    {
                        var stream = client.GetStream();
                        stream.Write(framed, 0, framed.Length);
                    }
                }
                catch { }
            }
        }

        public void BroadcastPacketExcept(string exceptEndpoint, PacketType type, byte[] payload)
        {
            byte[] framed = PacketFramer.WrapPacket(type, payload);
            foreach (var kvp in _clients)
            {
                if (kvp.Key == exceptEndpoint) continue;
                try
                {
                    if (kvp.Value != null && kvp.Value.Connected)
                    {
                        var stream = kvp.Value.GetStream();
                        stream.Write(framed, 0, framed.Length);
                    }
                }
                catch { }
            }
        }

        public void SendWhiteboardLine(WhiteboardLinePacket line)
        {
            BroadcastPacket(PacketType.WhiteboardDrawLine, WhiteboardLinePacket.Serialize(line));
        }

        public void SendWhiteboardClear()
        {
            BroadcastPacket(PacketType.WhiteboardClear, Array.Empty<byte>());
        }

        public void SendWhiteboardPose(WhiteboardPosePacket pose)
        {
            BroadcastPacket(PacketType.WhiteboardPose, WhiteboardPosePacket.Serialize(pose));
        }

        private void BroadcastBeacon()
        {
            using (UdpClient udp = new UdpClient())
            {
                udp.EnableBroadcast = true;
                IPEndPoint broadcastEndpoint = new IPEndPoint(IPAddress.Broadcast, _udpDiscoveryPort);
                byte[] messageBytes = Encoding.UTF8.GetBytes($"{PacketFramer.DiscoveryBeaconMessage}|{_tcpPort}");

                while (!_shouldStop)
                {
                    try
                    {
                        // 1. General broadcast (255.255.255.255)
                        udp.Send(messageBytes, messageBytes.Length, broadcastEndpoint);

                        // 2. Subnet-directed broadcast on every active adapter (e.g. Wi-Fi)
                        foreach (var iface in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                        {
                            if (iface.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                            if (iface.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;

                            foreach (var uni in iface.GetIPProperties().UnicastAddresses)
                            {
                                if (uni.Address.AddressFamily == AddressFamily.InterNetwork && uni.IPv4Mask != null)
                                {
                                    byte[] ipBytes = uni.Address.GetAddressBytes();
                                    byte[] maskBytes = uni.IPv4Mask.GetAddressBytes();
                                    if (ipBytes.Length == 4 && maskBytes.Length == 4)
                                    {
                                        byte[] bcastBytes = new byte[4];
                                        for (int i = 0; i < 4; i++)
                                        {
                                            bcastBytes[i] = (byte)(ipBytes[i] | ~maskBytes[i]);
                                        }
                                        IPEndPoint ifaceBcast = new IPEndPoint(new IPAddress(bcastBytes), _udpDiscoveryPort);
                                        udp.Send(messageBytes, messageBytes.Length, ifaceBcast);
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception) { }

                    Thread.Sleep(1500);
                }
            }
        }

        private void OnDestroy()
        {
            StopServer();
        }

        public static string GetLocalIPAddress()
        {
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                    {
                        string str = ip.ToString();
                        if (!str.StartsWith("169.254.")) // Ignore APIPA / unconfigured interfaces
                        {
                            return str;
                        }
                    }
                }
            }
            catch { }
            return "127.0.0.1";
        }
    }
}
