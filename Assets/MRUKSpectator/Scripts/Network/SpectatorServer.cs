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
        public event Action<string> OnClientConnectedEvent;
        public event Action<string> OnClientDisconnectedEvent;

        public bool IsRunning { get; private set; }
        public int ConnectedClientCount => _clientThreads.Count;
        public string ServerIpAddress { get; private set; } = "127.0.0.1";

        private TcpListener _tcpListener;
        private Thread _listenerThread;
        private Thread _beaconThread;
        private volatile bool _shouldStop;

        private readonly ConcurrentDictionary<string, TcpClient> _clients = new();
        private readonly ConcurrentDictionary<string, Thread> _clientThreads = new();
        private readonly ConcurrentQueue<string> _roomJsonQueue = new();
        private readonly ConcurrentQueue<PosePacket> _poseQueue = new();
        private readonly ConcurrentQueue<string> _connectedEvents = new();
        private readonly ConcurrentQueue<string> _disconnectedEvents = new();

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

                while (!_shouldStop && client.Connected)
                {
                    try
                    {
                        if (PacketFramer.TryReadPacket(stream, out PacketType type, out byte[] payload))
                        {
                            switch (type)
                            {
                                case PacketType.RoomData:
                                    string json = Encoding.UTF8.GetString(payload);
                                    _roomJsonQueue.Enqueue(json);
                                    break;

                                case PacketType.PoseData:
                                    PosePacket pose = PosePacket.Deserialize(payload);
                                    _poseQueue.Enqueue(pose);
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
            _disconnectedEvents.Enqueue(endpoint);
            Debug.Log($"[SpectatorServer] Client disconnected: {endpoint}");
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
                        udp.Send(messageBytes, messageBytes.Length, broadcastEndpoint);
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
                        return ip.ToString();
                    }
                }
            }
            catch { }
            return "127.0.0.1";
        }
    }
}
