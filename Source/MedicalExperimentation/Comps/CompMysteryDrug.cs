using System.Text;
using RimWorld;
using Verse;

namespace MedicalExperimentation
{
    public class CompProperties_MysteryDrug : CompProperties
    {
        public string codePrefix = "RX"; // RX therapeutic, CX combat, TX toxic
        public string revealedDescription; // the real effect text, shown only once discovered

        public CompProperties_MysteryDrug() { compClass = typeof(CompMysteryDrug); }
    }

    // Masks an experimental compound's identity until the colony has discovered what it does.
    // Identity state lives in GameComponent_PharmaLedger, keyed by the product defName.
    public class CompMysteryDrug : ThingComp
    {
        private CompProperties_MysteryDrug Props => (CompProperties_MysteryDrug)props;

        private bool Discovered => GameComponent_PharmaLedger.Instance?.IsDiscovered(parent.def) ?? false;

        // Deterministic, official-sounding code derived from the def (stable per version).
        public string Code => CodeFor(parent.def, Props.codePrefix);

        public static string CodeFor(ThingDef def, string prefix)
        {
            int h = def.shortHash;
            int num = (h % 9000 + 9000) % 9000 + 1000; // 1000..9999
            char batch = (char)('a' + ((h / 9000) % 5 + 5) % 5);
            return prefix + "-" + num + "-" + batch;
        }

        // Code for a compound ThingDef without an instance (used by the ledger UI).
        public static string CodeFor(ThingDef def)
        {
            var p = def.GetCompProperties<CompProperties_MysteryDrug>();
            return CodeFor(def, p?.codePrefix ?? "RX");
        }

        public override string TransformLabel(string label)
        {
            if (Discovered) return label; // real name, e.g. "adrenal catalyst"
            return "experimental compound " + Code;
        }

        public override string CompInspectStringExtra()
        {
            var ledger = GameComponent_PharmaLedger.Instance;
            if (ledger == null) return null;
            if (ledger.IsDiscovered(parent.def))
            {
                if (!Props.revealedDescription.NullOrEmpty()) return Props.revealedDescription;
                var recipe = ExperimentResolver.RecipeForProduct(parent.def);
                return "ME_Identified".Translate() + (recipe?.effectSummary.NullOrEmpty() == false ? ": " + recipe.effectSummary : "");
            }
            float hyp = ledger.HypothesisStrength(parent.def);
            if (hyp > 0f)
            {
                var recipe = ExperimentResolver.RecipeForProduct(parent.def);
                string guess = recipe?.effectSummary.NullOrEmpty() == false ? recipe.effectSummary : "uncertain";
                return "ME_Hypothesis".Translate(guess, hyp.ToStringPercent());
            }
            return "ME_Unidentified".Translate();
        }
    }
}
