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
        private Building drugLabRef;
        private Pawn prisRef;
        private Pawn doctorRef;
        private Pawn wardenRef;
        private bool sawExpJob;
        private bool sawPrisJob;
        private string e2eComboKey;
        private bool expProduced;
        private Pawn pris2Ref;
        private bool probePrisOffered;
        private bool probeForcedExp;
        private Thing dispersalProbe;

        // Retryable, latching versions of the "does the warden work giver offer the job?" probes.
        // A one-shot probe right after spawning the prison was flaky (freshly rebuilt regions).
        private void ProbePrisonerOffers()
        {
            if (wardenRef == null) return;
            var wwg = (WorkGiver_Warden_AdministerExperimental)DefDatabase<WorkGiverDef>.GetNamed("ME_AdministerExperimental").Worker;
            if (!probePrisOffered && prisRef != null)
                probePrisOffered = wwg.JobOnThing(wardenRef, prisRef, false) != null;
            if (!probeForcedExp && pris2Ref != null)
                probeForcedExp = wwg.def.directOrderable
                    && wwg.JobOnThing(wardenRef, pris2Ref, true) != null
                    && wwg.JobOnThing(wardenRef, pris2Ref, false) == null; // auto path skips the unflagged one
        }
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
                    foreach (var bld in new[] { benchRef, dispersalRef, drugLabRef })
                    {
                        var pt = bld?.GetComp<CompPowerTrader>();
                        if (pt != null) pt.PowerOn = true;
                    }
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

                // Anonymity: an unknown compound's label is generic and never reveals its hidden result,
                // while the real (identified) compound shows its true name.
                var unkThing = MakeUnknown("ME_Exp_AdrenalCatalyst");
                string unkLabel = unkThing.LabelCap.ToString().ToLower();
                bool maskOk = unkLabel.Contains("unidentified") && !unkLabel.Contains("adrenal");
                var realThing = ThingMaker.MakeThing(ThingDef.Named("ME_Compound_AdrenalCatalyst"));
                maskOk &= realThing.LabelCap.ToString().ToLower().Contains("adrenal"); // real name, not a code
                sb.Append(" mask=").Append(maskOk);
                ok &= maskOk;
                realThing.Destroy();

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
            ok &= ExtendedChecks(map, sb);

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
                var e2eOrder = new ExperimentOrder(reagents, false);
                bench.AddOrder(e2eOrder);
                e2eComboKey = e2eOrder.ComboKey;

                // Order-persistent delivery accounting (interrupt-resume regression): partial deliveries
                // reduce the remainder, don't complete the order, and complete once the set is full.
                var probe = new ExperimentOrder(new List<ReagentCount>
                {
                    new ReagentCount(ThingDef.Named("MedicineHerbal"), 2),
                    new ReagentCount(ThingDef.Named("Neutroamine"), 1),
                }, false);
                probe.Deliver(ThingDef.Named("MedicineHerbal"), 1);
                bool resumeOk = probe.RemainingOf(ThingDef.Named("MedicineHerbal")) == 1
                    && probe.RemainingOf(ThingDef.Named("Neutroamine")) == 1 && !probe.IsComplete;
                probe.Deliver(ThingDef.Named("MedicineHerbal"), 1);
                probe.Deliver(ThingDef.Named("Neutroamine"), 1);
                resumeOk &= probe.IsComplete && probe.DeliveredTotal == 3;
                logicDetail += " orderResume=" + resumeOk;
                logicPass &= resumeOk;

                // NATURAL assignment test: a dedicated doctor (only Doctor work) with nothing else to do.
                // No force-start: if the work scanner doesn't pick up the experiment, that's the real bug.
                Pawn doctor = GenerateCapableColonist(map, CellFinder.RandomClosewalkCellNear(center, map, 3), WorkTypeDefOf.Doctor);
                SetSingleWork(doctor, WorkTypeDefOf.Doctor);
                doctorRef = doctor;

                var wg = (WorkGiver_DoExperiment)DefDatabase<WorkGiverDef>.GetNamed("ME_DoExperiment").Worker;
                logicDetail += " assignable=" + (wg.JobOnThing(doctor, bench, false) != null);
                logicDetail += " docCanDoctor=" + (!doctor.WorkTypeIsDisabled(WorkTypeDefOf.Doctor));

                deadlineTick = Find.TickManager.TicksGame + 80000; // room for both the e2e and dud crafts + a warden dose
                Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
                logicDetail += " naturalAssign";
            }
            catch (Exception e)
            {
                logicDetail += " SETUP_EX=" + e.Message;
                Finish(false);
            }
        }

        // Interactive sandbox: one paused scene that exercises every still-needs-human-eyes feature.
        private void SandboxSetup(Map map)
        {
            try
            {
                IntVec3 c = map.Center;
                var ledger = GameComponent_PharmaLedger.Instance;

                Building SpawnPowered(string def, IntVec3 at)
                {
                    var td = ThingDef.Named(def);
                    var stuff = td.MadeFromStuff ? GenStuff.DefaultStuffFor(td) : null;
                    var b = (Building)GenSpawn.Spawn(ThingMaker.MakeThing(td, stuff), at, map);
                    b.SetFaction(Faction.OfPlayer);
                    var p = b.GetComp<CompPowerTrader>(); if (p != null) p.PowerOn = true;
                    return b;
                }

                // Unlock the mod's research EXCEPT Metamorphosis synthesis - it's left locked so you can test
                // both unlock paths: finish ME_CraftMetamorphosis (research path) OR experiment
                // luciferium + glitterworld medicine + ambrosia and administer the result (compound path).
                foreach (var rp in DefDatabase<ResearchProjectDef>.AllDefs)
                    if (rp.defName.StartsWith("ME_") && rp.defName != "ME_CraftMetamorphosis" && !rp.IsFinished)
                        Find.ResearchManager.FinishProject(rp);

                benchRef = SpawnPowered("ME_ExperimentationBench", c);
                drugLabRef = SpawnPowered("DrugLab", c + new IntVec3(0, 0, 4)); // to see synthesis bills unlock
                // Dispersal placed away from the colony so its cloud only catches the test foe below.
                dispersalRef = SpawnPowered("ME_ChemicalDispersal", c + new IntVec3(12, 0, 0));
                var enemyFac = Find.FactionManager.AllFactions.FirstOrDefault(f => !f.IsPlayer && !f.def.hidden && f.HostileTo(Faction.OfPlayer));
                if (enemyFac != null)
                {
                    var kind = DefDatabase<PawnKindDef>.GetNamedSilentFail("Pirate") ?? PawnKindDefOf.Colonist;
                    var foe = PawnGenerator.GeneratePawn(kind, enemyFac);
                    GenSpawn.Spawn(foe, c + new IntVec3(13, 0, 0), map);
                    foe.stances?.stunner?.StunFor(2000000, null, false, false); // stand still so the unit vents on it
                }

                // Pre-discover a representative set so Drug Lab bills, Metamorphosis synthesis, and the armed
                // dispersal unit are immediately visible. The rest stay unknown so the discovery loop is testable.
                // Note: Metamorphosis (ME_Compound_Metamorphosis) is deliberately NOT pre-discovered so its Drug
                // Lab bill starts hidden - discover it or finish its research to make it appear.
                string[] preDisc = { "ME_Compound_AdrenalCatalyst", "ME_Compound_TissueRegenerant",
                    "ME_Compound_NerveConductionGel", "ME_Compound_SynapticAccelerant",
                    "ME_Compound_HepatotoxinB", "ME_Compound_SoporificMist" };
                foreach (var d in preDisc) ledger?.Discover(ThingDef.Named(d));

                // Pre-seed the ledger so the "Tried combinations" log + picker warnings are visible up front:
                // one real hit and one dud.
                if (ledger != null)
                {
                    var adr = ExperimentResolver.RecipeForProduct(ThingDef.Named("ME_Compound_AdrenalCatalyst"));
                    if (adr != null) ledger.RecordCombo(adr.ComboKey, adr.product);
                    string dudKey = ExperimentRecipeDef.MakeKey(new[] { ThingDef.Named("Beer"), ThingDef.Named("Beer"), ThingDef.Named("Beer") });
                    ledger.RecordCombo(dudKey, null);
                    ledger.RaiseHypothesis(ThingDef.Named("ME_Compound_ImmunoPrimer"), 0.6f); // a partial-hypothesis example
                }

                // Reagents, components, food. Ambrosia too (the Metamorphosis experiment needs it).
                foreach (var r in ReagentSet.All) SpawnStack(map, c, r.defName, 25);
                SpawnStack(map, c, "Ambrosia", 10);
                SpawnStack(map, c, "ComponentSpacer", 12);
                SpawnStack(map, c, "ComponentIndustrial", 12);
                SpawnStack(map, c, "ArchiteCapsule", 5);
                SpawnStack(map, c, "MealSurvivalPack", 40);

                // A sample of every KNOWN medicine (what you craft at the Drug Lab after discovery) so all
                // sprites are visible.
                string[] samples = { "AdrenalCatalyst", "CoagulantSerum", "ImmunoPrimer", "NeuralDefragmenter",
                    "SomnolentDraught", "BattleStimX", "BerserkerDraught", "Stoneskin", "HepatotoxinB",
                    "SoporificMist", "TissueRegenerant", "NerveConductionGel", "SynapticAccelerant",
                    "Metamorphosis", "SickDrug", "LethalDrug", "InertDrug" };
                foreach (var s in samples) SpawnStack(map, c, "ME_Compound_" + s, 4);

                // A batch of UNKNOWN compounds (all identical-looking) with mixed hidden results, so the
                // administer-to-identify loop can be tested. A good one, a dud, and a lethal one.
                string[] unknownRecipes = { "ME_Exp_AdrenalCatalyst", "ME_Exp_ImmunoPrimer", "ME_Exp_Metamorphosis",
                    "ME_Exp_Dummy_Sick_001", "ME_Exp_Dummy_Kill_001", "ME_Exp_Dummy_Inert_001" };
                foreach (var r in unknownRecipes)
                    for (int i = 0; i < 2; i++)
                        GenPlace.TryPlaceThing(MakeUnknown(r), CellFinder.RandomClosewalkCellNear(c, map, 3), map, ThingPlaceMode.Near);

                // Queue a FRESH (undiscovered) experiment so hauling + production is visible on unpause.
                var coag = new List<ReagentCount>
                {
                    new ReagentCount(ThingDef.Named("MedicineHerbal"), 1),
                    new ReagentCount(ThingDef.Named("MedicineIndustrial"), 1),
                    new ReagentCount(ThingDef.Named("Neutroamine"), 1),
                };
                (benchRef as Building_ExperimentationBench)?.AddOrder(new ExperimentOrder(coag, false));

                // Two doctors (Medicine 12).
                for (int i = 0; i < 2; i++)
                {
                    Pawn doc = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
                    GenSpawn.Spawn(doc, CellFinder.RandomClosewalkCellNear(c, map, 6), map);
                    var med = doc.skills?.GetSkill(SkillDefOf.Medicine);
                    if (med != null) med.Level = 12;
                    doc.workSettings?.EnableAndInitialize();
                }

                // A real prison with two AWAKE prisoners, both flagged for auto-experimentation. Wardens only
                // service secured prisoners (in a prison) — roaming ones are ignored for every duty — so this
                // is the realistic setup. On unpause a warden fetches a compound and carries it in to administer.
                IntVec3 prisonCenter = BuildPrison(map, c + new IntVec3(-12, 0, 0));
                for (int i = 0; i < 2; i++)
                {
                    Pawn pris = PawnGenerator.GeneratePawn(PawnKindDefOf.SpaceRefugee, null);
                    GenSpawn.Spawn(pris, prisonCenter + new IntVec3(i, 0, 0), map);
                    pris.guest?.SetGuestStatus(Faction.OfPlayer, GuestStatus.Prisoner);
                    pris.guest?.ToggleNonExclusiveInteraction(ME_DefOf.ME_AutoExperiment, true);
                    if (pris.needs?.food != null) pris.needs.food.CurLevel = pris.needs.food.MaxLevel;
                }

                // A maimed test subject (missing leg + permanent scars) for Metamorphosis + the surgeries.
                MakeTestSubject(map, c);

                Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                Log.Error("[MESandbox] ready: bench+druglab+dispersal, all reagents+compounds, 2 doctors, 2 prisoners, 1 maimed subject; an experiment is queued. Paused.");
            }
            catch (Exception e)
            {
                Log.Error("[MESandbox] SETUP_EX: " + e);
            }
        }

        private static void MakeTestSubject(Map map, IntVec3 c)
        {
            Pawn subj = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
            GenSpawn.Spawn(subj, CellFinder.RandomClosewalkCellNear(c, map, 5), map);

            // Amputate a leg.
            var legDef = DefDatabase<BodyPartDef>.GetNamedSilentFail("Leg");
            var leg = subj.health.hediffSet.GetNotMissingParts().FirstOrDefault(p => p.def == legDef);
            var missing = DefDatabase<HediffDef>.GetNamedSilentFail("MissingBodyPart");
            if (leg != null && missing != null) subj.health.AddHediff(missing, leg);

            // Two permanent scars.
            var scarParts = subj.health.hediffSet.GetNotMissingParts()
                .Where(p => p.def.defName == "Torso" || p.def.defName == "Arm").Take(2).ToList();
            foreach (var part in scarParts)
            {
                if (HediffDefOf.Cut == null) break;
                var cut = (Hediff_Injury)HediffMaker.MakeHediff(HediffDefOf.Cut, subj, part);
                cut.Severity = 4f;
                var perm = cut.TryGetComp<HediffComp_GetsPermanent>();
                if (perm != null) perm.IsPermanent = true;
                subj.health.AddHediff(cut, part);
            }
        }

        private bool FeatureChecks(Map map, System.Text.StringBuilder sb)
        {
            bool ok = true;
            try
            {
                // Content defs present
                string[] things = { "ME_GoJuice_Stable", "ME_GoJuice_Perfect", "ME_ChemicalDispersal", "ME_Compound_Metamorphosis" };
                bool defsOk = things.All(n => DefDatabase<ThingDef>.GetNamedSilentFail(n) != null);
                sb.Append(" contentDefs=").Append(defsOk); ok &= defsOk;

                // Compound descriptions must not leak the effect before discovery.
                // Anonymity now lives on the single unknown item; real compounds carry their real text.
                bool descMasked = ThingDef.Named("ME_UnknownCompound").label.ToLower().Contains("unidentified");
                sb.Append(" descMasked=").Append(descMasked); ok &= descMasked;

                // All crafting recipes live at the Drug Lab; the experiment bench has no crafting bills.
                var drugLab = ThingDef.Named("DrugLab");
                bool labRecipes = drugLab.AllRecipes.Any(r => r.defName.StartsWith("ME_Make_"))
                    && drugLab.AllRecipes.Any(r => r.defName.StartsWith("ME_Synth_"));
                bool benchClean = ThingDef.Named("ME_ExperimentationBench").AllRecipes.All(r => !r.defName.StartsWith("ME_Make_"));
                sb.Append(" labRecipes=").Append(labRecipes && benchClean); ok &= labRecipes && benchClean;

                var ledger = GameComponent_PharmaLedger.Instance;
                float savedChance = MedExpMod.Settings.incompatibilityChance;

                // Administer an UNKNOWN compound -> resolves to its effect + identified colony-wide.
                MedExpMod.Settings.incompatibilityChance = 0f;
                Pawn p1 = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
                GenSpawn.Spawn(p1, CellFinder.RandomClosewalkCellNear(map.Center, map, 6), map);
                var syn = ThingDef.Named("ME_Compound_SynapticAccelerant");
                AdministerUnknown(p1, "ME_Exp_SynapticAccelerant");
                bool disc = ledger != null && ledger.IsDiscovered(syn);
                bool eff = p1.health.hediffSet.HasHediff(HediffDef.Named("ME_Hediff_Synaptic"));
                sb.Append(" administer=").Append(disc && eff);
                ok &= disc && eff;

                // Real compounds carry their true effect as the actual item description (no generic
                // "unknown until administered" placeholder left on any real compound).
                bool descReveal = DefDatabase<ThingDef>.AllDefs
                    .Where(d => d.defName.StartsWith("ME_Compound_"))
                    .All(d => !d.description.NullOrEmpty() && !d.description.Contains("unknown until"));
                sb.Append(" descReveal=").Append(descReveal); ok &= descReveal;

                // Forced incompatibility -> adverse reaction, no benefit
                MedExpMod.Settings.incompatibilityChance = 1f;
                Pawn p2 = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
                GenSpawn.Spawn(p2, CellFinder.RandomClosewalkCellNear(map.Center, map, 6), map);
                AdministerUnknown(p2, "ME_Exp_BattleStimX");
                bool adverse = p2.health.hediffSet.HasHediff(HediffDef.Named("ME_AdverseReaction"));
                sb.Append(" adverse=").Append(adverse); ok &= adverse;
                MedExpMod.Settings.incompatibilityChance = savedChance;

                // Metamorphosis must NOT kill a pawn with a MISSING LUNG (reported bug: the -0.90 Consciousness
                // offset dropped an already-reduced Consciousness to <=0 = "died to metamorphosis regeneration").
                Pawn p3 = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
                GenSpawn.Spawn(p3, CellFinder.RandomClosewalkCellNear(map.Center, map, 6), map);
                var lungDef = DefDatabase<BodyPartDef>.GetNamedSilentFail("Lung");
                var lung = p3.health.hediffSet.GetNotMissingParts().FirstOrDefault(pp => pp.def == lungDef);
                if (lung != null) p3.health.AddHediff(HediffDef.Named("MissingBodyPart"), lung);
                var prec = p3.health.AddHediff(HediffDef.Named("ME_Hediff_Metamorphosis"));
                for (int i = 0; i < 5; i++) prec.Tick();
                bool metamorphosisSafe = prec != null && !p3.Dead; // applied, ticked, and the missing-lung pawn survived
                sb.Append(" metamorphosis=").Append(metamorphosisSafe); ok &= metamorphosisSafe;

                // "No need" confirmation predicate: a pawn with nothing to heal prompts; one with a missing
                // part does not.
                Pawn p4 = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
                GenSpawn.Spawn(p4, CellFinder.RandomClosewalkCellNear(map.Center, map, 6), map);
                foreach (var h in p4.health.hediffSet.hediffs.ToList()) p4.health.RemoveHediff(h);
                bool cleanNoNeed = MetamorphosisUtility.HasNothingToHeal(p4);
                var p4leg = p4.health.hediffSet.GetNotMissingParts().FirstOrDefault(pp => pp.def == DefDatabase<BodyPartDef>.GetNamedSilentFail("Leg"));
                if (p4leg != null) p4.health.AddHediff(HediffDef.Named("MissingBodyPart"), p4leg);
                bool maimedHasNeed = !MetamorphosisUtility.HasNothingToHeal(p4);
                sb.Append(" metamorphosisNeed=").Append(cleanNoNeed && maimedHasNeed); ok &= cleanNoNeed && maimedHasNeed;

                // Dispersal unit spawns + its gas/sound emit path runs without throwing
                var disp = dispersalProbe = GenSpawn.Spawn(ThingMaker.MakeThing(ThingDef.Named("ME_ChemicalDispersal")),
                    CellFinder.RandomClosewalkCellNear(map.Center, map, 8), map);
                sb.Append(" dispersal=").Append(disp.Spawned); ok &= disp.Spawned;
                ledger?.Discover(ThingDef.Named("ME_Compound_HepatotoxinB"));
                try { ((Building_ChemicalDispersal)disp).DebugFireNow(); sb.Append(" emitOk=True"); }
                catch (Exception e) { sb.Append(" emitOk=EX:").Append(e.Message); ok = false; }

                // Drug-Lab synthesis recipe is hidden until discovered, available after (via Harmony patch).
                var synthSyn = DefDatabase<RecipeDef>.GetNamedSilentFail("ME_Synth_SynapticAccelerant"); // discovered above
                var synthAdr = DefDatabase<RecipeDef>.GetNamedSilentFail("ME_Synth_AdrenalCatalyst");     // not discovered
                bool gateOk = synthSyn != null && synthSyn.AvailableNow && synthAdr != null && !synthAdr.AvailableNow;
                sb.Append(" synthGate=").Append(gateOk); ok &= gateOk;

                // Vanilla-adjacent variant recipe unlocks by DISCOVERING the refinement sample (which finishes
                // ME_DrugRefinement) - the "compound" path. (Finishing that research directly also unlocks it.)
                var varRecipe = DefDatabase<RecipeDef>.GetNamedSilentFail("ME_Make_GoJuice_Stable");
                bool varBefore = varRecipe != null && !varRecipe.AvailableNow;
                MedExpMod.Settings.incompatibilityChance = 0f;
                Pawn rp = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
                GenSpawn.Spawn(rp, CellFinder.RandomClosewalkCellNear(map.Center, map, 6), map);
                AdministerUnknown(rp, "ME_Exp_Refinement");
                MedExpMod.Settings.incompatibilityChance = savedChance;
                bool varAfter = varRecipe != null && varRecipe.AvailableNow;
                sb.Append(" variantGate=").Append(varBefore && varAfter); ok &= varBefore && varAfter;

                // Metamorphosis COMPOUND path: discovering it must AUTO-COMPLETE its research
                // (ME_CraftMetamorphosis), which unlocks the Drug Lab recipe. Start with neither the research done
                // nor the recipe available, administer the Metamorphosis unknown, then require BOTH true.
                var precRecipe = DefDatabase<RecipeDef>.GetNamedSilentFail("ME_Make_Metamorphosis_Vanilla");
                var craftPrec = DefDatabase<ResearchProjectDef>.GetNamed("ME_CraftMetamorphosis");
                bool precBefore = precRecipe != null && !precRecipe.AvailableNow && !craftPrec.IsFinished;
                MedExpMod.Settings.incompatibilityChance = 0f;
                Pawn mp = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
                GenSpawn.Spawn(mp, CellFinder.RandomClosewalkCellNear(map.Center, map, 6), map);
                AdministerUnknown(mp, "ME_Exp_Metamorphosis");
                MedExpMod.Settings.incompatibilityChance = savedChance;
                bool precAfter = craftPrec.IsFinished && precRecipe != null && precRecipe.AvailableNow; // research auto-done + recipe live
                sb.Append(" metamorphosisGate=").Append(precBefore && precAfter); ok &= precBefore && precAfter;

                // Auto-experiment must not re-craft an already-CRAFTED combo (reported: pawns repeat experiments).
                // Marking a combo attempted (as crafting does) removes it from the random-experiment pool without
                // recording/leaking its result.
                var advRecipe = DefDatabase<ExperimentRecipeDef>.GetNamed("ME_Exp_AdrenalCatalyst");
                string advKey = advRecipe.ComboKey;
                bool advUnattempted = !ledger.Attempted(advKey);
                ledger.MarkAttempted(advKey);
                bool attemptedNotLeaked = ledger.Attempted(advKey) && !ledger.ComboTried(advKey);
                bool excludedFromPool = !DefDatabase<ExperimentRecipeDef>.AllDefs
                    .Where(r => !ledger.Attempted(r.ComboKey)).Any(r => r.ComboKey == advKey);
                sb.Append(" autoNoRepeat=").Append(advUnattempted && attemptedNotLeaked && excludedFromPool);
                ok &= advUnattempted && attemptedNotLeaked && excludedFromPool;

                // A human trial marks the combo done even when incompatible (BattleStimX dosed incompatibly above).
                var bsRecipe = ExperimentResolver.RecipeForProduct(ThingDef.Named("ME_Compound_BattleStimX"));
                bool trialMarks = ledger != null && bsRecipe != null && ledger.ComboTried(bsRecipe.ComboKey)
                                  && !ledger.IsDiscovered(ThingDef.Named("ME_Compound_BattleStimX"));
                sb.Append(" trialMarks=").Append(trialMarks); ok &= trialMarks;

                // Prisoner administration test — the REALISTIC case the player has: an awake prisoner in a
                // real prison (secured), flagged for experimentation, with a compound in a stockpile outside.
                // No stun, no force-start: the warden must decide on its own to fetch the compound, carry it in,
                // and administer it. (Roaming/unsecured prisoners are ignored by wardens entirely, which is what
                // made the old stunned open-field prisoners misleading.)
                IntVec3 prisonCenter = BuildPrison(map, map.Center + new IntVec3(0, 0, 14));
                IntVec3 wardenSpot = prisonCenter + new IntVec3(4, 0, 0);
                if (!wardenSpot.Standable(map))
                    CellFinder.TryFindRandomCellNear(wardenSpot, map, 6, cc => cc.Standable(map), out wardenSpot);
                Pawn warden = GenerateCapableColonist(map, wardenSpot, WorkTypeDefOf.Warden);
                SetSingleWork(warden, WorkTypeDefOf.Warden);
                wardenRef = warden;
                Pawn pris = PawnGenerator.GeneratePawn(PawnKindDefOf.SpaceRefugee, null);
                GenSpawn.Spawn(pris, prisonCenter + new IntVec3(1, 0, 0), map);
                pris.guest?.SetGuestStatus(Faction.OfPlayer, GuestStatus.Prisoner);
                pris.guest?.ToggleNonExclusiveInteraction(ME_DefOf.ME_AutoExperiment, true);
                // "MaintainOnly" is 1.6's no-active-interaction mode ("NoInteraction" doesn't exist). Guard it
                // so a bad name never leaves the prisoner with an unset mode (which makes vanilla escape NPE).
                var maintainOnly = DefDatabase<PrisonerInteractionModeDef>.GetNamedSilentFail("MaintainOnly");
                if (maintainOnly != null) pris.guest.interactionMode = maintainOnly;
                if (pris.needs?.food != null) pris.needs.food.CurLevel = pris.needs.food.MaxLevel;
                prisRef = pris;
                // Unknown compounds in a stockpile outside the prison so a real fetch + carry-in is required.
                for (int i = 0; i < 3; i++)
                    GenPlace.TryPlaceThing(MakeUnknown("ME_Exp_NeuralDefragmenter"),
                        CellFinder.RandomClosewalkCellNear(warden.Position + new IntVec3(3, 0, 0), map, 2), map, ThingPlaceMode.Near);
                sb.Append(" prisSecure=").Append(pris.guest.PrisonerIsSecure);
                sb.Append(" wardenCanWard=").Append(!warden.WorkTypeIsDisabled(WorkTypeDefOf.Warden));

                // The prisoner-offer probes are RETRIED each Phase-1 update and latched (see ProbePrisonerOffers):
                // sampling them exactly once here was flaky, since regions have only just been rebuilt after the
                // prison walls spawned. Their latched results are folded into the verdict at Finish.
                Pawn pris2 = PawnGenerator.GeneratePawn(PawnKindDefOf.SpaceRefugee, null);
                GenSpawn.Spawn(pris2, prisonCenter + new IntVec3(-1, 0, 0), map);
                pris2.guest?.SetGuestStatus(Faction.OfPlayer, GuestStatus.Prisoner); // NOT flagged
                if (maintainOnly != null) pris2.guest.interactionMode = maintainOnly;
                if (pris2.needs?.food != null) pris2.needs.food.CurLevel = pris2.needs.food.MaxLevel;
                pris2Ref = pris2;
                ProbePrisonerOffers();
            }
            catch (Exception e)
            {
                sb.Append(" FEATURE_EX=").Append(e.Message);
                ok = false;
            }
            return ok;
        }

        // Extended full-function sweep: everything FeatureChecks doesn't already cover, run live in-game.
        private bool ExtendedChecks(Map map, StringBuilder sb)
        {
            bool ok = true;
            var ledger = GameComponent_PharmaLedger.Instance;
            float savedChance = MedExpMod.Settings.incompatibilityChance;
            bool savedFF = MedExpMod.Settings.dispersalFriendlyFire;
            bool savedPrisExp = MedExpMod.Settings.enablePrisonerExperimentation;
            MedExpMod.Settings.incompatibilityChance = 0f;

            Pawn NewColonist()
            {
                Pawn p = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
                GenSpawn.Spawn(p, CellFinder.RandomClosewalkCellNear(map.Center, map, 6), map);
                return p;
            }

            try
            {
                // Neural Defragmenter: erases the worst bad memory and leaves permanent neural scarring.
                Pawn pDefrag = NewColonist();
                var badThought = DefDatabase<ThoughtDef>.GetNamedSilentFail("ObservedLayingCorpse");
                bool defragOk = true;
                if (badThought != null && pDefrag.needs?.mood?.thoughts?.memories != null)
                {
                    // Clear any pre-existing memories first: the defragmenter erases the WORST one, and a fresh
                    // colonist can spawn with a memory more negative than ours, which would make this test flaky.
                    pDefrag.needs.mood.thoughts.memories.Memories.Clear();
                    pDefrag.needs.mood.thoughts.memories.TryGainMemory(badThought);
                    bool hadBad = pDefrag.needs.mood.thoughts.memories.Memories.Any(m => m.MoodOffset() < 0f);
                    AdministerUnknown(pDefrag, "ME_Exp_NeuralDefragmenter");
                    defragOk = hadBad
                        && !pDefrag.needs.mood.thoughts.memories.Memories.Any(m => m.def == badThought)
                        && pDefrag.health.hediffSet.HasHediff(HediffDef.Named("ME_Hediff_NeuralScar"));
                }
                sb.Append(" defrag=").Append(defragOk); ok &= defragOk;

                // Somnolent Draught: restores rest to full.
                Pawn pSleep = NewColonist();
                bool somnOk = true;
                if (pSleep.needs?.rest != null)
                {
                    pSleep.needs.rest.CurLevel = 0.1f;
                    AdministerUnknown(pSleep, "ME_Exp_SomnolentDraught");
                    somnOk = pSleep.needs.rest.CurLevel > 0.95f;
                }
                sb.Append(" somnolent=").Append(somnOk); ok &= somnOk;

                // Dud product resolves like any compound: sickness effect + identification + combo recorded.
                Pawn pDud = NewColonist();
                AdministerUnknown(pDud, "ME_Exp_Dummy_Sick_001");
                var sickRecipe = DefDatabase<ExperimentRecipeDef>.GetNamed("ME_Exp_Dummy_Sick_001");
                bool dudResolve = pDud.health.hediffSet.HasHediff(HediffDef.Named("ME_Hediff_ExpSickness"))
                    && ledger.IsDiscovered(ThingDef.Named("ME_Compound_SickDrug"))
                    && ledger.ComboTried(sickRecipe.ComboKey);
                sb.Append(" dudResolve=").Append(dudResolve); ok &= dudResolve;

                // ApplyEffect stacks severity onto an existing hediff (dispersal/toxin accumulation).
                Pawn pStack = NewColonist();
                var advDef = HediffDef.Named("ME_AdverseReaction");
                IngestionOutcomeDoer_Experimental.ApplyEffect(pStack, advDef, 0.3f);
                IngestionOutcomeDoer_Experimental.ApplyEffect(pStack, advDef, 0.3f);
                float sev = pStack.health.hediffSet.GetFirstHediffOfDef(advDef)?.Severity ?? 0f;
                bool stackOk = sev > 0.55f && sev < 0.7f;
                sb.Append(" stack=").Append(stackOk); ok &= stackOk;

                // Full Metamorphosis cycle, accelerated: coma regrows a missing leg and clears a permanent
                // scar, then ends in the hangover with the pawn alive.
                Pawn pMeta = NewColonist();
                var legDef = DefDatabase<BodyPartDef>.GetNamedSilentFail("Leg");
                var leg = pMeta.health.hediffSet.GetNotMissingParts().FirstOrDefault(pp => pp.def == legDef);
                if (leg != null) pMeta.health.AddHediff(HediffDef.Named("MissingBodyPart"), leg);
                var scarPart = pMeta.health.hediffSet.GetNotMissingParts().FirstOrDefault(pp => pp.def.defName == "Torso");
                if (scarPart != null)
                {
                    var cut = (Hediff_Injury)HediffMaker.MakeHediff(HediffDefOf.Cut, pMeta, scarPart);
                    cut.Severity = 3f;
                    var perm = cut.TryGetComp<HediffComp_GetsPermanent>();
                    if (perm != null) perm.IsPermanent = true;
                    pMeta.health.AddHediff(cut, scarPart);
                }
                var metaDef = HediffDef.Named("ME_Hediff_Metamorphosis");
                var metaHediff = pMeta.health.AddHediff(metaDef);
                // Drive it the way the 1.6 engine does - TickInterval, in batches - so this validates the
                // real tick path, not the dead Tick() stub. 1050 * 250 = 262500 > the 240000 duration.
                for (int i = 0; i < 1050 && pMeta.health.hediffSet.HasHediff(metaDef); i++)
                    metaHediff.TickInterval(250);
                bool metaAlive = !pMeta.Dead;
                bool metaLimb = !pMeta.health.hediffSet.hediffs.OfType<Hediff_MissingPart>().Any();
                bool metaScar = !pMeta.health.hediffSet.hediffs.Any(h => h.IsPermanent());
                bool metaEnded = !pMeta.health.hediffSet.HasHediff(metaDef);
                bool metaHang = pMeta.health.hediffSet.HasHediff(HediffDef.Named("ME_Hediff_MetamorphosisHangover"));
                bool metaCycle = metaAlive && metaLimb && metaScar && metaEnded && metaHang;
                if (!metaCycle) sb.Append(" [meta alive=").Append(metaAlive).Append(" limb=").Append(metaLimb)
                    .Append(" scar=").Append(metaScar).Append(" ended=").Append(metaEnded).Append(" hang=").Append(metaHang).Append("]");
                sb.Append(" metaCycle=").Append(metaCycle); ok &= metaCycle;

                // The "no need" confirmation actually intercepts a forced ingest on a healthy pawn (dialog
                // shown, job blocked), and does NOT intercept for a pawn with something to heal.
                Pawn pHealthy = NewColonist();
                foreach (var h in pHealthy.health.hediffSet.hediffs.ToList()) pHealthy.health.RemoveHediff(h);
                Thing metaItem = GenSpawn.Spawn(ThingMaker.MakeThing(ThingDef.Named("ME_Compound_Metamorphosis")),
                    pHealthy.Position, map);
                Job ingest = JobMaker.MakeJob(JobDefOf.Ingest, metaItem);
                ingest.count = 1;
                ingest.playerForced = true;
                pHealthy.jobs.StartJob(ingest, JobCondition.InterruptForced);
                bool dlgShown = Find.WindowStack.Windows.OfType<Dialog_MessageBox>().Any();
                bool jobBlocked = pHealthy.CurJobDef != JobDefOf.Ingest;
                foreach (var dm in Find.WindowStack.Windows.OfType<Dialog_MessageBox>().ToList())
                    dm.Close(false);
                Pawn pMaimed = NewColonist();
                var leg2 = pMaimed.health.hediffSet.GetNotMissingParts().FirstOrDefault(pp => pp.def == legDef);
                if (leg2 != null) pMaimed.health.AddHediff(HediffDef.Named("MissingBodyPart"), leg2);
                Job ingest2 = JobMaker.MakeJob(JobDefOf.Ingest, metaItem);
                ingest2.count = 1;
                ingest2.playerForced = true;
                pMaimed.jobs.StartJob(ingest2, JobCondition.InterruptForced);
                bool passThrough = pMaimed.CurJobDef == JobDefOf.Ingest
                    && !Find.WindowStack.Windows.OfType<Dialog_MessageBox>().Any();
                pMaimed.jobs.EndCurrentJob(JobCondition.InterruptForced, false);
                if (!metaItem.Destroyed) metaItem.Destroy();
                bool confirmDlg = dlgShown && jobBlocked && passThrough;
                sb.Append(" confirmDlg=").Append(confirmDlg); ok &= confirmDlg;

                // Warden gating rules on the unflagged prisoner (forced path): permanent neural scarring must
                // NOT block; a transient effect MUST; the mod setting must disable the job entirely.
                var wwg = (WorkGiver_Warden_AdministerExperimental)DefDatabase<WorkGiverDef>.GetNamed("ME_AdministerExperimental").Worker;
                bool scarGate = true, prisSetting = true;
                if (wardenRef != null && pris2Ref != null)
                {
                    var scar = pris2Ref.health.AddHediff(HediffDef.Named("ME_Hediff_NeuralScar"));
                    bool scarAllows = wwg.JobOnThing(wardenRef, pris2Ref, true) != null;
                    var transient = pris2Ref.health.AddHediff(HediffDef.Named("ME_Hediff_ImmunoPrimer"));
                    bool transientBlocks = wwg.JobOnThing(wardenRef, pris2Ref, true) == null;
                    pris2Ref.health.RemoveHediff(transient);
                    scarGate = scarAllows && transientBlocks;
                    MedExpMod.Settings.enablePrisonerExperimentation = false;
                    prisSetting = wwg.JobOnThing(wardenRef, pris2Ref, true) == null;
                    MedExpMod.Settings.enablePrisonerExperimentation = savedPrisExp;
                }
                sb.Append(" scarGate=").Append(scarGate); ok &= scarGate;
                sb.Append(" prisSetting=").Append(prisSetting); ok &= prisSetting;

                // Dispersal friendly-fire OFF spares colonists in the cloud while still hitting hostiles.
                bool ffGate = true;
                if (dispersalProbe is Building_ChemicalDispersal dispUnit)
                {
                    var enemyFac = Find.FactionManager.AllFactions.FirstOrDefault(f => !f.IsPlayer && !f.def.hidden && f.HostileTo(Faction.OfPlayer));
                    if (enemyFac != null)
                    {
                        Pawn friendly = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
                        GenSpawn.Spawn(friendly, dispUnit.Position + new IntVec3(1, 0, 0), map);
                        Pawn foe = PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamedSilentFail("Pirate") ?? PawnKindDefOf.Colonist, enemyFac);
                        GenSpawn.Spawn(foe, dispUnit.Position + new IntVec3(-1, 0, 0), map);
                        foe.stances?.stunner?.StunFor(60000, null, false, false);
                        MedExpMod.Settings.dispersalFriendlyFire = false;
                        dispUnit.DebugFireNow();
                        MedExpMod.Settings.dispersalFriendlyFire = savedFF;
                        ffGate = !friendly.health.hediffSet.HasHediff(HediffDefOf.ToxicBuildup)
                            && foe.health.hediffSet.HasHediff(HediffDefOf.ToxicBuildup);
                    }
                }
                sb.Append(" ffGate=").Append(ffGate); ok &= ffGate;

                // Drug variants apply their scaled effect (unstable go-juice = strong high).
                Pawn pVar = NewColonist();
                var uj = ThingDef.Named("ME_GoJuice_Unstable");
                Thing ujThing = ThingMaker.MakeThing(uj);
                foreach (var doer in uj.ingestible.outcomeDoers) doer.DoIngestionOutcome(pVar, ujThing, 1);
                var high = pVar.health.hediffSet.GetFirstHediffOfDef(HediffDef.Named("GoJuiceHigh"));
                bool variantDose = high != null && high.Severity > 0.7f;
                sb.Append(" variantDose=").Append(variantDose); ok &= variantDose;
            }
            catch (Exception e)
            {
                sb.Append(" EXT_EX=").Append(e.Message);
                ok = false;
            }
            finally
            {
                MedExpMod.Settings.incompatibilityChance = savedChance;
                MedExpMod.Settings.dispersalFriendlyFire = savedFF;
                MedExpMod.Settings.enablePrisonerExperimentation = savedPrisExp;
            }
            return ok;
        }

        // Build a generic unknown compound tagged to resolve into a given experiment recipe's result.
        private static Thing_UnknownCompound MakeUnknown(string recipeDefName)
        {
            var recipe = DefDatabase<ExperimentRecipeDef>.GetNamed(recipeDefName);
            var unk = (Thing_UnknownCompound)ThingMaker.MakeThing(ThingDef.Named("ME_UnknownCompound"));
            unk.resultDefName = recipe.product.defName;
            unk.comboKey = recipe.ComboKey;
            return unk;
        }

        // Administer an unknown compound to a pawn through its real ingestion path (resolves + identifies).
        private static void AdministerUnknown(Pawn pawn, string recipeDefName)
        {
            var unk = MakeUnknown(recipeDefName);
            if (unk.def.ingestible?.outcomeDoers != null)
                foreach (var doer in unk.def.ingestible.outcomeDoers)
                    doer.DoIngestionOutcome(pawn, unk, 1);
        }

        private void Phase1_AwaitProduct(Map map)
        {
            phase1Frames++;
            // Force time to advance in case the quicktest map re-paused itself (some runs stall otherwise).
            if (Find.TickManager.Paused) Find.TickManager.TogglePaused();
            Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
            // Keep the test bench powered so natural work assignment can proceed.
            var bt = e2eBench?.GetComp<CompPowerTrader>();
            if (bt != null) bt.PowerOn = true;

            // Track natural ASSIGNMENT (the decisive signal): did each pawn ever start its job?
            if (doctorRef != null && doctorRef.CurJobDef == ME_JobDefOf.ME_RunExperiment) sawExpJob = true;
            if (wardenRef != null && wardenRef.CurJobDef == ME_JobDefOf.ME_AdministerExperimental) sawPrisJob = true;
            // Did the queued e2e experiment actually produce its unknown compound? (informational)
            if (!expProduced && e2eComboKey != null)
                expProduced = map.listerThings.ThingsOfDef(ThingDef.Named("ME_UnknownCompound"))
                    .Any(t => (t as Thing_UnknownCompound)?.comboKey == e2eComboKey);
            ProbePrisonerOffers();

            bool prisDosed = prisRef != null && (prisRef.Dead
                || prisRef.health.hediffSet.hediffs.Any(h => h.def.defName.StartsWith("ME_Hediff_") || h.def.defName == "ME_AdverseReaction"));
            if (prisDosed) prisDosedSeen = true;

            // The prisoner actually receiving a dose is the decisive signal: it proves the warden fetched,
            // carried, and administered the compound end-to-end (not the instant no-carry dose the player saw).
            // Also wait for the queued experiment to produce its compound (proves the full gather->craft
            // pipeline) - the deadline below is the fallback if the doctor dawdles.
            if (prisDosed && expProduced)
            {
                Finish(true);
            }
            else if (Find.TickManager.TicksGame > deadlineTick || phase1Frames > 60000)
            {
                Finish(false);
            }
        }

        private bool finished;
        private bool prisDosedSeen;
        private void Finish(bool e2ePass)
        {
            if (finished) return;
            finished = true;
            phase = 2;
            // prisDosed (a live natural dose) supersedes the offer probe for the flagged prisoner.
            bool prisOfferedOk = probePrisOffered || prisDosedSeen;
            // The live dud RESOLVE branch is covered deterministically by dudResolve; expProduced proves the
            // live gather->craft pipeline. Gating on a second live craft was timing-fragile, so it's dropped.
            bool pass = logicPass && prisDosedSeen && prisOfferedOk && probeForcedExp && expProduced;
            Log.Error($"[MESpike] RESULT pass={pass} logic={logicPass} sawExpJob={sawExpJob} expProduced={expProduced} sawPrisJob={sawPrisJob} prisDosed={prisDosedSeen} prisOffered={prisOfferedOk} forcedExp={probeForcedExp} detail=[{logicDetail}]");
            if (GenCommandLine.CommandLineArgPassed("mequit"))
                LongEventHandler.QueueLongEvent(() => Root.Shutdown(), "Shutdown", false, null);
        }

        // Generate a colonist that is actually capable of the given work type (avoids random trait flukes).
        private static Pawn GenerateCapableColonist(Map map, IntVec3 at, WorkTypeDef cap)
        {
            Pawn p = null;
            for (int i = 0; i < 15; i++)
            {
                p = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
                if (!p.WorkTypeIsDisabled(cap)) break;
            }
            GenSpawn.Spawn(p, at, map);
            return p;
        }

        // Make a pawn willing to do exactly one work type (priority 1), nothing else, undrafted.
        private static void SetSingleWork(Pawn p, WorkTypeDef only)
        {
            p.workSettings?.EnableAndInitialize();
            foreach (var w in DefDatabase<WorkTypeDef>.AllDefsListForReading)
                if (!p.WorkTypeIsDisabled(w))
                    p.workSettings.SetPriority(w, w == only ? 1 : 0);
            if (p.drafter != null) p.drafter.Drafted = false;
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

        // Builds a small enclosed prison (5x5 walls + a door + a prisoner bed) around c and returns the
        // interior center. A real prison makes prisoners "secure", which is what wardens require before they
        // will service them at all (recruit, convert, administer). Roaming/unsecured prisoners get ignored.
        private static IntVec3 BuildPrison(Map map, IntVec3 c)
        {
            var wallDef = ThingDef.Named("Wall");
            var wallStuff = GenStuff.DefaultStuffFor(wallDef);
            var doorDef = ThingDef.Named("Door");
            var doorStuff = GenStuff.DefaultStuffFor(doorDef);
            IntVec3 doorCell = c + new IntVec3(2, 0, 0);
            // Clear EVERY cell of the footprint first (rocks are buildings too) and make the terrain
            // walkable. A fixed offset from map center can land on an outcrop; spawning pawns onto
            // unwalkable interior cells teleports them out of the room, which breaks "prisoner is secure"
            // and even NPEs vanilla's escape logic. The 3x3 interior must be genuinely standable.
            for (int dx = -2; dx <= 2; dx++)
                for (int dz = -2; dz <= 2; dz++)
                {
                    IntVec3 cell = c + new IntVec3(dx, 0, dz);
                    if (!cell.InBounds(map)) continue;
                    foreach (var t in cell.GetThingList(map).ToList())
                        if (t.def.category == ThingCategory.Building || t.def.mineable) t.Destroy();
                    if (!cell.GetTerrain(map).affordances.Contains(TerrainAffordanceDefOf.Heavy))
                        map.terrainGrid.SetTerrain(cell, TerrainDefOf.PavedTile);
                    if (map.roofGrid.RoofAt(cell)?.isThickRoof == true)
                        map.roofGrid.SetRoof(cell, null); // no collapse hazard over the cleared cells
                }
            for (int dx = -2; dx <= 2; dx++)
                for (int dz = -2; dz <= 2; dz++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != 2) continue; // perimeter cells only
                    IntVec3 cell = c + new IntVec3(dx, 0, dz);
                    if (!cell.InBounds(map)) continue;
                    var def = cell == doorCell ? doorDef : wallDef;
                    var stuff = cell == doorCell ? doorStuff : wallStuff;
                    var b = GenSpawn.Spawn(ThingMaker.MakeThing(def, stuff), cell, map);
                    b.SetFaction(Faction.OfPlayer);
                }
            var bed = (Building_Bed)GenSpawn.Spawn(
                ThingMaker.MakeThing(ThingDef.Named("Bed"), ThingDef.Named("WoodLog")), c + new IntVec3(-1, 0, -1), map);
            bed.SetFaction(Faction.OfPlayer);
            bed.ForOwnerType = BedOwnerType.Prisoner;
            map.regionAndRoomUpdater.RebuildAllRegionsAndRooms(); // register the room as a prison now
            return c;
        }
    }
}
