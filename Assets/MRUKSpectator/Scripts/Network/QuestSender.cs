using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Meta.XR.MRUtilityKit;
using UnityEngine;

namespace MRUKSpectator.Network
{
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

        public bool IsConnected => _tcpClient != null && _tcpClient.Connected;
        public string TargetServerIp => _resolvedPcIp;
        public string StatusMessage { get; private set; } = "Initializing...";

        private string _resolvedPcIp = "";
        private TcpClient _tcpClient;
        private NetworkStream _networkStream;
        private Thread _discoveryThread;
        private Thread _networkWorkerThread;
        private volatile bool _shouldStop;
        private float _lastPoseTime;
        private string _cachedRoomJson = "";

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

            if (!string.IsNullOrEmpty(_manualPcIp))
            {
                _resolvedPcIp = _manualPcIp;
                StartClientThread();
            }
            else
            {
                StartDiscoveryThread();
            }
        }

        private void OnMRUKSceneLoaded()
        {
            Debug.Log("[QuestSender] MRUK Scene Loaded! Capturing room data...");
            CaptureAndSendRoom();
        }

        public void CaptureAndSendRoom()
        {
            try
            {
                if (MRUK.Instance == null) return;

                _cachedRoomJson = MRUK.Instance.SaveSceneToJsonString(false, (System.Collections.Generic.List<MRUKRoom>)null);
                Debug.Log($"[QuestSender] Serialized room JSON ({_cachedRoomJson.Length} bytes)");

                if (IsConnected)
                {
                    SendPacket(PacketType.RoomData, Encoding.UTF8.GetBytes(_cachedRoomJson));
                    StatusMessage = "Room Sent to PC";
                }
                else
                {
                    StatusMessage = "Room cached, waiting for PC connection...";
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[QuestSender] Error serializing room: {ex.Message}");
            }
        }

        private void Update()
        {
            if (!IsConnected || _headTransform == null) return;

            float interval = 1f / Mathf.Max(1f, _poseSendFrequency);
            if (Time.time - _lastPoseTime >= interval)
            {
                _lastPoseTime = Time.time;
                SendCurrentPose();
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

                        // Immediately transmit cached room if available
                        if (!string.IsNullOrEmpty(_cachedRoomJson))
                        {
                            SendPacket(PacketType.RoomData, Encoding.UTF8.GetBytes(_cachedRoomJson));
                        }
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

        private void Disconnect()
        {
            try { _networkStream?.Close(); } catch { }
            try { _tcpClient?.Close(); } catch { }
            _networkStream = null;
            _tcpClient = null;
            StatusMessage = "Disconnected. Reconnecting...";
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
