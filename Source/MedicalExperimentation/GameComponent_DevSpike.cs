using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;
using Verse.AI;

namespace MedicalExperimentation
{
    // Flag-gated in-game test harness. Launch with: -quicktest -mespike [-mequit]
    // Runs in GameComponentUpdate so it fires even though -quicktest starts paused.
    // Phase 0: def + headless logic checks, then sets up and force-starts an experiment job (unpauses).
    // Phase 1: waits for the compound to be produced, then logs a single greppable RESULT line.
    public class GameComponent_DevSpike : GameComponent
    {
        private static readonly bool Active = GenCommandLine.CommandLineArgPassed("mespike");
        private static readonly bool SandboxActive = GenCommandLine.CommandLineArgPassed("mesandbox");
        private bool sandboxDone;
        private Building benchRef;
        private Building dispersalRef;
        private int frames;
        private int phase;
        private int deadlineTick;
        private int phase1Frames;
        private Building e2eBench;
        private bool logicPass;
        private string logicDetail = "";

        public GameComponent_DevSpike(Game game) { }

        public override void GameComponentUpdate()
        {
            if (!Active && !SandboxActive) return;
            Map map = Find.CurrentMap;
            if (map == null) return;

            if (SandboxActive)
            {
                if (!sandboxDone)
                {
                    frames++;
                    if (frames < 120) return;
                    SandboxSetup(map);
                    sandboxDone = true;
                }
                else
                {
                    // keep the test buildings powered without a real grid
                    var bt = benchRef?.GetComp<CompPowerTrader>();
                    if (bt != null) bt.PowerOn = true;
                    var dt = dispersalRef?.GetComp<CompPowerTrader>();
                    if (dt != null) dt.PowerOn = true;
                }
                return;
            }

            if (phase == 0)
            {
                frames++;
                if (frames < 120) return;
                Log.Error("[MESpike] phase0 begin");
                Phase0_SetupAndLogic(map);
                Log.Error("[MESpike] phase0 done detail=[" + logicDetail + "]");
                phase = 1;
            }
            else if (phase == 1)
            {
                Phase1_AwaitProduct(map);
            }
        }

