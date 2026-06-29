using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace MedicalExperimentation
{
    // Surgery worker that removes permanent injuries (scars) on the operated part. Used by the
    // discovered "regrow scar tissue" and "nerve repair" operations. Disease is not affected.
    public class RecipeWorker_RestoreTissue : Recipe_Surgery
    {
        public override void ApplyOnPawn(Pawn pawn, BodyPartRecord part, Pawn billDoer, List<Thing> ingredients, Bill bill)
        {
            if (billDoer != null && CheckSurgeryFail(billDoer, pawn, ingredients, part, bill))
                return;

            // Clear permanent scars across the whole body (the surgeon works at `part`, but the treatment
            // restores scarred tissue generally). Disease/infection are not Hediff_Injury, so untouched.
            var scars = pawn.health.hediffSet.hediffs.Where(h => h.IsPermanent()).ToList();
            if (scars.Count > 0)
            {
                foreach (var h in scars) pawn.health.RemoveHediff(h);
            }
            else
            {
                // No scars: heal lingering injuries instead so the operation is never wasted.
                var inj = pawn.health.hediffSet.hediffs.OfType<Hediff_Injury>().ToList();
                foreach (var h in inj) pawn.health.RemoveHediff(h);
            }
        }
    }
}
