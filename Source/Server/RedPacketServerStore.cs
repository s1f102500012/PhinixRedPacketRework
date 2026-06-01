using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Xml;

namespace Natsuki.PhinixRedPacketRework.Server
{
    internal sealed class RedPacketServerStore
    {
        private static readonly TimeSpan PacketLifetime = TimeSpan.FromHours(24);
        private static readonly TimeSpan CompletedRetention = TimeSpan.FromHours(24);
        private static readonly TimeSpan ExpiredRetention = TimeSpan.FromDays(7);

        private readonly Dictionary<string, RedPacketStateSnapshot> packets = new Dictionary<string, RedPacketStateSnapshot>(StringComparer.OrdinalIgnoreCase);
        private readonly object syncRoot = new object();

        public bool TryCreate(string senderUuid, RedPacketCreateRequest request, DateTime nowUtc, out RedPacketStateSnapshot snapshot, out RedPacketFailureReason failureReason, out string failureMessage)
        {
            snapshot = null;
            failureReason = RedPacketFailureReason.None;
            failureMessage = null;

            if (string.IsNullOrEmpty(senderUuid) ||
                request == null ||
                request.Item == null ||
                string.IsNullOrEmpty(request.Item.DefName) ||
                request.Item.Count <= 0 ||
                request.PacketCount <= 0 ||
                request.PacketCount > request.Item.Count)
            {
                failureReason = RedPacketFailureReason.InvalidRequest;
                failureMessage = "Invalid red packet request.";
                return false;
            }

            lock (syncRoot)
            {
                ExpireDueLocked(nowUtc);
                PruneLocked(nowUtc);

                string packetId = string.IsNullOrEmpty(request.PacketId) ? Guid.NewGuid().ToString() : request.PacketId;
                if (packets.ContainsKey(packetId))
                {
                    failureReason = RedPacketFailureReason.InvalidRequest;
                    failureMessage = "Duplicate red packet id.";
                    return false;
                }

                snapshot = new RedPacketStateSnapshot
                {
                    PacketId = packetId,
                    SenderUuid = senderUuid,
                    SenderDisplayName = request.SenderDisplayName,
                    Item = CloneItem(request.Item),
                    TotalCount = request.Item.Count,
                    RemainingCount = request.Item.Count,
                    TotalPackets = request.PacketCount,
                    RemainingPackets = request.PacketCount,
                    Kind = request.Kind,
                    CreatedAtUtcTicks = nowUtc.Ticks,
                    ExpiresAtUtcTicks = nowUtc.Add(PacketLifetime).Ticks,
                    CompletedAtUtcTicks = 0,
                    Expired = false,
                    Claims = new List<RedPacketClaimSnapshot>()
                };
                packets[snapshot.PacketId] = ClonePacket(snapshot);
                return true;
            }
        }

        public bool TryClaim(string packetId, string claimerUuid, DateTime nowUtc, Random random, out RedPacketClaimEvent claimEvent, out RedPacketStateSnapshot snapshot, out RedPacketFailureReason failureReason, out string failureMessage)
        {
            claimEvent = null;
            snapshot = null;
            failureReason = RedPacketFailureReason.None;
            failureMessage = null;

            if (string.IsNullOrEmpty(packetId) || string.IsNullOrEmpty(claimerUuid))
            {
                failureReason = RedPacketFailureReason.InvalidRequest;
                failureMessage = "Invalid claim request.";
                return false;
            }

            lock (syncRoot)
            {
                ExpireDueLocked(nowUtc);
                PruneLocked(nowUtc);

                if (!packets.TryGetValue(packetId, out RedPacketStateSnapshot packet))
                {
                    failureReason = RedPacketFailureReason.PacketNotFound;
                    failureMessage = "Red packet does not exist.";
                    return false;
                }

                if (packet.Expired || packet.ExpiresAtUtcTicks <= nowUtc.Ticks)
                {
                    packet.Expired = true;
                    packet.CompletedAtUtcTicks = packet.CompletedAtUtcTicks == 0 ? nowUtc.Ticks : packet.CompletedAtUtcTicks;
                    snapshot = ClonePacket(packet);
                    failureReason = RedPacketFailureReason.PacketExpired;
                    failureMessage = "Red packet has expired.";
                    return false;
                }

                if (string.Equals(packet.SenderUuid, claimerUuid, StringComparison.OrdinalIgnoreCase))
                {
                    failureReason = RedPacketFailureReason.SenderCannotClaim;
                    failureMessage = "Sender cannot claim their own red packet.";
                    return false;
                }

                if ((packet.Claims ?? new List<RedPacketClaimSnapshot>()).Any(claim => string.Equals(claim.ClaimerUuid, claimerUuid, StringComparison.OrdinalIgnoreCase)))
                {
                    failureReason = RedPacketFailureReason.AlreadyClaimed;
                    failureMessage = "Red packet already claimed.";
                    return false;
                }

                if (packet.RemainingCount <= 0 || packet.RemainingPackets <= 0)
                {
                    failureReason = RedPacketFailureReason.Empty;
                    failureMessage = "Red packet is empty.";
                    return false;
                }

                int amount = CalculateClaimAmount(packet, random);
                packet.RemainingCount -= amount;
                packet.RemainingPackets -= 1;
                packet.Claims = packet.Claims ?? new List<RedPacketClaimSnapshot>();
                packet.Claims.Add(new RedPacketClaimSnapshot
                {
                    ClaimerUuid = claimerUuid,
                    Amount = amount,
                    ClaimedAtUtcTicks = nowUtc.Ticks
                });

                if (packet.RemainingCount <= 0 || packet.RemainingPackets <= 0)
                {
                    packet.RemainingCount = Math.Max(0, packet.RemainingCount);
                    packet.RemainingPackets = Math.Max(0, packet.RemainingPackets);
                    packet.CompletedAtUtcTicks = nowUtc.Ticks;
                }

                snapshot = ClonePacket(packet);
                claimEvent = new RedPacketClaimEvent
                {
                    PacketId = packet.PacketId,
                    SenderUuid = packet.SenderUuid,
                    ClaimerUuid = claimerUuid,
                    Item = CloneItem(packet.Item),
                    Amount = amount,
                    Completed = packet.RemainingCount <= 0 || packet.RemainingPackets <= 0
                };
                return true;
            }
        }

