using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace MedicalExperimentation
{
    // Lets the player pick exactly 3 reagents (repeats allowed) from the curated subset, then queues the
    // experiment on the bench. This custom picker is the one piece vanilla bills cannot do.
    public class Dialog_PickReagents : Window
    {
        private readonly Building_ExperimentationBench bench;
        private readonly List<ThingDef> chosen = new List<ThingDef>();
        private Vector2 scroll;

        public Dialog_PickReagents(Building_ExperimentationBench bench)
        {
            this.bench = bench;
            forcePause = true;
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
        }

        public override Vector2 InitialSize => new Vector2(640f, 540f);

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 34f), "ME_NewExperiment".Translate());
            Text.Font = GameFont.Small;

            float y = 40f;
            Widgets.Label(new Rect(0f, y, inRect.width, 24f), "ME_PickHint".Translate());
            y += 28f;

            // Chosen slots (3)
            float slotW = 180f, slotH = 34f;
            for (int i = 0; i < 3; i++)
            {
                Rect slot = new Rect(i * (slotW + 8f), y, slotW, slotH);
                Widgets.DrawBox(slot);
                if (i < chosen.Count)
                {
                    Rect icon = new Rect(slot.x + 2f, slot.y + 2f, 30f, 30f);
                    Widgets.ThingIcon(icon, chosen[i]);
                    Widgets.Label(new Rect(slot.x + 36f, slot.y + 6f, slotW - 60f, 24f), chosen[i].LabelCap);
                    if (Widgets.ButtonText(new Rect(slot.xMax - 22f, slot.y + 6f, 20f, 22f), "x"))
                    {
                        chosen.RemoveAt(i);
                        break;
                    }
                }
                else
                {
                    Widgets.Label(new Rect(slot.x + 6f, slot.y + 6f, slotW - 12f, 24f), "ME_EmptySlot".Translate());
                }
            }
            y += slotH + 10f;

            // Reagent grid (scroll)
            Rect outRect = new Rect(0f, y, inRect.width, inRect.height - y - 90f);
            var all = ReagentSet.All;
            float rowH = 34f;
            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, all.Count * rowH);
            Widgets.BeginScrollView(outRect, ref scroll, viewRect);
            float ry = 0f;
            foreach (var def in all)
            {
                Rect row = new Rect(0f, ry, viewRect.width, rowH - 2f);
                if (Mouse.IsOver(row)) Widgets.DrawHighlight(row);
                Widgets.ThingIcon(new Rect(row.x + 2f, row.y + 1f, 30f, 30f), def);
                Widgets.Label(new Rect(row.x + 38f, row.y + 6f, row.width - 140f, 24f), def.LabelCap);
                if (chosen.Count < 3 && Widgets.ButtonText(new Rect(row.xMax - 96f, row.y + 4f, 90f, 26f), "ME_Add".Translate()))
                {
                    chosen.Add(def);
                }
                ry += rowH;
            }
            Widgets.EndScrollView();

            // Footer
            float fy = inRect.height - 80f;
            bool ready = chosen.Count == 3;

            if (ready)
            {
                string hint = PriorResultHint();
                if (hint != null)
                {
                    GUI.color = new Color(0.95f, 0.8f, 0.4f);
                    Widgets.Label(new Rect(0f, fy - 2f, inRect.width, 24f), hint);
                    GUI.color = Color.white;
                }
            }
            if (Widgets.ButtonText(new Rect(inRect.width - 320f, fy + 30f, 150f, 36f), "ME_Confirm".Translate()) && ready)
            {
                Queue();
                Close();
            }
            if (Widgets.ButtonText(new Rect(inRect.width - 160f, fy + 30f, 150f, 36f), "CancelButton".Translate()))
            {
                Close();
            }
            if (!ready)
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(0f, fy + 38f, 300f, 24f), "ME_NeedThree".Translate(chosen.Count));
                GUI.color = Color.white;
            }
        }

        // If this exact 3-reagent combo was tried before, tell the player what it made so they don't waste reagents.
        private string PriorResultHint()
        {
            var ledger = GameComponent_PharmaLedger.Instance;
            if (ledger == null) return null;
            string key = ExperimentRecipeDef.MakeKey(chosen);
            if (!ledger.ComboTried(key)) return null;
            if (ledger.ComboWasDud(key)) return "ME_ComboWasDud".Translate();
            ThingDef prod = ledger.ComboResult(key);
            if (prod == null) return null;
            string name = ledger.IsDiscovered(prod)
                ? prod.LabelCap.ToString()
                : "experimental compound " + CompMysteryDrug.CodeFor(prod);
            return "ME_ComboMade".Translate(name);
        }

        private void Queue()
        {
            // Aggregate duplicates into counts.
            var grouped = chosen.GroupBy(d => d)
                .Select(g => new ReagentCount(g.Key, g.Count()))
                .ToList();
            bench.AddOrder(new ExperimentOrder(grouped, false));
            Messages.Message("ME_ExperimentQueued".Translate(), bench, MessageTypeDefOf.TaskCompletion, false);
        }
    }
}
