#if !PRODUCTION_BUILD
// #define DEBUG_GEN
// #define DEBUG_GEN_SEED

// uncomment this to pause between steps when generating ship
// #define DEBUG_GEN_STEPS
#endif
#define GEN_VISUAL
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Game.CodeGen;
using Game.Commands;
using Game.Constants;
using Game.Data;
using Game.Modifiers;
using Game.Scenarios;
using Game.Systems.Ships;
using Game.Systems.UIUX;
using KL.Clock;
using KL.Grid;
using KL.Utils;
using UnityEngine;
using UnityEngine.InputSystem;

using Game.Utils;
using Game.UI;
namespace Game.Procgen {
    public sealed class ShipFactory {
        #if !PRODUCTION_BUILD
        [SelfInit]
        private static void Cleanup() {
            IsGenDebug = false;
        }
        public static bool IsGenDebug;
        #else
        public const bool IsGenDebug = false;
        #endif
        public GameState S;
        private double loadStartTime;
        private readonly List<IShipGenStage> stages = new();

        public GameSetupData Data;
        public ShipShapeConfig ShipShape;
        private ShipSpec spec;
        public ShipSpec Spec => spec;

        public bool IsSuccess;
        private readonly bool canStartOver;
        public readonly ShipFactoryReport Report;
        private readonly int center;
        private readonly int width;
        private readonly int height;
        private readonly int seed;
        private int cellWidth;
        private int cellHeight;
        public PosRect ConfinementArea { get; private set; }
        private bool needsConfinement;
        public NPCShip ForShip;
        // Set when the structure stages are added; the new-game preview reads it to draw rooms
        public ShipLayout Layout;

        public void Confine(PosRect area) {
            ConfinementArea = area;
            needsConfinement = true;
        }

        public void AddStage(IShipGenStage stage) {
            HashSet<Type> stageTypes = null;
            foreach (var attr in stage.GetType().GetCustomAttributes(true)) {
                bool isValid = true;
                if (attr is MustGoAfterGenStageAttribute mustGoAfter) {
                    stageTypes ??= stages.Select((s) => s.GetType()).ToHashSet();
                    foreach (var must in mustGoAfter.MustGoAfter) {
                        // a subclass (TreeDoorGenStage for InnerDoorGenStage) does the job too
                        if (!stageTypes.Any(t => must.IsAssignableFrom(t))) {
                            D.Err("Refusing to add ShipGenStage: {0}. It must go after: {1}", stage, must);
                            isValid = false;
                        }
                    }
                } else if (attr is MustGoBeforeGenStageAttribute mustGoBefore) {
                    stageTypes ??= stages.Select((s) => s.GetType()).ToHashSet();
                    foreach (var must in mustGoBefore.MustGoBefore) {
                        if (stageTypes.Contains(must)) {
                            D.Err("Refusing to add ShipGenStage: {0}. It must go before: {1}", stage, must);
                            isValid = false;
                        }
                    }
                }
                if (!isValid) { return; }
            }
            stages.Add(stage);
        }
        public ShipFactory(PosRect rect, int seed, bool canStartOver) {
            Report = new ShipFactoryReport();
            Report.Seed = seed;
            Report.ShipWidth = rect.Width;
            Report.ShipHeight = rect.Height;
            Report.ShipSize = rect.Size;
            this.canStartOver = canStartOver;
            center = rect.CenterPos;
            width = rect.Width;
            height = rect.Height;
            this.seed = seed;
        }

        public void SetCellSize(int w, int h) {
            cellWidth = w;
            cellHeight = h;
        }

