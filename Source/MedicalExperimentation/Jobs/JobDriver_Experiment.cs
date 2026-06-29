using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace MedicalExperimentation
{
    // Pawn works at the bench, then consumes the exact reserved reagents and resolves the outcome:
    // a correct combo always produces its compound; a wrong one runs the Medicine-skill-scaled salvage roll.
    // Reagents are consumed at completion (reserved up front), so a save/load mid-job simply restarts cleanly.
    public class JobDriver_Experiment : JobDriver
    {
        private const int WorkAmount = 2200;

        private Building_ExperimentationBench Bench => job.targetA.Thing as Building_ExperimentationBench;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (!pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed)) return false;
            if (job.targetQueueB != null)
            {
                for (int i = 0; i < job.targetQueueB.Count; i++)
                {
                    int cnt = (job.countQueue != null && i < job.countQueue.Count) ? job.countQueue[i] : 1;
                    if (!pawn.Reserve(job.targetQueueB[i], job, 1, cnt, null, errorOnFailed)) return false;
                }
            }
            return true;
        }

        public override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOnBurningImmobile(TargetIndex.A);

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.InteractionCell);

            Toil work = ToilMaker.MakeToil("ME_work");
            work.defaultCompleteMode = ToilCompleteMode.Delay;
            work.defaultDuration = WorkAmount;
            work.WithProgressBarToilDelay(TargetIndex.A);
            work.handlingFacing = true;
            work.activeSkill = () => SkillDefOf.Medicine;
            work.tickAction = delegate
            {
                pawn.skills?.Learn(SkillDefOf.Medicine, 0.075f);
                pawn.rotationTracker.FaceTarget(job.targetA);
            };
            work.FailOnCannotTouch(TargetIndex.A, PathEndMode.InteractionCell);
            yield return work;

            Toil finalize = ToilMaker.MakeToil("ME_finalize");
            finalize.defaultCompleteMode = ToilCompleteMode.Instant;
            finalize.initAction = Resolve;
            yield return finalize;
        }

        private void Resolve()
        {
            Building_ExperimentationBench bench = Bench;
            if (bench == null) return;
            Map map = pawn.Map;

            // Consume the reserved reagents, recording the multiset that was used.
            var defs = new List<ThingDef>();
            if (job.targetQueueB != null)
            {
                for (int i = 0; i < job.targetQueueB.Count; i++)
                {
                    Thing th = job.targetQueueB[i].Thing;
                    if (th == null || th.Destroyed) continue;
                    int cnt = (job.countQueue != null && i < job.countQueue.Count) ? job.countQueue[i] : 1;
                    int take = Math.Min(cnt, th.stackCount);
                    if (take <= 0) continue;
                    for (int k = 0; k < take; k++) defs.Add(th.def);
                    th.SplitOff(take).Destroy();
                }
            }

            string key = ExperimentRecipeDef.MakeKey(defs);
            var ledger = GameComponent_PharmaLedger.Instance;
            ExperimentRecipeDef recipe = ExperimentResolver.ResolveByKey(key);
            IntVec3 dropCell = bench.InteractionCell.IsValid ? bench.InteractionCell : bench.Position;

            if (recipe != null && recipe.product != null)
            {
                Thing product = ThingMaker.MakeThing(recipe.product);
                product.stackCount = Math.Max(1, recipe.productCount);
                GenPlace.TryPlaceThing(product, dropCell, map, ThingPlaceMode.Near);
                ledger?.RecordCombo(key, false);
                Messages.Message("ME_ExperimentSuccess".Translate(), product, MessageTypeDefOf.PositiveEvent, false);
            }
            else
            {
                float lvl = pawn.skills?.GetSkill(SkillDefOf.Medicine)?.Level ?? 0f;
                int salvaged = ExperimentResolver.RollSalvage(lvl);
                defs.Shuffle();
                for (int i = 0; i < salvaged && i < defs.Count; i++)
                {
                    Thing back = ThingMaker.MakeThing(defs[i]);
                    back.stackCount = 1;
                    GenPlace.TryPlaceThing(back, dropCell, map, ThingPlaceMode.Near);
                }
                ledger?.RecordCombo(key, true);
                Messages.Message("ME_ExperimentFail".Translate(salvaged), bench, MessageTypeDefOf.NeutralEvent, false);
            }

            bench.NotifyOrderCompleted(key);
        }
    }
}
