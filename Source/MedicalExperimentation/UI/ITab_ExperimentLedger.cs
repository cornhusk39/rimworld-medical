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
            int total = recipes.Count;
            int found = recipes.Count(r => ledger != null && ledger.IsDiscovered(r.product));

            Rect outer = new Rect(0f, 0f, size.x, size.y).ContractedBy(10f);
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(outer.x, outer.y, outer.width, 30f), "ME_LedgerHeader".Translate(found, total));
            Text.Font = GameFont.Small;

            Rect viewArea = new Rect(outer.x, outer.y + 36f, outer.width, outer.height - 36f);
            float rowH = 26f;
            // estimate content height
            int dudCount = ledger?.DudComboKeys.Count() ?? 0;
            float height = (total + dudCount + 6) * rowH + 80f;
            Rect view = new Rect(0f, 0f, viewArea.width - 16f, height);
            Widgets.BeginScrollView(viewArea, ref scroll, view);
            float y = 0f;

            // Discovered
            y = SectionHeader(view.width, y, "ME_LedgerDiscovered".Translate());
            foreach (var r in recipes.Where(r => ledger != null && ledger.IsDiscovered(r.product)))
            {
                Rect row = new Rect(0f, y, view.width, rowH);
                if (Mouse.IsOver(row)) Widgets.DrawHighlight(row);
                Widgets.Label(new Rect(4f, y + 3f, view.width * 0.45f, rowH), r.product.LabelCap);
                Widgets.Label(new Rect(view.width * 0.45f, y + 3f, view.width * 0.55f, rowH), ComboLabel(r));
                y += rowH;
            }

            // Gaps (undiscovered) - shown by code + hypothesis, not by combo
            y += 6f;
            y = SectionHeader(view.width, y, "ME_LedgerGaps".Translate());
            foreach (var r in recipes.Where(r => ledger == null || !ledger.IsDiscovered(r.product)))
            {
                Rect row = new Rect(0f, y, view.width, rowH);
                if (Mouse.IsOver(row)) Widgets.DrawHighlight(row);
                string code = CompMysteryDrug.CodeFor(r.product);
                Widgets.Label(new Rect(4f, y + 3f, view.width * 0.45f, rowH), "ME_UnknownCode".Translate(code));
                float hyp = ledger?.HypothesisStrength(r.product) ?? 0f;
                string hint = hyp > 0f ? "ME_HypoShort".Translate(r.effectSummary, hyp.ToStringPercent()) : "ME_NoData".Translate();
                GUI.color = new Color(0.7f, 0.7f, 0.7f);
                Widgets.Label(new Rect(view.width * 0.45f, y + 3f, view.width * 0.55f, rowH), hint);
                GUI.color = Color.white;
                y += rowH;
            }

            // Failed combos
            if (dudCount > 0)
            {
                y += 6f;
                y = SectionHeader(view.width, y, "ME_LedgerFailed".Translate());
                foreach (var key in ledger.DudComboKeys)
                {
                    Rect row = new Rect(0f, y, view.width, rowH);
                    if (Mouse.IsOver(row)) Widgets.DrawHighlight(row);
                    GUI.color = new Color(0.75f, 0.55f, 0.55f);
                    Widgets.Label(new Rect(4f, y + 3f, view.width, rowH), KeyToLabel(key));
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
