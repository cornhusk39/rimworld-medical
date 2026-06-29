using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace MedicalExperimentation
{
    // Data definition for one discoverable compound: the exact 3-reagent combination that produces it,
    // the product, and metadata. Matching is by multiset of reagent defNames (order-independent).
    public class ExperimentRecipeDef : Def
    {
        public List<ReagentCount> reagents = new List<ReagentCount>();
        public ThingDef product;
        public int productCount = 4;
        public float workAmount = 2200f;
        public bool toxic;                          // weaponizable branch
        public ResearchProjectDef unlocksResearch;  // surgery-unlock compounds complete this on discovery
        public string effectSummary;                // short text shown on discovery / in the ledger

        private string cachedKey;

        // Canonical order-independent key, e.g. "MedicineIndustrial|Neutroamine|WakeUp".
        public string ComboKey
        {
            get
            {
                if (cachedKey == null) cachedKey = MakeKey(reagents);
                return cachedKey;
            }
        }

        public int TotalReagentCount => reagents.Sum(r => r.count);

        // Build a canonical key from a multiset of (def,count): expand by count, sort defNames, join.
        public static string MakeKey(IEnumerable<ReagentCount> rc)
        {
            var expanded = new List<string>();
            foreach (var r in rc)
            {
                if (r.thingDef == null) continue;
                for (int i = 0; i < r.count; i++) expanded.Add(r.thingDef.defName);
            }
            expanded.Sort(System.StringComparer.Ordinal);
            return string.Join("|", expanded);
        }

        public static string MakeKey(IEnumerable<ThingDef> defs)
        {
            var list = defs.Where(d => d != null).Select(d => d.defName).ToList();
            list.Sort(System.StringComparer.Ordinal);
            return string.Join("|", list);
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (var e in base.ConfigErrors()) yield return e;
            if (product == null) yield return "ExperimentRecipeDef " + defName + " has null product";
            if (TotalReagentCount != 3) yield return "ExperimentRecipeDef " + defName + " must total exactly 3 reagents (has " + TotalReagentCount + ")";
        }
    }
}
