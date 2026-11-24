#if !STARDEUS_DEMO
using System;
using System.Collections.Generic;
using System.Linq;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Rendering;
using Game.Systems.AI;
using Game.Systems.AI.Combat;
using Game.Systems.Creatures;
using Game.Systems.Items;
using Game.Systems.Ships;
using Game.Systems.UIUX;
using Game.Systems.Space;
using Game.Utils;
using KL.Grid;
using KL.Randomness;
using KL.Utils;

namespace Game.UI {
    public sealed class DevSituations {
        private GameState S => The.R.State;
        public struct Sit {
            public string Title;
            public string Icon;
            public string Tooltip;
            public Action Method;
            public static Sit Create(string icon, string title, string tip, Action method) {
                return new Sit() {
                    Icon = icon,
                    Title = title,
                    Tooltip = tip,
                    Method = method,
                };
            }
        }

        public static readonly List<Sit> All = new() {
            Sit.Create(IconId.CPreviewCrawler01,
                "Human With Face Hugger",
                "Spawns a human with a Crawler attached to their face",
                HumanWithCrawler),
            Sit.Create(IconId.CPreviewCleaningBot01,
                "Cleaning Bot Ride Cleaning Bot",
                "Spawns a cleaning bot with cat brain and a cleaning bot in the center of camera view and makes the cat ride the cleaning bot",
                CleaningBotRidingCleaningBot),
            Sit.Create(IconId.CPreviewRobot01,
                "Robot Ride Cleaning Bot",
                "Spawns a robot with cat brain and a cleaning bot in the center of camera view and makes the cat ride the cleaning bot",
                RobotRidingCleaningBot),
            Sit.Create(IconId.CPreviewHuman01,
                "Human Ride Cleaning Bot",
                "Spawns a human with cat brain and a cleaning bot in the center of camera view and makes the cat ride the cleaning bot",
                HumanRidingCleaningBot),
            Sit.Create(IconId.CPreviewCat01,
                "Cat Ride Cleaning Bot",
                "Spawns a cat and a cleaning bot in the center of camera view and makes the cat ride the cleaning bot",
                CatRidingCleaningBot),
            Sit.Create(IconId.CPersona,
                "Spawn Guests + Pickup Ship",
                "Spawns 3 Adventurer guests at the camera center and an idle Adventurer ship nearby - hail it and use Send Beings to test the guest repatriation flow",
                SpawnGuestsWithPickupShip),
            Sit.Create(IconId.CMop,
                "Add Dirt To Area",
                "Spawns a bunch of dirt in the area in the center of camera view",
                MakeAreaDirty),
            Sit.Create(IconId.CWarning,
                "Start Meteoroid Shower",
                "Starts a heavy meteoroid shower",
                MeteoroidShower),
            Sit.Create(GraphicsId.ObjAsteroidsAsteroid01,
                "Start Micro Meteoroids",
                "Starts a micro meteoroid shower",
                MicroMeteors),
            Sit.Create(IconId.CWarning,
                "Start Magnetic Storm",
                "Starts a beautiful magnetic storm (zoom out to see). It disables some devices.",
                AddSpaceEffect_MagneticStorm),
            Sit.Create(IconId.CWarning,
                "Start Dust Storm",
                "Starts a dust storm",
                AddSpaceEffect_DustStorm),
            Sit.Create(GraphicsId.SpaceObjectsShipsShipFighter01_R,
                "Ship Combat VS 01 Fighter",
                "Ship combat against a small fighter class ship",
                ShipCombatWith_Fighter),
            Sit.Create(GraphicsId.SpaceObjectsShipsShipCorvette01_R,
                "Ship Combat VS 02 Corvette",
                "Ship combat against a small corvette class ship",
                ShipCombatWith_Corvette),
            Sit.Create(GraphicsId.SpaceObjectsShipsShipFrigate01_R,
                "Ship Combat VS 03 Frigate",
                "Ship combat against a frigate class ship",
                ShipCombatWith_Frigate),
            Sit.Create(GraphicsId.SpaceObjectsShipsShipDestroyer01_R,
                "Ship Combat VS 04 Destroyer",
                "Ship combat against a destroyer class ship",
                ShipCombatWith_Destroyer),
            Sit.Create(GraphicsId.SpaceObjectsShipsShipCruiser01_R,
                "Ship Combat VS 05 Cruiser",
                "Ship combat against a cruiser class ship",
                ShipCombatWith_Cruiser),
            Sit.Create(GraphicsId.SpaceObjectsShipsShipBattlecruiser01_R,
                "Ship Combat VS 06 Battlecruiser",
                "Ship combat against a battlecruiser class ship",
                ShipCombatWith_Battlecruiser),
            Sit.Create(GraphicsId.SpaceObjectsShipsShipBattleship01_R,
                "Ship Combat VS 07 Battleship",
                "Ship combat against a battleship class ship",
                ShipCombatWith_Battleship),
            Sit.Create(GraphicsId.SpaceObjectsShipsShipCarrier01_R,
                "Ship Combat VS 08 Carrier",
                "Ship combat against a carrier class ship",
                ShipCombatWith_Carrier),
            Sit.Create(GraphicsId.SpaceObjectsShipsShipDreadnought01_R,
                "Ship Combat VS 09 Dreadnought",
                "Ship combat against a large dreadnought class ship",
                ShipCombatWith_Dreadnought),
            Sit.Create(GraphicsId.SpaceObjectsShipsShipTitan01_R,
                "Ship Combat VS 10 Titan",
                "Ship combat against a large titan class ship",
                ShipCombatWith_Titan),
            Sit.Create(GraphicsId.SpaceObjectsStationsSpaceStation01,
                "Ship Combat VS Space Station",
                "Ship combat against a space station",
                ShipCombatWith_SpaceStation),
        };
        private static void ShipCombatWith_Fighter() {
            ShipCombatWith("Fighter");
        }
        private static void ShipCombatWith_Corvette() {
            ShipCombatWith("Corvette");
        }
        private static void ShipCombatWith_Frigate() {
            ShipCombatWith("Frigate");
        }
        private static void ShipCombatWith_Cruiser() {
            ShipCombatWith("Cruiser");
        }
        private static void ShipCombatWith_Destroyer() {
            ShipCombatWith("Destroyer");
        }
        private static void ShipCombatWith_Battlecruiser() {
            ShipCombatWith("Battlecruiser");
        }
        private static void ShipCombatWith_Battleship() {
            ShipCombatWith("Battleship");
        }
        private static void ShipCombatWith_Carrier() {
            ShipCombatWith("Carrier");
        }
        private static void ShipCombatWith_Dreadnought() {
            ShipCombatWith("Dreadnought");
        }
        private static void ShipCombatWith_Titan() {
            ShipCombatWith("Titan");
        }
        private static void ShipCombatWith_SpaceStation() {
            ShipCombatWith("SpaceStation");
        }
        private static void ShipCombatWith(string shipClassId) {
            var tpl = new NPCShip();
            tpl.Class = ShipClassDef.Get(shipClassId);
            var S = The.R.State;
            var ship = S.Query.SpawnShipTplAt.Ask(S.Universe.Player.Parent, tpl);

            var offset = S.Sys.ShipNav.ShipForward;
            offset = Vectors.Rotate(offset, -S.Sys.ShipNav.ShipDefaultRot + S.Sys.Ship.Rotation);
            ship.TeleportToSectorLocation(A.Player.Location + offset);
            NPCShipBehavior_Sector_Combat.AssignTo(ship, A.Player.Ship);
            S.Clock.SetSpeed(ClockSpeed.Normal);
        }
        private static void MicroMeteors() {
            A.S.Query.TriggerStoryEvent.Ask("Space/MicroMeteoroidStrike");
        }

