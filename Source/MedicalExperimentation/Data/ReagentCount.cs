using Verse;

namespace MedicalExperimentation
{
    // A reagent + how many of it. Used both in ExperimentRecipeDef (XML) and in saved orders (IExposable).
    // XML form: <li><thingDef>MedicineIndustrial</thingDef><count>1</count></li>
    public class ReagentCount : IExposable
    {
        public ThingDef thingDef;
        public int count = 1;

        public ReagentCount() { }
        public ReagentCount(ThingDef thingDef, int count) { this.thingDef = thingDef; this.count = count; }

        public void ExposeData()
        {
            Scribe_Defs.Look(ref thingDef, "thingDef");
            Scribe_Values.Look(ref count, "count", 1);
        }
    }
}
