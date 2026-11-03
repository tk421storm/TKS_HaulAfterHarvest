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

            //Dubs Bad Hygiene: toilet, washing and drinking needs get a haul first, like eating
            if (ModsConfig.IsActive("Dubwise.DubsBadHygiene"))
            {
                string[] needJobNames = { "UseToilet", "haveWildPoo", "takeShower", "useWashBucket", "washAtCell", "DBHDrinkFromBasin", "DBHDrinkFromGround" };
                foreach (string name in needJobNames)
                {
                    JobDef def = DefDatabase<JobDef>.GetNamedSilentFail(name);
                    if (def != null)
                    {
                        PendingHauls.needJobs.Add(def);
                    }
                }
            }

            //Pick Up And Haul: found by def name, so no reference to its DLL; stays null without the mod
            PendingHauls.haulToInventory = DefDatabase<WorkGiverDef>.GetNamedSilentFail("HaulToInventory")?.Worker as WorkGiver_Scanner;

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
            listingStandard.Label("TKSHaulAfterHarvestSameWorkDistance".Translate(settings.sameWorkDistance), tooltip: "TKSHaulAfterHarvestSameWorkDistanceToolTip".Translate());
            settings.sameWorkDistance = (int)listingStandard.Slider(settings.sameWorkDistance, 5f, 100f);
            listingStandard.CheckboxLabeled("TKSHaulAfterHarvestEnablePlants".Translate(), ref settings.enablePlants, "TKSHaulAfterHarvestEnablePlantsToolTip".Translate());
            listingStandard.CheckboxLabeled("TKSHaulAfterHarvestIncludeTrees".Translate(), ref settings.includeTrees, "TKSHaulAfterHarvestIncludeTreesToolTip".Translate());
            listingStandard.CheckboxLabeled("TKSHaulAfterHarvestEnableAnimals".Translate(), ref settings.enableAnimals, "TKSHaulAfterHarvestEnableAnimalsToolTip".Translate());
            listingStandard.CheckboxLabeled("TKSHaulAfterHarvestEnableMining".Translate(), ref settings.enableMining, "TKSHaulAfterHarvestEnableMiningToolTip".Translate());
            listingStandard.CheckboxLabeled("TKSHaulAfterHarvestEnableDeconstruct".Translate(), ref settings.enableDeconstruct, "TKSHaulAfterHarvestEnableDeconstructToolTip".Translate());
            listingStandard.CheckboxLabeled("TKSHaulAfterHarvestEnableStrip".Translate(), ref settings.enableStrip, "TKSHaulAfterHarvestEnableStripToolTip".Translate());
            listingStandard.CheckboxLabeled("TKSHaulAfterHarvestEnableSkulls".Translate(), ref settings.enableSkulls, "TKSHaulAfterHarvestEnableSkullsToolTip".Translate());
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

        //more of the same work further away than this (in cells) still gets a haul first
        public int sameWorkDistance = 20;

        //haul after harvesting plants
        public bool enablePlants = true;

        //haul after cutting plants (chopping trees drops wood)
        public bool includeTrees = true;

        //haul after milking or shearing animals
        public bool enableAnimals = true;

        //haul after mining
        public bool enableMining = true;

        //haul after deconstructing
        public bool enableDeconstruct = true;

        //haul after stripping
        public bool enableStrip = true;

        //haul after extracting skulls
        public bool enableSkulls = true;

        //haul after fishing (Odyssey)
        public bool enableFishing = true;

        //only pawns with the Hauling work type enabled
        public bool requireHauling = true;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref radius, "radius", 5);
            Scribe_Values.Look(ref sameWorkDistance, "sameWorkDistance", 20);
            Scribe_Values.Look(ref enablePlants, "enablePlants", true);
            Scribe_Values.Look(ref includeTrees, "includeTrees", true);
            Scribe_Values.Look(ref enableAnimals, "enableAnimals", true);
            Scribe_Values.Look(ref enableMining, "enableMining", true);
            Scribe_Values.Look(ref enableDeconstruct, "enableDeconstruct", true);
            Scribe_Values.Look(ref enableStrip, "enableStrip", true);
            Scribe_Values.Look(ref enableSkulls, "enableSkulls", true);
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
        Deconstruct,
        Strip,
        Skulls,
        Fishing
    }

    //which kind of work each pawn just finished, waiting to see if their next job is more of the same
    public static class PendingHauls
    {
        public static Dictionary<Pawn, WorkGroup> pending = new Dictionary<Pawn, WorkGroup>();

        //other mods' need jobs treated like eating (filled at startup, empty without those mods)
        public static HashSet<JobDef> needJobs = new HashSet<JobDef>();

        //Pick Up And Haul's work giver (filled at startup, null without that mod)
        public static WorkGiver_Scanner haulToInventory;

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
            if (def == JobDefOf.Deconstruct)
            {
                return WorkGroup.Deconstruct;
            }
            if (def == JobDefOf.Strip)
            {
                return WorkGroup.Strip;
            }
            if (def == JobDefOf.ExtractSkull)
            {
                return WorkGroup.Skulls;
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
                case WorkGroup.Deconstruct:
                    return settings.enableDeconstruct;
                case WorkGroup.Strip:
                    return settings.enableStrip;
                case WorkGroup.Skulls:
                    return settings.enableSkulls;
                case WorkGroup.Fishing:
                    return settings.enableFishing;
                default:
                    return false;
            }
        }
    }

    //growing zones with hauling after harvest turned off, by zone ID; zones not listed are on
    public class MapComponent_HaulZones : MapComponent
    {
        public HashSet<int> disabledZones = new HashSet<int>();

        public MapComponent_HaulZones(Map map) : base(map)
        {
        }

        public static bool IsDisabled(Map map, IntVec3 cell)
        {
            if (!cell.InBounds(map) || !(map.zoneManager.ZoneAt(cell) is Zone_Growing zone))
            {
                return false;
            }
            return map.GetComponent<MapComponent_HaulZones>().disabledZones.Contains(zone.ID);
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref disabledZones, "disabledZones", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && disabledZones == null)
            {
                disabledZones = new HashSet<int>();
            }
        }
    }

    [StaticConstructorOnStartup]
    [HarmonyPatch(typeof(Zone_Growing), nameof(Zone_Growing.GetGizmos))]
    public static class Zone_Growing_GetGizmos_Patch
    {
        private static readonly Texture2D HaulIcon = ContentFinder<Texture2D>.Get("UI/Designators/Haul");

        //"Haul after harvest" toggle after vanilla's sow/cut toggles; on unless turned off for this zone
        [HarmonyPostfix]
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Zone_Growing __instance)
        {
            foreach (Gizmo gizmo in __result)
            {
                yield return gizmo;
            }
            HashSet<int> disabled = __instance.Map.GetComponent<MapComponent_HaulZones>().disabledZones;
            yield return new Command_Toggle
            {
                defaultLabel = "TKSHaulAfterHarvestZone".Translate(),
                defaultDesc = "TKSHaulAfterHarvestZoneDesc".Translate(),
                icon = HaulIcon,
                isActive = () => !disabled.Contains(__instance.ID),
                toggleAction = delegate
                {
                    if (!disabled.Remove(__instance.ID))
                    {
                        disabled.Add(__instance.ID);
                    }
                }
            };
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

            //growing zone with the toggle off: no haul. The last plant's cell decides (it keeps its position once
            //harvested), since the pawn often stands just outside the zone
            if ((group == WorkGroup.Plants || group == WorkGroup.Trees)
                && MapComponent_HaulZones.IsDisabled(___pawn.Map, job.targetA.IsValid ? job.targetA.Cell : ___pawn.Position))
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

            //more of the same work close by: keep going, the haul waits for the end of the run.
            //further away counts as the end of the run, so haul here before walking off
            if (SameRun(group, PendingHauls.GroupOf(newJob.def)) && IsNearby(___pawn, newJob))
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

            Job haul = FindHaulJob(___pawn, group, TKS_HaulAfterHarvest.settings);
            if (haul != null)
            {
                //the replaced job is dropped; the pawn picks it (or something better) again after the haul
                newJob = haul;
                tag = JobTag.MiscWork;
            }
        }

        //stripping and skull extraction are one run either way round, so no haul between the two on the same bodies
        private static bool SameRun(WorkGroup group, WorkGroup next)
        {
            if (group == next)
            {
                return true;
            }
            return (group == WorkGroup.Strip || group == WorkGroup.Skulls) && (next == WorkGroup.Strip || next == WorkGroup.Skulls);
        }

        //next job's target within sameWorkDistance of the pawn; a job without a target counts as nearby
        private static bool IsNearby(Pawn pawn, Job newJob)
        {
            //vanilla harvest jobs have no targetA yet, only a queue of plants (closest first when sorted)
            LocalTargetInfo target = newJob.targetA;
            if (!target.IsValid && !newJob.targetQueueA.NullOrEmpty())
            {
                target = newJob.targetQueueA[0];
            }
            if (!target.IsValid)
            {
                return true;
            }
            int maxDist = TKS_HaulAfterHarvest.settings.sameWorkDistance;
            return (target.Cell - pawn.Position).LengthHorizontalSquared <= maxDist * maxDist;
        }

        //only step in front of other work, eating, sleeping, recreation or modded needs - never fleeing,
        //firefighting, combat, mental states or orders
        private static bool CanHaulBefore(Pawn pawn, Job newJob)
        {
            if (!pawn.Spawned || pawn.Drafted || pawn.InMentalState || newJob.playerForced)
            {
                return false;
            }
            return newJob.workGiverDef != null || newJob.def == JobDefOf.Ingest || newJob.def == JobDefOf.LayDown || newJob.def.joyKind != null
                || PendingHauls.needJobs.Contains(newJob.def);
        }

        //closest haulable in range; one search through the map's haulables list
        private static Job FindHaulJob(Pawn pawn, WorkGroup group, TKS_HaulAfterHarvestSettings settings)
        {
            int maxDistSquared = settings.radius * settings.radius;

            Job best = null;
            int bestDist = int.MaxValue;

            foreach (Thing thing in pawn.Map.listerHaulables.ThingsPotentiallyNeedingHauling())
            {
                ConsiderHaul(pawn, thing, maxDistSquared, ref best, ref bestDist);
            }

            //chunks are only in the haulables list once marked for hauling; after mining, take one along
            //unmarked if a stockpile accepts it (the haul itself doesn't need the mark)
            if (group == WorkGroup.Mining)
            {
                foreach (Thing chunk in pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.Chunk))
                {
                    ConsiderHaul(pawn, chunk, maxDistSquared, ref best, ref bestDist);
                }
            }

            //Pick Up And Haul decides how to haul the closest thing, gathering more into the pawn's inventory or
            //falling back to a vanilla haul itself. Asked only for the winner because it sorts all haulables each call
            if (best != null && PendingHauls.haulToInventory != null)
            {
                return PendingHauls.haulToInventory.JobOnThing(pawn, best.targetA.Thing, false) ?? best;
            }

            return best;
        }

        //replaces best with a haul of thing if it's closer and can go to storage
        private static void ConsiderHaul(Pawn pawn, Thing thing, int maxDistSquared, ref Job best, ref int bestDist)
        {
            int dist = (thing.Position - pawn.Position).LengthHorizontalSquared;
            if (dist > maxDistSquared || dist >= bestDist)
            {
                return;
            }

            //reach, reservation and storage checks only for things that would beat the current best
            if (thing.IsForbidden(pawn) || !HaulAIUtility.PawnCanAutomaticallyHaulFast(pawn, thing, false))
            {
                return;
            }

            //null when there's no better storage for it
            Job haul = HaulAIUtility.HaulToStorageJob(pawn, thing, false);
            if (haul != null)
            {
                best = haul;
                bestDist = dist;
            }
        }
    }
}
