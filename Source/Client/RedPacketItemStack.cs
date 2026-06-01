using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Thing = Verse.Thing;

namespace Natsuki.PhinixRedPacketRework.Client
{
    internal sealed class RedPacketItemStack
    {
        public readonly List<Thing> Things;
        public int Selected;

        public RedPacketItemStack(IEnumerable<Thing> things)
        {
            Things = (things ?? Enumerable.Empty<Thing>())
                .Where(thing => thing != null && !thing.Destroyed)
                .ToList();
        }

        public int Count => Things.Sum(thing => thing.stackCount);

        public string Label => Things.Count > 0 ? Things[0].LabelCapNoCount.ToString() : "???";

        public ThingDef ThingDef => Things.Count > 0 ? Things[0].def : null;

        public ThingDef StuffDef => Things.Count > 0 ? Things[0].Stuff : null;

        public ThingStyleDef StyleDef => Things.Count > 0 ? Things[0].StyleDef : null;

        public bool CanStack(Thing thing)
        {
            if (thing == null || Things.Count == 0) return false;
            return Things.All(existing => existing != null && existing.CanStackWith(thing));
        }

        public IEnumerable<Thing> PopSelected()
        {
            List<Thing> poppedThings = new List<Thing>();
            int remaining = Selected;

            for (int i = 0; i < Things.Count && remaining > 0; i++)
            {
                Thing thing = Things[i];
                if (thing == null || thing.Destroyed) continue;

                if (thing.stackCount > remaining)
                {
                    poppedThings.Add(thing.SplitOff(remaining));
                    remaining = 0;
                }
                else
                {
                    poppedThings.Add(thing);
                    remaining -= thing.stackCount;
                }
            }

            foreach (Thing thing in poppedThings)
            {
                Things.Remove(thing);
            }

            Selected = 0;
            return poppedThings;
        }

        public static List<RedPacketItemStack> GroupThings(IEnumerable<Thing> things)
        {
            List<RedPacketItemStack> stacks = new List<RedPacketItemStack>();
            foreach (Thing thing in things ?? Enumerable.Empty<Thing>())
            {
                if (thing == null || thing.Destroyed || thing.stackCount <= 0) continue;

                RedPacketItemStack stack = stacks.FirstOrDefault(candidate => candidate.CanStack(thing));
                if (stack == null)
                {
                    stacks.Add(new RedPacketItemStack(new[] { thing }));
                    continue;
                }

                stack.Things.Add(thing);
            }

            return stacks.OrderBy(stack => stack.Label, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}
