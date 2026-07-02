using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MedicalExperimentation
{
    // A recipe that produces an experimental compound (i.e. its product carries CompMysteryDrug) only
    // becomes craftable once the colony has discovered that compound by testing it on a pawn. This is how
    // a human trial "unlocks the medicine as a work bill" at the Drug Lab.
    [HarmonyPatch(typeof(RecipeDef), nameof(RecipeDef.AvailableNow), MethodType.Getter)]
    public static class Patch_RecipeDef_AvailableNow
    {
        private static HashSet<string> gatedRecipes;

        public static void Postfix(RecipeDef __instance, ref bool __result)
        {
            if (!__result) return;
            if (gatedRecipes == null) BuildSet();
            if (!gatedRecipes.Contains(__instance.defName)) return;

            ThingDef product = __instance.ProducedThingDef;
            var ledger = GameComponent_PharmaLedger.Instance;
            __result = product != null && ledger != null && ledger.IsDiscovered(product);
        }

        private static void BuildSet()
        {
            gatedRecipes = new HashSet<string>();
            foreach (RecipeDef r in DefDatabase<RecipeDef>.AllDefs)
            {
                // Recipes with their own research prerequisite (e.g. Metamorphosis's ME_CraftMetamorphosis) are
                // governed by that research - discovering the compound auto-finishes it, and researching it
                // the long way also unlocks it. Don't double-gate those on discovery too, or finishing the
                // research alone would never surface the recipe (a reported bug).
                if (r.researchPrerequisite != null
                    || (r.researchPrerequisites != null && r.researchPrerequisites.Count > 0))
                    continue;

                ThingDef product = r.ProducedThingDef;
                if (product != null && product.HasComp(typeof(CompMysteryDrug)))
                    gatedRecipes.Add(r.defName);
            }
        }
    }
}
