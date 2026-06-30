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

            // A human trial counts as completing the experiment regardless of outcome, so the bench will
            // not re-queue this combo (covers compounds obtained off the bench too).
            MarkComboTried(compound);

            if (incompatible)
            {
                ApplyAdverse(pawn, compound);
                return; // no benefit, no identification from a botched test
            }

            // Normal effect.
            if (hediffDef != null) ApplyEffect(pawn, hediffDef, severity);

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

        // Applies an effect hediff, stacking severity onto an existing one (so e.g. toxic buildup
        // accumulates across repeated doses / disperser hits toward its lethal cap). Shared by the
        // ingestion path and the chemical dispersal unit.
        public static void ApplyEffect(Pawn pawn, HediffDef def, float severity)
        {
            if (pawn?.health == null || def == null) return;
            float add = severity > 0f ? severity : def.initialSeverity;
            Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(def);
            if (existing != null)
            {
                float cap = def.maxSeverity;
                existing.Severity = cap > 0f ? System.Math.Min(existing.Severity + add, cap) : existing.Severity + add;
            }
            else
            {
                Hediff h = HediffMaker.MakeHediff(def, pawn);
                h.Severity = add;
                pawn.health.AddHediff(h);
            }
        }

        private static void MarkComboTried(ThingDef compound)
        {
            var ledger = GameComponent_PharmaLedger.Instance;
            var recipe = ExperimentResolver.RecipeForProduct(compound);
            if (ledger != null && recipe != null && !ledger.ComboTried(recipe.ComboKey))
                ledger.RecordCombo(recipe.ComboKey, compound);
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
