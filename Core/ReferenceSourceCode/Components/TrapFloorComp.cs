using System;
using System.Collections.Generic;
using Game.Commands;
using Game.Constants;
using Game.Data;
using Game.Story.Events;
using Game.Systems.UIUX;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Systems.BodyParts;
using Game.Systems.Creatures;
using Game.Systems.Items;
using Game.Systems.UIUX.Codex.Pages;

namespace Game.Components {
    public sealed class TrapFloorComp : BaseComponent<TrapFloorComp>,
            ICopyableComp, IDefPowerProvider {

        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new TrapFloorComp());
        }
        private const float DefaultEnergyCost = 10f;
        private void UpdateTrapGraphic() {
            if (wasTriggered == isTrapTriggered) { return; }
            wasTriggered = isTrapTriggered;
            graphics?.SetAnimationFrame(isTrapTriggered ? 1 : 0);
        }

        public bool TriggerEnemy;
        public bool TriggerAnyone;

        private TileGraphicsComp graphics;
        public bool IsClosed => isTrapTriggered;
        public bool IsTrapReady => isTrapReady;
        public float ChargeProgress => resetTimeHours > 0f ? charge / resetTimeHours : 0f;

        private BFloat chargeProgressB;
        public BFloat ChargeProgressB => chargeProgressB ??= new BFloat(ChargeProgress);

        private BCallback stateChangedB;
        public BCallback StateChangedB => stateChangedB ??= new BCallback();

        private bool isTrapTriggered;
        private bool isTrapReady;
        private float damageAmt;
        private bool wasTriggered;
        private float energyCost;
        private float charge;
        private float resetTimeHours;
        private bool isSubscribed;
        private int posIdx;

        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetBool("IsTriggered", isTrapTriggered);
            data.SetBool("TrigEnemy", TriggerEnemy);
            data.SetBool("TrigAnyone", TriggerAnyone);
            data.SetFloat("Charge", charge);
        }

        protected override void OnLoad(ComponentData data) {
            isTrapTriggered = data.GetBool("IsTriggered", isTrapTriggered);
            TriggerEnemy = data.GetBool("TrigEnemy", TriggerEnemy);
            TriggerAnyone = data.GetBool("TrigAnyone", TriggerAnyone);
            charge = data.GetFloat("Charge", 0f);
        }

        public override void OnLateReady(bool wasLoaded) {
            wasTriggered = true;
            graphics = Entity.GetComponent<TileGraphicsComp>();
            UpdateTrapGraphic();
            isTrapReady = !isTrapTriggered && charge >= resetTimeHours;
            posIdx = Tile.Transform.PosIdx;
            if (!isSubscribed) {
                isSubscribed = true;
                S.Sig.BeingPosChanged.AddListener(OnBeingPosChange);
            }
        }

        public override void OnRemove() {
            if (isSubscribed) {
                isSubscribed = false;
                S.Sig.BeingPosChanged.RemoveListener(OnBeingPosChange);
            }
        }

        public override void UpdatePosIdx(int p, int dx, int dy) {
            posIdx = Pos.NeighborAt(posIdx, dx, dy);
        }

        // Grounded means "cannot fly": anyone in a suit with thrusters (CombatSuit01 and the
        // like) counted as flying over the spikes, so traps missed most raiders and hit
        // nobody when fired by hand either (STA-1893). Only beings that fly by their own body
        // (a MiniJet, a flying species) hover over the trap.
        private static bool WalksOnFloor(Being b) {
            if (b.Motion.IsGrounded) { return true; }
            var srcs = b.Data.AbilitySources;
            for (int i = srcs.Count - 1; i >= 0; i--) {
                var src = srcs[i];
                if (src.Ability.IdH == AbilityIdH.Fly && src.Slot is not InventoryItemSlot
                        && src.Slot.IsUsableWithParent) {
                    return false;
                }
            }
            return true;
        }

        private void OnBeingPosChange(Being b, int from, int to) {
            // A being placed on the map (a load, a spawn) is not stepping onto the trap, and
            // during a load its body is not restored yet (STA-1937)
            if (from == Pos.Invalid) { return; }
            if (!isTrapTriggered && isTrapReady && to == posIdx
                    && b.Carrier == null
                    && WalksOnFloor(b) && b.IsAlive && !b.Data.Body.IsIncapacitated) {
                if (TriggerAnyone || (TriggerEnemy && !b.Data.IsFriendly)) {
                    TriggerTrap(true);
                }
            }
        }

        protected override void OnConfig() {
            damageAmt = Config.GetInt(PropertyIdH.Damage, 75);
            energyCost = Config.GetFloat(PropertyIdH.EnergyCost, DefaultEnergyCost);
            TriggerEnemy = true;
            TriggerAnyone = false;
            resetTimeHours = Config.GetFloat(PropertyIdH.ResetTimeHours, 2f);
            if (resetTimeHours < 0.01f) {
                resetTimeHours = 0.01f;
            }
            isTrapTriggered = false;
        }

        public void AddDefPower(Def def, ComponentConfig config, ref DefPower power) {
            power.AddConsumption(config.GetFloat(PropertyIdH.EnergyCost, DefaultEnergyCost));
        }

        public override string ToString() {
            return " * TrapFloor";
        }
        private ElectricNodeComp elNode;
        public ElectricNodeComp ENode => elNode;

        public bool HasSubmenuNow => true;

        public string SubmenuTitle => Tile.Definition.NameT;

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ElectricNodeAdded) {
                if (sender is ElectricNodeComp elnode) {
                    this.elNode = elnode;
                    elnode.SetConsumerWantedInput(energyCost);
                    elnode.AddAfterTick(this, AfterTickGrid);
                }
            } else if (message == MsgIdH.ElectricNodeConnectionChanged) {
                if (Tile.ENode.IsConnected && EntityUtils.IsWithinMargin(Tile.PosIdx, false, false) && Ready.Gen) {
                    S.UIQuery.ShowTutorialPopup.Ask(TrapFloorPage.PageId, 3f);
                }
            }
        }

        private long lastGridTick;

        private void AfterTickGrid() {
            var isConsuming = elNode.IsConsuming;
            if (isTrapReady != isConsuming) {
                isTrapReady = isConsuming;
                stateChangedB?.Invoke();
            }
            if (isTrapReady && !isTrapTriggered && (TriggerEnemy || TriggerAnyone)) {
                // auto pierce all eggs
                bool foundEgg = false;
                if (S.Objs.ByPos.TryGetValue(Tile.PosIdx, out var objs)) {
                    for (int i = objs.Count - 1; i >= 0; i--) {
                        Obj o = objs[i];
                        if (o.Carrier != null) { continue; }
                        if (o.HasComponent<EggComp>()) {
                            o.Health.TakeDamage(o.Health.Health, DamageType.Piercing);
                            foundEgg = true;
                        }
                    }
                }
                if (foundEgg) {
                    TriggerTrap(true);
                    return;
                }
            }
            if (isTrapReady && isTrapTriggered) {
                var tick = G.Ticks;
                if (lastGridTick == 0) {
                    lastGridTick = tick;
                    return;
                }
                var tickDiff = tick - lastGridTick;
                lastGridTick = tick;
                var hoursPassed = tickDiff / (float) Consts.TicksPerHour;
                charge += hoursPassed;
                if (charge >= resetTimeHours) {
                    charge = resetTimeHours;
                    SetTriggered(false);
                } else {
                    chargeProgressB?.Set(ChargeProgress);
                }
                UpdateBar();
            }
        }

        // Public for tests
        public void TriggerTrap(bool automatic) {
            if (Tile?.Health?.IsHeavilyDamaged == true) {
                if (!automatic) {
                    S.Sig.ShowErrorMessage.Send($"{T.OperationFailed}: {T.OperationFailedDeviceHeavilyDamaged}");
                }
                return;
            }
            if (isTrapTriggered || elNode == null ||
                    !elNode.IsConsuming || !elNode.IsReachable) {
                if (!automatic) {
                    UISounds.PlayActionDenied();
                }
                return;
            }
            SetTriggered(true);
        }

        // for procgen
        public void SetTriggered(bool triggered) {
            var wasTriggeredBefore = isTrapTriggered;
            if (!isTrapTriggered && triggered) {
                DealDamage();
            }
            isTrapTriggered = triggered;
            if (triggered) {
                charge = 0f;
                lastGridTick = 0;
                Sounds.PlaySpikes(Entity.Position);
                UpdateBar();
            }
            UpdateTrapGraphic();
            if (wasTriggeredBefore != isTrapTriggered) {
                stateChangedB?.Invoke();
            }
        }

        private void DealDamage() {
            if (S.Beings.ByPos.TryGetValue(Entity.PosIdx, out var beings)) {
                for (int i = beings.Count - 1; i >= 0; i--) {
                    Being b = beings[i];
                    if (WalksOnFloor(b) || (b.IsDead && b.Carrier == null)) {
                        DealDamageTo(b);
                    }
                }
            }
        }

        private void DealDamageTo(Being b) {
            b.AIAgent?.CombatUnit?.OnHarmed();
            var sev = Severities.Random(Severity.Mild, Severity.Severe);
            Func<BodyPartSlot, bool> incCheck;
            if (b.Graphics.IsVertical && !b.IsDead) {
                incCheck = (bp) => {
                    if (bp.Type.IdH == BodyPartIdH.Paw) {
                        return true;
                    }
                    if (bp.Type.IdH == BodyPartIdH.Foot) {
                        return true;
                    }
                    if (bp.Type.IdH == BodyPartIdH.Leg) {
                        return true;
                    }
                    if (bp.Type.IdH == BodyPartIdH.Body) {
                        return true;
                    }
                    if (bp.Type.IdH == BodyPartIdH.Mobilizer) {
                        return true;
                    }
                    return false;
                };
            } else {
                incCheck = null;
            }
            var numParts = S.Rng.OneOf(2, 3);
            for (int i = 0; i < numParts; i++) {
                BeingUtils.DamageRandomBodyPart(S, b.Data, Rng.Unseeded,
                    DamageType.Piercing, sev, Tile, 1f, incCheck);
            }
        }

        private bool isShowingInfoBar;

        private void UpdateBar() {
            if (!Tile.IsConstructed) { return; }
            if (charge < resetTimeHours) {
                isShowingInfoBar = true;
                var pct = charge / resetTimeHours;
                var bar = ShowInfoBar(Consts.ColorFuelBar, pct,
                    0f, 0, T.BatteryChargeBar_V1.F(Units.Percentage(pct)));
                bar.IsPretranslated = true;
            } else if (isShowingInfoBar) {
                isShowingInfoBar = false;
                HideInfoBar();
            }
        }

        private static readonly List<string> triggerTypeOpts = new();

        private static string TriggerNameFor(int opt) {
            switch (opt) {
                case 0: return T.NoTarget;
                case 1: return $"{T.NounTarget}: {T.NounEnemy}";
                case 2: return $"{T.NounTarget}: {T.All}";
            }
            return T.Unknown;
        }

        private static List<string> TriggerTypeOpts {
            get {
                if (triggerTypeOpts.Count == 0) {
                    for (int i = 0; i < 3; i++) {
                        triggerTypeOpts.Add(TriggerNameFor(i));
                    }
                }
                return triggerTypeOpts;
            }
        }

        public bool IsCopyable => Tile.ENode?.IsReachable != false;
        public string CopyConfigText => Tile.Definition.NameT;
        public bool CopyConfigTo(ICopyableComp target) {
            bool changed = false;
            if (target is TrapFloorComp t) {
                if (t.TriggerAnyone != TriggerAnyone) {
                    changed = true;
                    t.TriggerAnyone = TriggerAnyone;
                }
                if (t.TriggerEnemy != TriggerEnemy) {
                    changed = true;
                    t.TriggerEnemy = TriggerEnemy;
                }
            }
            return changed;
        }

        public bool CanCopyTo(ICopyableComp comp) {
            return comp is TrapFloorComp;
        }
    }
}
