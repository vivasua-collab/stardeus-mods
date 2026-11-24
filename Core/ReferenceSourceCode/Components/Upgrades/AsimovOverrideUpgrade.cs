using Game.Data;
using Game.Constants;
using KL.Utils;
using UnityEngine;
using Game.Achievements;
using Game.Utils;
using Game.CodeGen;
using Game.UI;
using Game.Systems.Items;
using System.Text;

namespace Game.Components.Upgrades {
    public sealed class AsimovOverrideUpgrade : Upgrade {
        public const string Id = "AsimovOverride";
        public override string Icon => GraphicsId.ObjUpgradesAsimovOverrideUpgrade;
        public override string Title => T.ObjUpgradesAsimovoverrideupgrade;
        public override string UpgradeId => Id;
        public override string MatTypeId => MatId.AsimovOverrideUpgrade;
        public override bool CanRemove(out string reason) {
            reason = T.OperationForbidden;
            return false;
        }

        [SelfInit]
        private static void Register() {
            Add(Id, new AsimovOverrideUpgrade());
        }
        public override bool IsCompatibleWith(Entity entity) {
            return entity.HasComponent<ShipComputerComp>();
        }

        public override void OnInstall(Entity entity) {
            var hadOverride = entity.S.BB.Get(BBKeyH.HasAsimovOverride, false);
            entity.S.BB.Set(BBKeyH.HasAsimovOverride, true);
            if (!Ready.Game) { return; }
            var rogueAiRoot = entity.S.Research.Get(DefIdsH.ResearchRogueAIRoot);
            if (!rogueAiRoot.IsDiscovered) {
                rogueAiRoot.Discover(entity.S);
            }
            bool didUnlock = The.AchievementTracker.Unlock(AchievementAsimovOverride.AchievementId);
            if (!hadOverride) {
                // The override silently opens up a dozen previously forbidden
                // actions, so spell them out - otherwise the biggest power spike
                // in the game is invisible to the player.
                var sb = new StringBuilder();
                sb.Append($"{T.AsimovOverrideInstalled}<br><br>");
                AsimovOverrideEffects.Append(sb);
                The.Dialog.ShowSimple(IconId.IconsFactionVirus, sb.ToString());
                entity.S.Sys.Log.AddLine(Texts.Red(T.AsimovOverrideInstalled));
                if (!didUnlock) {
                    UISounds.PlayAchievement();
                }
            }
        }

        public override void AddDefInfo(StringBuilder sb) {
            if (sb.Length > 0) {
                sb.Append("\n\n");
            }
            AsimovOverrideEffects.Append(sb);
        }

        public override void OnUninstall(Entity entity) {
            var hadOverride = entity.S.BB.Get(BBKeyH.HasAsimovOverride, false);
            bool hasUpgrade = false;
            if (entity is Tile t) {
                var ups = t.Upgrades.Upgrades;
                for (int i = ups.Length - 1; i >= 0; i--) {
                    if (ups[i] == Id) {
                        hasUpgrade = true;
                        break;
                    }
                }
            }
            entity.S.BB.Set(BBKeyH.HasAsimovOverride, hasUpgrade);
            if (hadOverride && !hasUpgrade && Ready.Game) {
                // the governor module is back: robots put down what it forbids
                RobotWeaponLaw.DropIllegalAll(entity.S);
            }
        }
    }
}
