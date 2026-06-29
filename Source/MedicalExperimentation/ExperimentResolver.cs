using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace MedicalExperimentation
{
    // Matches a chosen reagent multiset to a discoverable compound, and rolls material salvage on failure.
    public static class ExperimentResolver
    {
        private static Dictionary<string, ExperimentRecipeDef> byCombo;
        private static Dictionary<string, ExperimentRecipeDef> byProduct;

        private static void EnsureIndex()
        {
            if (byCombo != null) return;
            byCombo = new Dictionary<string, ExperimentRecipeDef>();
            byProduct = new Dictionary<string, ExperimentRecipeDef>();
            foreach (var def in DefDatabase<ExperimentRecipeDef>.AllDefs)
            {
                if (!byCombo.ContainsKey(def.ComboKey)) byCombo[def.ComboKey] = def;
                if (def.product != null) byProduct[def.product.defName] = def;
            }
        }

        // The compound a combo produces, or null if the combo is a dud.
        public static ExperimentRecipeDef ResolveByKey(string comboKey)
        {
            EnsureIndex();
            return byCombo.TryGetValue(comboKey, out var d) ? d : null;
        }

        public static ExperimentRecipeDef ResolveByReagents(IEnumerable<ThingDef> defs)
            => ResolveByKey(ExperimentRecipeDef.MakeKey(defs));

        public static ExperimentRecipeDef RecipeForProduct(ThingDef product)
        {
            EnsureIndex();
            if (product == null) return null;
            return byProduct.TryGetValue(product.defName, out var d) ? d : null;
        }

        // Salvage probability split for a wrong combo, by Medicine level (1..20). Returns (waste, s1, s2, s3).
        public static (float waste, float s1, float s2, float s3) SalvageDistribution(float medicineLevel)
        {
            float t = Mathf.Clamp01((medicineLevel - 1f) / 19f);
            float waste = 0.50f - 0.45f * t;
            float s1 = 0.20f - 0.15f * t;
            float s2 = 0.15f;
            float s3 = 0.15f + 0.60f * t;
            return (waste, s1, s2, s3);
        }

        // Number of the 3 reagents recovered on a failed experiment (0..3).
        public static int RollSalvage(float medicineLevel)
        {
            var (waste, s1, s2, _s3) = SalvageDistribution(medicineLevel);
            float r = Rand.Value;
            if (r < waste) return 0;
            if (r < waste + s1) return 1;
            if (r < waste + s1 + s2) return 2;
            return 3;
        }
    }
}
