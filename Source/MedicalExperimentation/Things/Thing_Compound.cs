using RimWorld;
using Verse;

namespace MedicalExperimentation
{
    // A real, identified compound item. Its def description is a generic placeholder (shared with the old
    // mystery phase), so the info card reads the real effect text from the compound's comp/recipe instead.
    // These are never a mystery — the unknown phase is handled entirely by ME_UnknownCompound.
    public class Thing_Compound : ThingWithComps
    {
        public override string DescriptionFlavor
        {
            get
            {
                var props = GetComp<CompMysteryDrug>()?.props as CompProperties_MysteryDrug;
                if (props != null && !props.revealedDescription.NullOrEmpty())
                    return props.revealedDescription;

                var recipe = ExperimentResolver.RecipeForProduct(def);
                if (recipe != null && !recipe.effectSummary.NullOrEmpty())
                    return recipe.effectSummary;

                return base.DescriptionFlavor;
            }
        }
    }
}
