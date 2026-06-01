using System.Collections.Generic;
using Verse;

namespace Natsuki.PhinixRedPacketRework.Client
{
    public sealed class RedPacketMod : Mod
    {
        public static RedPacketSettings Settings;

        public RedPacketMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<RedPacketSettings>();
        }
    }

    public sealed class RedPacketSettings : ModSettings
    {
        public List<string> ReturnedPacketIds = new List<string>();

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref ReturnedPacketIds, "returnedPacketIds", LookMode.Value);
            if (ReturnedPacketIds == null)
            {
                ReturnedPacketIds = new List<string>();
            }
        }

        public bool HasReturned(string packetId)
        {
            return !string.IsNullOrEmpty(packetId) && ReturnedPacketIds.Contains(packetId);
        }

        public void MarkReturned(string packetId)
        {
            if (string.IsNullOrEmpty(packetId) || ReturnedPacketIds.Contains(packetId))
            {
                return;
            }

            ReturnedPacketIds.Add(packetId);
            RedPacketMod.Settings?.Write();
        }
    }
}
