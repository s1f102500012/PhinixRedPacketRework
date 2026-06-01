using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using Utils.Framework;

namespace Natsuki.PhinixRedPacketRework
{
    public static class RedPacketProtocol
    {
        public const string Capability = "natsuki.redpacket";
        public const string SnapshotType = "redpacket.snapshot";
        public const string CreateRequestType = "redpacket.create.request";
        public const string ClaimRequestType = "redpacket.claim.request";
        public const string ClaimEventType = "redpacket.claim.event";
        public const string FailureEventType = "redpacket.failure.event";

        public static FrameworkPacket CreateCommand(string messageType, string sessionId, string senderUuid, object payload)
        {
            FrameworkPacket packet = new FrameworkPacket
            {
                Flow = global::Phinix.Framework.FrameworkFlow.Command,
                CommandKind = global::Phinix.Framework.FrameworkCommandKind.Request,
                MessageType = messageType,
                MessageId = Guid.NewGuid().ToString(),
                SessionId = sessionId,
                SenderUuid = senderUuid,
                PayloadJson = payload == null ? string.Empty : FrameworkSerialization.SerializePayload(payload)
            };
            packet.SetCorrelationId(packet.MessageId);
            return packet;
        }

        public static FrameworkPacket CreateServerCommand(
            string messageType,
            global::Phinix.Framework.FrameworkCommandKind commandKind,
            string sessionId,
            object payload,
            string correlationId = null)
        {
            FrameworkPacket packet = new FrameworkPacket
            {
                Flow = global::Phinix.Framework.FrameworkFlow.Command,
                CommandKind = commandKind,
                MessageType = messageType,
                MessageId = Guid.NewGuid().ToString(),
                SessionId = sessionId,
                SenderUuid = FrameworkProtocol.SystemSenderUuid,
                PayloadJson = payload == null ? string.Empty : FrameworkSerialization.SerializePayload(payload)
            };
            packet.SetCorrelationId(correlationId ?? packet.MessageId);
            return packet;
        }
    }

    [DataContract]
    public enum RedPacketKind
    {
        [EnumMember]
        Normal = 0,
        [EnumMember]
        Lucky = 1
    }

    [DataContract]
    public enum RedPacketFailureReason
    {
        [EnumMember]
        None = 0,
        [EnumMember]
        ServerExtensionMissing = 1,
        [EnumMember]
        InvalidRequest = 2,
        [EnumMember]
        PacketNotFound = 3,
        [EnumMember]
        PacketExpired = 4,
        [EnumMember]
        AlreadyClaimed = 5,
        [EnumMember]
        SenderCannotClaim = 6,
        [EnumMember]
        Empty = 7
    }

    [DataContract]
    public sealed class RedPacketItemSnapshot
    {
        [DataMember(Order = 0)]
        public string DefName { get; set; }

        [DataMember(Order = 1)]
        public string StuffDefName { get; set; }

        [DataMember(Order = 2)]
        public int Quality { get; set; }

        [DataMember(Order = 3)]
        public int HitPoints { get; set; }

        [DataMember(Order = 4)]
        public int Count { get; set; }
    }

    [DataContract]
    public sealed class RedPacketClaimSnapshot
    {
        [DataMember(Order = 0)]
        public string ClaimerUuid { get; set; }

        [DataMember(Order = 1)]
        public int Amount { get; set; }

        [DataMember(Order = 2)]
        public long ClaimedAtUtcTicks { get; set; }
    }

    [DataContract]
    public sealed class RedPacketStateSnapshot
    {
        [DataMember(Order = 0)]
        public string PacketId { get; set; }

        [DataMember(Order = 1)]
        public string SenderUuid { get; set; }

        [DataMember(Order = 2)]
        public string SenderDisplayName { get; set; }

        [DataMember(Order = 3)]
        public RedPacketItemSnapshot Item { get; set; }

        [DataMember(Order = 4)]
        public int TotalCount { get; set; }

        [DataMember(Order = 5)]
        public int RemainingCount { get; set; }

        [DataMember(Order = 6)]
        public int TotalPackets { get; set; }

        [DataMember(Order = 7)]
        public int RemainingPackets { get; set; }

        [DataMember(Order = 8)]
        public RedPacketKind Kind { get; set; }

        [DataMember(Order = 9)]
        public long CreatedAtUtcTicks { get; set; }

        [DataMember(Order = 10)]
        public long ExpiresAtUtcTicks { get; set; }

        [DataMember(Order = 11)]
        public long CompletedAtUtcTicks { get; set; }

        [DataMember(Order = 12)]
        public bool Expired { get; set; }

        [DataMember(Order = 13)]
        public List<RedPacketClaimSnapshot> Claims { get; set; } = new List<RedPacketClaimSnapshot>();
    }

    [DataContract]
    public sealed class RedPacketSnapshotPayload
    {
        [DataMember(Order = 0)]
        public List<RedPacketStateSnapshot> Packets { get; set; } = new List<RedPacketStateSnapshot>();
    }

    [DataContract]
    public sealed class RedPacketCreateRequest
    {
        [DataMember(Order = 0)]
        public string PacketId { get; set; }

        [DataMember(Order = 1)]
        public string SenderDisplayName { get; set; }

        [DataMember(Order = 2)]
        public RedPacketItemSnapshot Item { get; set; }

        [DataMember(Order = 3)]
        public int PacketCount { get; set; }

        [DataMember(Order = 4)]
        public RedPacketKind Kind { get; set; }
    }

    [DataContract]
    public sealed class RedPacketClaimRequest
    {
        [DataMember(Order = 0)]
        public string PacketId { get; set; }
    }

    [DataContract]
    public sealed class RedPacketClaimEvent
    {
        [DataMember(Order = 0)]
        public string PacketId { get; set; }

        [DataMember(Order = 1)]
        public string SenderUuid { get; set; }

        [DataMember(Order = 2)]
        public string ClaimerUuid { get; set; }

        [DataMember(Order = 3)]
        public RedPacketItemSnapshot Item { get; set; }

        [DataMember(Order = 4)]
        public int Amount { get; set; }

        [DataMember(Order = 5)]
        public bool Completed { get; set; }
    }

    [DataContract]
    public sealed class RedPacketFailureEvent
    {
        [DataMember(Order = 0)]
        public string PacketId { get; set; }

        [DataMember(Order = 1)]
        public string RequestType { get; set; }

        [DataMember(Order = 2)]
        public RedPacketFailureReason Reason { get; set; }

        [DataMember(Order = 3)]
        public string Message { get; set; }
    }
}
