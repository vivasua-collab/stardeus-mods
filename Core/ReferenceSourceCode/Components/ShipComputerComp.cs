using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using Game.Systems;
using Game.Systems.UIUX;
using Game.Systems.Firex;
using Game.Systems.Notify;
using Game.UI;
using Game.Input;
using UnityEngine;

namespace Game.Components {
    public sealed class ShipComputerComp : BaseComponent<ShipComputerComp> {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new ShipComputerComp());
        }

        public ShipComputerEfficiencyComp Efficiency;
        public ElectricNodeComp ENode;
        private NotificationHandle disconnectedNotification;
        private NotificationHandle damagedNotification;
        // For tests
        public bool IsShowingDisconnectedAlert => disconnectedNotification.IsValid;
        public override void OnLateReady(bool wasLoaded) {
            Efficiency = Entity.GetComponent<ShipComputerEfficiencyComp>();
            Ready.When(WhenH.AllSystemsLoaded, UpdateShipComputerDamagedFX);
            if (wasLoaded) {
                Ready.When(WhenH.AllSystemsLoaded, RestoreDisconnectedAlert);
            }
            // A computer joins or leaves the ship's grid when any connection changes
            S.Sig.ElectricityGridChanged.AddListener(UpdateShipComputerDamagedFX);
        }

        // The alert is not saved, so a load rebuilds it, quietly: a Ship Computer that was
        // unlinked before the save has not just lost its connection
        private void RestoreDisconnectedAlert() {
            ValidateShipComputerConnectivity(announce: false);
        }

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ElectricNodeAdded) {
                if (sender is ElectricNodeComp elNode) {
                    this.ENode = elNode;
                    elNode.AddAfterTick(this, AfterTickGrid);
                }
            } else if (message == MsgIdH.ElectricNodeConnectionChanged) {
                ValidateShipComputerConnectivity(announce: true);
            } else if (message == MsgIdH.TookDamage
                    || message == MsgIdH.RepairedSomeDamage
                    || message == MsgIdH.RepairedAllDamage
                    || message == MsgIdH.HealthWentToZero
                    || message == MsgIdH.ConstructionFinished) {
                UpdateShipComputerDamagedFX();
            }
        }

        private void UpdateShipComputerDamagedFX() {
            RefreshDamagedFX(S);
        }

        // The grey screen alone doesn't tell the player that the ship computer causes it
        private void UpdateDamagedNotification(bool countsForShip) {
            var health = Tile.Health;
            bool damaged = countsForShip && Entity.IsActive && IsConstructed
                && health.Health < health.MaxHealth;
            if (!damaged) {
                if (damagedNotification.IsValid) {
                    Notification.Remove(ref damagedNotification);
                }
            } else if (!damagedNotification.IsValid) {
                damagedNotification = Notification.BuildFor(Tile, null,
                    $"{Texts.Red(T.Damaged)}: {Tile.Definition.NameT}", 0, NotificationType.Alert)
                    .Group("cpu_dmg")
                    .Show();
            }
        }

        public static void RefreshDamagedFX(GameState S) {
            float totalHealth = 0f;
            float totalMaxHealth = 0f;
            var computers = S.Components.AllByType<ShipComputerComp>();
            if (computers != null) {
                var main = FindMain(S);
                foreach (ShipComputerComp computer in computers) {
                    bool counts = main == null || computer.BelongsToShipOf(main);
                    computer.UpdateDamagedNotification(counts);
                    if (!counts || !computer.Entity.IsActive || !computer.IsConstructed) {
                        continue;
                    }
                    var health = computer.Tile.Health;
                    totalHealth += Mathf.Clamp(health.Health, 0f, health.MaxHealth);
                    totalMaxHealth += health.MaxHealth;
                }
            }
            var damagePercent = totalMaxHealth > 0f
                ? 1f - totalHealth / totalMaxHealth
                : S.Scenario.IsSandbox ? 0f : 1f;
            CameraControls.SetShipComputerDamagedFX(damagePercent);
        }

        // Only the ship's own computers glitch the screen: the main one, and any other on its
        // grid. A derelict's or an unpowered spare computer does not (STA-1832). With one
        // computer left it is the main one, so its damage always shows.
        private bool BelongsToShipOf(ShipComputerComp main) {
            if (this == main) { return true; }
            var mainNode = main.Tile.ENode;
            var node = Tile.ENode;
            return mainNode != null && node != null
                && mainNode.IsConnected && node.IsConnected
                && node.Node.GridId == mainNode.Node.GridId;
        }

        private bool HasOtherConnectedShipComputer =>
            S.Components.CountMatching<ShipComputerComp>(
                c => c != this && c.Tile.ENode.IsConnected, 1) > 0;

        private void ValidateShipComputerConnectivity(bool announce) {
            if (!Tile.IsConstructed || Tile.ENode.IsConnected || HasOtherConnectedShipComputer) {
                RemoveDisconnectedNotification();
            } else if (!disconnectedNotification.IsValid) {
                var tip = $"{Tile.Definition.NameT}: {Texts.Red(T.EnergyNotConnected)}";
                disconnectedNotification = Notification.BuildFor(Tile, null, Texts.Red(T.EnergyNotConnected),
                    0, NotificationType.Critical)
                    .WithTooltip(tip)
                    .Group("cpu_conn")
                    .Show();
                if (!announce) { return; }
                var line = Tile.NameLink;
                line = $"{line}: {Texts.Red(T.EvtConnectionLoss)}";
                S.Sig.ShowFanfare.Send(line);
                S.Sys.Log.AddLine(line);
            }
        }

        private void RemoveDisconnectedNotification() {
            if (disconnectedNotification.IsValid) {
                Notification.Remove(ref disconnectedNotification);
                var line = Texts.LinkToTile(Tile);
                line = $"{line}: {Texts.Green(T.AdjAvailable)}";
                S.Sig.ShowFanfare.Send(line);
                S.Sys.Log.AddLine(line);
            }
        }

        private void AfterTickGrid() {
            Efficiency.ApplyHeatSinks();

        }

        public override string ToString() {
            return " * ShipComputer";
        }

        public override void OnRemove() {
            S.Sig.ElectricityGridChanged.RemoveListener(UpdateShipComputerDamagedFX);
            RefreshDamagedFX(S);
            if (damagedNotification.IsValid) {
                Notification.Remove(ref damagedNotification);
            }
            if (A.InstantBuild) { return; }
            if (!Ready.Gen) { return; }
            if (Tile.TryGetComponent<ReinstallableComp>(out var r)) {
                if (r.IsPending) { return; }
            }
            if (Tile.Health.IsHeavilyDamaged) {
                Explosion.Create(S, 10,
                    position: entity.Position,
                    flags: ExplosionFlags.SkipEvent);
                // S.CmdQ.Enqueue(new CmdCreateExplosion(entity.Position, 10, 300, true));
            }
            // in case this ship computer is not connected to energy
            S.Sys.Electricity.EnqueueRebuild();
        }

        // The ship is wherever its main computer stands. Derelicts carry their own CPUs, so the
        // first CPU found is not necessarily the ship's: a docked derelict's older CPU usually
        // comes first. The main one is a constructed CPU on an island, connected to a grid when
        // any is, on the largest grid, with the larger island breaking a tie.
        public static ShipComputerComp FindMain(GameState S) {
            var all = S.Components.AllByType<ShipComputerComp>();
            if (all == null) { return null; }
            var areas = S.Sys.Areas;
            // Systems initialize alphabetically, so Areas can ask before Electricity exists
            var electricity = S.Sys.Electricity;
            int candidates = 0;
            bool anyConnected = false;
            foreach (ShipComputerComp c in all) {
                if (!IsMainCandidate(c, areas)) { continue; }
                candidates++;
                anyConnected |= c.Tile.ENode?.IsConnected == true;
            }
            bool connectedOnly = candidates > 1 && anyConnected;
            ShipComputerComp best = null;
            int bestGridSize = -1;
            int bestIslandSize = -1;
            foreach (ShipComputerComp c in all) {
                if (!IsMainCandidate(c, areas)) { continue; }
                var node = c.Tile.ENode;
                if (connectedOnly && node?.IsConnected != true) { continue; }
                int gridSize = 0;
                if (node != null && electricity != null
                        && electricity.TryGetGrid(node.Node.GridId, out var grid)) {
                    gridSize = grid.Size;
                }
                int islandSize = areas.IslandSizeAt(c.Tile.PosIdx);
                if (gridSize > bestGridSize
                        || (gridSize == bestGridSize && islandSize > bestIslandSize)) {
                    best = c;
                    bestGridSize = gridSize;
                    bestIslandSize = islandSize;
                }
            }
            return best;
        }

        private static bool IsMainCandidate(ShipComputerComp c, AreaSys areas) {
            return c.Entity.IsActive && c.IsConstructed && areas.IslandAt(c.Tile.PosIdx) > 0;
        }

        public static void ShowApps(GameState S) {
            UIUtils.CloseFullScreenUI();
            var cpu = S.Components.FindFirstMatching<ShipComputerComp>(
                c => c.Tile.ENode.IsConnected
                    && EntityUtils.IsWithinMargin(c.Tile.PosIdx, false, false));
            if (cpu == null) {
                cpu = S.Components.FindFirstMatching<ShipComputerComp>(
                    c => EntityUtils.IsWithinMargin(c.Tile.PosIdx, false, false));
            }
            if (cpu == null) {
                cpu = S.Components.FindFirstByType<ShipComputerComp>();
            }
            if (cpu != null) {
                S.UIQuery.ShowSelectedEntityTab.Ask(ShipComputerAppsTab.TabId);
                S.Sig.SelectEntity.Send(cpu.Tile);
            } else {
                EntityUtils.CameraFocusOnShip();
            }
        }
    }
}
