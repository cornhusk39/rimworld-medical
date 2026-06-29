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
        }
    }
}
