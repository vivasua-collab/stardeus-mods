using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using Game.Visuals;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.UI;
using Game.Commands;
using System;
using Game.Systems.Atmo;
using Game.Tools;

namespace Game.Components {
    public sealed class ShipEngineComp : BaseComponent<ShipEngineComp>,
            IRotationDependent, IRelocatable, IDefPowerProvider {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new ShipEngineComp());
        }
        private const float DefaultEnergyCost = 50f;

        // A maneuvering thruster only draws while it recharges
        public void AddDefPower(Def def, ComponentConfig config, ref DefPower power) {
            if (config.GetBool(PropertyIdH.IsManeuvering, false)) {
                power.AddConsumption(config.GetFloat(PropertyIdH.ManeuveringChargeCost, 0f));
            } else {
                power.AddConsumption(config.GetFloat(PropertyIdH.EnergyCost, DefaultEnergyCost));
            }
        }

        private Vector2 FlamePosition => Tile.Position;
        private Quaternion FlameRot => Facing.ToQuaternion(Tile.Transform.Rotation);
        private float particleLifeTime;
        public float ActualPush { get; private set; }
        private bool wantsPush;
        private bool wantsHSPush;
        public bool HasPush { get; private set; }
        public float EnergyCost { get; private set; }
        public float Power { get; private set; }
        public float PowerWithUpgrades => Power * (1f + PowerUpgradeMultiplier);
        public bool IsPowered => Tile.ENode.IsPowerable;
        public float PowerUpgradeMultiplier;
        public float ExtraExplosionChance;
        public float RangeUpgradeMultiplier;

        public bool HasSubmenuNow => !IsManeuvering;
        public string SubmenuTitle => IsManeuvering ? Entity.Definition.NameT : T.Engine;

        private string emissionEffect;
        private MultiParticles particles;
        private string[] worksInLayers;
        // In tiles
        private Vector2 flameSize;
        private HeatProducerComp heatProducer;
        public HyperdriveComp Hyperdrive;
        private int burnTemp;
        public bool IsManeuvering;
        // Set by BeginMoveSection for the section currently being previewed
        private bool isSectionOutsideMargin;
        private float maneuveringChargeCost;
        private float maneuveringCharge;
        public float ManeuveringCharge => maneuveringCharge;
        private float maneuveringChargeHours;
        private float maneuveringRange;
        // InstantBuild waives the charge requirement in BeginMoveSection, so it has to
        // waive it here too - otherwise a sandbox move would be gated at zero range.
        private float EffectiveManeuveringCharge =>
            A.InstantBuild ? 1f : maneuveringCharge;
        // What this one controller adds to the section's maneuvering range. The upgrade
        // multiplier belongs to the thruster it was installed on, so it is applied here,
        // per controller, and never to the summed range of every other controller.
        public float ManeuveringRangeContribution => maneuveringRange
            * (1f + RangeUpgradeMultiplier) * EffectiveManeuveringCharge;
        protected override void OnConfig() {
            EnergyCost = Config.GetFloat(PropertyIdH.EnergyCost, DefaultEnergyCost);
            IsManeuvering = Config.GetBool(PropertyIdH.IsManeuvering, false);
            if (IsManeuvering) {
                maneuveringChargeHours = Config.GetInt(PropertyIdH.ManeuveringChargeHours, 6);
                maneuveringChargeCost = Config.GetFloat(PropertyIdH.ManeuveringChargeCost);
                maneuveringRange = Config.GetFloat(PropertyIdH.ManeuveringRange, 25f);
            } else {
                Power = Config.GetFloat(PropertyIdH.Power, 0);
                flameSize = Config.GetVector2(PropertyIdH.FlameSize, Consts.V2Zero);
                particleLifeTime = Config.GetFloat(PropertyIdH.ParticleLifeTime, 1f);
                burnTemp = Mathf.RoundToInt(Units.CelsiusToKelvin(
                    Config.GetInt(PropertyIdH.MaxTempCelsius, 1000)));
                emissionEffect = Config.GetString(PropertyIdH.LegacyEmissionEffect, null);
                worksInLayers = Config.GetStringSet(PropertyIdH.WorksIn, null);
            }
        }

        public bool WorksIn(string layer) {
            if (IsManeuvering) { return false; }
            return Arrays.ContainsSafe(worksInLayers, layer);
        }

        private void RegisterEngine() {
            if (!isEngineRegistered) {
                isEngineRegistered = true;
                S.Sys.ShipNav.AddEngine(this);
                S.Sig.AtmoRecalculated.AddListener(OnAtmoRecalculated);
            }
        }
        private void UnregisterEngine() {
            if (isEngineRegistered) {
                isEngineRegistered = false;
                S.Sys.ShipNav.RemoveEngine(this);
                S.Sig.AtmoRecalculated.RemoveListener(OnAtmoRecalculated);
            }
        }

        private void OnAtmoRecalculated(AtmoLayer layer) {
            if (layer.Id == AtmoLayerIdH.Temp) {
                RadiateHeat();
            }
        }

        public override void OnReady(bool wasLoaded) {
            RegisterEngine();
        }

        public override void OnLateReady(bool wasLoaded) {
            heatProducer = Entity.GetComponent<HeatProducerComp>();
            Hyperdrive = Entity.GetComponent<HyperdriveComp>();
            thrustVFX = Entity.GetComponent<VFXComp>();
            UpdateBurnEffects();
            if (wasLoaded) {
                RadiateHeat();
            }
            if (Tile.IsConstructed) {
                ShowChargeBar();
            }
        }

        public ElectricNodeComp ENode => Tile.ENode;
        private bool isEngineRegistered;
        // a flame drawn by a VFX component set to IsLinkedToThrust (VFXComp)
        private VFXComp thrustVFX;

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ElectricNodeAdded) {
                if (sender is ElectricNodeComp elnode) {
                    elnode.AddAfterTick(this, AfterTickGrid);
                }
            }
            if (message == MsgIdH.TookDamage) {
                Kill(true, true);
            } else if (message == MsgIdH.TilePosChanged) {
                particles?.MoveTo(FlamePosition, 0);
                lastMotionVecSqrMag = 0;
                cookPositions.Clear();
            }
        }

        ///<summary>WARNING, this should follow the CanPushInDirection call
        ///as that call caches lastPushQueryResult that is later used in this method</summary>
        ///<returns>true when there's no more need to push</returns>
        // FIXME this gets called multiple times before energy tick happens,
        // cache the result
        public void RequestMotion(bool hyperspace) {
            if (!IsManeuvering) {
                if (hyperspace) {
                    wantsHSPush = true;
                } else {
                    wantsPush = true;
                }
            }
        }

        private void UpdateBurnEffects() {
            if (IsManeuvering) { return; }
            if (emissionEffect != null) {
                UpdateLegacyParticles(HasPush);
            }
            thrustVFX?.SetThrustVisible(HasPush);
            if (heatProducer != null) {
                heatProducer.Output = HasPush ? 1f : 0f;
            }
        }

        private void UpdateLegacyParticles(bool isWanted) {
            if (particles == null && isWanted) {
                particles = MultiParticles.Create(emissionEffect,
                    Rng.UUint, FlamePosition);
                particles.SetClock(S.Clock);
                particles.MoveTo(FlamePosition, 0);
                particles.Rotate(FlameRot);
            }
            if (particles != null) {
                particles.Play(isWanted);
                if (isWanted) {
                    particles.SetLifeTime(particleLifeTime);
                }
            }
        }

        public override void OnRemove() {
            particles?.Destroy();
            UnregisterEngine();
        }


        public override string ToString() {
            return string.Format(" * ShipEngine [{0}]", particles);
        }

        private float lastPercentage;

        private void ChargeManeuvering() {
            if (!ENode.IsPowerable) { return; }
            if (maneuveringCharge >= 1f) {
                ENode.SetConsumerWantedInput(0f);
                return;
            }
            ENode.SetConsumerWantedInput(maneuveringChargeCost);
            if (!ENode.IsConsuming) { return; }
            maneuveringCharge += 10 /
                (maneuveringChargeHours * Consts.TicksPerHour);
            if (maneuveringCharge > 1f) {
                maneuveringCharge = 1f;
            }
            if (maneuveringCharge != lastPercentage) {
                ShowChargeBar();
            }
        }

        private void ShowChargeBar() {
            if (!IsManeuvering) { return; }
            lastPercentage = maneuveringCharge;
            var bar = ShowInfoBar(Consts.GreenRectColor, maneuveringCharge,
            maneuveringCharge, 1, T.PercentageChargeBar_V1.F(
                    Units.Percentage(maneuveringCharge)));
            bar.IsPretranslated = true;
        }

        public override void OnSave() {
            var data = GetOrCreateData();
            if (IsManeuvering) {
                data.SetFloat("MCharge", maneuveringCharge);
            } else {
                data.SetBool("HasPush", HasPush);
                data.SetBool("WantsHSPush", wantsHSPush);
                data.SetBool("WantsPush", wantsPush);
            }
        }

        protected override void OnLoad(ComponentData data) {
            maneuveringCharge = data.GetFloat("MCharge", 0f);
            HasPush = data.GetBool("HasPush", HasPush);
            wantsHSPush = data.GetBool("WantsHSPush", wantsHSPush);
            wantsPush = data.GetBool("WantsPush", wantsPush);
        }

        private void AfterTickGrid() {
            if (IsManeuvering) {
                ChargeManeuvering();
                return;
            }


            var elNode = ENode;
            var hyperdriveWantsEnergy = Hyperdrive?.TryRunHyperdrive(elNode) == true;
            var needsPower = wantsPush || wantsHSPush || hyperdriveWantsEnergy;

            if (needsPower) {
                // We'll make an assumption that if it's set, we can provide it
                // if energy is enough
                var energyMul = 1f + PowerUpgradeMultiplier * 0.1f;
                var reqEnergy = EnergyCost * energyMul;
                elNode.SetConsumerWantedInput(reqEnergy);
            } else {
                elNode.SetConsumerWantedInput(0f);
            }

            if (!wantsPush && !wantsHSPush) {
                ActualPush = 0;
                HasPush = false;
                currentHeat = Mathf.Clamp(currentHeat - 50f, 0, burnTemp);
                UpdateBurnEffects();
                return;
            }
            if (elNode.IsConsuming) {
                ActualPush = PowerWithUpgrades;
                HasPush = true;
                UpdateBurnEffects();
                currentHeat = Mathf.Clamp(currentHeat + 50f, 0, burnTemp);
                RadiateHeat();
            } else {
                ActualPush = 0;
                HasPush = false;
                currentHeat = Mathf.Clamp(currentHeat - 50f, 0, burnTemp);
                UpdateBurnEffects();
            }
        }
        private readonly List<int> cookPositions = new();
        private float currentHeat;
        private PosRect cookArea;
        private float lastMotionVecSqrMag;
        // The tiles the flame heats and damages while the engine pushes (RadiateHeat).
        // Generation keeps what it mounts in space out of it: a solar panel behind a
        // thruster blows up in the first flight
        public bool TryGetFlameArea(out PosRect area) {
            area = default;
            return TryGetFlameVector(out var vec, out var shift)
                && TryComputeFlameArea(vec, shift, out area);
        }

        private bool TryGetFlameVector(out Vector2 vec, out Vector2 shift) {
            vec = shift = Vector2.zero;
            if (flameSize.x == 0) { return false; }
            var halfWidth = Mathf.FloorToInt(Tile.Transform.Width * 0.5f);
            switch (Tile.Transform.Rotation) {
                case Facing.Type.Up: {
                    vec = new(flameSize.x, flameSize.y);
                    shift = new(-halfWidth, 0);
                    break;
                }
                case Facing.Type.Down: {
                    vec = new(flameSize.x, -flameSize.y);
                    shift = new(-halfWidth, 0);
                    break;
                }
                case Facing.Type.Left: {
                    vec = new(-flameSize.y, flameSize.x);
                    shift = new(0, -halfWidth);
                    break;
                }
                case Facing.Type.Right: {
                    vec = new(flameSize.y, flameSize.x);
                    shift = new(0, -halfWidth);
                    break;
                }
                default: {
                    shift = vec = Consts.V2One;
                    D.Err("Unknown facing");
                    break;
                }
            }
            return vec.sqrMagnitude >= 0.000001f;
        }

        private static readonly List<int> flamePositions = new();
        // FIXME doesn't work if engine is 3x3 and flame is more than 5 wide
        private bool TryComputeFlameArea(Vector2 vec, Vector2 shift, out PosRect area) {
            area = default;
            var gridH = S.GridHeight;
            var pos = EntityUtils.CenterOf(Entity.CenterIdx);
            flamePositions.Clear();
            var xSign = Mathf.Sign(vec.x);
            var ySign = Mathf.Sign(vec.y);
            var sx = Mathf.RoundToInt(shift.x);
            var sy = Mathf.RoundToInt(shift.y);
            var vx = Mathf.RoundToInt(System.Math.Abs(vec.x) + sx);
            var vy = Mathf.RoundToInt(System.Math.Abs(vec.y) + sy);
            for (int x = sx; x < vx; x++) {
                for (int y = sy; y < vy; y++) {
                    var px = Mathf.FloorToInt(pos.x + x * xSign);
                    var py = Mathf.FloorToInt(pos.y + y * ySign);
                    if (px < 0 || py < 0 || px >= gridH || py >= gridH) {
                        continue;
                    }
                    flamePositions.Add(Pos.FromXY(px, py));
                }
            }
            if (flamePositions.Count == 0) { return false; }
            area = Pos.Encapsulate(flamePositions);
            return true;
        }

        private void RadiateHeat() {
            if (!HasPush || currentHeat < 1f) { return; }
            // D.Warn("radiating heat: {0} | {1}", Texts.Temperature(currentHeat), Tile);
            if (!TryGetFlameVector(out var vec, out var shift)) { return; }
            var vecSqrMag = vec.sqrMagnitude;
            var magChange = System.Math.Abs(lastMotionVecSqrMag - vecSqrMag);
            if (magChange > 1f) {
                cookPositions.Clear();
                lastMotionVecSqrMag = vecSqrMag;
                if (!TryComputeFlameArea(vec, shift, out cookArea)) { return; }
                cookArea.ForEach((p) => {
                    cookPositions.Add(p);
                });
            }
            //var d = S.Heat.Data;
            for (int i = cookPositions.Count - 1; i >= 0; i--) {
                int p = cookPositions[i];
                //d[p] = currentHeat;
                var obj = EntityUtils.GetTopmostParentTile(p);
                if (obj == entity) { continue; }
                // don't heat up open space
                if (obj == null) { continue; }
                if (obj.Definition.LayerId > WorldLayer.Floor) {
                    if (!obj.IsConstructed) {
                        obj = EntityUtils.FloorAt(i);
                    }
                }
                if (obj == null || !obj.IsConstructed) {
                    continue;
                }
                S.SetTemp(p, currentHeat);
                if (obj.Definition.LayerId != WorldLayer.Floor && Rng.UChance(0.05f)) {
                    obj.Health.TakeDamage(25f, DamageType.Heat);
                    if (obj.Flammable.CanExplode && Rng.UChance(0.1f)) {
                        obj.Flammable.CreateExplosion(false);
                    }
                }
            }
        }

        public void Kill(bool local, bool hyperspace) {
            if (hyperspace && wantsHSPush) {
                ActualPush = 0;
                wantsHSPush = false;
                HasPush = false;
                UpdateBurnEffects();
            }
            if (local && wantsPush) {
                ActualPush = 0;
                wantsPush = false;
                HasPush = false;
                UpdateBurnEffects();
            }
        }

        public void RotateShip(sbyte dir) {
            if (!ENode.IsPowerable || !ENode.IsReachable) {
                S.Sig.ShowErrorMessage.Send($"{T.OperationAborted}: {T.ShipEnginesOffline}");
                return;
            }
            if (maneuveringCharge < 1f) {
                S.Sig.ShowErrorMessage.Send($"{T.OperationAborted}: {T.Charging}");
                return;
            }
            int anchor = FindAnchor();
            var islandId = S.Sys.Areas.IslandAt(anchor);
            if (islandId == 0) {
                return;
            }
            var rekt = Pos.Encapsulate(S.Sys.Areas.Islands[islandId]);

            var cmd = new CmdRotateSection(islandId, rekt.CenterPos, dir, true);
            cmd.OnSuccess = () => {
                maneuveringCharge = 0f;
                ShowChargeBar();
            };
            S.CmdQ.Enqueue(cmd);
        }

        public void BeginRotatePreview(sbyte dir) {
            if (!ENode.IsPowerable || !ENode.IsReachable) {
                S.Sig.ShowErrorMessage.Send($"{T.OperationAborted}: {T.ShipEnginesOffline}");
                return;
            }
            if (maneuveringCharge < 1f) {
                S.Sig.ShowErrorMessage.Send($"{T.OperationAborted}: {T.Charging}");
                return;
            }
            int anchor = FindAnchor();
            var islandId = S.Sys.Areas.IslandAt(anchor);
            if (islandId == 0) {
                return;
            }
            ToolRotateSectionPreview.Begin(this, dir);
        }

        public void BeginMoveSection() {
            if (S.Sys.Ship.IsMoving) {
                S.Sig.ShowErrorMessage.Send($"{T.OperationAborted}: {T.ShipMustBeStoppedFirst}");
                return;
            }
            if (!ENode.IsPowerable || !ENode.IsReachable) {
                S.Sig.ShowErrorMessage.Send($"{T.OperationAborted}: {T.ShipEnginesOffline}");
                return;
            }
            if (maneuveringCharge < 1f && !A.InstantBuild) {
                S.Sig.ShowErrorMessage.Send($"{T.OperationAborted}: {T.Charging}");
                return;
            }
            int anchor = FindAnchor();
            var islandId = S.Sys.Areas.IslandAt(anchor);
            if (islandId == 0) {
                S.Sig.ShowErrorMessage.Send(
                    $"{T.OperationAborted}: {T.EngineMoveSectionTooSmall_V1.F(T.OverlayTipIslands)}");
                return;
            }

            var section = new HashSet<int>(S.Sys.Areas.Islands[islandId]);
            section = SectionMoveSet.Build(S, islandId, section, false, out var failure);
            if (failure.Failed) {
                S.Sig.ShowErrorMessage.Send($"{T.OperationAborted}: {T.WarningInvalidLocation}");
                return;
            }

            // The margin rule exists to keep the ship away from the map edge. A section
            // that already sits outside it (a derelict we bolted thrusters onto, or our
            // own section winched out there) can't be protected by it any more, and
            // enforcing it would pin the section in place forever, so drop it for this
            // move. Straddling the margin counts as outside for the same reason.
            isSectionOutsideMargin = false;
            foreach (var p in section) {
                if (!EntityUtils.IsWithinMargin(p, false, false)) {
                    isSectionOutsideMargin = true;
                    break;
                }
            }

            // deselect the thruster
            S.Query.DeselectIfSelected.Ask(Tile);
            S.Sig.HideOverlay.Send();

            if (UIShowing.ShipConfig) {
                S.UIQuery.ToggleShipConfig.Ask(false);
            }

            // No range without a thruster - every controller on the section, this one
            // included, contributes its own configured range.
            float range = ManeuveringRangeContribution;
            ForAllOtherManeuvering((ctrl) => {
                range += ctrl.ManeuveringRangeContribution;
            });

            var rekt = Pos.Encapsulate(section);
            // EntityUtils.CameraFocusOnMap();
            S.Sig.CameraFocus.Send(rekt.Center(), rekt.Extents,
                Mathf.Max(rekt.Extents.magnitude, range * 2.5f));

            ToolChoosePos.BeginTiles(section, Consts.BlueRectColor,
                OnChooseManeuverPos, OnValidateManeuverPos,
                    range: Mathf.RoundToInt(range), allowRotation: false);
        }

        private bool OnValidateManeuverPos(int pos, Vector2 motion, HashSet<int> initial) {
            if (isSectionOutsideMargin) {
                if (Pos.IsOOB(pos)) {
                    return false;
                }
            } else if (!EntityUtils.IsWithinMargin(pos, false, false)) {
                return false;
            }
            if (initial.Contains(pos)) {
                return true;
            }
            var obj = EntityUtils.GetTopmostParentTile(pos);
            if (obj == null) {
                return true;
            }
            if (obj.Definition.LayerId < WorldLayer.Objects
                    || obj.Constructable.Placement == PlacementConstraint.OnFloor) {
                return false;
            }
            foreach (var o in obj.Transform.Outline) {
                if (initial.Contains(o)) {
                    // this object belongs to the section we're moving
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Same island only!
        /// </summary>
        /// <param name="action"></param>
        /// <param name="includeSelf"></param>
        private void ForAllOtherManeuvering(Action<ShipEngineComp> action) {
            var allCtrls = S.Components.GenFindAllMatching<ShipEngineComp>(
                (e) => e.IsManeuvering && e.ENode.IsPowerable
                    && e.ENode.IsReachable);

            if (Arrays.IsEmpty(allCtrls)) {
                return;
            }

            var islandId = S.Sys.Areas.IslandAt(FindAnchor());
            foreach (var ctrl in allCtrls) {
                if (ctrl == this) { continue; }
                var an = ctrl.FindAnchor();
                if (S.Sys.Areas.IslandAt(an) == islandId) {
                    action(ctrl);
                }
            }
        }

        private ChoosePosResult OnChooseManeuverPos(int origin, Vector2 vector) {
            var center = Pos.ToVector(origin);
            var x = (int) vector.x - (int) center.x;
            var y = (int) vector.y - (int) center.y;
            var anchor = FindAnchor();
            var moveTarget = Pos.ToVector(anchor) + new Vector2(x, y);

            var cmd = new CmdPushSection(anchor,
                anchor, moveTarget, AfterPush, true,
                allowOutsideMargin: isSectionOutsideMargin)
                    .InstantPush();
            S.CmdQ.Enqueue(cmd);
            return ChoosePosResult.Success;
        }

        private void AfterPush(CmdPushSection cmd) {
            float amt = cmd.StepsMade / 20f;
            ExhaustCharge(amt);
            ForAllOtherManeuvering((ctrl) => {
                ctrl.ExhaustCharge(amt);
            });
        }

        private void ExhaustCharge(float amt) {
            maneuveringCharge -= amt;
            if (maneuveringCharge < 0f) {
                maneuveringCharge = 0f;
            }
            ShowChargeBar();
        }

        public int FindAnchor() {
            var pos = Tile.Transform.PosIdx;
            var rot = Tile.Transform.Rotation;
            var offset = Facing.ToOffset(rot, -1f);

            var anchor = Pos.NeighborAt(pos, (int) offset.x, (int) offset.y);
            // D.Warn("Anchor: {0}", Pos.StringFromTo(pos, anchor));
            if (CanBeAnchor(anchor)) {
                return anchor;
            }

            anchor = Pos.N(pos);
            if (CanBeAnchor(anchor)) { return anchor; }
            anchor = Pos.S(pos);
            if (CanBeAnchor(anchor)) { return anchor; }
            anchor = Pos.E(pos);
            if (CanBeAnchor(anchor)) { return anchor; }
            anchor = Pos.W(pos);
            if (CanBeAnchor(anchor)) { return anchor; }

            return pos;
        }

        private bool CanBeAnchor(int pos) {
            return S.Sys.Areas.IslandAt(pos) > 0;
        }

        public void RelocateTo(Entity target) {
            if (target.TryGetComponent<ShipEngineComp>(out var eng)) {
                if (eng.IsManeuvering) {
                    eng.maneuveringCharge = maneuveringCharge;
                    eng.ShowChargeBar();
                }
            }
        }

        public void AfterRotationUpdated() {
            particles?.Destroy();
            particles = null;
        }
    }
}
