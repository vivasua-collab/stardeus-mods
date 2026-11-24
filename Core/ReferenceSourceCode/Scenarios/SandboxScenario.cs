using System.Collections;
using Game.Audio;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Platform.Presence;
using Game.Procgen;
using Game.Systems.Creatures;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Systems.Items;
using Game.Components;
using System.Collections.Generic;
using System;
using Game.Systems.UIUX;
using Game.Components.Upgrades;

namespace Game.Scenarios {
    public class SandboxScenario : BaseRandomShipScenario {
        [SelfInit]
        private static void Load() {
            Register(new SandboxScenario());
        }
        public static string ScenarioId = "sandbox";
        public override string Id => ScenarioId;
        public override bool IsSandbox => true;
        public override int StartingPetCount => 0;
        public override string NameT => T.ScenarioSandbox;
        public override string DescT => T.ScenarioSandboxTooltip;
        // Creative mode isn't an easy mode - a neutral baseline is the honest default.
        public override string QuickStartDifficultyId => Difficulty.DefaultId;
        public override (Species, int)[] GetStartingBeingCounts() {
            return new (Species, int)[] {
                (Species.Get(SpeciesIdH.Robot), 1),
                (Species.Get(SpeciesIdH.Drone), 1),
                (Species.Get(SpeciesIdH.Carrier), 1),
                (Species.Get(SpeciesIdH.Human), 2),
            };
        }

        protected override void AfterGenerate() {
            // Install snake
            var cpu = S.Components.FindFirstByType<ShipComputerComp>();
            cpu.Tile.Upgrades.Install(0, UpgradeRegistry.GetByMat(MatId.SnakeCartridgeUpgrade));

            var state = The.R.State;
            #if !STARDEUS_DEMO
            DevMenu.ToggleInstantBuild(true);
            #endif
            // A.Tools.ClearNotificationDots(S);
            The.ScreenBlock.Hide();
            WPopup.Create(false, IconId.CInfo, T.ScenarioSandbox, T.SandboxWelcome)
                .Spawn();
            if (G.ClockSpeed < ClockSpeed.Unlimited) {
                Coroutines.Start(CameraControls.FadeCamera(), this, "FadeCamera", false, false);
            }
        }

        public override void AfterLoad(GameState s) {
        }

        public override string PresenceId => PresenceActivity.Sandbox;

        public override int Priority => int.MaxValue;
        protected override void ConfigureShipFactory(ShipFactory factory, Rng rng,
                bool isPreview) {
            factory.AddStage(new RectangleShapeGenStage());
            if (isPreview) { return; }
            factory.AddStage(new StartingAreaGenStage(false, true));
            factory.AddStage(new ColonistGenStage(GenItemFlags.SpaceReady));
        }
        protected override void AddValidationStages(ShipFactory factory, Rng rng) {
            factory.AddStage(new ValidateShipGenStage(new Dictionary<Type, int>() {
                { typeof(ShipComputerComp), 1 },
            }));
        }
    }
}
