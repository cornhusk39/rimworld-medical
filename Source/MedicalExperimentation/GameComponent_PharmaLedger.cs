using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MedicalExperimentation
{
    // Colony-wide knowledge of experimental pharmacology. Persisted in the save (NOT on the defs, which
    // are global singletons shared across saves). Keyed by defName / combo key strings for save stability.
    public class GameComponent_PharmaLedger : GameComponent
    {
        // product defName -> identified (effect fully known, shown by real name)
        private HashSet<string> discovered = new HashSet<string>();
        // product defName -> hypothesis strength 0..1 (partial knowledge from a doctor's analysis)
        private Dictionary<string, float> hypotheses = new Dictionary<string, float>();
        // combo keys the colony has tried (any outcome), and the subset that produced nothing
        private HashSet<string> triedCombos = new HashSet<string>();
        private HashSet<string> dudCombos = new HashSet<string>();

        public GameComponent_PharmaLedger(Game game) { }

        public static GameComponent_PharmaLedger Instance => Current.Game?.GetComponent<GameComponent_PharmaLedger>();

        public bool IsDiscovered(ThingDef product) => product != null && discovered.Contains(product.defName);

        // Returns true if this call newly discovered it.
        public bool Discover(ThingDef product)
        {
            if (product == null) return false;
            bool isNew = discovered.Add(product.defName);
            return isNew;
        }

        public float HypothesisStrength(ThingDef product)
        {
            if (product == null) return 0f;
            return hypotheses.TryGetValue(product.defName, out float v) ? v : 0f;
        }

        public void RaiseHypothesis(ThingDef product, float strength)
        {
            if (product == null) return;
            float cur = HypothesisStrength(product);
            if (strength > cur) hypotheses[product.defName] = strength;
        }

        public void RecordCombo(string comboKey, bool wasDud)
        {
            if (comboKey.NullOrEmpty()) return;
            triedCombos.Add(comboKey);
            if (wasDud) dudCombos.Add(comboKey);
            else dudCombos.Remove(comboKey);
        }

        public bool ComboTried(string comboKey) => triedCombos.Contains(comboKey);
        public bool ComboDud(string comboKey) => dudCombos.Contains(comboKey);

        public IEnumerable<string> DiscoveredDefNames => discovered;
        public IEnumerable<string> DudComboKeys => dudCombos;
        public int DiscoveredCount => discovered.Count;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref discovered, "discovered", LookMode.Value);
            Scribe_Collections.Look(ref hypotheses, "hypotheses", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref triedCombos, "triedCombos", LookMode.Value);
            Scribe_Collections.Look(ref dudCombos, "dudCombos", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (discovered == null) discovered = new HashSet<string>();
                if (hypotheses == null) hypotheses = new Dictionary<string, float>();
                if (triedCombos == null) triedCombos = new HashSet<string>();
                if (dudCombos == null) dudCombos = new HashSet<string>();
            }
        }
    }
}
