using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace MedicalExperimentation
{
    // Warden carries one dose of an experimental compound to a flagged prisoner and administers it,
    // running the compound's ingestion outcome (effect + identification, or an adverse reaction).
    // TargetA = prisoner, TargetB = drug.
    public class JobDriver_AdministerExperimental : JobDriver
    {
        private Pawn Prisoner => (Pawn)job.targetA.Thing;
        private Thing Drug => job.targetB.Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (!pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed)) return false;
            if (!pawn.Reserve(job.targetB, job, 10, 1, null, errorOnFailed)) return false;
            return true;
        }

        public override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.B);
            this.FailOnDestroyedOrNull(TargetIndex.A);
            this.FailOn(() => !Prisoner.IsPrisonerOfColony
                              || !Prisoner.guest.IsInteractionEnabled(ME_DefOf.ME_AutoExperiment));

            yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.ClosestTouch)
                .FailOnDespawnedNullOrForbidden(TargetIndex.B);
            yield return Toils_Haul.StartCarryThing(TargetIndex.B);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

            Toil administer = ToilMaker.MakeToil("ME_administer");
            administer.defaultCompleteMode = ToilCompleteMode.Delay;
            administer.defaultDuration = 250;
            administer.handlingFacing = true;
            administer.WithProgressBarToilDelay(TargetIndex.A);
            administer.tickAction = () => pawn.rotationTracker.FaceTarget(job.targetA);
            administer.AddFinishAction(Administer);
            yield return administer;
        }

        private void Administer()
        {
            Thing held = pawn.carryTracker.CarriedThing;
            if (held == null || held.Destroyed) return;
            Thing one = held.stackCount > 1 ? held.SplitOff(1) : held;
            if (one.def.ingestible?.outcomeDoers != null)
            {
                foreach (var doer in one.def.ingestible.outcomeDoers)
                    doer.DoIngestionOutcome(Prisoner, one, 1);
            }
            if (!one.Destroyed) one.Destroy();
            // drop any remainder still carried
            if (pawn.carryTracker.CarriedThing != null)
                pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out _);
        }
    }
}
