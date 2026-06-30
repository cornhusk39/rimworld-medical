using RimWorld;
using Verse;

namespace MedicalExperimentation
{
    // An experimental compound item. Its info-card description stays generic while the compound is
    // unknown, then switches to the real effect text once the colony has identified it (the static
    // ThingDef.description can't change on its own, so we override the flavor the info card reads).
    public class Thing_Compound : ThingWithComps
    {
        public override string DescriptionFlavor
        {
            get
            {
                var mystery = GetComp<CompMysteryDrug>();
                var ledger = GameComponent_PharmaLedger.Instance;
                if (mystery == null || ledger == null) return base.DescriptionFlavor;

                if (ledger.IsDiscovered(def))
                {
                    var props = mystery.props as CompProperties_MysteryDrug;
                    if (props != null && !props.revealedDescription.NullOrEmpty())
                        return props.revealedDescription;

                    var recipe = ExperimentResolver.RecipeForProduct(def);
                    if (recipe != null && !recipe.effectSummary.NullOrEmpty())
                        return recipe.effectSummary;
                }
                return base.DescriptionFlavor;
            }
        }
    }
}
