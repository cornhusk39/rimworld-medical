using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace MedicalExperimentation
{
    // Warden fetches one dose of an experimental compound, carries it to a flagged prisoner, and
    // administers it (running its ingestion outcome: effect + identification, or an adverse reaction).
    // Modeled on vanilla feed-patient so the pickup/carry is visible and doesn't loop.
    // TargetA = prisoner, TargetB = the compound stack.
    public class JobDriver_AdministerExperimental : JobDriver
    {
        private Pawn Prisoner => (Pawn)job.targetA.Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (!pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed)) return false;
            if (!pawn.Reserve(job.targetB, job, 10, 1, null, errorOnFailed)) return false;
            return true;
        }

        public override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);
            this.FailOnDestroyedNullOrForbidden(TargetIndex.B);
            this.FailOn(() => !Prisoner.IsPrisonerOfColony
                              || (!job.playerForced && !Prisoner.guest.IsInteractionEnabled(ME_DefOf.ME_AutoExperiment)));

            // Walk to the compound and pick up one dose into the carry tracker.
            yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.ClosestTouch)
                .FailOnDespawnedNullOrForbidden(TargetIndex.B);
            yield return Toils_Haul.StartCarryThing(TargetIndex.B, putRemainderInQueue: false,
                subtractNumTakenFromJobCount: false, failIfStackCountLessThanJobCount: false);

            // Carry it to the prisoner.
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch)
                .FailOnDestroyedOrNull(TargetIndex.A);

            Toil administer = ToilMaker.MakeToil("ME_administer");
            administer.defaultCompleteMode = ToilCompleteMode.Delay;
            administer.defaultDuration = 250;
            administer.handlingFacing = true;
            administer.WithProgressBarToilDelay(TargetIndex.A);
            administer.tickAction = () => pawn.rotationTracker.FaceTarget(job.targetA);
            administer.FailOnCannotTouch(TargetIndex.A, PathEndMode.Touch);
            yield return administer;

            Toil dose = ToilMaker.MakeToil("ME_dose");
            dose.defaultCompleteMode = ToilCompleteMode.Instant;
            dose.initAction = Administer;
            yield return dose;
        }

        private void Administer()
        {
            // Use the dose the warden carried over.
            Thing drug = pawn.carryTracker.CarriedThing;
            if (drug == null) return;

            Thing one = drug.stackCount > 1 ? drug.SplitOff(1) : drug;
            if (one.def.ingestible?.outcomeDoers != null)
            {
                foreach (var doer in one.def.ingestible.outcomeDoers)
                    doer.DoIngestionOutcome(Prisoner, one, 1);
            }
            if (!one.Destroyed) one.Destroy();

            // Put any leftover carried doses back down.
            if (pawn.carryTracker.CarriedThing != null)
                pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out _);
        }
    }
}
