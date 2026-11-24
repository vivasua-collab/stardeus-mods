using System;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Mental;
using Game.Systems.UIUX;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Ships {
    public sealed class BattleStationComp : BaseComponent<BattleStationComp>,
            ICanBeUnconfigured, ICopyableComp, IRelocatable, IOperatorWorkSource {

        [Flags]
        private enum WarningFlags {
            None = 0,
            Unconfigured = 1 << 0,
            NoActiveWeapons = 1 << 1,
        }
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new BattleStationComp());
        }

        public const int DefaultWeaponCapacity = 5;

        public ShipWeaponDef WeaponDef { get; private set; }
        public ShipWeaponTargetTypeDef PreferredTargetType { get; private set; }
        private B<ShipWeaponDef> weaponDefB;
        public B<ShipWeaponDef> WeaponDefB => weaponDefB ??= new B<ShipWeaponDef>(WeaponDef);
        public int ExtraWeaponCapacity;
        public long NextSalvoTick;
        public float OperatorAccuracyBonus = 1.0f;
        public readonly BInt AssignedWeapons = new(0);
        private ExtraInfoComp extraInfo;

        private OperatableDeviceComp operatable;
        public OperatableDeviceComp Operatable => operatable;
        private ElectricNodeComp elNode;

        public bool IsOperatorPresent => operatable.Operator != null;
        public int TotalWeaponCapacity => DefaultWeaponCapacity + ExtraWeaponCapacity;

        public string DeviceCategory => T.NounBattleStationPlural;
        public Tile DeviceTile => Tile;
        public bool IsCopyable => true;
        public string CopyConfigText => T.NounBattleStation;

        public bool ManualFireDone;
        public bool ManualFireMode;
        // Effective manual fire = per-station toggle OR the ship-wide Manual fire mode.
        public bool EffectiveManualFireMode =>
            ManualFireMode
            || S.Universe.Player.Ship.CombatState.WeaponSettings.FireMode
                != ShipWeaponFireMode.Automatic;
        private bool isRegistered;
        private WarningFlags warningFlags;
        private WarningFlags showingWarningFlags;

        public override void OnLateReady(bool wasLoaded) {
            if (!entity.TryGetComponent(out extraInfo)) {
                D.Err("ExtraInfo comp is required for BattleStation! {0}",
                    Tile.Definition.Id);
            }
            entity.TryGetComponent(out operatable);
            if (wasLoaded) {
                SetExtraIcon();
                if (Tile.IsConstructed) {
                    Ready.When(WhenH.ShipDataRestored, RestoreStation);
                }
            }
        }

        private void RestoreStation() {
            RegisterStation(elNode?.IsConnected == true);
        }

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ElectricNodeAdded) {
                if (sender is ElectricNodeComp el) {
                    elNode = el;
                    el.AddAfterTick(this, AfterTickGrid);
                }
            }
            if (message == MsgIdH.ElectricNodeConnectionChanged) {
                // Tile.ENode rather than the cached elNode: a connection can be
                // restored before ElectricNodeAdded has been received
                RegisterStation(Tile.ENode?.IsConnected == true);
            }
        }
        private void AfterTickGrid() {
            // Before the early return, so switching off or dropping off the grid clears it
            UpdateWarning();
            if (!elNode.IsPowerable) { return; }
            var ship = S.Universe.Player.Ship;
            if (WeaponDef != null && AssignedWeapons.Value > 0 && ship.IsInCombat) {
                if (!operatable.HasGoal) {
                    operatable.HailOperator(false);
                }
            } else {
                if (operatable.HasGoal) {
                    operatable.DismissOperator(false);
                }
            }
            // also out of combat: a station kept manned fires at space threats then (ShipWeapon.IsHeldReadyByStation)
            if (ManualFireDone) {
                var cs = ship.CombatState;
                bool anyFiring = false;
                foreach (var w in cs.Weapons) {
                    if (w.BackingStation != this) { continue; }
                    if (w.Phase == ShipWeaponPhase.Firing) {
                        anyFiring = true;
                        break;
                    }
                }
                if (!anyFiring) {
                    ManualFireDone = false;
                }
            }
        }

        public void UpdateWarning() {
            // Off the grid the electric node's own icon is the warning that matters, and a
            // station that cannot run shows neither of its own
            var canWarn = UnconfiguredWarning.IsApplicable(Tile);
            if (canWarn && WeaponDef == null) {
                if ((warningFlags & WarningFlags.Unconfigured) == WarningFlags.None) {
                    warningFlags |= WarningFlags.Unconfigured;
                    S.Sig.UnconfiguredChanged.Send(this, true);
                }
            } else {
                if ((warningFlags & WarningFlags.Unconfigured) == WarningFlags.Unconfigured) {
                    warningFlags &= ~WarningFlags.Unconfigured;
                    S.Sig.UnconfiguredChanged.Send(this, false);
                }
            }
            if (canWarn) {
                CheckHasActiveWeapons();
            } else {
                warningFlags &= ~WarningFlags.NoActiveWeapons;
            }
            if (warningFlags == showingWarningFlags) {
                return;
            }
            if ((warningFlags & WarningFlags.Unconfigured) == WarningFlags.Unconfigured) {
                if (showingWarningFlags != WarningFlags.Unconfigured) {
                    showingWarningFlags = WarningFlags.Unconfigured;
                    ShowInfoIcon(IconId.CNotConfigured, T.InfoDeviceUnconfigured);
                }
            } else if ((warningFlags & WarningFlags.NoActiveWeapons) == WarningFlags.NoActiveWeapons) {
                if (showingWarningFlags != WarningFlags.NoActiveWeapons) {
                    showingWarningFlags = WarningFlags.NoActiveWeapons;
                    ShowInfoIcon(IconId.CTime, T.BattleStationNoAssignedActiveWeapons);
                }
            } else {
                if (showingWarningFlags != WarningFlags.None) {
                    showingWarningFlags = WarningFlags.None;
                    HideInfoIcon();
                }
            }
        }

        private void RegisterStation(bool add) {
            if (isRegistered == add) { return; }
            // A relocated copy is connected before it's built; RelocateTo registers it
            // once construction is done
            if (add && !Tile.IsConstructed) { return; }
            if (add && !EntityUtils.IsWithinMargin(Tile.CenterIdx, false, false)) { return; }

            var ship = S.Universe.Player.Ship;
            if (add) {
                ship.CombatState.AddBattleStation(this);
                S.Sig.ShipCombatChanged.AddListener(OnShipCombatChanged);
                if (ship.IsInCombat) {
                    operatable?.HailOperator(false);
                }
            } else {
                S.Sig.ShipCombatChanged.RemoveListener(OnShipCombatChanged);
                ship.CombatState.RemoveBattleStation(this);
                // the recalculation below only visits registered stations, so this one
                // would keep showing weapons that now belong to nobody
                AssignedWeapons.Set(0);
            }
            ShipCombatAI.RecalculateBattleStationAssignments(ship.CombatState);
            isRegistered = add;
            UpdateWarning();
            S.Sig.BattleStationChanged.Send(this);
        }

        public override void OnRemove() {
            RegisterStation(false);
        }

        private void OnShipCombatChanged(bool started) {
            if (started) {
                operatable.HailOperator(false);
            } else {
                operatable.DismissOperator(false);
                NextSalvoTick = 0;
                OperatorAccuracyBonus = 1.0f;
                ManualFireDone = false;
            }
        }

        public void SetWeaponDef(ShipWeaponDef weaponDef) {
            WeaponDef = weaponDef;
            weaponDefB?.Set(weaponDef);
            SetExtraIcon();
            var ship = S.Universe.Player.Ship;
            ShipCombatAI.RecalculateBattleStationAssignments(ship.CombatState);
            UpdateWarning();
            S.Sig.BattleStationChanged.Send(this);
        }

        public void SetPreferredTargetType(ShipWeaponTargetTypeDef targetType) {
            PreferredTargetType = targetType;
            // Immediately clear device targets on weapons backed by this station so
            // ChooseTarget re-runs on the next tick with the new preference.
            if (S?.Universe?.Player?.Ship?.IsInCombat == true) {
                var cs = S.Universe.Player.Ship.CombatState;
                foreach (var w in cs.Weapons) {
                    if (w.BackingStation != this) { continue; }
                    w.ClearTarget_ShipDevice();
                }
            }
            S.Sig.BattleStationChanged.Send(this);
        }

        private void SetExtraIcon() {
            if (WeaponDef == null) {
                extraInfo.HideInfoIcon();
            } else {
                extraInfo.ShowInfoIcon(WeaponDef.Def.Preview, WeaponDef.Def.CodexLink);
            }
        }


        // Null-safe on its own rather than trusting the caller's IsOperatorPresent check: mods
        // patch that gate, and a throw here stops the whole ship combat tick
        private Jobs OperatorJobs => operatable?.Operator?.Data?.Mind?.Jobs;

        public void UpdateAccuracyBonus() {
            var jobs = OperatorJobs;
            if (jobs == null) {
                OperatorAccuracyBonus = 1.0f;
                return;
            }
            var skillLevel = jobs.LevelOf(JobTypeIdH.Shooting);
            if (CurveDef.TryGet("ShootingSkillAccuracyCurve", out var curve)) {
                OperatorAccuracyBonus = curve.Evaluate(skillLevel);
            }
        }

        // Device work never trains at a battle station: TrainOperator pays for ship combat, and
        // outside it the operator only sits there
        public bool IsOperatorWorking => false;

        // Each tick of a ship fight trains the operator like a tick of a ground fight
        // (ShipCombatAI.TickBattleStations). The device work rate was far below decay at high
        // levels, so a gunner lost Shooting while fighting.
        public void TrainOperator() {
            if (AssignedWeapons.Value == 0) { return; }
            var jobs = OperatorJobs;
            if (jobs == null || !jobs.TryJob(operatable.JobType.IdHash, out var slot)) { return; }
            if (slot.Incapable) { return; }
            slot.TrainSkill(Tunables.CombatShootingXpPerCycleTick, false);
        }

        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetString("WeaponDefId", WeaponDef?.Id);
            data.SetString("TargetTypeId", PreferredTargetType?.Id);
            if (NextSalvoTick > G.Ticks) {
                data.SetLong("NextSalvoTick", NextSalvoTick);
            }
            if (ManualFireMode) {
                data.SetBool("ManualFireMode", true);
            }
        }

        protected override void OnLoad(ComponentData data) {
            var weaponDefId = data.GetString("WeaponDefId", null);
            if (weaponDefId != null) {
                WeaponDef = ShipWeaponDef.Get(weaponDefId);
                if (WeaponDef == null) {
                    D.Warn("Failed to load battle station weapon def: {0}", weaponDefId);
                }
            }
            var ttId = data.GetString("TargetTypeId", null);
            if (ttId != null &&
                    ShipWeaponTargetTypeDef.All.TryGetValue(Hashes.S(ttId), out var tt)) {
                PreferredTargetType = tt;
            }
            NextSalvoTick = data.GetLong("NextSalvoTick", 0);
            ManualFireMode = data.GetBool("ManualFireMode", false);
        }

        public void CheckHasActiveWeapons() {
            if (AssignedWeapons.Value == 0) {
                if ((warningFlags & WarningFlags.NoActiveWeapons) == WarningFlags.None) {
                    warningFlags |= WarningFlags.NoActiveWeapons;
                }
            } else {
                if ((warningFlags & WarningFlags.NoActiveWeapons) == WarningFlags.NoActiveWeapons) {
                    warningFlags &= ~WarningFlags.NoActiveWeapons;
                }
            }
        }

        public bool CopyConfigTo(ICopyableComp target) {
            var changed = false;
            if (target is BattleStationComp bs) {
                if (ManualFireMode != bs.ManualFireMode) {
                    bs.ManualFireMode = ManualFireMode;
                    changed = true;
                }
                if (bs.WeaponDef != WeaponDef) {
                    bs.SetWeaponDef(WeaponDef);
                    changed = true;
                }
                if (bs.PreferredTargetType != PreferredTargetType) {
                    bs.SetPreferredTargetType(PreferredTargetType);
                    changed = true;
                }
            }
            return changed;
        }

        public bool CanCopyTo(ICopyableComp comp) {
            return comp is BattleStationComp;
        }

        public void RelocateTo(Entity target) {
            if (target.TryGetComponent<BattleStationComp>(out var bs)) {
                CopyConfigTo(bs);
                // Relocation transfers electric connections without emitting
                // ElectricNodeConnectionChanged, so the target station is never registered
                // into the combat state and its weapons stay unassigned/inactive until
                // save/load. Register it once the relocated tile is built and reconnected
                // (next update), mirroring the save/load restore path.
                S.Clock.OnNextUpdate(() => {
                    if (bs.Tile.IsActive && bs.Tile.IsConstructed) {
                        bs.RestoreStation();
                    }
                });
            }
        }

        public bool TryManualFire() {
            if (IsOperatorPresent) {
                if (!EffectiveManualFireMode) {
                    D.Warn("Can't manual fire, operator present.");
                    return false;
                }
            }
            if (ManualFireDone) {
                D.Warn("Can't manual fire, already done.");
                return false;
            }

            var cs = S.Universe.Player.Ship.CombatState;

            // All chargeable weapons assigned to this station must be fully charged
            bool hasAnyChargeable = false;
            foreach (var w in cs.Weapons) {
                if (w.BackingStation != this) { continue; }
                if (!w.CanShoot || !w.IsReachable) { continue; }
                hasAnyChargeable = true;
                if (w.Phase != ShipWeaponPhase.Ready) {
                    D.Warn("Can't manual fire, not all weapons are ready.");
                    return false;
                }
            }
            if (!hasAnyChargeable) {
                D.Warn("Can't manual fire, no chargeable weapons.");
                return false;
            }

            UpdateAccuracyBonus();
            bool firedAny = false;
            int failShoot = 0;
            foreach (var w in cs.Weapons) {
                if (w.BackingStation != this) { continue; }
                if (w.Phase != ShipWeaponPhase.Ready) { continue; }
                if (!w.IsReachable) { continue; }
                if (w.TargetShip == null && !w.HasEngageableNonShipTarget) { continue; }
                if (w.IsHeldByVent) { continue; }

                if (!w.HasEngageableNonShipTarget) {
                    if (cs.IsNegotiating
                            || w.TargetShip?.CombatState.IsNegotiating == true) {
                        continue;
                    }
                }

                // Manual fire is an explicit player command - the player was already
                // warned about a potential flux overload via the fire button's tooltip,
                // so we shoot regardless of the automatic flux threshold guard.
                w.AccuracyBonusFromBattleStation = OperatorAccuracyBonus;
                if (w.Shoot(S, cs.Rng)) {
                    cs.WeaponsShotThisTick++;
                    firedAny = true;
                } else {
                    failShoot++;
                }
            }

            if (firedAny) {
                ManualFireDone = true;
            } else {
                D.Warn("Couldn't fire any weapons - failed to shoot: {0}", failShoot);
            }
            return firedAny;
        }

        /// <summary>
        /// Previews the outcome of a manual fire command without actually shooting:
        /// projected total flux percent and whether it would push the ship into overload.
        /// Returns false if no weapon on this station is currently ready to fire.
        /// </summary>
        public bool GetManualFireFluxPreview(out float futureTotalPercent, out bool willOverload) {
            futureTotalPercent = 0f;
            willOverload = false;
            var cs = S.Universe.Player.Ship.CombatState;
            float sumHard = 0f;
            float sumSoft = 0f;
            var anyReady = false;
            foreach (var w in cs.Weapons) {
                if (w.BackingStation != this) { continue; }
                if (w.Phase != ShipWeaponPhase.Ready) { continue; }
                if (w.TargetShip == null && !w.HasEngageableNonShipTarget) { continue; }
                if (w.IsHeldByVent) { continue; }
                sumHard += w.WeaponDef.HardFluxPerShot;
                sumSoft += w.WeaponDef.SoftFluxPerShot;
                anyReady = true;
            }
            if (!anyReady) { return false; }
            futureTotalPercent = cs.Flux.FutureTotalPercent(sumHard, sumSoft);
            willOverload = futureTotalPercent > 1f;
            return true;
        }
    }
}
