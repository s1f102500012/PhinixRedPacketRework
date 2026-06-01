using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Thing = Verse.Thing;

namespace Natsuki.PhinixRedPacketRework.Client
{
    internal sealed class RedPacketClientState
    {
        private readonly object syncRoot = new object();
        private readonly Dictionary<string, RedPacketStateSnapshot> packets = new Dictionary<string, RedPacketStateSnapshot>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<Thing>> sentThings = new Dictionary<string, List<Thing>>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> pendingClaims = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> processedClaimEvents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> returnedExpiredPackets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public void ReplacePackets(IEnumerable<RedPacketStateSnapshot> snapshots)
        {
            lock (syncRoot)
            {
                packets.Clear();
                foreach (RedPacketStateSnapshot packet in snapshots ?? Enumerable.Empty<RedPacketStateSnapshot>())
                {
                    if (packet == null || string.IsNullOrEmpty(packet.PacketId)) continue;
                    packets[packet.PacketId] = packet;
                }
            }
        }

        public RedPacketStateSnapshot[] GetPackets()
        {
            lock (syncRoot)
            {
                return packets.Values
                    .OrderByDescending(packet => packet.CreatedAtUtcTicks)
                    .ToArray();
            }
        }

        public bool TryGetPacket(string packetId, out RedPacketStateSnapshot packet)
        {
            lock (syncRoot)
            {
                return packets.TryGetValue(packetId ?? string.Empty, out packet);
            }
        }

        public void TrackSentThings(string packetId, IEnumerable<Thing> things)
        {
            if (string.IsNullOrEmpty(packetId)) return;

            lock (syncRoot)
            {
                sentThings[packetId] = (things ?? Enumerable.Empty<Thing>())
                    .Where(thing => thing != null && !thing.Destroyed)
                    .ToList();
            }
        }

        public List<Thing> TakeSentThings(string packetId)
        {
            lock (syncRoot)
            {
                if (!sentThings.TryGetValue(packetId ?? string.Empty, out List<Thing> things))
                {
                    return new List<Thing>();
                }

                sentThings.Remove(packetId);
                return things.Where(thing => thing != null && !thing.Destroyed).ToList();
            }
        }

        public void ConsumeSentThings(string packetId, int amount)
        {
            if (string.IsNullOrEmpty(packetId) || amount <= 0) return;

            List<Thing> toDestroy = new List<Thing>();
            lock (syncRoot)
            {
                if (!sentThings.TryGetValue(packetId, out List<Thing> things)) return;

                int remaining = amount;
                for (int i = 0; i < things.Count && remaining > 0; i++)
                {
                    Thing thing = things[i];
                    if (thing == null || thing.Destroyed) continue;

                    int take = Math.Min(thing.stackCount, remaining);
                    thing.stackCount -= take;
                    remaining -= take;
                    if (thing.stackCount <= 0)
                    {
                        toDestroy.Add(thing);
                        things[i] = null;
                    }
                }

                things.RemoveAll(thing => thing == null || thing.Destroyed);
                if (things.Count == 0)
                {
                    sentThings.Remove(packetId);
                }
            }

            foreach (Thing thing in toDestroy)
            {
                if (thing != null && !thing.Destroyed) thing.Destroy();
            }
        }

        public void AddPendingClaim(string packetId)
        {
            if (string.IsNullOrEmpty(packetId)) return;
            lock (syncRoot) pendingClaims.Add(packetId);
        }

        public void RemovePendingClaim(string packetId)
        {
            if (string.IsNullOrEmpty(packetId)) return;
            lock (syncRoot) pendingClaims.Remove(packetId);
        }

        public bool IsPendingClaim(string packetId)
        {
            lock (syncRoot) return pendingClaims.Contains(packetId ?? string.Empty);
        }

        public bool MarkClaimEventProcessed(string packetId, string claimerUuid)
        {
            string key = string.Concat(packetId ?? string.Empty, ":", claimerUuid ?? string.Empty);
            lock (syncRoot)
            {
                if (processedClaimEvents.Contains(key)) return false;
                processedClaimEvents.Add(key);
                return true;
            }
        }

        public bool MarkExpiredReturned(string packetId)
        {
            lock (syncRoot)
            {
                if (returnedExpiredPackets.Contains(packetId ?? string.Empty)) return false;
                returnedExpiredPackets.Add(packetId);
                return true;
            }
        }

        public int CountClaimable(string localUuid, DateTime nowUtc)
        {
            lock (syncRoot)
            {
                return packets.Values.Count(packet =>
                    packet != null &&
                    !packet.Expired &&
                    packet.ExpiresAtUtcTicks > nowUtc.Ticks &&
                    packet.RemainingCount > 0 &&
                    packet.RemainingPackets > 0 &&
                    packet.SenderUuid != localUuid &&
                    !HasClaimed(packet, localUuid));
            }
        }

        public static bool HasClaimed(RedPacketStateSnapshot packet, string uuid)
        {
            if (packet == null || string.IsNullOrEmpty(uuid)) return false;
            return (packet.Claims ?? new List<RedPacketClaimSnapshot>())
                .Any(claim => claim != null && claim.ClaimerUuid == uuid);
        }
    }
}
