using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MedicalExperimentation
{
    public class CompProperties_MysteryDrug : CompProperties
    {
        public string codePrefix = "RX"; // RX therapeutic, CX combat, TX toxic
        public string revealedDescription; // the real effect text

        public CompProperties_MysteryDrug() { compClass = typeof(CompMysteryDrug); }
    }

    // Marks a real, identified compound and carries its effect text. The mystery phase lives entirely on the
    // generic ME_UnknownCompound item; by the time you hold a real ME_Compound_* (crafted at the Drug Lab once
    // its combo is identified) you already know what it is, so these are never masked. This comp only tags the
    // def for recipe gating (Patch_RecipeDef_AvailableNow) and surfaces the effect in the item's info.
    public class CompMysteryDrug : ThingComp
    {
        private CompProperties_MysteryDrug Props => (CompProperties_MysteryDrug)props;

        // Deterministic, official-sounding code derived from the def (stable per version). Used by the ledger /
        // picker to label compounds the colony hasn't identified yet, not by the item itself.
        public static string CodeFor(ThingDef def, string prefix)
        {
            int h = def.shortHash;
            int num = (h % 9000 + 9000) % 9000 + 1000; // 1000..9999
            char batch = (char)('a' + ((h / 9000) % 5 + 5) % 5);
            return prefix + "-" + num + "-" + batch;
        }

        public static string CodeFor(ThingDef def)
        {
            var p = def.GetCompProperties<CompProperties_MysteryDrug>();
            return CodeFor(def, p?.codePrefix ?? "RX");
        }

        // Short, one-line effect summary (the map inspect pane clips long text, so keep it brief here).
        private string ShortEffect()
        {
            var recipe = ExperimentResolver.RecipeForProduct(parent.def);
            return recipe != null && !recipe.effectSummary.NullOrEmpty() ? recipe.effectSummary.CapitalizeFirst() : null;
        }

        public override string CompInspectStringExtra() => ShortEffect();

        // Adds an "Effect" row to the info card's left-hand stat list (where vanilla medicines list theirs).
        // The full effect text lives in the item description; this row shows the short summary.
        public override IEnumerable<StatDrawEntry> SpecialDisplayStats()
        {
            string summary = ShortEffect();
            string full = !Props.revealedDescription.NullOrEmpty() ? Props.revealedDescription
                : (parent.def.description ?? "ME_EffectIdentified".Translate());
            yield return new StatDrawEntry(StatCategoryDefOf.Basics, "ME_EffectStat".Translate(),
                summary ?? "ME_EffectIdentified".Translate(), full, 2490);
        }
    }
}
