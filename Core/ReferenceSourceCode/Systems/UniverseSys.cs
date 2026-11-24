using System;
using System.Collections.Generic;
using System.Linq;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Data.Space;
using Game.Procgen.Space;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using KL.Collections;
using KL.Randomness;
using KL.Utils;
using Game.Systems.Space;
using MonoMod.Utils;
using Game.Input;
using Game.Systems.Story;

namespace Game.Systems {
    public sealed class UniverseSys : GameSystem {
        public const string SysId = "UniverseSys";
        public override string Id => SysId;

        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new UniverseSys());
        }

        protected override void OnInitialize() {
            S.Sig.AfterLoadState.AddListener(OnLoadSave);
            S.Sig.TelescopeScanComplete.AddListener(OnTelescopeScanComplete);
        }

        private void OnTelescopeScanComplete() {
            var playerRegion = SOUtils.GetPlayerSector(S)?.Region;
            if (playerRegion == null) {
                D.Err("Player region is null on telescope scan! {0}", A.Player.Parent);
                return;
            }
            var pLoc = playerRegion.Position;
            var regionsWithDist = new Dictionary<SpaceObject, float>();

            foreach (var g in S.Universe.Root.Children) {
                if (g.Type.Id != SpaceObjectTypeId.Region) { continue; }
                if (g.HasTerraformable) { continue; }
                if (!g.HasChildren) { continue; }
                if (g.SpaceEntity.Biome.FactionsByDominance?.Count > 0) {
                    continue;
                }
                // if (g.SpaceEntity.Biome.Effects?.Count > 0) { continue; }
                var dist = Vectors.Distance(pLoc, g.Region.Position);
                regionsWithDist.Add(g, dist);
                // D.Warn("Adding potential region: {0}. Dist: {1}", g.DisplayName, dist);
            }

            if (regionsWithDist.Count == 0) {
                // fallback
                foreach (var g in S.Universe.Root.Children) {
                    if (g.Type.Id != SpaceObjectTypeId.Region) { continue; }
                    if (g.HasTerraformable) { continue; }
                    if (!g.HasChildren) { continue; }
                    var dist = Vectors.Distance(pLoc, g.Region.Position);
                    regionsWithDist.Add(g, dist);
                    // D.Warn("Adding potential region: {0}. Dist: {1}", g.DisplayName, dist);
                }

                if (regionsWithDist.Count == 0) {
                    var txt = $"{T.ResearchTelescope}: {Texts.Red(T.ScanNotFound_V1.F(T.SpaceObjectPlanet))}";
                    S.Sys.Log.AddLine(txt);
                    S.Sig.ShowFanfare.Send(txt);
                    return;
                }
            }

            var potentialPlanets = new List<SpaceObject>();
            var potentialPlanetsBackup = new List<SpaceObject>();
            var filtered = regionsWithDist.OrderByDescending(kv => kv.Value).Take(3)
                .ToDictionary(kv => kv.Key, kv => kv.Value);

            if (Arrays.IsEmpty(filtered)) {
                var txt = $"{T.ResearchTelescope}: {Texts.Red(T.ScanNotFound_V1.F(T.SpaceObjectPlanet))}";
                S.Sys.Log.AddLine(txt);
                S.Sig.ShowFanfare.Send(txt);
                return;
            }

            var selectedRegion = S.Rng.WeightedFrom(filtered);
            foreach (var sector in selectedRegion.Children) {
                try {
                    if (sector.Sector == null) { continue; }
                    if (sector.SpaceEntity.Biome.Template.Id != "Planet") { continue; }
                    if (sector.HasTerraformable) { continue; }
                    potentialPlanetsBackup.Add(sector);
                    if (sector.FactionId != null) { continue; }
                    if (sector.IsVisited) { continue; }
                    if (sector.Knowledge.IsFullyScanned) { continue; }
                    potentialPlanets.Add(sector);
                } catch (System.Exception ex) {
                    D.LogEx(ex, "Failed processing sector in telescope results: {0}", sector);
                }
            }

            var selectedSector = Arrays.IsEmpty(potentialPlanets) ?
                S.Rng.From(potentialPlanetsBackup) :
                S.Rng.From(potentialPlanets);

            if (selectedRegion == null || selectedSector == null) {
                var txt = $"{T.ResearchTelescope}: {Texts.Red(T.ScanNotFound_V1.F(T.SpaceObjectPlanet))}";
                S.Sys.Log.AddLine(txt);
                S.Sig.ShowFanfare.Send(txt);
                return;
            }

            selectedSector.Flags |= SOFlags.Terraformable;
            selectedRegion.Flags |= SOFlags.Terraformable;

            selectedSector.SpaceEntity.Biome.Effects.Clear();
            selectedSector.FactionId = null;
            selectedSector.POIs?.Remove(SpaceObjectTypeIdH.Port);

            // #if PRODUCTION_BUILD
            // fix this translation first
            // #endif
            var planetLine = T.TelescopeFoundPlanet_V2.F(
                    Texts.Bold(selectedRegion.StarmapLink),
                    Texts.Bold(selectedSector.StarmapLink));
            // The.Dialog.ShowWithAction(IconId.CComputing, planetLine, null);

            var data = StoryEventData.CreateCustom(IconId.CTerraform,
                T.FoundTerraformablePlanet, planetLine);
            S.Sig.AddCustomStoryEvent.Send(data, true);
            var loc = $"{selectedRegion.StarmapLink} {GlyphMap.ArrowRight} {selectedSector.StarmapLink}";
            loc = $"{Texts.Green(T.FoundTerraformablePlanet)}: {loc}";
            S.Sys.Log.AddLine(loc);
            S.Sig.ShowFanfare.Send(loc);

            S.Sig.FoundTerraformable.Send(selectedSector);

            if (S.Sys.Terraform.KnownTerraformable.Count < 2) {
                var qdata = S.Query.GetSharedQuestData.Ask();
                qdata.SetInt("HabitablePlanetId", selectedSector.Id);
            }

            S.UIQuery.RebuildStarmap.Ask(false, false);
        }

        private void OnLoadSave(GameState state) {
            ValidateAllSpaceObjects();
            #if !PRODUCTION_BUILD
            if (The.TestsAreRunning) {
                Ready.When(WhenH.ShipDataRestored, ValidateAllShips);
                return;
            }
            #endif
            if (state.Universe.Version < GenUniverse.Version) {
                Ready.WhenGameLate(this, "RegenerateUniverse",
                    () => Regenerate(state.SeedSpace));
            } else {
                Ready.When(WhenH.ShipDataRestored, ValidateAllShips);
            }
        }

        private void ValidateAllSpaceObjects() {
            #if !PRODUCTION_BUILD
            #if !UNITY_EDITOR
            // this is very slow, and with many tests it slows everything down
            if (The.TestsAreRunning) { return; }
            #endif
            #endif
            Caches.SpaceObjectList.Clear();
            foreach (var so in S.Universe.ObjectsById.Values) {
                if (so.HasChildren) {
                    foreach (var child in so.Children) {
                        if (child.Parent != so) {
                            Caches.SpaceObjectList.Add(child);
                        }
                    }
                }
            }
            foreach (var so in S.Universe.ObjectsById.Values) {
                if (so.Parent == null) {
                    if (so.IsShip) {
                        continue;
                    } else if (so.Type.IdH == SpaceObjectTypeIdH.Hyperspace) {
                        continue;
                    } else if (so.Type.IdH == SpaceObjectTypeIdH.Universe) {
                        continue;
                    }
                    D.Err("SO has no parent! {0}", so);
                    Caches.SpaceObjectList.Add(so);
                    continue;
                }
            }
            foreach (var so in Caches.SpaceObjectList) {
                SOUtils.RemoveSO(so);
                if (S.Universe.ObjectsById.ContainsValue(so)) {
                    D.Err("Failed to remove SO: {0}", so);
                }
                foreach (var obj in S.Universe.ObjectsById.Values) {
                    if (obj.HasChildren && obj.Children.Contains(so)) {
                        obj.RemoveChild(so);
                    }
                }
            }
            Caches.SpaceObjectList.Clear();

            foreach (var so in S.Universe.ObjectsById.Values) {
                if (so.HasChildren) {
                    for (int i = so.Children.Count - 1; i >= 0; i--) {
                        SpaceObject child = so.Children[i];
                        if (child.Parent == null) {
                            D.Err("SO child has no parent! {0}, actual parent: {1}",
                                child, so);
                            so.RemoveChild(child);
                            Caches.SpaceObjectList.Add(child);
                        }
                        if (child.Parent != so) {
                            D.Err("SO child has wrong parent! {0}, actual parent: {1}",
                                child, so);
                            so.RemoveChild(child);
                            Caches.SpaceObjectList.Add(child);
                        }
                    }
                }
            }
            foreach (var so in Caches.SpaceObjectList) {
                SOUtils.RemoveSO(so);
                if (S.Universe.ObjectsById.ContainsValue(so)) {
                    D.Err("Failed to remove SO: {0}", so);
                }
            }
            Caches.SpaceObjectList.Clear();

            foreach (var so in S.Universe.ObjectsById.Values) {
                foreach (var so2 in S.Universe.ObjectsById.Values) {
                    if (so2.HasChildren && so2.Children.Contains(so)) {
                        if (so.Parent != so2) {
                            D.Err("Found stray child in another parent, removing: {0} -> {1}. Actual parent: {2}",
                                so, so2, so.Parent);
                            so2.Children.Remove(so);
                        }
                    }
                }
            }
        }

        private void ValidateAllShips() {
            Caches.SpaceObjectList.Clear();
            foreach (var so in S.Universe.ObjectsById.Values) {
                if (so.Ship != null) {
                    if (so.Ship.Faction == null) {
                        D.Err("SO has no faction! {0}", so);
                        so.Ship.Faction = FactionDef.FastGet(FactionIdH.Adventurer);
                    }
                    if (so.Parent != null) {
                        if (so.Ship.IsPlayer) {
                            // this bricks loading if we handle player here
                            continue;
                        }
                        if (so.Ship.Sector != so.Parent.Sector) {
                            D.Err("Ship/SO has wrong sector! {0}: {1} != {2}",
                                so.Ship, so.Parent.Sector,
                                so.Ship.Sector);
                            so.Parent.RemoveChild(so);
                            if (A.Player.Parent.Sector == so.Ship.Sector) {
                                A.Player.Parent.AddChild(so);
                            }
                        }
                    }
                    continue;
                }
                if (so.Type.IdH == SpaceObjectTypeIdH.Ship) {
                    D.Err("SO is ship without ship data! {0}", so);
                    Caches.SpaceObjectList.Add(so);
                }
                if (so.Type.IdH == SpaceObjectTypeIdH.SpaceStation) {
                    S.Sys.NPCShip.Migration_CovertSpaceStation(so);
                }
                if (so.Type.IdH == SpaceObjectTypeIdH.ShipDerelict) {
                    S.Sys.NPCShip.Migration_CovertDerelict(so);
                }
            }
            foreach (var so in Caches.SpaceObjectList) {
                S.Sys.NPCShip.RemoveShip(so.Ship);
                if (S.Universe.ObjectsById.ContainsValue(so)) {
                    D.Err("Failed to remove SO: {0}", so);
                }
            }
            Caches.SpaceObjectList.Clear();
            // ValidateAllSpaceObjects();
        }

        /// <summary>
        /// Don't use this, use ShipSys.TryEnterObject instead!
        /// </summary>
        // avoidStations: the new game's first entry. A station visible from the spawn point
        // sits right in the game background at the start, where a playtester took it for
        // part of the starting sector's tutorial.
        public void EnterObject(SpaceObject enter, SpaceObject previous,
                bool avoidStations = false) {
            if (!GenerateSector(enter, true)) {
                // sector was generated before
            }
            if (enter.Sector != null) {
                S.Sys.NPCShip.LoadObjectsFor(enter);
            }

            var p = S.Universe.Player;
            p.Parent.RemoveChild(p);
            enter.AddChild(p);

            if (previous == null) {
                D.Err("Previous space object is null when entering! {0}", enter);
                return;
            }

            // Save player location
            previous.MarkVisited();
            enter.MarkVisited();

            // D.Log("Entering SO: {0} -> {1}. PL: {2}", previous, enter, p.Location);
            if (previous.Type.Id == SpaceObjectTypeId.BlackHole) {
                // Force to exit the black hole at the center of the galaxy
                // even if it was visited manually before
                p.Location = enter.Location;
            } else {
                // We're going up
                if (previous.Parent == enter) {
                    p.Location = previous.Location;
                } else {
                    SetEnterPoint(p, enter, avoidStations);
                }
            }
            // when hyperjumping before entering hyperspace in region or universe map
            // the rebuild makes things break
            if (enter.Type.IdH != SpaceObjectTypeIdH.Hyperspace
                    || A.StarmapCtx.Layer == UniverseLayer.Sector) {
                S.UIQuery.RebuildStarmap.Ask(true, false);
            }
        }

        public bool GenerateSector(SpaceObject obj, bool isEntering, bool reveal = false) {
            if (obj == null) { return false; }
            if (obj.Type.IdH != SpaceObjectTypeIdH.Sector) {
                var isUnknown = obj.Type.IdH == SpaceObjectTypeIdH.SectorUnknown;
                if (!isUnknown) {
                    // already generated
                    return false;
                }
                // already marked as unknown, only entering or a scan reveal generates it
                if (!isEntering && !reveal) {
                    return false;
                }
            }
            if (obj.HasChildren) { return false; }
            if (obj.Sector == null) {
                D.Err("SO.Sector is null! {0}", obj);
                return false;
            }
            if (!isEntering && obj.Sector.Biome?.Template is SectorTemplate secTpl) {
                if (secTpl.Surprise && !reveal && !A.InstantBuild) {
                    // won't generate before entering
                    obj.Type = SpaceObjectType.Find(SpaceObjectTypeIdH.SectorUnknown);
                    obj.ResetLooks();
                    return false;
                }
            }
            var gen = GenUniverse.GetSectorGenerator(obj.Sector);
            obj.Type = SpaceObjectType.Find(gen.TypeId);
            gen.GenerateSubObjects(S.Universe, obj);
            obj.ResetLooks();
            obj.UI?.Refresh();
            return true;
        }

        private void SetEnterPoint(SpaceObject player, SpaceObject entering,
                bool avoidStations) {
            var ed = entering.ExitDist;
            // D.Warn("Exit dist: {0}", ed);
            // var enterDir = (player.Location - entering.Location).normalized;
            var enterDir = Rng.UInsideUnitCircle().normalized;
            player.Location = enterDir * ed;
            if (!avoidStations || !IsAnyStationVisibleFrom(entering, player.Location)) {
                return;
            }
            // Other directions first, then further out: the exit ring is where the player
            // would come in anyway, so it is the least surprising place to start
            for (int ring = 0; ring < 3; ring++) {
                var dist = ed * (1f + ring * 0.5f);
                for (int i = 0; i < 16; i++) {
                    var loc = Rng.UInsideUnitCircle().normalized * dist;
                    if (!IsAnyStationVisibleFrom(entering, loc)) {
                        player.Location = loc;
                        return;
                    }
                }
            }
            D.Warn("Could not find a start point out of view of space stations in {0}",
                entering);
        }

        public static bool IsAnyStationVisibleFrom(SpaceObject sector,
                UnityEngine.Vector2 loc) {
            if (sector.Children == null) { return false; }
            foreach (var child in sector.Children) {
                if (child.Ship?.Class.IsSpaceStation != true) { continue; }
                if (child.IsVisibleFrom(loc)) { return true; }
            }
            return false;
        }

        public override void Unload() { }

        public string GetName() {
            return Id;
        }

        public Entity Entity => null;

        [Obsolete("Replace will S.UIQuery.ShowStarmap.Ask(true)")]
        public void OpenStarmap() {
            S.UIQuery.ShowStarmap.Ask(true);
        }

        public void FactionsIn(SpaceObject so, Dictionary<FactionDef, float> res,
                int fallbackFactionId) {
            if (so.Type.IdH == SpaceObjectTypeIdH.Hyperspace) {
                D.ErrSoft("Trying to get factions in hyperspace!");
                res.Add(FactionDef.FastGet(fallbackFactionId), 0.001f);
                return;
            }
            SpaceObject parent = so;
            while (parent != null && parent.SpaceEntity?.Biome.FactionsByDominance == null) {
                parent = parent.Parent;
            }
            if (parent == null) {
                // this is normal in starting location
                // D.Err("Failed to find sector/region in parent: {0}", so);
                res.Add(FactionDef.FastGet(fallbackFactionId), 0.001f);
                return;
            }
            res.AddRange(parent.SpaceEntity.Biome.FactionsByDominance);
        }

        public void Regenerate(int seed) {
            if (!Ready.Save) {
                D.Err("Failed regenerating universe due to save not ready!");
                return;
            }
            S.Sys.ShipNav.IsQuickHyperspace = true;
            if (UIShowing.Starmap) {
                S.UIQuery.ShowStarmap.Ask(false);
            }
            D.Warn("Regenerating Universe. Version: {0}", GenUniverse.Version);
            var s = S;
            s.Sys.Expedition.EndAll();
            if (s.Sys.Copters.Missions.Count > 0) {
                foreach (var m in s.Sys.Copters.Missions) {
                    m.SetStage(CopterMissionStageH.Error);
                }
            }
            var player = s.Universe.Player;
            s.Sys.Universe.EnterObject(s.Universe.Hyperspace, player.Parent);

            var gen = new GenSpace(S, seed);
            var rng = new Rng(seed);
            // generate space
            var map = gen.Generate(S.Query.GetSpaceMap.Ask()?.GenParams ?? default);

            // A regenerated universe is a fresh start, so it follows the new game rule, the
            // fallback below included
            var sectors = new List<SpaceSector>();
            foreach (var reg in map.Regions) {
                foreach (var sec in reg.Sectors) {
                    if (StartingSector.IsAllowed(sec)) {
                        sectors.Add(sec);
                    }
                }
            }
            SpaceSector startingSector = null;
            var startingRegion = StartingRegion.PickRandom(map, null, rng);
            if (startingRegion != null) {
                startingSector = sectors
                    .Where(s => s.Region == startingRegion)
                    .OrderBy(s => rng.Value)
                    .FirstOrDefault();
            }

            if (startingSector == null) {
                D.Err("Failed to find a nice starting sector!");
                startingSector = rng.From(sectors);
            }
            var oldShip = player.Ship;
            S.Sys.NPCShip.DepopulateUniverse(keepPlayer: false);
            var newU = GenUniverse.Create(S, startingSector.Region);
            newU.HyperspaceLocation = startingSector.SO;

            var newPlayer = newU.Player;
            newU.ObjectsById.Remove(newPlayer.Id);
            var newPlayerParent = newU.Player.Parent;
            newPlayerParent.RemoveChild(newPlayer);
            player.Id = newU.NextId;
            player.Parent?.RemoveChild(player);
            newPlayerParent.AddChild(player);
            player.Location = newPlayer.Location;
            s.Sys.Get<SpaceSys>(SpaceSys.SysId).SetMap(map);
            newU.Player = player;
            newU.ObjectsById.Add(player.Id, player);
            s.Universe = newU;
            s.Sig.UniverseCreated.Send(newU);
            s.Sys.Planets.Reset();
            s.Sys.Ship.TeleportTo(newPlayer.Location);
            s.UIQuery.RebuildStarmap.Ask(true, true);
            S.TelescopeScanCount = 0;
            S.TelescopeProgress = 0f;
            if (s.Sys.Terraform.KnownTerraformable.Count > 0) {
                s.Sys.Terraform.KnownTerraformable.Clear();
            }
            if (oldShip != null) {
                s.Sys.NPCShip.MigratePlayerShip(oldShip);
            } else {
                s.Sys.NPCShip.CreatePlayerShip();
            }
            var storyCtx = s.Query.GetStoryContext.Ask();
            storyCtx.CurrentSector = startingSector;
            s.Sys.ShipNav.ExitHyperspace(null);
            s.Sig.UniverseRegenerated.Send();
            s.Sys.Scan.Reset();
            s.Sys.ShipNav.IsQuickHyperspace = false;
            // if (s.Sys.Quest != null) {
            //     s.Sys.Quest.ResetQuest(QuestId_Legacy.ColonizePlanet);
            //     s.Sys.Quest.ResetQuest(QuestId_Legacy.FindHabitablePlanet);
            // }
        }

        public void ShowInStarmap(SpaceObject so) {
            if (so == null) {
                D.Err("Trying to show null SO in starmap!");
                return;
            }
            if (so.IsShip && so.Parent == null) {
                // ship is in hyperspace or not spawned?
                so = so.Ship.Sector.SO;
            }
            if (so.Region != null) {
                ShowStarmapRegion(so);
            } else {
                if (so.Sector != null) {
                    ShowStarmapSector(so);
                } else if (so.Parent?.Sector != null) {
                    ShowStarmapLocal(so);
                }
            }
        }

        public static void ShowStarmapRegion(SpaceObject so) {
            var reg = so;
            while (reg != null && reg.Region == null) {
                reg = reg.Parent;
            }
            if (reg == null) {
                D.Err("Couldn't find region for so: {0}", so);
                return;
            }
            A.StarmapCtx.SwitchLayer(UniverseLayer.Universe);
            if (!UIShowing.Starmap) {
                A.S.UIQuery.ShowStarmap.Ask(true);
            }
            A.S.UIQuery.StarmapFocusOn.Ask(reg, true);//, "UnivSys.ShowStarmapRegion");
        }
        public static void ShowStarmapSector(SpaceObject so) {
            if (so == null) {
                D.Err("Trying to show null starmap sector!");
                return;
            }
            var sec = so;
            while (sec != null && sec.Sector == null) {
                sec = sec.Parent;
            }
            if (sec == null) {
                D.Err("Couldn't find sector for so: {0}", so);
                return;
            }
            var playerSector = SOUtils.GetPlayerSector(so.S);
            if (playerSector == null) {
                D.Err("Player sector null!");
                return;
            }
            var jumpProgress = so.S.Query.GetHyperjumpProgress.Ask();
            if (playerSector.Region != sec.Parent?.Region || (A.IsInHyperspace && jumpProgress.IsMovingInUniverseLayer)) {
                // sector we can't look into, show the parent
                ShowStarmapRegion(sec);
                return;
            }

            if (!UIShowing.Starmap) {
                A.S.UIQuery.ShowStarmap.Ask(true);
            }
            A.S.UIQuery.StarmapFocusOn.Ask(sec, true);
        }

        public static void ShowStarmapLocal(SpaceObject so) {
            var sec = so.Parent;
            while (sec != null && sec.Sector == null) {
                sec = sec.Parent;
            }
            if (sec == null) {
                D.Err("Couldn't find sector for so: {0}", so);
                return;
            }
            var playerSector = SOUtils.GetPlayerSector(so.S);

            if (playerSector.Region != sec.Parent.Region) {
                // sector we can't look into, show the parent
                ShowStarmapRegion(sec);
                return;
            }

            if (playerSector != sec.Sector || A.IsInHyperspace) {
                ShowStarmapSector(sec);
                return;
            }


            A.StarmapCtx.SwitchLayer(UniverseLayer.Sector);
            if (!UIShowing.Starmap) {
                A.S.UIQuery.ShowStarmap.Ask(true);
            }
            A.S.UIQuery.StarmapFocusOn.Ask(so, false);
        }
    }
}
