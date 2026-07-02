using System.Linq;
using RimWorld;
using Verse;

namespace MedicalExperimentation
{
    // Reusable one-shot effects fired when an effect hediff is applied, configured from XML.
    // Lets several compounds share one comp instead of a bespoke class each.
    public class HediffCompProperties_OnAddEffects : HediffCompProperties
    {
        public bool removeWorstMemory;        // Neural Defragmenter
        public bool restToFull;               // Somnolent Draught
        public ThoughtDef memoryThought;      // mood memory to grant
        public float mentalBreakChance;       // Berserker Draught
        public HediffDef permanentHediff;     // lasting downside (e.g. neural scar)
        public bool killPawn;                 // lethal experimental drug
        public bool stopBleeding;             // Coagulant Serum: clot all currently-bleeding wounds

        public HediffCompProperties_OnAddEffects() { compClass = typeof(HediffComp_OnAddEffects); }
    }

    public class HediffComp_OnAddEffects : HediffComp
    {
        private HediffCompProperties_OnAddEffects Props => (HediffCompProperties_OnAddEffects)props;

        public override void CompPostPostAdd(DamageInfo? dinfo)
        {
            base.CompPostPostAdd(dinfo);
            Pawn pawn = Pawn;
            if (pawn == null) return;

            if (Props.removeWorstMemory)
            {
                var mem = pawn.needs?.mood?.thoughts?.memories;
                if (mem != null)
                {
                    var worst = mem.Memories
                        .Where(m => m.MoodOffset() < 0f)
                        .OrderBy(m => m.MoodOffset())
                        .FirstOrDefault();
                    if (worst != null) mem.RemoveMemory(worst);
                }
            }

            if (Props.restToFull && pawn.needs?.rest != null)
                pawn.needs.rest.CurLevel = 1f;

            if (Props.memoryThought != null)
                pawn.needs?.mood?.thoughts?.memories?.TryGainMemory(Props.memoryThought);

            if (Props.permanentHediff != null && pawn.health != null
                && !pawn.health.hediffSet.HasHediff(Props.permanentHediff))
                pawn.health.AddHediff(Props.permanentHediff);

            if (Props.mentalBreakChance > 0f && Rand.Chance(Props.mentalBreakChance)
                && pawn.mindState?.mentalStateHandler != null && pawn.RaceProps.Humanlike)
            {
                pawn.mindState.mentalStateHandler.TryStartMentalState(
                    MentalStateDefOf.Berserk, "ME_BerserkerDraught".Translate(), forced: true);
            }

            if (Props.stopBleeding && pawn.health != null)
            {
                // Clot every currently-bleeding wound: a perfect "tend" zeroes an injury's bleed rate. This
                // stops the ongoing blood loss at its source (existing BloodLoss then recovers on its own),
                // which is what a coagulant should do - matching the compound's "slows bleeding" description.
                foreach (var inj in pawn.health.hediffSet.hediffs.OfType<Hediff_Injury>()
                    .Where(h => h.Bleeding).ToList())
                {
                    if (inj.TryGetComp<HediffComp_TendDuration>() != null)
                        inj.Tended(1f, 1f, 0);
                }
            }

            if (Props.killPawn && !pawn.Dead)
            {
                pawn.Kill(null);
            }
        }
    }
}
