using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace MRUKSpectator.Network
{
    public enum PacketType : byte
    {
        Ping = 0,
        RoomData = 1,
        PoseData = 2,
        ConnectionAck = 3,
        AssignPlayer = 4,
        SpaceSharingData = 5,
        SpawnMarker = 10,
        DeleteMarker = 11,
        ClearMarkers = 12,
        RequestMarkers = 13
    }

    [Serializable]
    public struct AssignPlayerPacket
    {
        public string playerId;
        public Color32 playerColor;
        public byte role; // 0 = Host, 1 = Guest

        public static byte[] Serialize(AssignPlayerPacket packet)
        {
            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                writer.Write(packet.playerId ?? "Player_1");
                writer.Write(packet.playerColor.r);
                writer.Write(packet.playerColor.g);
                writer.Write(packet.playerColor.b);
                writer.Write(packet.playerColor.a);
                writer.Write(packet.role);
                return ms.ToArray();
            }
        }

        public static AssignPlayerPacket Deserialize(byte[] data)
        {
            using (var ms = new MemoryStream(data))
            using (var reader = new BinaryReader(ms))
            {
                return new AssignPlayerPacket
                {
                    playerId = reader.ReadString(),
                    playerColor = new Color32(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte()),
                    role = reader.ReadByte()
                };
            }
        }
    }

    [Serializable]
    public struct SpaceSharingDataPacket
    {
        public string groupUuid;
        public string alignmentRoomUuid;
        public Vector3 floorPosition;
        public Quaternion floorRotation;

        public static byte[] Serialize(SpaceSharingDataPacket packet)
        {
            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                writer.Write(packet.groupUuid ?? string.Empty);
                writer.Write(packet.alignmentRoomUuid ?? string.Empty);
                writer.Write(packet.floorPosition.x);
                writer.Write(packet.floorPosition.y);
                writer.Write(packet.floorPosition.z);
                writer.Write(packet.floorRotation.x);
                writer.Write(packet.floorRotation.y);
                writer.Write(packet.floorRotation.z);
                writer.Write(packet.floorRotation.w);
                return ms.ToArray();
            }
        }

        public static SpaceSharingDataPacket Deserialize(byte[] data)
        {
            using (var ms = new MemoryStream(data))
            using (var reader = new BinaryReader(ms))
            {
                return new SpaceSharingDataPacket
                {
                    groupUuid = reader.ReadString(),
                    alignmentRoomUuid = reader.ReadString(),
                    floorPosition = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                    floorRotation = PosePacket.ReadSafeQuaternion(reader)
                };
            }
        }
    }

    [Serializable]
    public struct PosePacket
    {
        public string playerId;
        public Vector3 position;
        public Quaternion rotation;
        public float timestamp;

        public static byte[] Serialize(PosePacket pose)
        {
            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                writer.Write(pose.playerId ?? "User1");
                writer.Write(pose.position.x);
                writer.Write(pose.position.y);
                writer.Write(pose.position.z);
                writer.Write(pose.rotation.x);
                writer.Write(pose.rotation.y);
                writer.Write(pose.rotation.z);
                writer.Write(pose.rotation.w);
                writer.Write(pose.timestamp);
                return ms.ToArray();
            }
        }

        public static PosePacket Deserialize(byte[] data)
        {
            using (var ms = new MemoryStream(data))
            using (var reader = new BinaryReader(ms))
            {
                PosePacket packet = new PosePacket
                {
                    playerId = reader.ReadString(),
                    position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                    rotation = ReadSafeQuaternion(reader),
                    timestamp = reader.ReadSingle()
                };
                return packet;
            }
        }

        internal static Quaternion ReadSafeQuaternion(BinaryReader reader)
        {
            float x = reader.ReadSingle();
            float y = reader.ReadSingle();
            float z = reader.ReadSingle();
            float w = reader.ReadSingle();
            float sqrMag = x * x + y * y + z * z + w * w;
            if (sqrMag < 0.0001f || float.IsNaN(sqrMag) || float.IsInfinity(sqrMag))
            {
                return Quaternion.identity;
            }
            float invMag = 1.0f / Mathf.Sqrt(sqrMag);
            return new Quaternion(x * invMag, y * invMag, z * invMag, w * invMag);
        }
    }

    [Serializable]
    public struct MarkerPacket
    {
        public string markerId;
        public Vector3 localPosition;
        public Quaternion localRotation;
        public Color32 color;
        public string label;
        public float scale;

        public static byte[] Serialize(MarkerPacket marker)
        {
            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                writer.Write(marker.markerId ?? Guid.NewGuid().ToString("N"));
                writer.Write(marker.localPosition.x);
                writer.Write(marker.localPosition.y);
                writer.Write(marker.localPosition.z);
                writer.Write(marker.localRotation.x);
                writer.Write(marker.localRotation.y);
                writer.Write(marker.localRotation.z);
                writer.Write(marker.localRotation.w);
                writer.Write(marker.color.r);
                writer.Write(marker.color.g);
                writer.Write(marker.color.b);
                writer.Write(marker.color.a);
                writer.Write(marker.label ?? "Marker");
                writer.Write(marker.scale > 0f ? marker.scale : 1f);
                return ms.ToArray();
            }
        }

        public static MarkerPacket Deserialize(byte[] data)
        {
            using (var ms = new MemoryStream(data))
            using (var reader = new BinaryReader(ms))
            {
                return new MarkerPacket
                {
                    markerId = reader.ReadString(),
                    localPosition = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                    localRotation = PosePacket.ReadSafeQuaternion(reader),
                    color = new Color32(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte()),
                    label = reader.ReadString(),
                    scale = reader.ReadSingle()
                };
            }
        }
    }

    public static class PacketFramer
    {
        public const int DefaultTcpPort = 9050;
        public const int DefaultUdpDiscoveryPort = 9051;
        public const string DiscoveryBeaconMessage = "MRUK_SPECTATOR_BEACON";

        public static byte[] WrapPacket(PacketType type, byte[] payload)
        {
            int payloadLength = payload != null ? payload.Length : 0;
            byte[] framed = new byte[5 + payloadLength];

            // 4-byte payload length (big endian)
            framed[0] = (byte)((payloadLength >> 24) & 0xFF);
            framed[1] = (byte)((payloadLength >> 16) & 0xFF);
            framed[2] = (byte)((payloadLength >> 8) & 0xFF);
            framed[3] = (byte)(payloadLength & 0xFF);

            // 1-byte packet type
            framed[4] = (byte)type;

            if (payloadLength > 0)
            {
                Buffer.BlockCopy(payload, 0, framed, 5, payloadLength);
            }

            return framed;
        }

        public static byte[] WrapString(PacketType type, string message)
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(message ?? string.Empty);
            return WrapPacket(type, utf8);
        }

        public static bool TryReadPacket(Stream stream, out PacketType type, out byte[] payload)
        {
            type = PacketType.Ping;
            payload = null;

            byte[] header = new byte[5];
            int read = ReadExact(stream, header, 0, 5);
            if (read < 5) return false;

            int payloadLength = (header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3];
            type = (PacketType)header[4];

            if (payloadLength < 0 || payloadLength > 10 * 1024 * 1024) // 10MB sanity cap
            {
                throw new InvalidDataException($"Invalid packet size: {payloadLength}");
            }

            payload = new byte[payloadLength];
            if (payloadLength > 0)
            {
                int payloadRead = ReadExact(stream, payload, 0, payloadLength);
                if (payloadRead < payloadLength) return false;
            }

            return true;
        }

        private static int ReadExact(Stream stream, byte[] buffer, int offset, int count)
        {
            int totalRead = 0;
            while (totalRead < count)
            {
                int read = stream.Read(buffer, offset + totalRead, count - totalRead);
                if (read <= 0) break;
                totalRead += read;
            }
            return totalRead;
        }
    }
}