        public void Initialize(GameState s, bool preview) {
            #if !PRODUCTION_BUILD
            IsGenDebug = false;
            // DONT REMOVE THIS LINE. It helps reproducing random test failures if they depend on some seed
            D.Log("Will generate ship with seed: {0} {1}x{2}. Preview: {3}",
                seed, width, height, preview);
            #else
            // DONT REMOVE THIS LINE
            D.Warn("Will generate ship with seed: {0} {1}x{2}. Preview: {3}",
                seed, width, height, preview);
            #endif
            S = s;
            loadStartTime = Time.realtimeSinceStartupAsDouble;
            spec = new ShipSpec();
            if (s != null) {
                s.Map.ShipSpec = spec;
            }
            spec.Width = width;
            spec.Height = height;
            spec.Seed = seed;
            spec.CanStartOver = canStartOver;
            Pos.ToXY(center, out var x, out var y);
            spec.CenterX = x;
            spec.CenterY = y;
            spec.CreateRng();
            if (needsConfinement) {
                spec.SetGenArea(ConfinementArea);
            }

            if (cellWidth == 0) {
                spec.CellWidth = spec.Rng.Range(
                    Consts.MinShipCellWidth, Consts.MaxShipCellWidth);
                spec.CellHeight = spec.Rng.Range(
                    Consts.MinShipCellHeight, Consts.MaxShipCellHeight);
            } else {
                spec.CellWidth = cellWidth;
                spec.CellHeight = cellHeight;
            }

            spec.TemplateWidth = width / spec.CellWidth;
            spec.TemplateHeight = height / spec.CellHeight / 2;

            spec.CreateTemplate();
            if (ShipFactoryReport.WantsFullReport) {
                Report.SpaceMap = S.Query.GetSpaceMap.Ask();
            }
        }

        private IEnumerator RunStage(IShipGenStage stage) {
            ShipFactoryReport.ReportEntry reportEntry = default;
            if (!stage.SkipRNG) {
                reportEntry = new ShipFactoryReport.ReportEntry();
                var rng = spec.Rng.Fork();
                reportEntry.SeedBefore = rng.Int;
                reportEntry.Stage = stage.GetType().ToString();
                stage.SetRng(rng);
            }
            var runStart = Time.realtimeSinceStartupAsDouble;
            stage.Initialize(spec, S);

            int attempts = 3;
            do {
                var islandsBefore = S.Sys.Areas.IslandCount;
                if (stage.RequiresAreas) {
                    S.Sig.ForceAreas.Send();
                }
                var startTime = Time.realtimeSinceStartupAsDouble;
                #if DEBUG_GEN
                var startMem = P.UsedMemory;
                #endif
                stage.Attempts++;
                #if DEBUG_GEN
                // D.Warn("GEN_SHIP Starting {0}", stage.LogPrefix);
                #endif

                #if DEBUG_GEN_SEED
                    // D.Warn("GEN_SHIP Seed debug before {0} next int {1}",
                        //stage.LogPrefix, Rng.Int);
                #endif

                if (stage.Attempts > 1) {
                    LoadingMsg($"{stage.Message} (x{stage.Attempts})");
                } else {
                    LoadingMsg(stage.Message);
                }

                if (stage.ForceDebug) {
                    D.Err("Before stage: {0}. Press spacebar to continue when ready.", stage);
                    yield return PauseGen(S, this);
                }

                if (stage.IsAsync) {
                    yield return stage.ApplyAsync();
                } else {
                    stage.Apply();
                }

                if (stage.ForceDebug) {
                    D.Err("After stage: {0}. Press spacebar to continue when ready.",
                        stage);
                    yield return PauseGen(S, this);
                }

                #if DEBUG_GEN_SEED
                    // D.Warn("GEN_SHIP Seed debug after {0} next int {1}",
                        //stage.LogPrefix, spec.Rng.Int);
                #endif

                spec = stage.Result;
                #if DEBUG_GEN
                var endMem = P.UsedMemory;
                #endif


                // Wait for some frames without any commands before continuing
                if (!Ready.Areas) {
                    S.Sig.ForceAreas.Send();
                    D.Ass(Ready.Areas, "Areas not ready!");
                }
                if (!Ready.CmdQ) {
                    D.Err("Command queue expected to be ready but isn't: {0}",
                        D.DumpCollection(S.CmdQ.DumpPendingCommands));
                }

                if (stage.ProducesIslands) {
                    S.Sig.ForceAreas.Send();
                    //yield return Ready.WaitForAreas(this);
                }
                #if DEBUG_GEN
                var islandsAfter = S.Sys.Areas.IslandCount;
                D.Warn(string.Format("{0} [{1}]  Islands {2} -> {3})",
                    stage.LogPrefix, spec.GenCommand,  islandsBefore, islandsAfter));
                #endif

                #if DEBUG_GEN_STEPS
                DumpSpec(spec, stage.GetType().ToString());
                #endif
            } while (stage.NeedsRepeating && attempts-- > 0);

            if (!stage.SkipRNG) {
                reportEntry.SeedAfter = stage.StageRng.Int;
                reportEntry.Attempts = stage.Attempts;
                reportEntry.Duration = Time.realtimeSinceStartupAsDouble - runStart;
                #if !PRODUCTION_BUILD
                if (The.TestsAreRunning && ShipFactoryReport.WantsFullReport) {
                    reportEntry.CalculateDefs();
                }
                #endif
                Report.Add(reportEntry);
            }
        }

