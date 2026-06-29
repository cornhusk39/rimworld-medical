using System.Collections.Generic;
using System.Linq;
using Verse;

namespace MedicalExperimentation
{
    // A queued experiment on a bench: the 3 reagents the player chose, and whether to repeat it.
    public class ExperimentOrder : IExposable
    {
        public List<ReagentCount> reagents = new List<ReagentCount>();
        public bool repeat;

        public ExperimentOrder() { }
        public ExperimentOrder(List<ReagentCount> reagents, bool repeat)
        {
            this.reagents = reagents;
            this.repeat = repeat;
        }

        public string ComboKey => ExperimentRecipeDef.MakeKey(reagents);

        public string Label
        {
            get
            {
                return string.Join(" + ", reagents
                    .Where(r => r.thingDef != null)
                    .Select(r => r.count > 1 ? r.count + "x " + r.thingDef.label : r.thingDef.label));
            }
        }

        public void ExposeData()
        {
            Scribe_Collections.Look(ref reagents, "reagents", LookMode.Deep);
            Scribe_Values.Look(ref repeat, "repeat", false);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && reagents == null) reagents = new List<ReagentCount>();
        }
    }
}
