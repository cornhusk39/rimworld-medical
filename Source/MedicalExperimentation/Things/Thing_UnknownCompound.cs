using RimWorld;
using Verse;

namespace MedicalExperimentation
{
    // A single generic "unidentified experimental compound" item. Every experiment produces one of these,
    // regardless of what it will turn out to be. The batch remembers, out of sight, which compound it
    // resolves to and the exact combo that made it; nothing about its appearance reveals either. It is
    // identified only by administering it to a subject (see IngestionOutcomeDoer_UnknownResolve).
    //
    // Kept unstackable (stackLimit 1 in XML) so batches never merge and leak information by their stacking
    // behaviour, matching the "always generic" design.
    public class Thing_UnknownCompound : ThingWithComps
    {
        public string resultDefName; // the real compound ThingDef this resolves to (incl. dud products)
        public string comboKey;      // the exact reagent combo that produced it, for the ledger

        public ThingDef ResultDef => resultDefName.NullOrEmpty()
            ? null : DefDatabase<ThingDef>.GetNamedSilentFail(resultDefName);

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref resultDefName, "ME_resultDefName");
            Scribe_Values.Look(ref comboKey, "ME_comboKey");
        }

        public override string GetInspectString()
        {
            string baseStr = base.GetInspectString();
            string mine = "ME_Unidentified".Translate();
            return baseStr.NullOrEmpty() ? mine : baseStr + "\n" + mine;
        }
    }
}
