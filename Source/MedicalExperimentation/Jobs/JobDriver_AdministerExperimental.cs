using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace MedicalExperimentation
{
    // Warden goes to a flagged prisoner and administers one dose of an experimental compound, running its
    // ingestion outcome (effect + identification, or an adverse reaction). The dose is consumed from the
    // reserved stock at completion (no separate haul step, which was prone to aborting and looping).
    // TargetA = prisoner, TargetB = the compound stack.
    public class JobDriver_AdministerExperimental : JobDriver
    {
        private Pawn Prisoner => (Pawn)job.targetA.Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (!pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed)) return false;
            if (job.targetB.HasThing && !pawn.Reserve(job.targetB, job, 10, 1, null, errorOnFailed)) return false;
            return true;
        }

        public override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);
            this.FailOn(() => !Prisoner.IsPrisonerOfColony
                              || !Prisoner.guest.IsInteractionEnabled(ME_DefOf.ME_AutoExperiment));

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

            Toil administer = ToilMaker.MakeToil("ME_administer");
            administer.defaultCompleteMode = ToilCompleteMode.Delay;
            administer.defaultDuration = 250;
            administer.handlingFacing = true;
            administer.WithProgressBarToilDelay(TargetIndex.A);
            administer.tickAction = () => pawn.rotationTracker.FaceTarget(job.targetA);
            yield return administer;

            Toil dose = ToilMaker.MakeToil("ME_dose");
            dose.defaultCompleteMode = ToilCompleteMode.Instant;
            dose.initAction = Administer;
            yield return dose;
        }

        private void Administer()
        {
            Thing drug = job.targetB.Thing;
            // fall back to any reachable dose of the same kind if the reserved one is gone
            if ((drug == null || drug.Destroyed) && job.targetB.HasThing) drug = null;
            if (drug == null) return;

            Thing one = drug.stackCount > 1 ? drug.SplitOff(1) : drug;
            if (one.def.ingestible?.outcomeDoers != null)
            {
                foreach (var doer in one.def.ingestible.outcomeDoers)
                    doer.DoIngestionOutcome(Prisoner, one, 1);
            }
            if (!one.Destroyed) one.Destroy();
        }
    }
}
