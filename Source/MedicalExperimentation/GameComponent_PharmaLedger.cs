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
        // every combo the colony has tried -> the product defName it made ("" means it produced nothing)
        private Dictionary<string, string> comboResults = new Dictionary<string, string>();
        // every combo that has been crafted at the bench, whether or not its result was administered yet.
        // Kept separate from comboResults so auto-experiment/random-queue don't re-craft an already-made combo
        // (the result is only recorded on administration, so comboResults alone would let it repeat).
        private HashSet<string> attempted = new HashSet<string>();

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

        // product == null means the combo produced nothing (a dud).
        public void RecordCombo(string comboKey, ThingDef product)
        {
            if (comboKey.NullOrEmpty()) return;
            comboResults[comboKey] = product?.defName ?? "";
            attempted.Add(comboKey);
        }

        public bool ComboTried(string comboKey) => comboResults.ContainsKey(comboKey);

        // Mark a combo as crafted (an unknown compound was produced). Does NOT reveal its result.
        public void MarkAttempted(string comboKey)
        {
            if (!comboKey.NullOrEmpty()) attempted.Add(comboKey);
        }

        // True once a combo has been crafted (or its result recorded). Used to stop the bench re-making it.
        public bool Attempted(string comboKey) => attempted.Contains(comboKey);

        // Returns the product ThingDef a tried combo made; null if untried OR if it was a dud (use ComboTried/ComboWasDud to tell apart).
        public ThingDef ComboResult(string comboKey)
        {
            if (comboResults.TryGetValue(comboKey, out string defName) && !defName.NullOrEmpty())
                return DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            return null;
        }

        public bool ComboWasDud(string comboKey) => comboResults.TryGetValue(comboKey, out string d) && d.NullOrEmpty();

        public IEnumerable<string> DiscoveredDefNames => discovered;
        public IReadOnlyDictionary<string, string> AllComboResults => comboResults;
        public int DiscoveredCount => discovered.Count;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref discovered, "discovered", LookMode.Value);
            Scribe_Collections.Look(ref hypotheses, "hypotheses", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref comboResults, "comboResults", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref attempted, "attempted", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (discovered == null) discovered = new HashSet<string>();
                if (hypotheses == null) hypotheses = new Dictionary<string, float>();
                if (comboResults == null) comboResults = new Dictionary<string, string>();
                if (attempted == null) attempted = new HashSet<string>();
                // Back-fill for saves from before this field existed: anything already recorded was attempted.
                foreach (var k in comboResults.Keys) attempted.Add(k);
            }
        }
    }
}