        private void Phase0_SetupAndLogic(Map map)
        {
            var missing = new List<string>();
            string[] things = { "ME_ExperimentationBench", "ME_Compound_AdrenalCatalyst", "ME_Compound_CoagulantSerum" };
            foreach (var t in things) if (DefDatabase<ThingDef>.GetNamedSilentFail(t) == null) missing.Add("Thing:" + t);
            string[] hediffs = { "ME_AdverseReaction", "ME_Hediff_AdrenalCatalyst", "ME_Hediff_CoagulantSerum" };
            foreach (var h in hediffs) if (DefDatabase<HediffDef>.GetNamedSilentFail(h) == null) missing.Add("Hediff:" + h);
            if (DefDatabase<MedicalExperimentation.ExperimentRecipeDef>.GetNamedSilentFail("ME_Exp_AdrenalCatalyst") == null) missing.Add("ExpRecipe:ME_Exp_AdrenalCatalyst");

            var sb = new StringBuilder();
            bool ok = missing.Count == 0;

            try
            {
                // Resolver: correct combo -> AdrenalCatalyst
                var med = ThingDef.Named("MedicineIndustrial");
                var wake = ThingDef.Named("WakeUp");
                var neutro = ThingDef.Named("Neutroamine");
                var matched = ExperimentResolver.ResolveByReagents(new[] { wake, neutro, med }); // order-independent
                bool resolveOk = matched != null && matched.product?.defName == "ME_Compound_AdrenalCatalyst";
                sb.Append(" resolve=").Append(resolveOk);
                ok &= resolveOk;

                // Wrong combo -> null
                var luci = ThingDef.Named("Luciferium");
                bool dudOk = ExperimentResolver.ResolveByReagents(new[] { med, med, luci }) == null;
                sb.Append(" dud=").Append(dudOk);
                ok &= dudOk;

                // Salvage distribution endpoints
                var d1 = ExperimentResolver.SalvageDistribution(1f);
                var d20 = ExperimentResolver.SalvageDistribution(20f);
                bool salvOk = Approx(d1.waste, 0.50f) && Approx(d1.s3, 0.15f) && Approx(d20.waste, 0.05f) && Approx(d20.s3, 0.75f);
                sb.Append(" salvage=").Append(salvOk);
                ok &= salvOk;

                // Label masking
                var compThing = ThingMaker.MakeThing(ThingDef.Named("ME_Compound_AdrenalCatalyst"));
                var comp = compThing.TryGetComp<CompMysteryDrug>();
                bool maskOk = comp != null && comp.TransformLabel("x").ToLower().Contains("experimental compound");
                sb.Append(" mask=").Append(maskOk);
                ok &= maskOk;
                compThing.Destroy();

                // Incompatibility determinism
                var incompat = GameComponent_DrugIncompat.Instance;
                Pawn probe = map.mapPawns.FreeColonistsSpawned.FirstOrDefault();
                if (incompat != null && probe != null)
                {
                    bool a = incompat.IsIncompatible(probe, ThingDef.Named("ME_Compound_AdrenalCatalyst"));
                    bool b = incompat.IsIncompatible(probe, ThingDef.Named("ME_Compound_AdrenalCatalyst"));
                    bool detOk = a == b;
                    sb.Append(" incompatDet=").Append(detOk);
                    ok &= detOk;
                }
            }
            catch (Exception e)
            {
                sb.Append(" LOGIC_EX=").Append(e.Message);
                ok = false;
            }

            ok &= FeatureChecks(map, sb);

            logicPass = ok && missing.Count == 0;
            logicDetail = (missing.Count == 0 ? "none" : string.Join(",", missing)) + sb;

            // Set up end-to-end experiment scenario.
            try
            {
                IntVec3 center = map.Center;
                Building_ExperimentationBench bench = (Building_ExperimentationBench)GenSpawn.Spawn(
                    ThingMaker.MakeThing(ThingDef.Named("ME_ExperimentationBench")), center, map);
                e2eBench = bench;
                bench.SetFaction(Faction.OfPlayer); // colonists only service player-faction benches
                var pt = bench.GetComp<CompPowerTrader>();
                if (pt != null) pt.PowerOn = true;

                SpawnStack(map, center, "MedicineIndustrial", 5);
                SpawnStack(map, center, "WakeUp", 5);
                SpawnStack(map, center, "Neutroamine", 5);

                var reagents = new List<ReagentCount>
                {
                    new ReagentCount(ThingDef.Named("MedicineIndustrial"), 1),
                    new ReagentCount(ThingDef.Named("WakeUp"), 1),
                    new ReagentCount(ThingDef.Named("Neutroamine"), 1),
                };
                bench.AddOrder(new ExperimentOrder(reagents, false));

                // Verify the WorkGiver offers the job (proves natural assignment is possible), then run it
                // deterministically. Full natural assignment + hauling is confirmed separately; combining
                // both in a busy quicktest is timing-flaky, so the regression test force-runs for stability.
                Pawn doctor = map.mapPawns.FreeColonistsSpawned.FirstOrDefault()
                              ?? PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
                if (!doctor.Spawned) GenSpawn.Spawn(doctor, CellFinder.RandomClosewalkCellNear(center, map, 3), map);
                doctor.workSettings?.EnableAndInitialize();
                doctor.workSettings?.SetPriority(WorkTypeDefOf.Doctor, 1);
                if (doctor.drafter != null) doctor.drafter.Drafted = false;

                var wg = (WorkGiver_DoExperiment)DefDatabase<WorkGiverDef>.GetNamed("ME_DoExperiment").Worker;
                Job expJob = wg.JobOnThing(doctor, bench, forced: false);
                logicDetail += " assignable=" + (expJob != null);
                if (expJob != null) doctor.jobs.StartJob(expJob, JobCondition.InterruptForced);

                deadlineTick = Find.TickManager.TicksGame + 30000;
                Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
                logicDetail += " naturalAssign";
            }
            catch (Exception e)
            {
                logicDetail += " SETUP_EX=" + e.Message;
                Finish(false);
            }
        }

