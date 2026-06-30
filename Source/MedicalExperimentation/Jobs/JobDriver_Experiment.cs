using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace MedicalExperimentation
{
    // Pawn hauls the exact chosen reagents to the bench one at a time, then works and resolves the outcome:
    // a correct combo always produces its compound; a wrong one runs the Medicine-skill-scaled salvage roll.
    // Hauled reagents are tallied in `gathered` (saved), so a mid-job save/load is safe.
    public class JobDriver_Experiment : JobDriver
    {
        private const int WorkAmount = 2200;
        private Dictionary<ThingDef, int> gathered = new Dictionary<ThingDef, int>();

        private Building_ExperimentationBench Bench => job.targetA.Thing as Building_ExperimentationBench;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (!pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed)) return false;
            pawn.ReserveAsManyAsPossible(job.GetTargetQueue(TargetIndex.B), job);
            return true;
        }

        public override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOnBurningImmobile(TargetIndex.A);

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

            // Haul each chosen reagent to the bench, then work.
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
                if (carried != null && !carried.Destroyed)
                {
                    gathered[carried.def] = (gathered.TryGetValue(carried.def, out int n) ? n : 0) + carried.stackCount;
                    carried.Destroy();
                }
            };
            yield return deposit;
            yield return Toils_Jump.JumpIf(extract, () => !job.GetTargetQueue(TargetIndex.B).NullOrEmpty());

            yield return doWork;

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

            var defs = new List<ThingDef>();
            foreach (var kv in gathered)
                for (int i = 0; i < kv.Value; i++) defs.Add(kv.Key);

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

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref gathered, "ME_gathered", LookMode.Def, LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && gathered == null)
                gathered = new Dictionary<ThingDef, int>();
        }
    }
}
