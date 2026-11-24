using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class DeployResourceMinerAction : BaseEntityAction {
        public const string ActionId = "DeployResourceMiner";

        [SelfInit]
        private static void Register() {
            RegisterAction(new DeployResourceMinerAction());
        }

        public override string Id => ActionId;

        protected override bool ApplyFor(GameState s, Tile tile,
                out EntityActionData data) {
            tile = EntityUtils.ParentObj(tile);
            if (!CanApply(tile, out var miner)) {
                data = EntityActionData.Unavailable;
                return false;
            }

            data = new EntityActionData(this,
                (l, p, i) => Create(l, p, i, miner),
                Priority.Low);
            return true;
        }

        private WBase Create(Lifetime l, RectTransform p, int i,
                ResourceMinerComp miner) {
            var applicable = !miner.IsReserved && !miner.Tile.Health.IsHeavilyDamaged;
            var widget = SpawnButton(l, applicable, p, i);
            widget.Load(IconId.CDrill, T.Deploy, () => Deploy(miner));
            UIUtils.AddTipFunc(l, widget, () => BuildTooltip(miner));
            return widget;
        }

        private static void Deploy(ResourceMinerComp miner) {
            if (!miner.Tile.IsActive) {
                UIUtils.RefreshSelectedEntityUI();
                return;
            }
            if (miner.IsReserved) {
                miner.S.Sig.ShowErrorMessage.Send(T.MaterialReserved_V1.F(miner.Tile.NameLink));
                return;
            }

            if (miner.Tile.Health.IsHeavilyDamaged) {
                miner.S.Sig.ShowErrorMessage.Send($"{T.OperationFailed}: {T.OperationFailedDeviceHeavilyDamaged}");
                return;
            }

            miner.S.BB.Set((int) BBKeyH.MinerId, miner.EntityId);
            miner.S.UIQuery.ShowStarmap.Ask(true);
        }

        private static bool CanApply(Tile tile, out ResourceMinerComp miner) {
            miner = null;
            if (!tile.IsActive || !tile.IsConstructed || !tile.TryGetComponent(out miner)) {
                return false;
            }

            return true;
        }

        private static string BuildTooltip(ResourceMinerComp miner) {
            var tip = $"{Texts.Accented(T.MiningDepthMax)}: {Units.Depth(miner.AutoMineMaxDepth)}\n\n{T.ResourceMinerTip}";
            if (miner.IsReserved) {
                return $"{Texts.Red(T.MaterialReserved_V1.F(miner.Tile.NameLink))}\n\n{tip}";
            }
            if (miner.Tile.Health.IsHeavilyDamaged) {
                return $"{Texts.Red(T.OperationFailedDeviceHeavilyDamaged)}\n\n{tip}";
            }

            return tip;
        }
    }
}