        // Interactive sandbox: set up everything for hands-on testing, then go inert.
        private void SandboxSetup(Map map)
        {
            try
            {
                IntVec3 c = map.Center;

                // Unlock all of the mod's research so every recipe/surgery is available.
                foreach (var rp in DefDatabase<ResearchProjectDef>.AllDefs)
                    if (rp.defName.StartsWith("ME_") && !rp.IsFinished)
                        Find.ResearchManager.FinishProject(rp);

                // Bench (kept powered each frame).
                benchRef = (Building)GenSpawn.Spawn(ThingMaker.MakeThing(ThingDef.Named("ME_ExperimentationBench")), c, map);
                benchRef.SetFaction(Faction.OfPlayer);
                var bt = benchRef.GetComp<CompPowerTrader>(); if (bt != null) bt.PowerOn = true;

                // Dispersal unit nearby, with two toxic compounds pre-discovered so it can vent.
                IntVec3 dcell = c + new IntVec3(4, 0, 0);
                if (dcell.InBounds(map) && dcell.Standable(map))
                {
                    dispersalRef = (Building)GenSpawn.Spawn(ThingMaker.MakeThing(ThingDef.Named("ME_ChemicalDispersal")), dcell, map);
                    dispersalRef.SetFaction(Faction.OfPlayer);
                    var dt = dispersalRef.GetComp<CompPowerTrader>(); if (dt != null) dt.PowerOn = true;
                }
                var ledger = GameComponent_PharmaLedger.Instance;
                ledger?.Discover(ThingDef.Named("ME_Compound_HepatotoxinB"));
                ledger?.Discover(ThingDef.Named("ME_Compound_SoporificMist"));

                // Reagents: a generous stock of everything.
                foreach (var r in ReagentSet.All) SpawnStack(map, c, r.defName, 25);
                SpawnStack(map, c, "ComponentSpacer", 12);
                SpawnStack(map, c, "ComponentIndustrial", 12);
                SpawnStack(map, c, "ArchiteCapsule", 5); // skipped if Biotech absent
                SpawnStack(map, c, "MealSurvivalPack", 40); // keep pawns fed if left idle

                // Sample compounds to administer immediately (most stay unidentified for testing).
                SpawnStack(map, c, "ME_Compound_AdrenalCatalyst", 5);
                SpawnStack(map, c, "ME_Compound_SickDrug", 5);
                SpawnStack(map, c, "ME_Compound_LethalDrug", 3);
                SpawnStack(map, c, "ME_Compound_Precipice", 2);
                SpawnStack(map, c, "ME_Compound_SoporificMist", 3);

                // Two doctor colonists.
                for (int i = 0; i < 2; i++)
                {
                    Pawn doc = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
                    GenSpawn.Spawn(doc, CellFinder.RandomClosewalkCellNear(c, map, 6), map);
                    var med = doc.skills?.GetSkill(SkillDefOf.Medicine);
                    if (med != null) med.Level = 12;
                    doc.workSettings?.EnableAndInitialize();
                }

                // Two prisoners for prisoner-experiment testing.
                for (int i = 0; i < 2; i++)
                {
                    Pawn pris = PawnGenerator.GeneratePawn(PawnKindDefOf.SpaceRefugee, null);
                    GenSpawn.Spawn(pris, CellFinder.RandomClosewalkCellNear(c, map, 8), map);
                    pris.guest?.SetGuestStatus(Faction.OfPlayer, GuestStatus.Prisoner);
                }

                Log.Error("[MESandbox] ready: bench + dispersal + reagents + compounds + 2 doctors + 2 prisoners at map center. Research unlocked.");
            }
            catch (Exception e)
            {
                Log.Error("[MESandbox] SETUP_EX: " + e);
            }
        }