        public RedPacketStateSnapshot[] GetSnapshot(DateTime nowUtc)
        {
            lock (syncRoot)
            {
                ExpireDueLocked(nowUtc);
                PruneLocked(nowUtc);
                return packets.Values
                    .OrderByDescending(packet => packet.CreatedAtUtcTicks)
                    .Select(ClonePacket)
                    .ToArray();
            }
        }

        public void Save(string path)
        {
            lock (syncRoot)
            {
                RedPacketStoreState state = new RedPacketStoreState
                {
                    Packets = packets.Values.Select(ClonePacket).ToList()
                };

                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using (XmlWriter writer = XmlWriter.Create(path, new XmlWriterSettings { Indent = true }))
                {
                    new DataContractSerializer(typeof(RedPacketStoreState)).WriteObject(writer, state);
                }
            }
        }

        public void Load(string path)
        {
            lock (syncRoot)
            {
                packets.Clear();
                if (!File.Exists(path))
                {
                    Save(path);
                    return;
                }

                RedPacketStoreState state;
                using (XmlReader reader = XmlReader.Create(path))
                {
                    state = new DataContractSerializer(typeof(RedPacketStoreState)).ReadObject(reader) as RedPacketStoreState ?? new RedPacketStoreState();
                }

                foreach (RedPacketStateSnapshot packet in state.Packets ?? new List<RedPacketStateSnapshot>())
                {
                    if (packet == null || string.IsNullOrEmpty(packet.PacketId))
                    {
                        continue;
                    }

                    packets[packet.PacketId] = ClonePacket(packet);
                }
            }
        }

        private static int CalculateClaimAmount(RedPacketStateSnapshot packet, Random random)
        {
            if (packet.RemainingPackets <= 1)
            {
                return packet.RemainingCount;
            }

            int max = packet.RemainingCount - (packet.RemainingPackets - 1);
            if (max <= 1)
            {
                return 1;
            }

            if (packet.Kind == RedPacketKind.Normal)
            {
                return Math.Max(1, (int)Math.Ceiling(packet.RemainingCount / (double)packet.RemainingPackets));
            }

            return random.Next(1, max + 1);
        }

        private void ExpireDueLocked(DateTime nowUtc)
        {
            foreach (RedPacketStateSnapshot packet in packets.Values)
            {
                if (packet == null || packet.Expired || packet.RemainingCount <= 0 || packet.ExpiresAtUtcTicks > nowUtc.Ticks)
                {
                    continue;
                }

                packet.Expired = true;
                packet.CompletedAtUtcTicks = nowUtc.Ticks;
            }
        }

        private void PruneLocked(DateTime nowUtc)
        {
            long completedBefore = nowUtc.Subtract(CompletedRetention).Ticks;
            long expiredBefore = nowUtc.Subtract(ExpiredRetention).Ticks;
            List<string> remove = packets
                .Where(entry =>
                    entry.Value != null &&
                    entry.Value.CompletedAtUtcTicks > 0 &&
                    ((entry.Value.Expired && entry.Value.CompletedAtUtcTicks < expiredBefore) ||
                     (!entry.Value.Expired && entry.Value.CompletedAtUtcTicks < completedBefore)))
                .Select(entry => entry.Key)
                .ToList();

            foreach (string packetId in remove)
            {
                packets.Remove(packetId);
            }
        }

        private static RedPacketStateSnapshot ClonePacket(RedPacketStateSnapshot packet)
        {
            if (packet == null)
            {
                return null;
            }

            return new RedPacketStateSnapshot
            {
                PacketId = packet.PacketId,
                SenderUuid = packet.SenderUuid,
                SenderDisplayName = packet.SenderDisplayName,
                Item = CloneItem(packet.Item),
                TotalCount = packet.TotalCount,
                RemainingCount = packet.RemainingCount,
                TotalPackets = packet.TotalPackets,
                RemainingPackets = packet.RemainingPackets,
                Kind = packet.Kind,
                CreatedAtUtcTicks = packet.CreatedAtUtcTicks,
                ExpiresAtUtcTicks = packet.ExpiresAtUtcTicks,
                CompletedAtUtcTicks = packet.CompletedAtUtcTicks,
                Expired = packet.Expired,
                Claims = (packet.Claims ?? new List<RedPacketClaimSnapshot>())
                    .Select(claim => new RedPacketClaimSnapshot
                    {
                        ClaimerUuid = claim.ClaimerUuid,
                        Amount = claim.Amount,
                        ClaimedAtUtcTicks = claim.ClaimedAtUtcTicks
                    })
                    .ToList()
            };
        }

        private static RedPacketItemSnapshot CloneItem(RedPacketItemSnapshot item)
        {
            if (item == null)
            {
                return null;
            }

            return new RedPacketItemSnapshot
            {
                DefName = item.DefName,
                StuffDefName = item.StuffDefName,
                Quality = item.Quality,
                HitPoints = item.HitPoints,
                Count = item.Count
            };
        }

        [DataContract]
        private sealed class RedPacketStoreState
        {
            [DataMember(Order = 0)]
            public List<RedPacketStateSnapshot> Packets { get; set; } = new List<RedPacketStateSnapshot>();
        }
    }
}
