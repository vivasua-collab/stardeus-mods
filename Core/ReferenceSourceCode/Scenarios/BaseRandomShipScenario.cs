using System.Collections;
using Game.Constants;
using Game.Data;
using Game.Procgen;
using Game.UI;
using KL.Grid;
using Game.Utils;
using Game.CodeGen;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using UnityEngine.SceneManagement;
using Game.Modifiers;
using System.Collections.Generic;
using Game.Mods;
using Game.Research;
using Game.Systems.Creatures;
using Game.Systems.UIUX;

namespace Game.Scenarios {
    public abstract class BaseRandomShipScenario : Scenario {
        protected float shipSize => ShipGenExperiments.ShipSizeOr(ModifierShipSize.Current);
        protected virtual string[] things => null;

        protected virtual string[] thingsInSpace => null;
        protected virtual bool addDeadBeings => true;

        protected virtual string[] objs => null;
        protected virtual int minRandomLoot => 5;
        protected virtual int maxRandomLoot => 15;
        protected virtual int lootTechLevel => 3;
        protected virtual int numShuttles => 1;
        protected virtual int maxIslandSize => Mathf.RoundToInt(1400 * shipSize * baseSizeScale);

        // The island and section sizes in the destruction stages were tuned on 72x52 starting
        // ships. Scaled by the scenario's own base size, smaller ships still split and lose
        // sections the way those did, instead of coming out of destruction in one piece.
        protected float baseSizeScale {
            get {
                var shape = ShipShapeConfig.ForScenario(Id);
                var w = (shape.WidthFrom + shape.WidthTo) * 0.5f;
                var h = (shape.HeightFrom + shape.HeightTo) * 0.5f;
                return w * h / (72.5f * 52.5f);
            }
        }
        protected virtual int maxEngines => Mathf.RoundToInt(4 * Mathf.Clamp(shipSize * 2, 4, 12));
        protected virtual string[] engineOptions => new string[] {
            DefIds.ObjectsDevicesThruster
        };
        public override (Species, int)[] GetStartingBeingCounts() {
            return new (Species, int)[] {
                (Species.Get(SpeciesIdH.Robot), 1),
                (Species.Get(SpeciesIdH.Drone), 1),
                (Species.Get(SpeciesIdH.Carrier), 1),
                (Species.Get(SpeciesIdH.Human), 2),
            };
        }
        protected virtual void BeforeGenerate() {}
        protected virtual void AfterGenerate() {
            The.ScreenBlock.Hide();
            A.Grid.RebuildGrid(true);
        }

        // Safety net for Tutorial/EstablishAutomatedProduction: when the intro found nobody
        // who can run the Crafting Station, the Assembler tutorial needs the Assembler
        // buildable. It costs DatoidTech, which needs a harvester that is out of reach on day
        // 3, so it is granted here rather than researched. Called from the intro's completion
        // callback, after the crew check has set (or not set) CrewCannotCraft. Silent unlock:
        // no unlock-effect notifications for something the player never chose.
        protected void UnlockAssemblerIfCrewCannotCraft() {
            if (!S.Query.GetQuestBool.Ask(QuestBoolIds.CrewCannotCraft)) { return; }
            var item = S.Research.GetAutoHash(DefIds.ResearchProductionAssemblyImproved);
            if (item == null) {
                D.Err("Assembler research node not found: {0}",
                    DefIds.ResearchProductionAssemblyImproved);
                return;
            }
            item.Unlock();
        }
        public override IEnumerator GeneratePreview(Texture2D tex, GameSetupData data) {
            if (tex == null) {
                D.Err("Texture2D not provided for GeneratePreview! {0}", this);
                yield break;
            }
            var mapW = ModifierMapSize.Current;
            if (tex.width != mapW || tex.height != mapW) {
                D.Err("Texture2D dimensions for preview are wrong! {0}x{1}, expected {2}x{3}",
                    tex.width, tex.height, mapW, mapW);
                yield break;
            }
            //var sf = new ShipFactory(new PosRect(0, 0, shipW, shipW), seed, false);
            var sf = CreateFactory(data, 0, true);
            yield return sf.Preview(tex);
            try {
                ShipPreviewRooms.Draw(tex, sf);
                tex.Apply(true, false);
            } catch (System.Exception ex) {
                D.LogEx(ex, "Failed drawing preview rooms");
            }
        }

