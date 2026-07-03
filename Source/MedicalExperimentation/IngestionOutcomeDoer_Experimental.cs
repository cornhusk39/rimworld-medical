using RimWorld;
using Verse;

namespace MedicalExperimentation
{
    // Applies a compound's real effect when administered, reveals its identity to the colony, and unlocks any
    // research tied to it. If the patient is incompatible with this compound (permanent ~2% per pair), they
    // suffer an adverse reaction instead and the compound is NOT identified from that test.
    //
    // This lives on the real (identified) compound defs so a known, Drug-Lab-crafted dose still applies its
    // effect. The mystery phase is handled by ME_UnknownCompound, which routes through the shared Resolve()
    // below with the actual combo the player mixed.
    public class IngestionOutcomeDoer_Experimental : IngestionOutcomeDoer
    {
        public HediffDef hediffDef;
        public float severity = -1f;
        public float adverseSeverity = 0.5f;

        public override void DoIngestionOutcomeSpecial(Pawn pawn, Thing ingested, int ingestedCount)
        {
            if (pawn?.health == null || ingested == null) return;
            ThingDef compound = ingested.def;
            var recipe = ExperimentResolver.RecipeForProduct(compound);
            Resolve(pawn, compound, recipe?.ComboKey, hediffDef, severity, adverseSeverity);
        }

        // Shared resolution: incompatibility check, record the exact combo tried, apply the effect (or an
        // adverse reaction), and identify the compound colony-wide. comboKey may be null (effect only).
        public static void Resolve(Pawn pawn, ThingDef compound, string comboKey, HediffDef effect,
            float severity, float adverseSeverity)
        {
            if (pawn?.health == null || compound == null) return;

            var incompat = GameComponent_DrugIncompat.Instance;
            bool incompatible = incompat != null && incompat.IsIncompatible(pawn, compound);

            // A human trial counts as completing the experiment regardless of outcome, so the bench will
            // not re-queue this combo. Record the actual combo the player mixed.
            var ledger = GameComponent_PharmaLedger.Instance;
            if (ledger != null && !comboKey.NullOrEmpty() && !ledger.ComboTried(comboKey))
                ledger.RecordCombo(comboKey, compound);

            if (incompatible)
            {
                ApplyAdverse(pawn, compound, adverseSeverity);
                return; // no benefit, no identification from a botched test
            }

            if (effect != null)
                ApplyEffect(pawn, effect, severity);
            else
                // An inert compound should not fail silently - the player needs to know the dose "took"
                // and simply did nothing, or the test reads like a bug.
                Messages.Message("ME_NoEffectMsg".Translate(pawn.LabelShort, compound.label),
                    pawn, MessageTypeDefOf.NeutralEvent, false);

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

        private static void ApplyAdverse(Pawn pawn, ThingDef compound, float adverseSeverity)
        {
            HediffDef adverseDef = DefDatabase<HediffDef>.GetNamedSilentFail("ME_AdverseReaction");
            if (adverseDef != null)
            {
                Hediff h = HediffMaker.MakeHediff(adverseDef, pawn);
                float sev = adverseSeverity;
                sev += (MedExpMod.Settings?.adverseLethalityCap ?? 0f) * 0.5f; // mod setting can allow harsher reactions
                h.Severity = sev;
                pawn.health.AddHediff(h);
            }
            Messages.Message("ME_AdverseReactionMsg".Translate(pawn.LabelShort, compound.LabelCap), pawn, MessageTypeDefOf.NegativeHealthEvent, false);
        }
    }
}