        // 583252946 - preview different
        public IEnumerator Preview(Texture2D tex) {
            try {
                Initialize(null, true);
                var pixels = tex.GetPixels();
                for (int i = 0; i < pixels.Length; i++) {
                    pixels[i] = Consts.ColorTransparent;
                }
                tex.SetPixels(pixels);
            } catch (System.Exception ex) {
                D.LogEx(ex, "Failed preview init");
            }
            for (int i = 0; i < stages.Count; i++) {
                var stage = stages[i];
                if (stage is IShipPreviewGenStage pstage) {
                    if (stage.RequiresAreas) {
                        D.Err("Preview stage required areas! It shouldn't: {0}", stage);
                    }
                    // Don't remove this! this touches the rng as the runStage would
                    if (!stage.SkipRNG) {
                        var rng = spec.Rng.Fork();
                        var reportSeedBefore = rng.Int;
                        pstage.SetRng(rng);
                    }
                    pstage.Initialize(spec, null);
                    int attempts = 3;
                    do {
                        if (pstage.IsAsync) {
                            yield return pstage.GeneratePreviewAsync(tex);
                        } else {
                            try {
                                pstage.GeneratePreview(tex);
                            } catch (System.Exception ex) {
                                D.LogEx(ex, "Failed gen stage preview: {0}", pstage);
                            }
                        }
                    } while (stage.NeedsRepeating && attempts-- > 0);
                } else {
                    //D.Warn("Stumbled upon the first gen stage that is not ship preview: {0}", stage);
                    break;
                }
            }
            try {
                tex.Apply(true, false);
            } catch (System.Exception ex) {
                D.LogEx(ex, "Failed applying preview tex");
            }
        }

        /// <summary>
        /// For quick generation in-game
        /// </summary>
        /// <param name="s"></param>
        /// <returns></returns>
        public IEnumerator RunStages(GameState s) {
            IsSuccess = true;
            var totalStartTime = Time.realtimeSinceStartupAsDouble;
            IShipGenStage lastStage = null;
            S.BB.Set(BBKeyH.DestructionMatYield, 0.33f);
            for (int i = 0; i < stages.Count; i++) {
                IShipGenStage stage = stages[i];
                lastStage = stage;
                if (stage is IWithScenarioConfig withCfg) {
                    D.Err("Unsupported stage with scenario config in RunStages! {0}", stage);
                    // withCfg.SetConfig(Config);
                }
                yield return RunStage(stage);

                #if DEBUG_GEN_STEPS
                //if (!The.TestsAreRunning) {
                    yield return PauseGen(S, this);
                //}
                #endif
            }
            S.Sig.ForceAreas.Send();
            S.BB.Remove(BBKeyH.DestructionMatYield, 0f);
            S.Map.CheckStructuralIntegrity();
            S.Sig.ResetTileGraphics.Send();
            S.Flush();
            #if DEBUG_GEN
            // D.Warn("GEN_SHIP {0} finished in {1:0.00}s", spec.GenCommand,
                //totalEndTime - totalStartTime);
            DumpSpec(spec, "final");
            #endif
        }

