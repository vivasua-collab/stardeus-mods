using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Systems.UIUX;
using Game.Utils;

namespace Game.Components.Upgrades {
    public abstract class GameCartridgeUpgrade : Upgrade {
        public override string Icon => GraphicsId.ObjUpgradesGameCartridgeUpgrade;
        public abstract string MinigameId { get; }

        public override bool IsCompatibleWith(Entity entity) {
            return entity.HasComponent<ShipComputerComp>()
                || entity.HasComponent<ArcadeCabinetComp>();
        }

        public override void OnInstall(Entity entity) {
            if (!Ready.Game) { return; }
            if (IsUnlocked(MinigameId, entity)) { return; }
            var app = Registries.ShipComputerApps.Get(MinigameId, false);
            string gameName = app != null ? app.Name : MinigameId;
            WPopup.Create(false,
                    app?.Icon ?? Icon,
                    T.GameCartridgeInstalledTitle,
                    T.GameCartridgeInstalledSubtitle_V1.F(gameName))
                .WithAction(T.VerbPlay, () => A.S.UIQuery.ToggleMinigame.Ask(MinigameId))
                .WithAction2(T.Ok, null)
                .Spawn(center: true);
        }

        public static bool IsUnlocked(string minigameId) {
            return IsUnlocked(minigameId, null);
        }

        public static bool IsUnlocked(string minigameId, Entity ignoreHost) {
            if (!Ready.Game) { return false; }
            List<UpgradesComp> upgraded = A.S.Sys.Upg.Upgraded;
            if (upgraded == null) { return false; }
            for (int i = 0; i < upgraded.Count; i++) {
                UpgradesComp comp = upgraded[i];
                if (ignoreHost != null && comp.Entity == ignoreHost) { continue; }
                string[] slots = comp.Upgrades;
                if (slots == null) { continue; }
                for (int j = 0; j < slots.Length; j++) {
                    string id = slots[j];
                    if (id == null) { continue; }
                    if (UpgradeRegistry.Get(id) is GameCartridgeUpgrade cart
                            && cart.MinigameId == minigameId
                            && HostIsUsable(comp)) {
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool HostIsUsable(UpgradesComp comp) {
            return comp.Tile != null
                && comp.Tile.IsConstructed
                && (comp.Tile.ENode?.IsReachable != false);
        }
    }
}
