using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;

namespace TKS_HaulAfterHarvest
{
    [StaticConstructorOnStartup]
    public static class InsertHarmony
    {
        static InsertHarmony()
        {
            Harmony harmony = new Harmony("TKS_HaulAfterHarvest");
            harmony.PatchAll();
            Log.Message("TKS_HaulAfterHarvest: Patching finished");
        }
    }

    public class TKS_HaulAfterHarvest : Mod
    {
        public static TKS_HaulAfterHarvestSettings settings;

        public TKS_HaulAfterHarvest(ModContentPack content) : base(content)
        {
            settings = GetSettings<TKS_HaulAfterHarvestSettings>();
        }

        public override string SettingsCategory()
        {
            return "Haul After Harvest";
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listingStandard = new Listing_Standard();
            listingStandard.Begin(inRect);
            listingStandard.Label("TKSHaulAfterHarvestRadius".Translate(settings.radius), tooltip: "TKSHaulAfterHarvestRadiusToolTip".Translate());
            settings.radius = (int)listingStandard.Slider(settings.radius, 1f, 20f);
            listingStandard.CheckboxLabeled("TKSHaulAfterHarvestIncludeTrees".Translate(), ref settings.includeTrees, "TKSHaulAfterHarvestIncludeTreesToolTip".Translate());
            listingStandard.CheckboxLabeled("TKSHaulAfterHarvestRequireHauling".Translate(), ref settings.requireHauling, "TKSHaulAfterHarvestRequireHaulingToolTip".Translate());
            listingStandard.CheckboxLabeled("TKSHaulAfterHarvestCropOnly".Translate(), ref settings.cropOnly, "TKSHaulAfterHarvestCropOnlyToolTip".Translate());
            listingStandard.End();
            base.DoSettingsWindowContents(inRect);
        }
    }

    public class TKS_HaulAfterHarvestSettings : ModSettings
    {
        //how far from the pawn (in cells) to look for something to haul
        public int radius = 5;

        //also haul after cutting plants (chopping trees drops wood)
        public bool includeTrees = true;

        //only pawns with the Hauling work type enabled
        public bool requireHauling = true;

        //only haul what was just harvested, never other items nearby
        public bool cropOnly = false;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref radius, "radius", 5);
            Scribe_Values.Look(ref includeTrees, "includeTrees", true);
            Scribe_Values.Look(ref requireHauling, "requireHauling", true);
            Scribe_Values.Look(ref cropOnly, "cropOnly", false);
            base.ExposeData();
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.EndCurrentJob))]
    public static class Pawn_JobTracker_EndCurrentJob_Patch
    {
        //a prefix, so the haul is queued before the tracker picks the pawn's next job
        [HarmonyPrefix]
        public static void Prefix(Pawn_JobTracker __instance, Pawn ___pawn, JobCondition condition, bool startNewJob)
        {
            //cheapest checks first: most jobs that end aren't harvests
            Job job = __instance.curJob;
            if (job == null || !startNewJob || condition != JobCondition.Succeeded || !IsHarvestJob(job.def))
            {
                return;
            }

            if (!___pawn.Spawned || !___pawn.IsFreeColonist || ___pawn.Drafted || __instance.jobQueue.Count > 0)
            {
                return;
            }

            TKS_HaulAfterHarvestSettings settings = TKS_HaulAfterHarvest.settings;
            if (settings.requireHauling && (___pawn.workSettings == null || !___pawn.workSettings.WorkIsActive(WorkTypeDefOf.Hauling)))
            {
                return;
            }

            //the last plant in the batch - may already be gone, but its def is all we need
            ThingDef crop = job.targetA.Thing?.def.plant?.harvestedThingDef;
            if (settings.cropOnly && crop == null)
            {
                return;
            }

            Job haul = FindHaulJob(___pawn, crop, settings);
            if (haul != null)
            {
                __instance.jobQueue.EnqueueFirst(haul, JobTag.MiscWork);
            }
        }

        private static bool IsHarvestJob(JobDef def)
        {
            if (def == JobDefOf.Harvest || def == JobDefOf.HarvestDesignated)
            {
                return true;
            }
            return TKS_HaulAfterHarvest.settings.includeTrees && (def == JobDefOf.CutPlant || def == JobDefOf.CutPlantDesignated);
        }

        //closest haulable in range, preferring what was just harvested; one search through the map's haulables list
        private static Job FindHaulJob(Pawn pawn, ThingDef crop, TKS_HaulAfterHarvestSettings settings)
        {
            ICollection<Thing> haulables = pawn.Map.listerHaulables.ThingsPotentiallyNeedingHauling();
            int maxDistSquared = settings.radius * settings.radius;

            Thing bestCrop = null;
            int bestCropDist = int.MaxValue;
            Thing bestOther = null;
            int bestOtherDist = int.MaxValue;

            foreach (Thing thing in haulables)
            {
                bool isCrop = thing.def == crop;
                if (settings.cropOnly && !isCrop)
                {
                    continue;
                }

                int dist = (thing.Position - pawn.Position).LengthHorizontalSquared;
                if (dist > maxDistSquared || dist >= (isCrop ? bestCropDist : bestOtherDist))
                {
                    continue;
                }

                //reach and reservation checks only for things that would beat the current best
                if (thing.IsForbidden(pawn) || !HaulAIUtility.PawnCanAutomaticallyHaulFast(pawn, thing, false))
                {
                    continue;
                }

                if (isCrop)
                {
                    bestCrop = thing;
                    bestCropDist = dist;
                }
                else
                {
                    bestOther = thing;
                    bestOtherDist = dist;
                }
            }

            //HaulToStorageJob returns null when there's no better storage for it
            Job haul = bestCrop != null ? HaulAIUtility.HaulToStorageJob(pawn, bestCrop, false) : null;
            if (haul == null && bestOther != null)
            {
                haul = HaulAIUtility.HaulToStorageJob(pawn, bestOther, false);
            }
            return haul;
        }
    }
}
