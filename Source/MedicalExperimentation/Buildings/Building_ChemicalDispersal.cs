using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace MedicalExperimentation
{
    // Powered security device. When a hostile enters detection range it vents the selected (discovered)
    // toxic compound's effect over an area, then goes on cooldown. Chemicals do not discriminate: by
    // default everyone in the cloud is affected (toggle in mod settings).
    public class Building_ChemicalDispersal : Building
    {
        private const float DetectRadius = 7f;
        private const float CloudRadius = 3.5f;
        private const int CooldownTicks = 2500;

        private string selectedCompoundDefName;
        private int cooldownLeft;

        private CompPowerTrader powerComp;

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            powerComp = GetComp<CompPowerTrader>();
        }

        private ThingDef SelectedCompound
        {
            get
            {
                if (!selectedCompoundDefName.NullOrEmpty())
                {
                    var d = DefDatabase<ThingDef>.GetNamedSilentFail(selectedCompoundDefName);
                    if (d != null) return d;
                }
                // default to the first discovered toxic compound
                return DiscoveredToxicCompounds().FirstOrDefault();
            }
        }

        private static IEnumerable<ThingDef> DiscoveredToxicCompounds()
        {
            var ledger = GameComponent_PharmaLedger.Instance;
            foreach (var r in DefDatabase<ExperimentRecipeDef>.AllDefs)
                if (r.toxic && r.product != null && (ledger == null || ledger.IsDiscovered(r.product)))
                    yield return r.product;
        }

        private static HediffDef EffectHediffOf(ThingDef compound)
        {
            var doer = compound?.ingestible?.outcomeDoers?.OfType<IngestionOutcomeDoer_Experimental>().FirstOrDefault();
            return doer?.hediffDef;
        }

        public override void TickRare()
        {
            base.TickRare();
            if (cooldownLeft > 0) cooldownLeft -= 250;
            if (cooldownLeft > 0) return;
            if (powerComp != null && !powerComp.PowerOn) return;
            ThingDef compound = SelectedCompound;
            if (compound == null) return;
            if (EffectHediffOf(compound) == null) return; // never consume doses of a compound with no effect

            bool hostileNear = Map.mapPawns.AllPawnsSpawned.Any(p =>
                p.HostileTo(Faction.OfPlayer) && !p.Downed && !p.Dead
                && p.Position.InHorDistOf(Position, DetectRadius));
            if (!hostileNear) return;

            // Each shot consumes one dose of the selected compound from colony stock.
            if (!ConsumeDose(compound)) return;

            Emit(compound);
            cooldownLeft = CooldownTicks;
        }

        private int AvailableDoses(ThingDef compound)
        {
            if (compound == null) return 0;
            return Map.listerThings.ThingsOfDef(compound)
                .Where(t => !t.IsForbidden(Faction.OfPlayer))
                .Sum(t => t.stackCount);
        }

        private bool ConsumeDose(ThingDef compound)
        {
            Thing dose = Map.listerThings.ThingsOfDef(compound).FirstOrDefault(t => !t.IsForbidden(Faction.OfPlayer));
            if (dose == null) return false;
            dose.SplitOff(1).Destroy();
            return true;
        }

        // Test hook: vent immediately if a compound is selected (ignores cooldown/power/dose).
        public void DebugFireNow()
        {
            ThingDef c = SelectedCompound;
            if (c != null) Emit(c);
        }

        private void Emit(ThingDef compound)
        {
            HediffDef effect = EffectHediffOf(compound);
            if (effect == null) return;
            var doer = compound.ingestible.outcomeDoers.OfType<IngestionOutcomeDoer_Experimental>().First();
            float severity = doer.severity > 0f ? doer.severity : effect.initialSeverity;
            bool friendlyFire = MedExpMod.Settings?.dispersalFriendlyFire ?? true;

            // Vent sound (reused vanilla one-shot gas-deploy sound).
            DefDatabase<SoundDef>.GetNamedSilentFail("GasPack_Deploy")?.PlayOneShot(new TargetInfo(Position, Map));

            // Visible gas cloud: reuse the vanilla tox-gas fleck, plus colored puffs (teal for knockout,
            // green for toxic) so the cloud reads as the right compound.
            Color tint = compound.defName == "ME_Compound_SoporificMist"
                ? new Color(0.6f, 0.85f, 0.85f, 0.55f)
                : new Color(0.55f, 0.8f, 0.35f, 0.55f);
            FleckDef gasFleck = DefDatabase<FleckDef>.GetNamedSilentFail("Fleck_ToxGasSmall");
            foreach (var cell in GenRadial.RadialCellsAround(Position, CloudRadius, true))
            {
                if (!cell.InBounds(Map)) continue;
                if (gasFleck != null && Rand.Value < 0.85f)
                {
                    FleckCreationData d = FleckMaker.GetDataStatic(cell.ToVector3Shifted(), Map, gasFleck, Rand.Range(1.6f, 2.6f));
                    d.rotationRate = Rand.Range(-20f, 20f);
                    d.velocityAngle = Rand.Range(0f, 360f);
                    d.velocitySpeed = Rand.Range(0.3f, 0.7f);
                    Map.flecks.CreateFleck(d);
                }
                if (Rand.Value < 0.35f)
                    FleckMaker.ThrowDustPuffThick(cell.ToVector3Shifted(), Map, Rand.Range(1.4f, 2.2f), tint);
            }

            var affected = GenRadial.RadialDistinctThingsAround(Position, Map, CloudRadius, true)
                .OfType<Pawn>()
                .Where(p => !p.Dead && p.RaceProps.IsFlesh)
                .ToList();
            foreach (var p in affected)
            {
                if (!friendlyFire && p.Faction == Faction.OfPlayer) continue;
                IngestionOutcomeDoer_Experimental.ApplyEffect(p, effect, severity);
            }
            Messages.Message("ME_DispersalFired".Translate(compound.LabelCap), this, MessageTypeDefOf.NeutralEvent, false);
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (var g in base.GetGizmos()) yield return g;

            yield return new Command_Action
            {
                defaultLabel = "ME_SelectCompound".Translate(),
                defaultDesc = "ME_SelectCompoundDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/Commands/ME_NewExperiment", false) ?? BaseContent.BadTex,
                action = () =>
                {
                    var opts = DiscoveredToxicCompounds().Select(c =>
                        new FloatMenuOption(c.LabelCap, () => selectedCompoundDefName = c.defName)).ToList();
                    if (opts.Count == 0) opts.Add(new FloatMenuOption("ME_NoToxicDiscovered".Translate(), null));
                    Find.WindowStack.Add(new FloatMenu(opts));
                }
            };
        }

        public override string GetInspectString()
        {
            string s = base.GetInspectString();
            var c = SelectedCompound;
            if (!s.NullOrEmpty()) s += "\n";
            s += "ME_DispersalLoaded".Translate(c != null ? c.LabelCap.ToString() : "ME_None".Translate().ToString());
            if (c != null) s += "\n" + "ME_DispersalDoses".Translate(AvailableDoses(c));
            if (cooldownLeft > 0) s += "\n" + "ME_DispersalCooldown".Translate((cooldownLeft / 60).ToString());
            return s;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref selectedCompoundDefName, "selectedCompoundDefName");
            Scribe_Values.Look(ref cooldownLeft, "cooldownLeft", 0);
        }
    }
}
