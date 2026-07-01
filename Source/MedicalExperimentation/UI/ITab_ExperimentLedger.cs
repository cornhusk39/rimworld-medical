using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace MedicalExperimentation
{
    // Results ledger on the bench: discovered compounds, what remains to find (gaps), and failed combos.
    public class ITab_ExperimentLedger : ITab
    {
        private Vector2 scroll;

        public ITab_ExperimentLedger()
        {
            size = new Vector2(520f, 480f);
            labelKey = "ME_LedgerTab";
        }

        public override void FillTab()
        {
            var ledger = GameComponent_PharmaLedger.Instance;
            var recipes = DefDatabase<ExperimentRecipeDef>.AllDefsListForReading;
            // Group by product: many combos can map to the same compound (e.g. the failed/lethal dummies).
            var products = recipes.Select(r => r.product).Where(p => p != null).Distinct().ToList();
            int total = products.Count;
            int found = products.Count(p => ledger != null && ledger.IsDiscovered(p));

            Rect outer = new Rect(0f, 0f, size.x, size.y).ContractedBy(10f);
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(outer.x, outer.y, outer.width, 30f), "ME_LedgerHeader".Translate(found, total));
            Text.Font = GameFont.Small;

            Rect viewArea = new Rect(outer.x, outer.y + 36f, outer.width, outer.height - 36f);
            float rowH = 26f;
            // estimate content height
            int triedCount = ledger?.AllComboResults.Count ?? 0;
            float height = (total + triedCount + 6) * rowH + 80f;
            Rect view = new Rect(0f, 0f, viewArea.width - 16f, height);
            Widgets.BeginScrollView(viewArea, ref scroll, view);
            float y = 0f;

            // Discovered (grouped by product)
            y = SectionHeader(view.width, y, "ME_LedgerDiscovered".Translate());
            foreach (var p in products.Where(p => ledger != null && ledger.IsDiscovered(p)))
            {
                var r = recipes.First(x => x.product == p);
                Rect row = new Rect(0f, y, view.width, rowH);
                if (Mouse.IsOver(row)) Widgets.DrawHighlight(row);
                Widgets.Label(new Rect(4f, y + 3f, view.width * 0.45f, rowH), p.LabelCap);
                Widgets.Label(new Rect(view.width * 0.45f, y + 3f, view.width * 0.55f, rowH), ComboLabel(r));
                y += rowH;
            }

            // Gaps: only a count of what remains. Unidentified compounds are anonymous - no per-compound
            // codes or hints, so the ledger never gives away identities you haven't tested for.
            y += 6f;
            y = SectionHeader(view.width, y, "ME_LedgerGaps".Translate());
            int gaps = products.Count(p => ledger == null || !ledger.IsDiscovered(p));
            GUI.color = new Color(0.7f, 0.7f, 0.7f);
            Widgets.Label(new Rect(4f, y + 3f, view.width, rowH), "ME_GapsCount".Translate(gaps));
            GUI.color = Color.white;
            y += rowH;

            // Tried combinations log: every combo tried, and what it produced.
            if (triedCount > 0)
            {
                y += 6f;
                y = SectionHeader(view.width, y, "ME_LedgerTried".Translate());
                foreach (var kv in ledger.AllComboResults)
                {
                    Rect row = new Rect(0f, y, view.width, rowH);
                    if (Mouse.IsOver(row)) Widgets.DrawHighlight(row);
                    bool dud = kv.Value.NullOrEmpty();
                    GUI.color = dud ? new Color(0.75f, 0.55f, 0.55f) : new Color(0.8f, 0.85f, 0.8f);
                    Widgets.Label(new Rect(4f, y + 3f, view.width * 0.55f, rowH), KeyToLabel(kv.Key));
                    Widgets.Label(new Rect(view.width * 0.55f, y + 3f, view.width * 0.45f, rowH), ResultLabel(kv.Value));
                    GUI.color = Color.white;
                    y += rowH;
                }
            }

            Widgets.EndScrollView();
        }

        private static float SectionHeader(float width, float y, string label)
        {
            Text.Font = GameFont.Small;
            GUI.color = new Color(0.85f, 0.85f, 0.7f);
            Widgets.Label(new Rect(0f, y, width, 24f), label);
            GUI.color = Color.white;
            Widgets.DrawLineHorizontal(0f, y + 22f, width);
            return y + 26f;
        }

        private static string ComboLabel(ExperimentRecipeDef r)
        {
            return string.Join(" + ", r.reagents.Where(x => x.thingDef != null)
                .Select(x => x.count > 1 ? x.count + "x " + x.thingDef.label : x.thingDef.label));
        }

        private string ResultLabel(string productDefName)
        {
            if (productDefName.NullOrEmpty()) return "ME_ResultNothing".Translate();
            var def = DefDatabase<ThingDef>.GetNamedSilentFail(productDefName);
            if (def == null) return "?";
            var ledger = GameComponent_PharmaLedger.Instance;
            if (ledger != null && ledger.IsDiscovered(def)) return def.LabelCap;
            return "ME_UndiscoveredCompound".Translate();
        }

        private static string KeyToLabel(string key)
        {
            var parts = key.Split('|');
            var grouped = parts.GroupBy(p => p)
                .Select(g =>
                {
                    var def = DefDatabase<ThingDef>.GetNamedSilentFail(g.Key);
                    string lab = def?.label ?? g.Key;
                    return g.Count() > 1 ? g.Count() + "x " + lab : lab;
                });
            return string.Join(" + ", grouped);
        }
    }
}
