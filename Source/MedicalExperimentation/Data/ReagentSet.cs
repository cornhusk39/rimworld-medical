using System.Collections.Generic;
using System.Linq;
using Verse;

namespace MedicalExperimentation
{
    // The curated subset of items the player may pick from when designing an experiment.
    // Resolved from defNames at runtime; missing defs (e.g. DLC items) are skipped silently.
    [StaticConstructorOnStartup]
    public static class ReagentSet
    {
        private static readonly string[] DefNames =
        {
            // bases
            "MedicineHerbal", "MedicineIndustrial", "MedicineUltratech",
            // catalysts
            "Neutroamine", "Luciferium", "Penoxycyline",
            // stimulants / psychoactives
            "GoJuice", "WakeUp", "Yayo", "Flake", "PsychoidLeaves", "SmokeleafLeaves",
            // exotics
            "Ambrosia", "Chemfuel", "Beer"
        };

        private static List<ThingDef> cached;

        public static List<ThingDef> All
        {
            get
            {
                if (cached == null)
                {
                    cached = DefNames
                        .Select(n => DefDatabase<ThingDef>.GetNamedSilentFail(n))
                        .Where(d => d != null)
                        .ToList();
                }
                return cached;
            }
        }

        public static bool Contains(ThingDef def) => def != null && All.Contains(def);
    }
}
