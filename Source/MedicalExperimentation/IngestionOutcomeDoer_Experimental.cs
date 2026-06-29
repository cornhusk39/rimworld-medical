using RimWorld;
using Verse;

namespace MedicalExperimentation
{
    // Applies an experimental compound's real effect when administered, reveals its identity to the colony,
    // and unlocks any surgery tied to it. If the patient is incompatible with this compound (permanent ~2%
    // per pair), they suffer an adverse reaction instead and the compound is NOT identified from that test.
    public class IngestionOutcomeDoer_Experimental : IngestionOutcomeDoer
    {
        public HediffDef hediffDef;
        public float severity = -1f;
        public float adverseSeverity = 0.5f;

        public override void DoIngestionOutcomeSpecial(Pawn pawn, Thing ingested, int ingestedCount)
        {
            if (pawn?.health == null || ingested == null) return;
            ThingDef compound = ingested.def;

            var incompat = GameComponent_DrugIncompat.Instance;
            bool incompatible = incompat != null && incompat.IsIncompatible(pawn, compound);

            if (incompatible)
            {
                ApplyAdverse(pawn, compound);
                return; // no benefit, no identification from a botched test
            }

            // Normal effect.
            if (hediffDef != null)
            {
                Hediff h = HediffMaker.MakeHediff(hediffDef, pawn);
                h.Severity = severity > 0f ? severity : hediffDef.initialSeverity;
                pawn.health.AddHediff(h);
            }

            // Reveal identity colony-wide.
            var ledger = GameComponent_PharmaLedger.Instance;
            if (ledger != null && ledger.Discover(compound))
            {
                var recipe = ExperimentResolver.RecipeForProduct(compound);
                string summary = recipe != null && !recipe.effectSummary.NullOrEmpty() ? recipe.effectSummary : "";
                Messages.Message("ME_Discovered".Translate(compound.LabelCap, summary), pawn, MessageTypeDefOf.PositiveEvent, false);

                if (recipe?.unlocksResearch != null && !recipe.unlocksResearch.IsFinished)
                {
                    Find.ResearchManager.FinishProject(recipe.unlocksResearch);
                    Messages.Message("ME_SurgeryUnlocked".Translate(recipe.unlocksResearch.LabelCap), MessageTypeDefOf.PositiveEvent, false);
                }
            }
        }

        private void ApplyAdverse(Pawn pawn, ThingDef compound)
        {
            HediffDef adverseDef = DefDatabase<HediffDef>.GetNamedSilentFail("ME_AdverseReaction");
            if (adverseDef != null)
            {
                Hediff h = HediffMaker.MakeHediff(adverseDef, pawn);
                float sev = adverseSeverity;
                // mod setting can allow harsher reactions
                sev += (MedExpMod.Settings?.adverseLethalityCap ?? 0f) * 0.5f;
                h.Severity = sev;
                pawn.health.AddHediff(h);
            }
            Messages.Message("ME_AdverseReactionMsg".Translate(pawn.LabelShort, compound.LabelCap), pawn, MessageTypeDefOf.NegativeHealthEvent, false);
        }
    }
}