        private static void MeteoroidShower() {
            A.S.Query.TriggerStoryEvent.Ask("Space/MeteoroidShower");
        }
        private static void AddSpaceEffect_MagneticStorm() {
            AddSpaceEffect("MagneticStorm");
        }
        private static void AddSpaceEffect_DustStorm() {
            AddSpaceEffect("DustStorm");
        }

        private static void HumanWithCrawler() {
            var S = The.R.State;
            var pos = RenderUtils.CameraPosition;
            var posIdx = EntityUtils.ToPosSafe(pos);
            var human = BeingUtils.SpawnBeingWithEquipment(S, SpeciesIdH.Human, posIdx,
                equipment: GenItemFlags.Casual | GenItemFlags.SpaceReady);
            var helm = human.Data.Inventory.Equipment[InventorySlotIdH.Helmet]?.ObjData;
            if (human.Data.Inventory.TryUnequip(S, InventorySlotIdH.Helmet, out _)) {
                if (helm != null) {
                    S.Objs.Destroy(helm.Obj);
                }
            }
            var hat = human.Data.Inventory.Equipment[InventorySlotIdH.Hat]?.ObjData;
            if (human.Data.Inventory.TryUnequip(S, InventorySlotIdH.Hat, out _)) {
                if (hat != null) {
                    S.Objs.Destroy(hat.Obj);
                }
            }
            var crawler = BeingUtils.SpawnBeing(S, SpeciesIdH.Crawler, posIdx);
            AttackActionFacehug.TryFacehug(S, crawler.AIAgent.CombatUnit,
                human.AIAgent.CombatUnit, 1f);
        }

