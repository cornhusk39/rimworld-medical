using RimWorld;
using Verse;

namespace MedicalExperimentation
{
    [DefOf]
    public static class ME_DefOf
    {
        public static PrisonerInteractionModeDef ME_AutoExperiment;

        static ME_DefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(ME_DefOf));
    }
}
