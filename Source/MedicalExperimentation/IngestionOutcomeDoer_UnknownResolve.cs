using RimWorld;
using Verse;

namespace MedicalExperimentation
{
    // Runs when an unidentified compound is administered: looks up the hidden result the batch resolves to,
    // applies that compound's real effect, records the exact combo, and identifies it colony-wide (all via
    // the shared IngestionOutcomeDoer_Experimental.Resolve). This is the only way to learn what a mystery
    // compound is; its appearance never gives it away.
    public class IngestionOutcomeDoer_UnknownResolve : IngestionOutcomeDoer
    {
        public float adverseSeverity = 0.5f;

        public override void DoIngestionOutcomeSpecial(Pawn pawn, Thing ingested, int ingestedCount)
        {
            if (pawn?.health == null || !(ingested is Thing_UnknownCompound unknown)) return;

            ThingDef result = unknown.ResultDef;
            if (result == null) return;

            // Pull the resolved compound's effect from its own ingestion outcome (hediff + severity).
            HediffDef effect = null;
            float severity = -1f;
            if (result.ingestible?.outcomeDoers != null)
            {
                foreach (var d in result.ingestible.outcomeDoers)
                {
                    if (d is IngestionOutcomeDoer_Experimental exp)
                    {
                        effect = exp.hediffDef;
                        severity = exp.severity;
                        break;
                    }
                }
            }

            IngestionOutcomeDoer_Experimental.Resolve(pawn, result, unknown.comboKey, effect, severity, adverseSeverity);
        }
    }
}