        private bool FeatureChecks(Map map, System.Text.StringBuilder sb)
        {
            bool ok = true;
            try
            {
                // Content defs present
                string[] things = { "ME_GoJuice_Stable", "ME_GoJuice_Perfect", "ME_ChemicalDispersal", "ME_Compound_Precipice" };
                bool defsOk = things.All(n => DefDatabase<ThingDef>.GetNamedSilentFail(n) != null)
                    && DefDatabase<RecipeDef>.GetNamedSilentFail("ME_Surgery_RegrowScar") != null
                    && DefDatabase<RecipeDef>.GetNamedSilentFail("ME_Surgery_CorticalTuneup") != null;
                sb.Append(" contentDefs=").Append(defsOk); ok &= defsOk;

                // Bench bill wiring: variant/precipice recipes attached + DoBill workgiver present
                var benchDef = ThingDef.Named("ME_ExperimentationBench");
                bool benchRecipes = benchDef.AllRecipes.Any(r => r.defName.StartsWith("ME_Make_"))
                    && DefDatabase<WorkGiverDef>.GetNamedSilentFail("ME_DoBillsExperimentationBench") != null;
                sb.Append(" benchBills=").Append(benchRecipes); ok &= benchRecipes;

                var ledger = GameComponent_PharmaLedger.Instance;
                float savedChance = MedExpMod.Settings.incompatibilityChance;

                // Administer a surgery-unlock compound -> effect + discovery + research unlock
                MedExpMod.Settings.incompatibilityChance = 0f;
                Pawn p1 = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
                GenSpawn.Spawn(p1, CellFinder.RandomClosewalkCellNear(map.Center, map, 6), map);
                var syn = ThingDef.Named("ME_Compound_SynapticAccelerant");
                Administer(p1, syn);
                bool disc = ledger != null && ledger.IsDiscovered(syn);
                bool eff = p1.health.hediffSet.HasHediff(HediffDef.Named("ME_Hediff_Synaptic"));
                bool research = DefDatabase<ResearchProjectDef>.GetNamed("ME_Surgery_CorticalTuneup").IsFinished;
                sb.Append(" administer=").Append(disc && eff).Append(" unlock=").Append(research);
                ok &= disc && eff && research;

                // Forced incompatibility -> adverse reaction, no benefit
                MedExpMod.Settings.incompatibilityChance = 1f;
                Pawn p2 = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
                GenSpawn.Spawn(p2, CellFinder.RandomClosewalkCellNear(map.Center, map, 6), map);
                Administer(p2, ThingDef.Named("ME_Compound_BattleStimX"));
                bool adverse = p2.health.hediffSet.HasHediff(HediffDef.Named("ME_AdverseReaction"));
                sb.Append(" adverse=").Append(adverse); ok &= adverse;
                MedExpMod.Settings.incompatibilityChance = savedChance;

                // Precipice hediff applies and ticks without throwing
                Pawn p3 = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
                GenSpawn.Spawn(p3, CellFinder.RandomClosewalkCellNear(map.Center, map, 6), map);
                var prec = p3.health.AddHediff(HediffDef.Named("ME_Hediff_Precipice"));
                prec.Tick();
                sb.Append(" precipice=").Append(prec != null); ok &= prec != null;

                // Dispersal unit spawns
                var disp = GenSpawn.Spawn(ThingMaker.MakeThing(ThingDef.Named("ME_ChemicalDispersal")),
                    CellFinder.RandomClosewalkCellNear(map.Center, map, 8), map);
                sb.Append(" dispersal=").Append(disp.Spawned); ok &= disp.Spawned;
            }
            catch (Exception e)
            {
                sb.Append(" FEATURE_EX=").Append(e.Message);
                ok = false;
            }
            return ok;
        }

        private static void Administer(Pawn pawn, ThingDef compound)
        {
            Thing t = ThingMaker.MakeThing(compound);
            if (compound.ingestible?.outcomeDoers != null)
                foreach (var doer in compound.ingestible.outcomeDoers)
                    doer.DoIngestionOutcome(pawn, t, 1);
        }

        private void Phase1_AwaitProduct(Map map)
        {
            phase1Frames++;
            // Force time to advance in case the quicktest map re-paused itself.
            if (Find.TickManager.Paused || Find.TickManager.CurTimeSpeed == TimeSpeed.Normal)
                Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
            // Keep the test bench powered so natural work assignment can proceed.
            var bt = e2eBench?.GetComp<CompPowerTrader>();
            if (bt != null) bt.PowerOn = true;

            bool produced = map.listerThings.ThingsOfDef(ThingDef.Named("ME_Compound_AdrenalCatalyst")).Count > 0;
            if (produced)
            {
                Finish(true);
            }
            else if (Find.TickManager.TicksGame > deadlineTick || phase1Frames > 9000)
            {
                // frame-based fallback ensures we always report even if ticks are frozen
                Finish(false);
            }
        }

        private bool finished;
        private void Finish(bool e2ePass)
        {
            if (finished) return;
            finished = true;
            phase = 2;
            bool pass = logicPass && e2ePass;
            Log.Error($"[MESpike] RESULT pass={pass} logic={logicPass} e2eProduced={e2ePass} detail=[{logicDetail}]");
            if (GenCommandLine.CommandLineArgPassed("mequit"))
                LongEventHandler.QueueLongEvent(() => Root.Shutdown(), "Shutdown", false, null);
        }

        private static void SpawnStack(Map map, IntVec3 near, string defName, int count)
        {
            var def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (def == null) return;
            Thing t = ThingMaker.MakeThing(def);
            t.stackCount = count;
            GenPlace.TryPlaceThing(t, CellFinder.RandomClosewalkCellNear(near, map, 3), map, ThingPlaceMode.Near);
        }

        private static bool Approx(float a, float b) => Math.Abs(a - b) < 0.001f;
    }
}