        protected virtual void ConfigureShipFactory(ShipFactory factory, Rng rng,
                bool isPreview) {
            try {
                AddStructureStages(factory, rng);
            } catch (System.Exception ex) {
                D.LogEx(ex, "Failed AddStructureStages for scenario: {0}", this);
                ModRegistry.WarnAboutBrokenMod(this,
                    "Failed to AddStructureStages when configuring ShipFactory");
            }
            if (isPreview) { return; }
            try {
                AddPostStructureStages(factory, rng);
            } catch (System.Exception ex) {
                D.LogEx(ex, "Failed AddPostStructureStages for scenario: {0}", this);
                ModRegistry.WarnAboutBrokenMod(this,
                    "Failed to AddPostStructureStages when configuring ShipFactory");
            }
            try {
                AddDestructionStages(factory, rng);
            } catch (System.Exception ex) {
                D.LogEx(ex, "Failed AddDestructionStages for scenario: {0}", this);
                ModRegistry.WarnAboutBrokenMod(this,
                    "Failed to AddDestructionStages when configuring ShipFactory");
            }
            try {
                AddPostDestructionStages(factory, rng);
            } catch (System.Exception ex) {
                D.LogEx(ex, "Failed AddPostDestructionStages for scenario: {0}", this);
                ModRegistry.WarnAboutBrokenMod(this,
                    "Failed to AddPostDestructionStages when configuring ShipFactory");
            }
            try {
                AddLootStages(factory, rng);
            } catch (System.Exception ex) {
                D.LogEx(ex, "Failed AddLootStages for scenario: {0}", this);
                ModRegistry.WarnAboutBrokenMod(this,
                    "Failed to AddLootStages when configuring ShipFactory");
            }
            try {
                AddRandomLootStages(factory, rng);
            } catch (System.Exception ex) {
                D.LogEx(ex, "Failed AddRandomStages for scenario: {0}", this);
                ModRegistry.WarnAboutBrokenMod(this,
                    "Failed to AddRandomStages when configuring ShipFactory");
            }
            try {
                AddPostLootStages(factory, rng);
            } catch (System.Exception ex) {
                D.LogEx(ex, "Failed AddPostLootStages for scenario: {0}", this);
                ModRegistry.WarnAboutBrokenMod(this,
                    "Failed to AddPostLootStages when configuring ShipFactory");
            }
            try {
                AddCosmeticStages(factory, rng);
            } catch (System.Exception ex) {
                D.LogEx(ex, "Failed AddCosmeticStages for scenario: {0}", this);
                ModRegistry.WarnAboutBrokenMod(this,
                    "Failed to AddCosmeticStages when configuring ShipFactory");
            }
            try {
                AddFinalStages(factory, rng);
            } catch (System.Exception ex) {
                D.LogEx(ex, "Failed AddFinalStages for scenario: {0}", this);
                ModRegistry.WarnAboutBrokenMod(this,
                    "Failed to AddFinalStages when configuring ShipFactory");
            }
            try {
                AddValidationStages(factory, rng);
            } catch (System.Exception ex) {
                D.LogEx(ex, "Failed AddValidationStages for scenario: {0}", this);
                ModRegistry.WarnAboutBrokenMod(this,
                    "Failed to AddValidationStages when configuring ShipFactory");
            }
        }
        protected virtual void AddValidationStages(ShipFactory factory, Rng rng) {
            D.Err("No validation stages defined for scenario: {0}", this);
        }

        protected virtual void AddStructureStages(ShipFactory factory, Rng rng) {
            GenShip.AddStructureStages(factory, rng, maxEngines, 1f,
                centerAreaSize: 6, engineOptions, shipSize: shipSize, spotRooms: spotRooms);
        }

        // Rooms reserved before the rest grow (RoomPartitioner): one per stasis array
        protected virtual int spotRooms => Mathf.Max(1, StasisManifests?.Length ?? 1);

        protected virtual void AddDestructionStages(ShipFactory factory, Rng rng) {
            GenShip.AddDestructionStages(factory, rng, maxIslandSize);
        }

        protected virtual void AddPostStructureStages(ShipFactory factory, Rng rng) {
        }

        protected virtual void AddLootStages(ShipFactory factory, Rng rng) {
            GenShip.AddLootStages(factory, rng, numShuttles, things, thingsInSpace,
                objs, addDeadBeings);
        }

        protected virtual void AddRandomLootStages(ShipFactory factory, Rng rng) {
            /*
            Balance sucks with this
            var lootCount = rng.Range(minRandomLoot, maxRandomLoot);
            if (lootCount > 0) {
                GenShip.AddRandomLootStages(S, factory, rng, lootCount,
                    lootTechLevel, (d) => false);
            }
            */
        }

        protected virtual void AddPostLootStages(ShipFactory factory, Rng rng) {
            GenShip.AddPostLootStages(factory, rng);
        }
        protected virtual void AddCosmeticStages(ShipFactory factory, Rng rng) {
            GenShip.AddCosmeticStages(factory, rng);
        }

        protected virtual void AddPostDestructionStages(ShipFactory factory, Rng rng) {
            GenShip.AddPostDestructionStages(factory, rng);
        }
        protected virtual void AddFinalStages(ShipFactory factory, Rng rng) {

        }


