using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace MedicalExperimentation
{
    internal static class MetamorphosisUtility
    {
        // Metamorphosis only regrows missing parts, clears permanent scars, and heals injuries. If a pawn has
        // none of those, it has nothing to gain from Metamorphosis (it can't cure disease) - so putting them
        // through the coma is pure downside.
        public static bool HasNothingToHeal(Pawn p)
        {
            var hs = p?.health?.hediffSet;
            if (hs == null) return false;
            foreach (var h in hs.hediffs)
            {
                if (h is Hediff_MissingPart) return false;
                if (h is Hediff_Injury) return false;
                if (h.IsPermanent()) return false;
            }
            return true;
        }
    }

    // When a player deliberately orders a pawn to take REAL Metamorphosis but that pawn has nothing for it to
    // heal, confirm first - the compound will still force a multi-day coma and a weeks-long hangover for no
    // benefit. Covers both self-consume (Ingest) and force-feeding a patient (FeedPatient). The unidentified
    // compound version is never gated (you're testing it on purpose), and drug-policy intake isn't touched.
    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
    public static class Patch_MetamorphosisConfirm
    {
        private static readonly HashSet<int> confirmed = new HashSet<int>();

        public static bool Prefix(Pawn_JobTracker __instance, Job newJob, Pawn ___pawn)
        {
            if (newJob == null || !newJob.playerForced) return true;

            Thing drug = null;
            Pawn ingester = null;
            if (newJob.def == JobDefOf.Ingest)
            {
                drug = newJob.targetA.Thing;
                ingester = ___pawn;
            }
            else if (newJob.def == JobDefOf.FeedPatient)
            {
                // Vanilla FeedPatient layout: TargetA = the food/drug, TargetB = the patient.
                drug = newJob.targetA.Thing;
                ingester = newJob.targetB.Thing as Pawn;
            }
            else return true;

            if (drug == null || drug.def.defName != "ME_Compound_Metamorphosis" || ingester == null) return true;
            if (!MetamorphosisUtility.HasNothingToHeal(ingester)) return true;

            // Already confirmed this exact order -> let it through.
            if (confirmed.Remove(newJob.loadID)) return true;

            Pawn_JobTracker tracker = __instance;
            Job job = newJob;
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                "ME_MetamorphosisNoNeedConfirm".Translate(ingester.LabelShortCap),
                () => { confirmed.Add(job.loadID); tracker.StartJob(job); },
                destructive: true));
            return false; // hold the order until the player confirms
        }
    }
}
