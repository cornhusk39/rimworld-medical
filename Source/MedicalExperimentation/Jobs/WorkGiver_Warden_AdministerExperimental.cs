using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace MedicalExperimentation
{
    // Wardens administer an undiscovered experimental compound to any prisoner marked for auto-experimentation.
    public class WorkGiver_Warden_AdministerExperimental : WorkGiver_Warden
    {
        private static List<ThingDef> compoundDefs;
        private static List<ThingDef> CompoundDefs =>
            compoundDefs ??= DefDatabase<ThingDef>.AllDefs
                .Where(d => d.HasComp(typeof(CompMysteryDrug))).ToList();

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!(MedExpMod.Settings?.enablePrisonerExperimentation ?? true)) return null;
            if (!(t is Pawn prisoner)) return null;
            if (!ShouldTakeCareOfPrisoner(pawn, prisoner, forced)) return null;
            if (!prisoner.IsPrisonerOfColony) return null;
            if (!prisoner.guest.IsInteractionEnabled(ME_DefOf.ME_AutoExperiment)) return null;
            if (HasActiveExperimentEffect(prisoner)) return null;

            var ledger = GameComponent_PharmaLedger.Instance;
            Thing drug = null;
            foreach (var def in CompoundDefs)
            {
                if (ledger != null && ledger.IsDiscovered(def)) continue; // only test the unknown
                drug = GenClosest.ClosestThingReachable(pawn.Position, pawn.Map,
                    ThingRequest.ForDef(def), PathEndMode.ClosestTouch, TraverseParms.For(pawn),
                    9999f, x => !x.IsForbidden(pawn) && pawn.CanReserve(x, 10, 1, null, forced));
                if (drug != null) break;
            }
            if (drug == null) return null;
            if (!pawn.CanReserve(prisoner, 1, -1, null, forced)) return null;

            Job job = JobMaker.MakeJob(ME_JobDefOf.ME_AdministerExperimental, prisoner, drug);
            job.count = 1;
            return job;
        }

        private static bool HasActiveExperimentEffect(Pawn p)
        {
            var hs = p.health?.hediffSet;
            if (hs == null) return false;
            return hs.hediffs.Any(h => h.def.defName == "ME_AdverseReaction"
                                       || h.def.defName.StartsWith("ME_Hediff_"));
        }
    }
}
