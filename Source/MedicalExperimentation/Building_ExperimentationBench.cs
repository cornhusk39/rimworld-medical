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
        private bool autoExperiment;
        private int nextOrderId;

        public List<ExperimentOrder> Orders => orders;
        public bool HasOrders => orders != null && orders.Count > 0;

        public ExperimentOrder GetOrder(int id) => orders.FirstOrDefault(o => o.id == id);

        public override void TickRare()
        {
            base.TickRare();
            // While auto-experiment is on, keep one experiment queued as long as an untried recipe's
            // reagents are available. Stops automatically when nothing new can be made.
            if (autoExperiment && Spawned && orders.Count == 0)
                TryQueueRandomExperiment(quiet: true);
        }

        public void AddOrder(ExperimentOrder order)
        {
            if (order.id < 0) order.id = nextOrderId++;
            orders.Add(order);
        }

        // Removing an order refunds any reagents already delivered to it, so cancellation never eats them.
        public void RemoveOrder(ExperimentOrder order)
        {
            RefundDelivered(order);
            orders.Remove(order);
        }

        // Deconstructing, moving (minify), or destroying the bench must not swallow reagents already
        // delivered to in-progress orders - drop them at the bench first, the way vanilla work tables
        // scatter their held ingredients. DeSpawn fires while the building is still on the map.
        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            if (Spawned && Map != null)
                foreach (var order in orders)
                    RefundDelivered(order);
            base.DeSpawn(mode);
        }

        private void RefundDelivered(ExperimentOrder order)
        {
            if (order == null || order.DeliveredTotal == 0 || Map == null) return;
            foreach (var kv in order.delivered)
            {
                int remaining = kv.Value;
                while (remaining > 0)
                {
                    Thing back = ThingMaker.MakeThing(kv.Key);
                    back.stackCount = Mathf.Min(remaining, kv.Key.stackLimit);
                    remaining -= back.stackCount;
                    GenPlace.TryPlaceThing(back, InteractionCell.IsValid ? InteractionCell : Position, Map, ThingPlaceMode.Near);
                }
            }
            order.ResetDelivered();
        }

        // Called when an experiment completes: repeat orders reset for the next round, one-shots leave the queue.
        public void NotifyOrderCompleted(ExperimentOrder order)
        {
            if (order == null) return;
            if (order.repeat) order.ResetDelivered();
            else orders.Remove(order);
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
                action = () => TryQueueRandomExperiment(quiet: false)
            };

            yield return new Command_Toggle
            {
                defaultLabel = "ME_AutoExperiment".Translate(),
                defaultDesc = "ME_AutoExperimentDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/Commands/ME_NewExperiment", false) ?? BaseContent.BadTex,
                isActive = () => autoExperiment,
                toggleAction = () => autoExperiment = !autoExperiment
            };

            if (HasOrders)
            {
                yield return new Command_Action
                {
                    defaultLabel = "ME_CancelExperiment".Translate(),
                    defaultDesc = "ME_CancelExperimentDesc".Translate(),
                    icon = ContentFinder<Texture2D>.Get("UI/Designators/Cancel", false) ?? BaseContent.BadTex,
                    action = OpenCancelMenu
                };
            }

            if (Prefs.DevMode)
            {
                yield return new Command_Action
                {
                    defaultLabel = "Dev: unlock all experimental recipes",
                    defaultDesc = "Discovers every experimental compound and finishes the mod's research, so all Drug Lab recipes (including Metamorphosis) become available.",
                    action = UnlockAllForDev
                };
            }
        }

        // Cancel one queued experiment (or all). Cancelling refunds any reagents already delivered to that
        // order, so no materials are ever lost to a change of mind.
        private void OpenCancelMenu()
        {
            var options = new List<FloatMenuOption>();
            foreach (var order in orders.ToList())
            {
                var o = order;
                string label = o.Label;
                if (o.DeliveredTotal > 0)
                    label += " (" + "ME_ReagentsDelivered".Translate(o.DeliveredTotal, o.RequiredTotal) + ")";
                options.Add(new FloatMenuOption(label, () => RemoveOrder(o)));
            }
            if (orders.Count > 1)
                options.Add(new FloatMenuOption("ME_CancelAll".Translate(), () =>
                {
                    foreach (var o in orders.ToList()) RemoveOrder(o);
                }));
            Find.WindowStack.Add(new FloatMenu(options));
        }

        // Dev-only: identify every compound and finish the mod's research so all crafting recipes appear.
        private void UnlockAllForDev()
        {
            var ledger = GameComponent_PharmaLedger.Instance;
            int discovered = 0;
            if (ledger != null)
            {
                foreach (var d in DefDatabase<ThingDef>.AllDefs)
                    if (d.HasComp(typeof(CompMysteryDrug)) && !ledger.IsDiscovered(d))
                    {
                        ledger.Discover(d);
                        discovered++;
                    }
            }
            foreach (var rp in DefDatabase<ResearchProjectDef>.AllDefs)
                if (rp.defName.StartsWith("ME_") && !rp.IsFinished)
                    Find.ResearchManager.FinishProject(rp);

            Messages.Message("Dev: discovered " + discovered + " compounds and finished mod research.",
                this, MessageTypeDefOf.TaskCompletion, false);
        }

        // Picks a random experiment that has not been tried yet and whose reagents are all available
        // in the colony right now, and queues it. Returns false (and, if not quiet, messages) when none fit.
        private bool TryQueueRandomExperiment(bool quiet)
        {
            var ledger = GameComponent_PharmaLedger.Instance;
            // Skip combos already CRAFTED (attempted), not just administered - otherwise the bench re-makes
            // combos whose unknown compound is still sitting untested, spamming duplicates.
            var candidates = DefDatabase<ExperimentRecipeDef>.AllDefs
                .Where(r => ledger == null || !ledger.Attempted(r.ComboKey))
                .Where(ReagentsAvailable)
                .ToList();

            if (candidates.Count == 0)
            {
                if (!quiet) Messages.Message("ME_NoRandom".Translate(), this, MessageTypeDefOf.RejectInput, false);
                return false;
            }

            var pick = candidates.RandomElement();
            var reagents = pick.reagents.Select(rc => new ReagentCount(rc.thingDef, rc.count)).ToList();
            AddOrder(new ExperimentOrder(reagents, false));
            if (!quiet) Messages.Message("ME_RandomQueued".Translate(), this, MessageTypeDefOf.TaskCompletion, false);
            return true;
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
                // Show delivery progress so deposited reagents don't read as vanished - INCLUDING fully
                // delivered orders awaiting the crafting step (that's exactly when items "look gone").
                var active = orders.FirstOrDefault(o => o.DeliveredTotal > 0);
                if (active != null)
                    s += "\n" + "ME_ReagentsDelivered".Translate(active.DeliveredTotal, active.RequiredTotal);
            }
            return s;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref orders, "ME_orders", LookMode.Deep);
            Scribe_Values.Look(ref autoExperiment, "ME_autoExperiment", false);
            Scribe_Values.Look(ref nextOrderId, "ME_nextOrderId", 0);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && orders == null) orders = new List<ExperimentOrder>();
            // Back-fill ids for orders saved before ids existed.
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                foreach (var o in orders)
                    if (o.id < 0) o.id = nextOrderId++;
        }
    }
}
