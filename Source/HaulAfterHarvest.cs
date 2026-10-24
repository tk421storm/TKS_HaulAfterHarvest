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
            listingStandard.CheckboxLabeled("TKSHaulAfterHarvestEnablePlants".Translate(), ref settings.enablePlants, "TKSHaulAfterHarvestEnablePlantsToolTip".Translate());
            listingStandard.CheckboxLabeled("TKSHaulAfterHarvestIncludeTrees".Translate(), ref settings.includeTrees, "TKSHaulAfterHarvestIncludeTreesToolTip".Translate());
            listingStandard.CheckboxLabeled("TKSHaulAfterHarvestEnableAnimals".Translate(), ref settings.enableAnimals, "TKSHaulAfterHarvestEnableAnimalsToolTip".Translate());
            listingStandard.CheckboxLabeled("TKSHaulAfterHarvestEnableMining".Translate(), ref settings.enableMining, "TKSHaulAfterHarvestEnableMiningToolTip".Translate());
            if (ModsConfig.OdysseyActive)
            {
                listingStandard.CheckboxLabeled("TKSHaulAfterHarvestEnableFishing".Translate(), ref settings.enableFishing, "TKSHaulAfterHarvestEnableFishingToolTip".Translate());
            }
            listingStandard.CheckboxLabeled("TKSHaulAfterHarvestRequireHauling".Translate(), ref settings.requireHauling, "TKSHaulAfterHarvestRequireHaulingToolTip".Translate());
            listingStandard.End();
            base.DoSettingsWindowContents(inRect);
        }
    }

    public class TKS_HaulAfterHarvestSettings : ModSettings
    {
        //how far from the pawn (in cells) to look for something to haul
        public int radius = 5;

        //haul after harvesting plants
        public bool enablePlants = true;

        //haul after cutting plants (chopping trees drops wood)
        public bool includeTrees = true;

        //haul after milking or shearing animals
        public bool enableAnimals = true;

        //haul after mining
        public bool enableMining = true;

        //haul after fishing (Odyssey)
        public bool enableFishing = true;

        //only pawns with the Hauling work type enabled
        public bool requireHauling = true;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref radius, "radius", 5);
            Scribe_Values.Look(ref enablePlants, "enablePlants", true);
            Scribe_Values.Look(ref includeTrees, "includeTrees", true);
            Scribe_Values.Look(ref enableAnimals, "enableAnimals", true);
            Scribe_Values.Look(ref enableMining, "enableMining", true);
            Scribe_Values.Look(ref enableFishing, "enableFishing", true);
            Scribe_Values.Look(ref requireHauling, "requireHauling", true);
            base.ExposeData();
        }
    }

    public enum WorkGroup
    {
        None,
        Plants,
        Trees,
        Animals,
        Mining,
        Fishing
    }

    //which kind of work each pawn just finished, waiting to see if their next job is more of the same
    public static class PendingHauls
    {
        public static Dictionary<Pawn, WorkGroup> pending = new Dictionary<Pawn, WorkGroup>();

        public static WorkGroup GroupOf(JobDef def)
        {
            if (def == JobDefOf.Harvest || def == JobDefOf.HarvestDesignated)
            {
                return WorkGroup.Plants;
            }
            if (def == JobDefOf.CutPlant || def == JobDefOf.CutPlantDesignated)
            {
                return WorkGroup.Trees;
            }
            if (def == JobDefOf.Milk || def == JobDefOf.Shear)
            {
                return WorkGroup.Animals;
            }
            if (def == JobDefOf.Mine)
            {
                return WorkGroup.Mining;
            }
            //null without Odyssey, so never matches then
            if (def == JobDefOf.Fish)
            {
                return WorkGroup.Fishing;
            }
            return WorkGroup.None;
        }

        public static bool IsEnabled(WorkGroup group)
        {
            TKS_HaulAfterHarvestSettings settings = TKS_HaulAfterHarvest.settings;
            switch (group)
            {
                case WorkGroup.Plants:
                    return settings.enablePlants;
                case WorkGroup.Trees:
                    return settings.includeTrees;
                case WorkGroup.Animals:
                    return settings.enableAnimals;
                case WorkGroup.Mining:
                    return settings.enableMining;
                case WorkGroup.Fishing:
                    return settings.enableFishing;
                default:
                    return false;
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.EndCurrentJob))]
    public static class Pawn_JobTracker_EndCurrentJob_Patch
    {
        //note the finished work; the haul is decided in StartJob, once we know what the pawn does next
        [HarmonyPrefix]
        public static void Prefix(Pawn_JobTracker __instance, Pawn ___pawn, JobCondition condition, bool startNewJob)
        {
            //cheapest checks first: most jobs that end aren't ones we handle
            Job job = __instance.curJob;
            if (job == null || !startNewJob || condition != JobCondition.Succeeded)
            {
                return;
            }

            WorkGroup group = PendingHauls.GroupOf(job.def);
            if (group == WorkGroup.None || !PendingHauls.IsEnabled(group))
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

            PendingHauls.pending[___pawn] = group;
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
    public static class Pawn_JobTracker_StartJob_Patch
    {
        //if the pawn's next job isn't more of the same work, haul something nearby first instead
        [HarmonyPrefix]
        public static void Prefix(Pawn ___pawn, ref Job newJob, ref JobTag? tag, bool fromQueue)
        {
            if (PendingHauls.pending.Count == 0 || newJob == null || !PendingHauls.pending.TryGetValue(___pawn, out WorkGroup group))
            {
                return;
            }

            //vanilla's one-tick pause after a finished job - the real next job comes after it
            if (newJob.def == JobDefOf.Wait_MaintainPosture)
            {
                return;
            }

            PendingHauls.pending.Remove(___pawn);

            //more of the same work: keep going, the haul waits for the end of the run
            if (PendingHauls.GroupOf(newJob.def) == group)
            {
                return;
            }

            //cutting trees then moving on to sowing or harvesting is still plant work - no haul in between
            if (group == WorkGroup.Trees && (newJob.def == JobDefOf.Sow || PendingHauls.GroupOf(newJob.def) == WorkGroup.Plants))
            {
                return;
            }

            if (fromQueue || !CanHaulBefore(___pawn, newJob))
            {
                return;
            }

            Job haul = FindHaulJob(___pawn, TKS_HaulAfterHarvest.settings);
            if (haul != null)
            {
                //the replaced job is dropped; the pawn picks it (or something better) again after the haul
                newJob = haul;
                tag = JobTag.MiscWork;
            }
        }

        //only step in front of other work, eating, sleeping or recreation - never fleeing, firefighting,
        //combat, mental states or orders
        private static bool CanHaulBefore(Pawn pawn, Job newJob)
        {
            if (!pawn.Spawned || pawn.Drafted || pawn.InMentalState || newJob.playerForced)
            {
                return false;
            }
            return newJob.workGiverDef != null || newJob.def == JobDefOf.Ingest || newJob.def == JobDefOf.LayDown || newJob.def.joyKind != null;
        }

        //closest haulable in range; one search through the map's haulables list
        private static Job FindHaulJob(Pawn pawn, TKS_HaulAfterHarvestSettings settings)
        {
            ICollection<Thing> haulables = pawn.Map.listerHaulables.ThingsPotentiallyNeedingHauling();
            int maxDistSquared = settings.radius * settings.radius;

            Job best = null;
            int bestDist = int.MaxValue;

            foreach (Thing thing in haulables)
            {
                int dist = (thing.Position - pawn.Position).LengthHorizontalSquared;
                if (dist > maxDistSquared || dist >= bestDist)
                {
                    continue;
                }

                //reach, reservation and storage checks only for things that would beat the current best
                if (thing.IsForbidden(pawn) || !HaulAIUtility.PawnCanAutomaticallyHaulFast(pawn, thing, false))
                {
                    continue;
                }

                //null when there's no better storage for it
                Job haul = HaulAIUtility.HaulToStorageJob(pawn, thing, false);
                if (haul != null)
                {
                    best = haul;
                    bestDist = dist;
                }
            }

            return best;
        }
    }
}
