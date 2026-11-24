using Game.Data;
using Game.Utils;
using Game.CodeGen;
using Game.Systems.UIUX;
using Game.Systems.UIUX.Codex.Pages;

namespace Game.Components {
    public sealed class TractorBeamTargetComp : BaseComponent<TractorBeamTargetComp>,
            IDefPowerProvider {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new TractorBeamTargetComp());
        }
        private const float DefaultEnergyCost = 10f;

        public void AddDefPower(Def def, ComponentConfig config, ref DefPower power) {
            power.AddConsumption(config.GetFloat(PropertyIdH.EnergyCost, DefaultEnergyCost));
        }

        // FIXME known issue, when there is a deficit, the graphic will still show active circle
        private void UpdateGraphic() {
            if (hadProjectile == hasProjectile) { return; }
            hadProjectile = hasProjectile;
            graphics?.SetAnimationFrame(hasProjectile ? 1 : 0);
            //EntityUtils.TriggerChange(Tile);
        }

        private TileGraphicsComp graphics;
        private bool hasProjectile;
        private bool hadProjectile;
        private bool isPowered;
        private float maxConsumption;
        public bool HasProjectile => hasProjectile;
        public bool IsValidNow => isPowered && Tile.IsConstructed;

        private BCallback stateChangedB;
        public BCallback StateChangedB => stateChangedB ??= new BCallback();

        public override void OnLateReady(bool wasLoaded) {
            graphics = Entity.GetComponent<TileGraphicsComp>();
        }

        private ElectricNodeComp elNode;
        public ElectricNodeComp ENode => elNode;

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ConstructionFinished) {
                S.Sig.TractorBeamTargetBuilt.Send(Tile.PosIdx, this);
            } else if (message == MsgIdH.ElectricNodeAdded) {
                if (sender is ElectricNodeComp elnode) {
                    elNode = elnode;
                    isPowered = elNode.IsPowerable && !elNode.HasDeficit;
                    elNode.AddAfterTick(this, AfterTickGrid);
                }
            }
        }

        public override void OnRemove() {
        }

        protected override void OnConfig() {
            maxConsumption = Config.GetFloat(PropertyIdH.EnergyCost, DefaultEnergyCost);
        }

        public override string ToString() {
            return " * TractorBeamTarget";
        }

        private void AfterTickGrid() {
            isPowered = elNode.IsPowerable && !elNode.HasDeficit;
            if (!hasProjectile) {
                elNode.SetConsumerWantedInput(0f);
            } else {
                if (!elNode.IsPowerable) {
                    SetHasProjectile(false);
                }
                elNode.SetConsumerWantedInput(maxConsumption);
            }
        }

        private void DropTarget() {
            if (!hasProjectile) { return; }
            hasProjectile = false;
            UpdateGraphic();
            stateChangedB?.Invoke();
        }

        public void SetHasProjectile(bool hasProjectileNow) {
            if (Tile.Health.IsHeavilyDamaged) {
                DropTarget();
                return;
            }
            if (!isPowered || !Tile.ENode.IsReachable) {
                DropTarget();
                return;
            }
            hasProjectile = hasProjectileNow;
            UpdateGraphic();
            stateChangedB?.Invoke();
        }
    }
}
