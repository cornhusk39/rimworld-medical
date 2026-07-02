using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace MedicalExperimentation
{
    public class MedExpSettings : ModSettings
    {
        public float incompatibilityChance = 0.02f;
        public float adverseLethalityCap = 0.0f;        // 0 = never lethal; up to 1 allows worst outcomes
        public bool enablePrisonerExperimentation = true;
        public bool dispersalFriendlyFire = true;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref incompatibilityChance, "incompatibilityChance", 0.02f);
            Scribe_Values.Look(ref adverseLethalityCap, "adverseLethalityCap", 0.0f);
            Scribe_Values.Look(ref enablePrisonerExperimentation, "enablePrisonerExperimentation", true);
            Scribe_Values.Look(ref dispersalFriendlyFire, "dispersalFriendlyFire", true);
        }
    }

    public class MedExpMod : Mod
    {
        public static MedExpSettings Settings;

        public MedExpMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<MedExpSettings>();
            new Harmony("cornhusk39.medicalexperimentation").PatchAll(Assembly.GetExecutingAssembly());
            Log.Message("[Medical Experimentation] loaded.");
        }

        public override string SettingsCategory() => "Medical Experimentation";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var l = new Listing_Standard();
            l.Begin(inRect);
            l.Label("ME_Setting_Incompat".Translate(Settings.incompatibilityChance.ToStringPercent()));
            Settings.incompatibilityChance = l.Slider(Settings.incompatibilityChance, 0f, 0.25f);
            l.Label("ME_Setting_AdverseLethality".Translate(Settings.adverseLethalityCap.ToStringPercent()));
            Settings.adverseLethalityCap = l.Slider(Settings.adverseLethalityCap, 0f, 1f);
            l.CheckboxLabeled("ME_Setting_PrisonerExp".Translate(), ref Settings.enablePrisonerExperimentation);
            l.CheckboxLabeled("ME_Setting_FriendlyFire".Translate(), ref Settings.dispersalFriendlyFire, "ME_Setting_FriendlyFire_Desc".Translate());
            l.End();
        }
    }
}
