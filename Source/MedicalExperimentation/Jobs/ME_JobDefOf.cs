using RimWorld;
using Verse;

namespace MedicalExperimentation
{
    [DefOf]
    public static class ME_JobDefOf
    {
        public static JobDef ME_RunExperiment;
        public static JobDef ME_AdministerExperimental;

        static ME_JobDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(ME_JobDefOf));
    }
}