        public IEnumerator Build(GameState s) {
            IsSuccess = true;
            var totalStartTime = Time.realtimeSinceStartupAsDouble;
            Initialize(s, false);
            //AdaptiveVSync.SetMinFPS();
            #if !DEBUG_GEN && !GEN_VISUAL
            S.Sig.GenerationRunning.Send(true);
            #endif
            S.Clock.SetSpeed(ClockSpeed.Stopped);
            S.BB.Set(BBKeyH.DestructionMatYield, 0.33f);
            var startTime = 0d;
            var vsyncBefore = QualitySettings.vSyncCount;
            var targetFPSBefore = Application.targetFrameRate;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 1000;
            IShipGenStage lastStage = null;
            The.SysSig.AddLoadingStages.Send(stages.Count);
            for (int i = 0; i < stages.Count; i++) {
                if (lastStage != null) {
                    var endTime = Time.realtimeSinceStartupAsDouble;
                    #if DEBUG_GEN
                    D.Log("{0} in {1:0.00000}s [{2}]",
                        lastStage.LogPrefix, endTime - startTime,
                            spec.GenCommand);
                    #endif
                }
                startTime = Time.realtimeSinceStartupAsDouble;
                IShipGenStage stage = stages[i];
                lastStage = stage;
                if (stage is IWithScenarioConfig withCfg) {
                    withCfg.SetConfig(Data);
                }
                yield return RunStage(stage);
                The.SysSig.AdvanceLoadingStage.Send();
                #if !PRODUCTION_BUILD
                if (spec.CPUPos > 0) {
                    var cpu = EntityUtils.ParentObjAt(spec.CPUPos);
                    if (cpu?.Definition?.IdH != DefIdsH.ObjectsDevicesShipComputer) {
                        D.Err("spec.CPUPos does not point at actual ship computer! {0} | {1} | {2}",
                            Pos.String(spec.CPUPos), cpu, stage);
                    }
                }
                #endif
                if (canStartOver) {
                    var startOver = stage.StartOver ||
                        (stage.NeedsRepeating && stage.StartOverIfNeedsRepeating);
                    if (startOver) {
                        IsSuccess = false;
                        D.ErrSoft("Stage {0} requested to start over! Seed: {1}",
                            stage, seed);
                        S.Sig.ForceAreas.Send();
                        S.CmdQ.Enqueue(new CmdTilemapClear());
                        while (S.CmdQ.Size > 0) {
                            yield return null;
                            if (!GameState.IsCurrent(S)) { yield break; }
                        }
                        // To clear all info items from previous gen
                        S.Sys.Info.Unload();
                        break;
                    }
                }

                #if DEBUG_GEN_STEPS
                //if (!The.TestsAreRunning) {
                    yield return PauseGen(S, this);
                //}
                #endif
            }
            // S.Clock.SetSpeed(ClockSpeed.Normal);
            #if !DEBUG_GEN && !GEN_VISUAL
            S.Sig.GenerationRunning.Send(false);
            #endif
            S.Sig.ForceAreas.Send();
            Application.targetFrameRate = targetFPSBefore;
            QualitySettings.vSyncCount = vsyncBefore;
            S.BB.Remove(BBKeyH.DestructionMatYield, 0f);
            S.Map.CheckStructuralIntegrity();
            S.Sig.ResetTileGraphics.Send();
            if (!spec.HasGenArea) {
                // if we have a gen area we are making a derelict, otherwise
                // set default rotation to Right
                // FIXME generate ships with all directions
                S.Sys.ShipNav.ShipDirection = Facing.Type.Right;
            }

            //AdaptiveVSync.UnsetMinFps();
            var totalEndTime = Time.realtimeSinceStartupAsDouble;
            // Apply removals and such
            S.Flush();
            LoadingMsg("Ready");
            The.SysSig.LoadingMsg.Send(null);
            #if DEBUG_GEN
            // D.Warn("GEN_SHIP {0} finished in {1:0.00}s", spec.GenCommand,
                //totalEndTime - totalStartTime);
            DumpSpec(spec, "final");
            #endif
        }

        public static IEnumerator PauseGen(GameState S, object requestor) {
            #if !PRODUCTION_BUILD
            IsGenDebug = true;
            #endif
            //Uncomment to step through the gen one stage at a time
            yield return Ready.WaitForCmdQ(requestor);
            yield return null;
            if (!GameState.IsCurrent(S)) { yield break; }

            S.Sig.ResetTileGraphics.Send();
            yield return null;
            if (!GameState.IsCurrent(S)) { yield break; }
            Ready.SetRendering(true, "Visual steps");
            Ready.SetGame(true, "Visual steps");
            Ready.SetUserInput(true, "Gen Debug");

            The.ScreenBlock.HideImmediate();
            The.SysSig.ShowLoadingScreen.Send(null, false);

            while (Keyboard.current?.spaceKey.wasPressedThisFrame == false) {
                yield return null;
                if (!GameState.IsCurrent(S)) { yield break; }
            }
        }

        private void DumpSpec(ShipSpec spec, string stage) {
            D.Warn("Spec dump {0}", stage);
            D.Warn(spec.ToString());
            // D.Warn(spec.DebugString);
        }


        private void LoadingMsg(string msg) {
            The.SysSig.LoadingMsg.Send(msg);
        }

        public override string ToString() {
            return $"ShipFactory[{seed}|{D.DumpInstance(spec)}]";
        }

        public void ResetStages() {
            foreach (var stage in stages) {
                stage.Reset();
            }
        }
    }
}
