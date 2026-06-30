using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace MedicalExperimentation
{
    // The experimentation bench. Extends the vanilla work table (so it also runs normal production bills:
    // drug refinement, crafting discovered compounds). Adds a queue of player-designed experiments plus
    // the "New Experiment" command. The results ledger ITab is registered via the ThingDef.
    public class Building_ExperimentationBench : Building_WorkTable
    {
        private List<ExperimentOrder> orders = new List<ExperimentOrder>();

        public List<ExperimentOrder> Orders => orders;
        public bool HasOrders => orders != null && orders.Count > 0;

        public void AddOrder(ExperimentOrder order)
        {
            orders.Add(order);
        }

        public void RemoveOrder(ExperimentOrder order)
        {
            orders.Remove(order);
        }

        // Remove the first non-repeat order matching this combo (called when an experiment completes).
        public void NotifyOrderCompleted(string comboKey)
        {
            var match = orders.FirstOrDefault(o => o.ComboKey == comboKey && !o.repeat);
            if (match != null) orders.Remove(match);
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (var g in base.GetGizmos()) yield return g;

            yield return new Command_Action
            {
                defaultLabel = "ME_NewExperiment".Translate(),
                defaultDesc = "ME_NewExperimentDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/Commands/ME_NewExperiment", false) ?? BaseContent.BadTex,
                action = () => Find.WindowStack.Add(new Dialog_PickReagents(this))
            };

            yield return new Command_Action
            {
                defaultLabel = "ME_RandomExperiment".Translate(),
                defaultDesc = "ME_RandomExperimentDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/Commands/ME_NewExperiment", false) ?? BaseContent.BadTex,
                action = QueueRandomExperiment
            };
        }

        // Picks a random experiment that has not been tried yet and whose reagents are all available
        // in the colony right now, and queues it.
        private void QueueRandomExperiment()
        {
            var ledger = GameComponent_PharmaLedger.Instance;
            var candidates = DefDatabase<ExperimentRecipeDef>.AllDefs
                .Where(r => ledger == null || !ledger.ComboTried(r.ComboKey))
                .Where(ReagentsAvailable)
                .ToList();

            if (candidates.Count == 0)
            {
                Messages.Message("ME_NoRandom".Translate(), this, MessageTypeDefOf.RejectInput, false);
                return;
            }

            var pick = candidates.RandomElement();
            var reagents = pick.reagents.Select(rc => new ReagentCount(rc.thingDef, rc.count)).ToList();
            AddOrder(new ExperimentOrder(reagents, false));
            Messages.Message("ME_RandomQueued".Translate(), this, MessageTypeDefOf.TaskCompletion, false);
        }

        private bool ReagentsAvailable(ExperimentRecipeDef r)
        {
            foreach (var rc in r.reagents)
            {
                if (rc.thingDef == null) return false;
                int have = Map.listerThings.ThingsOfDef(rc.thingDef)
                    .Where(t => !t.IsForbidden(Faction.OfPlayer))
                    .Sum(t => t.stackCount);
                if (have < rc.count) return false;
            }
            return true;
        }

        public override string GetInspectString()
        {
            string s = base.GetInspectString();
            if (HasOrders)
            {
                if (!s.NullOrEmpty()) s += "\n";
                s += "ME_QueuedExperiments".Translate(orders.Count);
            }
            return s;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref orders, "ME_orders", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && orders == null) orders = new List<ExperimentOrder>();
        }
    }
}
