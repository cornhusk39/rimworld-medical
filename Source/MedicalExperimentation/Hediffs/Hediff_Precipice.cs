using System.Linq;
using RimWorld;
using Verse;

namespace MedicalExperimentation
{
    // Precipice: a multi-day regenerative coma. The patient is incapacitated and fragile while the
    // compound rebuilds the body, regrowing missing parts and clearing scars and old wounds. It does NOT
    // cure disease. When it finishes, the patient is left with a weeks-long Precipice Hangover.
    //
    // Judgement call (see BUILD-LOG): the design said "every body part to 1 HP". Literally 1-HP-ing the
    // brain/heart would guarantee death, which contradicts the chosen "mostly non-lethal" tuning, so this
    // models the fragility as deep incapacitation (capacities near zero via the hediff stages) rather than
    // setting vital organs to 1 HP. Easy to make literal later if desired.
    public class Hediff_Precipice : HediffWithComps
    {
        private const int Duration = 240000;   // ~4 days
        private const int HealInterval = 20000; // heal one thing roughly twice a day
        private bool completed;

        public override void Tick()
        {
            base.Tick();
            if (completed || pawn == null) return;

            if (ageTicks > 0 && ageTicks % HealInterval == 0)
                HealOne();

            if (ageTicks >= Duration)
                Complete();
        }

        private void HealOne()
        {
            // 1) Regrow a missing body part.
            var missing = pawn.health.hediffSet.hediffs
                .OfType<Hediff_MissingPart>()
                .Where(h => h.Part != null)
                .OrderByDescending(h => h.Part.coverageAbs)
                .FirstOrDefault();
            if (missing != null)
            {
                pawn.health.RestorePart(missing.Part);
                return;
            }

            // 2) Clear a permanent scar.
            var scar = pawn.health.hediffSet.hediffs.FirstOrDefault(h => h.IsPermanent());
            if (scar != null)
            {
                pawn.health.RemoveHediff(scar);
                return;
            }

            // 3) Heal a lingering injury (disease/infection hediffs are not Hediff_Injury, so untouched).
            var injury = pawn.health.hediffSet.hediffs.OfType<Hediff_Injury>().FirstOrDefault();
            if (injury != null)
            {
                injury.Heal(injury.Severity + 1f);
            }
        }

        private void Complete()
        {
            completed = true;
            var hangover = DefDatabase<HediffDef>.GetNamedSilentFail("ME_Hediff_PrecipiceHangover");
            if (hangover != null && !pawn.health.hediffSet.HasHediff(hangover))
                pawn.health.AddHediff(hangover);
            pawn.health.RemoveHediff(this);
            if (PawnUtility.ShouldSendNotificationAbout(pawn))
                Messages.Message("ME_PrecipiceComplete".Translate(pawn.LabelShort), pawn, MessageTypeDefOf.PositiveEvent, false);
        }
    }
}
