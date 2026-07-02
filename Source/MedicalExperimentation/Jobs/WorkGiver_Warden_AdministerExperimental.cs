using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace MedicalExperimentation
{
    // Wardens administer an unidentified experimental compound to any prisoner marked for auto-experimentation.
    // Every mystery batch is the same ME_UnknownCompound item; which one gets used doesn't matter, since none
    // reveal what they are until administered.
    public class WorkGiver_Warden_AdministerExperimental : WorkGiver_Warden
    {
        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!(MedExpMod.Settings?.enablePrisonerExperimentation ?? true)) return null;
            if (!(t is Pawn prisoner)) return null;
            if (!ShouldTakeCareOfPrisoner(pawn, prisoner, forced)) return null;
            if (!prisoner.IsPrisonerOfColony) return null;
            // A manual right-click order ("Prioritize experimenting on") works even if the prisoner isn't
            // flagged for auto-experimentation; the automatic warden job needs the flag.
            if (!forced && !prisoner.guest.IsInteractionEnabled(ME_DefOf.ME_AutoExperiment)) return null;
            if (HasActiveExperimentEffect(prisoner)) return null;

            Thing drug = GenClosest.ClosestThingReachable(pawn.Position, pawn.Map,
                ThingRequest.ForDef(ThingDef.Named("ME_UnknownCompound")), PathEndMode.ClosestTouch,
                TraverseParms.For(pawn), 9999f,
                x => !x.IsForbidden(pawn) && pawn.CanReserve(x, 10, 1, null, forced));
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
            // Permanent leftovers (e.g. neural scarring) must not block: they never go away, so counting
            // them would exclude the prisoner from experimentation forever. Only transient effects gate.
            return hs.hediffs.Any(h => (h.def.defName == "ME_AdverseReaction"
                                        || h.def.defName.StartsWith("ME_Hediff_"))
                                       && h.def.defName != "ME_Hediff_NeuralScar");
        }
    }
}