        protected virtual void SetShipSize(Rng rng, GameSetupData data,
                ShipShapeConfig shape) {
            var gridW = ModifierMapSize.Current;
            var gridH = gridW;

            var shipW = Mathf.RoundToInt(rng.Range(shape.WidthFrom, shape.WidthTo) * shipSize);
            var shipH = Mathf.RoundToInt(rng.Range(shape.HeightFrom, shape.HeightTo) * shipSize);

            data.ShipWidth = Mathf.RoundToInt(Mathf.Min(shipW, gridW * 0.9f));
            data.ShipHeight = Mathf.RoundToInt(Mathf.Min(shipH, gridH * 0.9f));
            if (data.ShipWidth % 2 == 1) {
                data.ShipWidth++;
            }
            if (data.ShipHeight % 2 == 1) {
                data.ShipHeight++;
            }
            // D.Log("Will generate ship with actual: {0}x{1}", shipW, shipH);
        }

        public IEnumerator Generate(GameSetupData data) {
            yield return Ready.WaitForMods(this);
            var startTime = Time.realtimeSinceStartupAsDouble;

            BeforeGenerate();

            Ready.SetGen(false, "Generating ship");
            int attempts = The.BB.Get(BBKeyH.GlobalGenAttempt, 0);
            ShipFactory factory = CreateFactory(data, attempts, false);

            var state = S;
            D.Ass(!state.IsGenFailure, "State started with gen failure!");
            if (attempts < 10) {
                if (attempts > 0) {
                    The.ScreenBlock.SetSideText(
                        T.StoryWrecked_Empty_ShipLoading_Side_Att_V1.F(
                                attempts + 1));
                    // D.Warn("Build Attempt: {0}", attempts);
                }
                yield return factory.Build(S);
            }
            attempts++;
            The.BB.Set(BBKeyH.GlobalGenAttempt, attempts);
            Ready.SetGen(true, "Done generating ship");

            factory.Report.Attempts = attempts;
            factory.Report.Duration = Time.realtimeSinceStartupAsDouble - startTime;
            factory.Report.IsSuccess = factory.IsSuccess;
            The.SysSig.OnBuildShip.Send(factory.Report);

            if (!factory.IsSuccess) {
                Sounds.LockMusic();
                IsGenFailure = true;
                state.IsGenFailure = true;
                Coroutines.CancelAllNonSystem();
                yield break;
            }

            Sounds.UnlockMusic();

            try {
                AfterGenerate();
                // Here, not as a gen stage: it runs once, on the ship that was kept
                if (GivesCleanerJetSuit) {
                    StartingJetSuit.GiveToCleaner(S, new Rng(S.Seed));
                }
                StartingCrewWeapons.EquipCrew(S, StartingWeapons, new Rng(S.Seed + 1));
                The.BB.Remove(BBKeyH.GlobalGenAttempt, 0);
                factory.ResetStages();
            } catch (System.Exception ex) {
                D.LogEx(ex, "Failed AfterGenerate for scenario: {0}", this);
                ModRegistry.WarnAboutBrokenMod(this, "Error in AfterGenerate");
            }

            // D.Log("Generated Ship: {0}", factory.Spec.GenCommand);
        }

        private ShipFactory CreateFactory(GameSetupData data, int attempts,
                bool isPreview) {
            var rng = new Rng(data.ShipSeed + attempts);
            var w = ModifierMapSize.Current;
            var h = w;
            var shape = ShipShapeConfig.ForScenario(Id);
            SetShipSize(rng, data, shape);
            var cellW = rng.Range(shape.CellWidthFrom, shape.CellWidthTo);
            var cellH = rng.Range(shape.CellHeightFrom, shape.CellHeightTo);
            // if (shipSize > 1f) {
            //     cellW = Mathf.CeilToInt(cellW * shipSize);
            //     cellH = Mathf.CeilToInt(cellH * shipSize);
            // } else {
            //     cellW = Mathf.RoundToInt(cellW);
            //     cellH = Mathf.RoundToInt(cellH);
            // }

            var centerX = w / 2;
            var centerY = h / 2;
            var shipW = data.ShipWidth;
            var shipH = data.ShipHeight;
            var shipRect = new PosRect(
                centerX - shipW / 2,
                centerY - shipH / 2,
                centerX + shipW / 2,
                centerY + shipH / 2
            );

            Pos.Prime(w, h);
            Consts.SetCornerMargin(w);
            var factory = new ShipFactory(shipRect, data.ShipSeed + attempts, true);
            factory.Data = data;
            factory.ShipShape = shape;
            factory.SetCellSize((int) cellW, (int) cellH);
            try {
                ConfigureShipFactory(factory, rng, isPreview);
            } catch (System.Exception ex) {
                D.LogEx(ex, "Failed ConfigureShipFactory for scenario: {0}", this);
                ModRegistry.WarnAboutBrokenMod(this,
                    "Failed to ConfigureShipFactory");
            }
            return factory;
        }

        public override IEnumerator DoSetup(GameState state, GameSetupData config) {
            yield return Generate(config);
            if (IsGenFailure) { yield break; }
            if (IsSandbox) {
                D.Warn("Setting sandbox true for scenario: {0}", this);
            }
            state.Prefs.SetBool(Pref.DevInstantBuild, IsSandbox);
            Ready.SetGame(true, "Done generating ship");
        }
    }
}
