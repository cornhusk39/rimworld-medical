using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MedicalExperimentation
{
    // Each (pawn, drug) pair carries a small permanent chance of incompatibility, decided once and
    // remembered forever. Stored as a flattened string->bool dictionary so serialization stays trivial
    // and survives pawn death / drug-mod removal. The roll is seeded deterministically so the same pair
    // always resolves the same way even before it is first cached.
    public class GameComponent_DrugIncompat : GameComponent
    {
        public const float DefaultChance = 0.02f;

        private Dictionary<string, bool> flags = new Dictionary<string, bool>();
        private List<string> keysWork;
        private List<bool> valsWork;

        public GameComponent_DrugIncompat(Game game) { }

        public static GameComponent_DrugIncompat Instance => Current.Game?.GetComponent<GameComponent_DrugIncompat>();

        public bool IsIncompatible(Pawn pawn, ThingDef drug)
        {
            if (pawn == null || drug == null) return false;
            string key = pawn.GetUniqueLoadID() + "|" + drug.defName;
            if (!flags.TryGetValue(key, out bool result))
            {
                float chance = MedExpMod.Settings?.incompatibilityChance ?? DefaultChance;
                Rand.PushState(Gen.HashCombineInt(pawn.thingIDNumber, drug.shortHash));
                result = Rand.Chance(chance);
                Rand.PopState();
                flags[key] = result;
            }
            return result;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref flags, "flags", LookMode.Value, LookMode.Value, ref keysWork, ref valsWork);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && flags == null) flags = new Dictionary<string, bool>();
        }
    }
}
