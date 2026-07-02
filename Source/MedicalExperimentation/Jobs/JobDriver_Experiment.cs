using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace MedicalExperimentation
{
    // Pawn hauls the order's still-missing reagents to the bench one at a time, then works and resolves.
    // Delivery progress lives on the ExperimentOrder (job.count = order id), so an interrupted job resumes
    // where it left off instead of re-gathering, and the experiment can never resolve with a partial set:
    // if the pawn loses what it was carrying (e.g. dropped it to vomit), the job ends and a fresh job
    // fetches only what is still missing.
    public class JobDriver_Experiment : JobDriver
    {
        private const int WorkAmount = 2200;
        private int orderId = -1;

        private Building_ExperimentationBench Bench => job.targetA.Thing as Building_ExperimentationBench;
        private ExperimentOrder Order => Bench?.GetOrder(orderId);

        // The work giver passes the order id in job.count, but ExtractNextTargetFromQueue overwrites
        // job.count with each hauled stack's count - so capture the id once at job start.
        public override void Notify_Starting()
        {
            base.Notify_Starting();
            if (orderId < 0) orderId = job.count;
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (!pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed)) return false;
            pawn.ReserveAsManyAsPossible(job.GetTargetQueue(TargetIndex.B), job);
            return true;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref orderId, "ME_orderId", -1);
        }

        public override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOnBurningImmobile(TargetIndex.A);
            this.FailOn(() => Order == null); // order removed while working

            Toil doWork = ToilMaker.MakeToil("ME_work");
            doWork.defaultCompleteMode = ToilCompleteMode.Delay;
            doWork.defaultDuration = WorkAmount;
            doWork.WithProgressBarToilDelay(TargetIndex.A);
            doWork.handlingFacing = true;
            doWork.activeSkill = () => SkillDefOf.Medicine;
            doWork.tickAction = delegate
            {
                pawn.skills?.Learn(SkillDefOf.Medicine, 0.075f);
                pawn.rotationTracker.FaceTarget(job.targetA);
            };
            doWork.FailOnCannotTouch(TargetIndex.A, PathEndMode.InteractionCell);

            // Haul each still-missing reagent to the bench, then work.
            yield return Toils_Jump.JumpIf(doWork, () => job.GetTargetQueue(TargetIndex.B).NullOrEmpty());

            Toil extract = Toils_JobTransforms.ExtractNextTargetFromQueue(TargetIndex.B);
            yield return extract;
            yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.ClosestTouch)
                .FailOnDespawnedNullOrForbidden(TargetIndex.B);
            yield return Toils_Haul.StartCarryThing(TargetIndex.B, putRemainderInQueue: true,
                subtractNumTakenFromJobCount: false, failIfStackCountLessThanJobCount: false);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.InteractionCell);

            Toil deposit = ToilMaker.MakeToil("ME_deposit");
            deposit.initAction = delegate
            {
                Thing carried = pawn.carryTracker.CarriedThing;
                if (carried == null || carried.Destroyed)
                {
                    // Lost the load mid-haul (e.g. dropped it to vomit). Resuming here would craft with a
                    // partial set, so end the job; the work giver re-issues one for what's still missing.
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }
                Order?.Deliver(carried.def, carried.stackCount);
                carried.Destroy();
            };
            yield return deposit;
            yield return Toils_Jump.JumpIf(extract, () => !job.GetTargetQueue(TargetIndex.B).NullOrEmpty());

            // Never craft with a partial set: if the order still isn't fully delivered (some haul failed
            // along the way), end and let a fresh job fetch the remainder.
            Toil gate = ToilMaker.MakeToil("ME_completeGate");
            gate.initAction = delegate
            {
                if (Order == null || !Order.IsComplete)
                    EndJobWith(JobCondition.Incompletable);
            };
            yield return gate;

            yield return doWork;

            Toil finalize = ToilMaker.MakeToil("ME_finalize");
            finalize.defaultCompleteMode = ToilCompleteMode.Instant;
            finalize.initAction = Resolve;
            yield return finalize;
        }

        private void Resolve()
        {
            Building_ExperimentationBench bench = Bench;
            ExperimentOrder order = Order;
            if (bench == null || order == null || !order.IsComplete) return;
            Map map = pawn.Map;

            // The key comes from the order's recipe (the delivered set equals it by the gate above).
            string key = order.ComboKey;
            var ledger = GameComponent_PharmaLedger.Instance;
            ExperimentRecipeDef recipe = ExperimentResolver.ResolveByKey(key);
            IntVec3 dropCell = bench.InteractionCell.IsValid ? bench.InteractionCell : bench.Position;

            if (recipe != null && recipe.product != null)
            {
                // A defined combo yields a single generic "unidentified compound", secretly tagged with the
                // result and the exact combo. The RESULT isn't recorded here (that's only learned by
                // administering it, or it would leak at craft time) - but we mark the combo ATTEMPTED so the
                // bench won't auto-experiment or random-queue the same combo again.
                var unk = (Thing_UnknownCompound)ThingMaker.MakeThing(ThingDef.Named("ME_UnknownCompound"));
                unk.resultDefName = recipe.product.defName;
                unk.comboKey = key;
                GenPlace.TryPlaceThing(unk, dropCell, map, ThingPlaceMode.Near);
                ledger?.MarkAttempted(key);
                Messages.Message("ME_ExperimentSuccess".Translate(), new TargetInfo(dropCell, map), MessageTypeDefOf.PositiveEvent, false);
            }
            else
            {
                float lvl = pawn.skills?.GetSkill(SkillDefOf.Medicine)?.Level ?? 0f;
                int salvaged = ExperimentResolver.RollSalvage(lvl);
                var defs = new List<ThingDef>();
                foreach (var rc in order.reagents)
                    for (int i = 0; i < rc.count && rc.thingDef != null; i++) defs.Add(rc.thingDef);
                defs.Shuffle();
                for (int i = 0; i < salvaged && i < defs.Count; i++)
                {
                    Thing back = ThingMaker.MakeThing(defs[i]);
                    back.stackCount = 1;
                    GenPlace.TryPlaceThing(back, dropCell, map, ThingPlaceMode.Near);
                }
                ledger?.RecordCombo(key, null);
                Messages.Message("ME_ExperimentFail".Translate(salvaged), bench, MessageTypeDefOf.NeutralEvent, false);
            }

            bench.NotifyOrderCompleted(order);
        }
    }
}