        // Lets the "Send Beings" repatriation flow (hail the ship, pick Send Beings) be
        // exercised without waiting for a real derelict rescue.
        private static void SpawnGuestsWithPickupShip() {
            var S = The.R.State;
            var fac = FactionDef.FastGet(FactionIdH.Adventurer);
            if (fac == null) {
                ShowError("Adventurer faction not found");
                return;
            }

            var pos = RenderUtils.CameraPosition;
            var posIdx = EntityUtils.ToPosSafe(pos);
            for (int i = 0; i < 3; i++) {
                var data = BeingUtils.GenBeingWithInventory(SpeciesIdH.Human,
                    Systems.Naming.BeingType.Friendly,
                    fac, GenItemFlags.Casual | GenItemFlags.SpaceReady, ColonyRole.Passenger);
                S.Beings.Create(posIdx, data);
                BeingUtils.UpdateFlagsFor(S, data);
                BeingUtils.AddInitialMentalBoostFor(data, 7);
            }
            S.Sig.RebuildBeingGroups.Send();

            var tpl = new NPCShip();
            tpl.Class = ShipClassDef.Get("Fighter");
            tpl.Faction = fac;
            var ship = S.Query.SpawnShipTplAt.Ask(S.Universe.Player.Parent, tpl);

            var offset = S.Sys.ShipNav.ShipForward;
            offset = Vectors.Rotate(offset, -S.Sys.ShipNav.ShipDefaultRot + S.Sys.Ship.Rotation);
            ship.TeleportToSectorLocation(A.Player.Location + offset);
            NPCShipBehavior_Sector_Idle.AssignTo(ship);
            S.Clock.SetSpeed(ClockSpeed.Normal);
            ShowSuccess("Spawned 3 Adventurer guests and a nearby pickup ship");
        }

        private static void MakeAreaDirty() {
            var S = The.R.State;
            var pos = RenderUtils.CameraPosition;
            var posIdx = EntityUtils.ToPosSafe(pos);
            var area = S.Sys.Areas.AnyAreaAt(posIdx);
            if (Arrays.IsEmpty(area)) {
                ShowError("No area at camera center!");
                return;
            }
            var scale = Rng.URange(5f, 15f);
            var shift = Rng.URange(1f, 5f);
            int added = 0;
            foreach (var p in area) {
                Pos.ToXY(p, out var x, out var y);
                var n = Rng.Unseeded.GetSimplex(x * shift, y * shift, scale);
                if (Math.Abs(n) < 0.2f && Rng.UChance(0.1f)) {
                    var sprite = Images.Matching(
                        GraphicsId.DirtDirtMark01.Replace("01", ""))
                            .OrderBy(v => Rng.UValue)
                            .First();
                    S.Sys.Dirt.Add(DirtData.Create(p, sprite));
                    added++;
                }
            }
            if (added == 0) {
                foreach (var p in area) {
                    if (Rng.UChance(0.2f)) {
                        var sprite = Images.Matching(
                            GraphicsId.DirtDirtMark01.Replace("01", ""))
                                .OrderBy(v => Rng.UValue)
                                .First();
                        S.Sys.Dirt.Add(DirtData.Create(p, sprite));
                    }
                }
            }
        }

        private static void AssignGoal(GameState S, AIAgentComp agent, AIGoal g, AIAction action = null) {
            g.ResetWorldState();
            var planner = S.Query.GetAIPlanner.Ask();
            if (action != null) {
                var forcedPlan = AIPlan.For(action);
                agent.DropGoal();
                if (!agent.SetGoal(g, forcedPlan, G.Ticks, true)) {
                    ShowError($"Failed to force set a goal {g} for {agent.Being}");
                } else {
                    ShowSuccess($"Goal forced on {agent.Being}: {g}");
                    EntityUtils.CameraFocusOn(agent.Being);
                }
                return;
            }
            if (planner.TryFormulateFor(g, agent, G.Ticks, out var plan)) {
                agent.DropGoal();
                g.ClearCounters();
                if (action != null) {
                    plan = AIPlan.For(action);
                }
                agent.SetGoal(g, plan, G.Ticks, true);

            } else {
                var msg = $"Agent failed to formulate a plan for:\n{g}";
                D.Warn(msg);
                A.S.Sig.ShowErrorMessage.Send(msg);
            }
        }
        private static void ShowSuccess(string msg) {
            The.SysSig.ShowTooltip.Send(Texts.Green(msg), true);
            UISounds.PlayDigitalConfirm();
        }

        private static void ShowError(string msg) {
            A.S.Sig.ShowErrorMessage.Send(msg);
        }

