using Game.Commands;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using Game.Systems.Creatures;
using Game.Systems.Story;

namespace Game.Components {
    public sealed class FloorVentComp : BaseComponent<FloorVentComp>,
            IHullIntegrityProvider, IDefPowerProvider {

        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new FloorVentComp());
        }
        private const float DefaultEnergyCost = 0.1f;
        private void UpdateVentGraphicAndPassability() {
            if (wasClosed == isClosed) { return; }
            wasClosed = isClosed;
            Tile.Passability.Update(true, isClosed, 0f);
            graphics?.SetAnimationFrame(isClosed ? 0 : 1);
            // graphics.SetAnimationFrame does that
            //EntityUtils.TriggerChange(Tile);
            S.Sig.RecalculatePassability.Send(Tile.PosIdx);
            Transmit(this, MsgIdH.HullIntegrityChanged);
        }

        private TileGraphicsComp graphics;
        public bool IsClosed => isClosed;
        private bool isClosed;
        private bool wasClosed;
        private float energyCost;

        public float Integrity => isClosed ? 1f : 0f;

        public string CommonActionId => "common.floor.vent";

        public bool IsReachableForCommonAction => Tile.ENode.IsReachable;

        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetBool("IsClosed", isClosed);
        }

        protected override void OnLoad(ComponentData data) {
            isClosed = data.GetBool("IsClosed", isClosed);
        }

        public override void OnLateReady(bool wasLoaded) {
            wasClosed = true;
            graphics = Entity.GetComponent<TileGraphicsComp>();
            UpdateVentGraphicAndPassability();
        }

        public override void OnRemove() {
        }

        protected override void OnConfig() {
            energyCost = Config.GetFloat(PropertyIdH.EnergyCost, DefaultEnergyCost);
            isClosed = true;
        }

        public void AddDefPower(Def def, ComponentConfig config, ref DefPower power) {
            power.AddConsumption(config.GetFloat(PropertyIdH.EnergyCost, DefaultEnergyCost));
        }

        public override string ToString() {
            return " * FloorVent";
        }
        private ElectricNodeComp elNode;
        public ElectricNodeComp ENode => elNode;
        // Toggling does nothing without power, so the Vent tab disables its button on this
        public bool IsPowered => elNode != null && elNode.IsConsuming && elNode.IsReachable;

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ElectricNodeAdded) {
                if (sender is ElectricNodeComp elnode) {
                    this.elNode = elnode;
                    elnode.SetConsumerWantedInput(energyCost);
                }
            }
        }

        // Public for tests
        public void ToggleVent() {
            if (Tile.Health.IsHeavilyDamaged) {
                S.Sig.ShowErrorMessage.Send(T.OperationFailedDeviceHeavilyDamaged);
                return;
            }
            if (!IsPowered) {
                UISounds.PlayActionDenied();
                return;
            }
            if (isClosed) {
                if (S.Beings.CanPerformHarmfulActionAt(entity.PosIdx)) {
                    SetClosed(false);
                } else {
                    UISounds.PlayActionDenied();
                }
            } else {
                SetClosed(true);
            }
        }

        // for procgen
        public void SetClosed(bool closed) {
            if (isClosed && !closed) {
                VentIntoSpace();
            }
            isClosed = closed;
            UpdateVentGraphicAndPassability();
        }

        private void VentIntoSpace() {
            var isInARoom = S.Sys.Areas.At(Entity.PosIdx) > 0;
            if (!isInARoom) { return; }
            var isPressurized = S.GetO2(Entity.PosIdx) > Consts.MinOxygen;
            if (!isPressurized) { return; }
            var obj = EntityUtils.ParentObjAt(Entity.PosIdx);
            int suckedOutCount = 0;
            if (obj != null) {
                if (obj.Passability.IsWalkable
                        && obj.Transform.Width == 1
                        && obj.Transform.Height == 1) {
                    S.CmdQ.Enqueue(new CmdRemoveTile(obj));
                    suckedOutCount++;
                }
            }
            if (S.Beings.ByPos.TryGetValue(Entity.PosIdx, out var beings)) {
                for (int i = beings.Count - 1; i >= 0; i--) {
                    Being b = beings[i];
                    if (b.Motion.IsGrounded) {
                        VentBeingIntoSpace(b);
                        suckedOutCount++;
                    }
                }
            }
            if (S.Objs.ByPos.TryGetValue(Entity.PosIdx, out var objs)) {
                for (int i = objs.Count - 1; i >= 0; i--) {
                    Obj o = objs[i];
                    if (o.Carrier == null) {
                        S.Objs.Destroy(o, true);
                        //S.RemoveEntity(o);
                        suckedOutCount++;
                    }
                }
            }
            if (suckedOutCount > 0) {
                #if !PRODUCTION_BUILD
                D.Err("Reminder: whoosh sound");
                #endif
            }
        }

        private void VentBeingIntoSpace(Being b) {
            if (b.Data.IsFriendly) {
                var data = StoryEventData.CreateCustom(
                    IconId.CSkull, T.EvVentedIntoSpace,
                    T.EvVentedIntoSpaceDesc_V1.F(b.Data.Mind.Name.DisplayName));
                S.Sig.AddCustomStoryEvent.Send(data, true);
                S.Sys.Log.AddLine(b, Texts.LinkTo(T.EvVentedIntoSpace, Tile.PosIdx));
            }
            BeingUtils.Eliminate(S, b.Data, T.EvVentedIntoSpace, G.Ticks);
        }
    }
}
