using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Meta.XR.MRUtilityKit;
using UnityEngine;

namespace MRUKSpectator.Network
{
    /// <summary>
    /// Coordinates local LAN multiplayer colocation between PC Spectator and Meta Quest headsets.
    /// Bridges the MRUK physical room anchor coordinate space with networked objects,
    /// and handles automatic UDP discovery so devices pair without manual IP entry.
    /// </summary>
    public class SpectatorNGOColocation : MonoBehaviour
    {
        public static SpectatorNGOColocation Instance { get; private set; }

        public enum NetworkRole
        {
            AutoDetect,
            HostPC,
            ClientHeadset
        }

        [Header("Colocation Settings")]
        [SerializeField] private NetworkRole _role = NetworkRole.AutoDetect;
        [SerializeField] private int _defaultPort = 7777;
        [SerializeField] private int _discoveryPort = 9051;
        [SerializeField] private string _discoveryBeacon = "MRUK_COLOCATION_BEACON";

        [Header("Room Alignment")]
        [Tooltip("If enabled, automatically offsets all spawned objects so their origin matches the physical MRUK room anchor.")]
        [SerializeField] private bool _autoAlignToMRUKRoom = true;

        public bool IsColocated { get; private set; }
        public string DiscoveredServerIp { get; private set; } = "";
        public string StatusMessage { get; private set; } = "Ready";

        private Thread _discoveryThread;
        private volatile bool _shouldStop;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            if (_role == NetworkRole.AutoDetect)
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                _role = NetworkRole.ClientHeadset;
#else
                _role = NetworkRole.HostPC;
#endif
            }
        }

        private void Start()
        {
            if (_role == NetworkRole.HostPC)
            {
                StartHostDiscoveryBeacon();
            }
            else
            {
                StartClientDiscoveryListener();
            }
        }

        /// <summary>
        /// Converts a world-space point into physical room-local space.
        /// Transmitted across network to guarantee all devices align to the same real-world surface.
        /// </summary>
        public Vector3 WorldToRoomLocal(Vector3 worldPoint)
        {
            MRUKRoom room = GetActiveRoom();
            return room != null ? room.transform.InverseTransformPoint(worldPoint) : worldPoint;
        }

        /// <summary>
        /// Converts a room-local point into headset/spectator world space.
        /// </summary>
        public Vector3 RoomLocalToWorld(Vector3 roomLocalPoint)
        {
            MRUKRoom room = GetActiveRoom();
            return room != null ? room.transform.TransformPoint(roomLocalPoint) : roomLocalPoint;
        }

        /// <summary>
        /// Converts a world rotation into room-local rotation.
        /// </summary>
        public Quaternion WorldToRoomRotation(Quaternion worldRot)
        {
            MRUKRoom room = GetActiveRoom();
            return room != null ? Quaternion.Inverse(room.transform.rotation) * worldRot : worldRot;
        }

        /// <summary>
        /// Converts a room-local rotation into world rotation.
        /// </summary>
        public Quaternion RoomLocalToWorldRotation(Quaternion roomLocalRot)
        {
            MRUKRoom room = GetActiveRoom();
            return room != null ? room.transform.rotation * roomLocalRot : roomLocalRot;
        }

        private MRUKRoom GetActiveRoom()
        {
            if (!_autoAlignToMRUKRoom || MRUK.Instance == null) return null;
            MRUKRoom room = MRUK.Instance.GetCurrentRoom();
            if (room == null && MRUK.Instance.Rooms.Count > 0)
            {
                room = MRUK.Instance.Rooms[0];
            }
            return room;
        }

        private void StartHostDiscoveryBeacon()
        {
            StatusMessage = "Broadcasting LAN beacon...";
            _discoveryThread = new Thread(() =>
            {
                using (var udp = new UdpClient())
                {
                    udp.EnableBroadcast = true;
                    var ep = new IPEndPoint(IPAddress.Broadcast, _discoveryPort);
                    byte[] bytes = Encoding.UTF8.GetBytes($"{_discoveryBeacon}|{_defaultPort}");

                    while (!_shouldStop)
                    {
                        try
                        {
                            udp.Send(bytes, bytes.Length, ep);
                        }
                        catch { }
                        Thread.Sleep(1500);
                    }
                }
            })
            { IsBackground = true };
            _discoveryThread.Start();
        }

        private void StartClientDiscoveryListener()
        {
            StatusMessage = "Searching for PC host on Wi-Fi...";
            _discoveryThread = new Thread(() =>
            {
                using (var udp = new UdpClient())
                {
                    udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    udp.Client.Bind(new IPEndPoint(IPAddress.Any, _discoveryPort));
                    var ep = new IPEndPoint(IPAddress.Any, 0);

                    while (!_shouldStop && string.IsNullOrEmpty(DiscoveredServerIp))
                    {
                        try
                        {
                            byte[] data = udp.Receive(ref ep);
                            string msg = Encoding.UTF8.GetString(data);
                            if (msg.StartsWith(_discoveryBeacon))
                            {
                                DiscoveredServerIp = ep.Address.ToString();
                                StatusMessage = $"Found Host at {DiscoveredServerIp}";
                                Debug.Log($"[SpectatorNGOColocation] Discovered Host PC: {DiscoveredServerIp}");
                                break;
                            }
                        }
                        catch { }
                    }
                }
            })
            { IsBackground = true };
            _discoveryThread.Start();
        }

        private void OnDestroy()
        {
            _shouldStop = true;
            try { _discoveryThread?.Abort(); } catch { }
        }
    }
}
