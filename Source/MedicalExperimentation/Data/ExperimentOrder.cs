using System.Collections.Generic;
using System.Linq;
using Verse;

namespace MedicalExperimentation
{
    // A queued experiment on a bench: the 3 reagents the player chose, and whether to repeat it.
    // Delivery progress lives HERE (not on the job), so an interrupted pawn resumes where the order
    // left off instead of re-gathering a full set, and a resumed job can never craft with a partial set.
    public class ExperimentOrder : IExposable
    {
        public int id = -1; // bench-assigned, stable identity for in-flight jobs
        public List<ReagentCount> reagents = new List<ReagentCount>();
        public bool repeat;
        public Dictionary<ThingDef, int> delivered = new Dictionary<ThingDef, int>();

        public ExperimentOrder() { }
        public ExperimentOrder(List<ReagentCount> reagents, bool repeat)
        {
            this.reagents = reagents;
            this.repeat = repeat;
        }

        public string ComboKey => ExperimentRecipeDef.MakeKey(reagents);

        public int DeliveredOf(ThingDef def) => delivered.TryGetValue(def, out int n) ? n : 0;

        public int RemainingOf(ThingDef def)
        {
            int need = reagents.Where(r => r.thingDef == def).Sum(r => r.count);
            return System.Math.Max(0, need - DeliveredOf(def));
        }

        public bool IsComplete => reagents.All(r => r.thingDef == null || RemainingOf(r.thingDef) == 0);

        public int DeliveredTotal => delivered.Values.Sum();
        public int RequiredTotal => reagents.Where(r => r.thingDef != null).Sum(r => r.count);

        public void Deliver(ThingDef def, int count)
        {
            if (def == null || count <= 0) return;
            delivered[def] = DeliveredOf(def) + count;
        }

        public void ResetDelivered() => delivered.Clear();

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
            Scribe_Values.Look(ref id, "id", -1);
            Scribe_Collections.Look(ref reagents, "reagents", LookMode.Deep);
            Scribe_Values.Look(ref repeat, "repeat", false);
            Scribe_Collections.Look(ref delivered, "delivered", LookMode.Def, LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (reagents == null) reagents = new List<ReagentCount>();
                if (delivered == null) delivered = new Dictionary<ThingDef, int>();
            }
        }
    }
}