        private static void CatRidingCleaningBot() {
            BeingRidingCleaningBot(DefIdsH.BeingsCat01, true);
        }
        private static void CleaningBotRidingCleaningBot() {
            BeingRidingCleaningBot(DefIdsH.BeingsCleaningBot01, false);
        }
        private static void HumanRidingCleaningBot() {
            BeingRidingCleaningBot(DefIdsH.BeingsHuman01, false);
        }
        private static void RobotRidingCleaningBot() {
            BeingRidingCleaningBot(DefIdsH.BeingsRobot01, false);
        }

        private static void BeingRidingCleaningBot(int speciesDefId, bool checkSafety) {
            var S = The.R.State;
            var pos = RenderUtils.CameraPosition;
            var posIdx = EntityUtils.ToPosSafe(pos);
            var o2 = S.GetO2(posIdx);
            var temp = S.GetTemp(posIdx);
            if (checkSafety) {
                if (o2 < Consts.MinOxygen * 2f || temp < Units.CelsiusToKelvin(15)) {
                    ShowError(
                        string.Format("Camera focused position is too cold / has not enough oxygen for cat to survive!: Temp: {0} (min: {1}). O2: {2} (min: {2})",
                        Texts.Temperature(temp), Texts.Temperature(Units.CelsiusToKelvin(15)),
                        Units.Percentage(o2), Units.Percentage(Consts.MinOxygen * 2f)));
                    return;
                }
            }
            var bot = SpawnBeing(S, posIdx, DefIdsH.BeingsCleaningBot01);
            Being cat;
            if (speciesDefId == DefIdsH.BeingsHuman01) {
                cat = BeingUtils.SpawnBeingWithEquipment(S, SpeciesIdH.Human, posIdx,
                    equipment: Systems.Items.GenItemFlags.Casual | Systems.Items.GenItemFlags.SpaceReady);
            } else {
                cat = SpawnBeing(S, posIdx, speciesDefId);
            }
            if (speciesDefId != DefIdsH.BeingsCat01) {
                if (cat.Data.Body.MindSlot.TryRemovePart(S, out var brain, null, false, out _, true)) {
                    S.Objs.Destroy(brain);
                    var catBrain = S.Objs.Create(cat.Position,
                        The.Defs.FastGet(DefIdsH.ObjPartsBrainBrainCat));
                    cat.Data.Body.MindSlot.InstallPart(S, catBrain.Data, null, false, out _);
                    cat.Data.Body.CheckCondition(false);
                }
            }
            cat.Data.StatsBeing.Hunger.UpdateBase(1f);
            cat.Data.StatsBeing.Sleep.UpdateBase(1f);
            cat.Data.StatsBeing.Toilet.UpdateBase(1f);
            S.Clock.SetSpeed(ClockSpeed.Normal);
            S.Clock.ExecuteAfterTicks(1, () => {
                S.Clock.SetSpeed(ClockSpeed.Stopped);
                D.Warn("Cat goals: {0}", D.DumpCollection(cat.AIAgent.Goals));
                cat.AIAgent.ClearGoal();
                foreach (var goal in cat.AIAgent.Goals) {
                    if (goal.Name == T.NeedFun) {
                        var act = Registries.AIActions.Get(AIActionFunRideBot.ActId);
                        AssignGoal(S, cat.AIAgent, goal, act);
                        break;
                    }
                }
                cat.Data.StatsBeing.Fun.UpdateBase(0.1f);
            });
        }

        private static Being SpawnBeing(GameState S, int posIdx, int speciesDef) {
            var vars = new GenBeingVars(
                Species.ForDef(speciesDef),
                Systems.Naming.BeingType.Friendly);
            vars.Body = FactionDef.RandomBodyVars(Rng.Unseeded);

            var data = S.Query.GenBeingFull.Ask(vars, Rng.Unseeded);
            return S.Beings.Create(posIdx, data);
        }

        public static void CreateSituation(GameState S, Sit sit) {
            if (!S.IsAchievementsBlocked) {
                WPopup.Create(false, IconId.CWarning, T.AchievementsWillBeDisabled, null,
                    "Creating situations will disable achievements for this save file!")
                    .WithAction(T.Cancel, null)
                    .WithAction2(T.Confirm, () => {
                        S.IsAchievementsBlocked = true;
                        sit.Method();
                    }).Spawn(center: true);
                return;
            }
            sit.Method();
        }

        private static void AddSpaceEffect(string id) {
            var sector = The.R.State.Universe.Player.Parent.Sector;
            if (sector == null) {
                ShowError("This command works only when your ship is inside a sector");
                return;
            }
            SpaceEffectConfig found = SpaceEffectConfig.Get(id);;
            if (found == null) {
                D.Err("Space effect not found: {0}", id);
                return;
            }

            var spaceSys = The.R.Sys.Get<SpaceSys>(SpaceSys.SysId);
            spaceSys.AddEffect(found.Effect, sector);
            ShowSuccess($"Added space effect: {found.Id}");
        }
    }
}
#endif
