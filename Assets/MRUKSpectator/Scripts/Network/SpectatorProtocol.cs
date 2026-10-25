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
        ConnectionAck = 3
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
                    rotation = new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                    timestamp = reader.ReadSingle()
                };
                return packet;
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
