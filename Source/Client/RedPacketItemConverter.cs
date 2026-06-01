using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using Verse;
using Thing = Verse.Thing;

namespace Natsuki.PhinixRedPacketRework.Client
{
    internal static class RedPacketItemConverter
    {
        public static RedPacketItemSnapshot FromThing(Thing thing, int count)
        {
            if (thing == null) return null;

            QualityCategory quality;
            int qualityValue = thing.TryGetQuality(out quality) ? (int)quality : 0;

            return new RedPacketItemSnapshot
            {
                DefName = thing.def?.defName ?? string.Empty,
                StuffDefName = thing.Stuff?.defName ?? string.Empty,
                Quality = qualityValue,
                HitPoints = thing.HitPoints,
                Count = count
            };
        }

        public static string LabelFor(RedPacketItemSnapshot item)
        {
            if (item == null || string.IsNullOrEmpty(item.DefName)) return "???";
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(item.DefName);
            return def != null ? def.LabelCap.ToString() : item.DefName;
        }

        public static ThingDef DefFor(RedPacketItemSnapshot item)
        {
            if (item == null || string.IsNullOrEmpty(item.DefName)) return null;
            return DefDatabase<ThingDef>.GetNamedSilentFail(item.DefName);
        }

        public static ThingDef StuffFor(RedPacketItemSnapshot item)
        {
            if (item == null || string.IsNullOrEmpty(item.StuffDefName)) return null;
            return DefDatabase<ThingDef>.GetNamedSilentFail(item.StuffDefName);
        }

        public static List<Thing> ToThings(RedPacketItemSnapshot item, int amount)
        {
            List<Thing> things = new List<Thing>();
            if (item == null || amount <= 0) return things;

            ThingDef thingDef = DefDatabase<ThingDef>.GetNamedSilentFail(item.DefName);
            ThingDef stuffDef = StuffFor(item);
            if (thingDef == null)
            {
                thingDef = DefDatabase<ThingDef>.GetNamedSilentFail("UnknownItem") ?? ThingDefOf.Silver;
            }

            int stackLimit = Math.Max(1, thingDef.stackLimit);
            int remaining = amount;
            while (remaining > 0)
            {
                int stackCount = Math.Min(stackLimit, remaining);
                Thing thing = ThingMaker.MakeThing(thingDef, stuffDef);
                thing.stackCount = stackCount;
                if (item.HitPoints > 0)
                {
                    thing.HitPoints = Math.Min(item.HitPoints, thing.MaxHitPoints);
                }

                if (item.Quality > 0)
                {
                    CompQuality qualityComp = thing.TryGetComp<CompQuality>();
                    if (qualityComp != null)
                    {
                        qualityComp.SetQuality((QualityCategory)item.Quality, ArtGenerationContext.Outsider);
                    }
                }

                TrySetUnknownOriginalLabel(thing, item.DefName);
                things.Add(thing);
                remaining -= stackCount;
            }

            return things;
        }

        public static LookTargets DropThings(IEnumerable<Thing> things, bool dropCurrentMap)
        {
            List<Thing> validThings = (things ?? Enumerable.Empty<Thing>())
                .Where(thing => thing != null && !thing.Destroyed)
                .ToList();
            if (validThings.Count == 0) return null;

            Map map = dropCurrentMap ? Find.CurrentMap : Find.AnyPlayerHomeMap ?? Find.CurrentMap;
            if (map == null && Current.Game != null)
            {
                map = Current.Game.Maps.FirstOrDefault(candidate => candidate != null && candidate.IsPlayerHome)
                    ?? Current.Game.Maps.FirstOrDefault(candidate => candidate != null);
            }

            if (map == null) return null;

            IntVec3 dropSpot = DropCellFinder.TradeDropSpot(map);
            DropPodUtility.DropThingsNear(dropSpot, map, validThings, canRoofPunch: false);
            return new LookTargets(dropSpot, map);
        }

        private static void TrySetUnknownOriginalLabel(Thing thing, string originalDefName)
        {
            if (thing == null || string.IsNullOrEmpty(originalDefName)) return;
            if (thing.def == null || thing.def.defName != "UnknownItem") return;

            FieldInfo field = thing.GetType().GetField("OriginalLabel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null && field.FieldType == typeof(string))
            {
                field.SetValue(thing, originalDefName);
            }
        }
    }
}
