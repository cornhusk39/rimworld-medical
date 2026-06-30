using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace MedicalExperimentation
{
    // Offers experiment jobs at benches that have queued orders, to pawns assigned Doctor work.
    public class WorkGiver_DoExperiment : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.InteractionCell;

        // Use the global-things scan path (the same one WorkGiver_Warden uses and which reliably fires),
        // returning the benches directly via listerThings (faction-independent, registered on spawn).
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            return pawn.Map.listerThings.ThingsOfDef(ThingDef.Named("ME_ExperimentationBench"));
        }

        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            foreach (var t in pawn.Map.listerThings.ThingsOfDef(ThingDef.Named("ME_ExperimentationBench")))
                if (t is Building_ExperimentationBench b && b.HasOrders) return false;
            return true;
        }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            return JobOnThing(pawn, t, forced) != null;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!(t is Building_ExperimentationBench bench) || !bench.HasOrders) return null;
            if (bench.IsForbidden(pawn) || !pawn.CanReserve(bench, 1, -1, null, forced)) return null;
            if (bench.IsBurning()) return null;
            CompPowerTrader power = bench.GetComp<CompPowerTrader>();
            if (power != null && !power.PowerOn) return null;

            foreach (var order in bench.Orders)
            {
                var queueThings = new List<LocalTargetInfo>();
                var queueCounts = new List<int>();
                if (TryFindReagents(pawn, order, queueThings, queueCounts))
                {
                    Job job = JobMaker.MakeJob(ME_JobDefOf.ME_RunExperiment, bench);
                    job.targetQueueB = queueThings;
                    job.countQueue = queueCounts;
                    return job;
                }
            }
            return null;
        }

        // Finds, for each reagent, enough reachable/reservable stacks to satisfy the count. All-or-nothing.
        private bool TryFindReagents(Pawn pawn, ExperimentOrder order, List<LocalTargetInfo> things, List<int> counts)
        {
            foreach (var rc in order.reagents)
            {
                if (rc.thingDef == null) return false;
                int need = rc.count;
                foreach (Thing thing in pawn.Map.listerThings.ThingsOfDef(rc.thingDef))
                {
                    if (need <= 0) break;
                    if (thing.IsForbidden(pawn)) continue;
                    if (!pawn.CanReserve(thing, 1, -1, null, false)) continue;
                    if (!pawn.CanReach(thing, PathEndMode.ClosestTouch, Danger.Deadly)) continue;
                    int take = System.Math.Min(need, thing.stackCount);
                    things.Add(thing);
                    counts.Add(take);
                    need -= take;
                }
                if (need > 0) return false;
            }
            return true;
        }
    }
}
