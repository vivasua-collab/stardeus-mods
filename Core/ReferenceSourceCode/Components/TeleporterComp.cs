using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using Game.Visuals;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Systems.Mental;
using Game.Systems.Motion;
using Game.Systems.Path;
using Game.Components.Upgrades;
using Game.Systems.UIUX.Codex.Pages;

namespace Game.Components {
    public sealed class TeleporterComp : BaseComponent<TeleporterComp>, IEnergyEfficiencyUpgradeable,
            IDefPowerProvider {
        // Every teleporter draws more the more of them the ship has, so the codex shows the
        // cost of the first linked pair at Balanced difficulty (level 1) as the minimum
        public void AddDefPower(Def def, ComponentConfig config, ref DefPower power) {
            var mul = config.GetFloat(PropertyIdH.EnergyCostMultiplier, 1f);
            power.AddConsumption(PathSys.TeleporterEnergyCost(2)
                * PathSys.TeleporterDifficultyCostMul(1f) * mul, float.PositiveInfinity);
        }
        private bool isAddedToPath;
        private float energyCost;
        public bool IsTeleporterActive => Outbound != null
            && CanTeleport && Outbound.CanTeleport;
        public bool CanTeleport {
            get {
                if (!Tile.IsConstructed) { return false; }
                if (Tile.Health.IsHeavilyDamaged) { return false; }
                var e = Tile.ENode;
                if (!e.IsRegistered && !e.IsInitialized) {
                    // let's be optimistic, this can be happening on load when
                    // electricity hasn't been initialized yet
                    return true;
                }
                if (!e.IsPowerable) { return false; }
                if (e.HasDeficit) { return false; }
                return e.IsConnected;
            }
        }

        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new TeleporterComp());
        }
        private float costMultiplier;

        protected override void OnConfig() {
            costMultiplier = Config.GetFloat(PropertyIdH.EnergyCostMultiplier, 1f);
        }

        public override void OnRemove() {
            RemoveFromPath();
        }

        public int outboundId;
        private TeleporterComp outbound;
        public TeleporterComp Outbound {
            get => EntityUtils.GetCached(S, ref outboundId, ref outbound);
            set {
                EntityUtils.SetCached(ref outboundId, ref outbound, value);
            }
        }

        private ElectricNodeComp elNode;
        public ElectricNodeComp ENode => elNode;
        private float upgradeEnergyEfficiency = 1f;

        public bool IsEnergyEfficiencyUpgradeable => true;

        public void ApplyEfficiencyUpgrade(float amt) {
            upgradeEnergyEfficiency += amt;
            if (upgradeEnergyEfficiency < 1f) {
                D.Err("Upgrade Efficiency < 1f after apply: {0} -> {1}",
                    amt, upgradeEnergyEfficiency);
                upgradeEnergyEfficiency = 1f;
            }
            // Apply the new efficiency immediately if the electric node is available
            if (elNode != null) {
                AfterTickGrid();
            }
        }

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ElectricNodeAdded) {
                if (sender is ElectricNodeComp elnode) {
                    elNode = elnode;
                    elnode.AddAfterTick(this, AfterTickGrid);
                    AfterTickGrid();
                }
            } else if (message == MsgIdH.ElectricNodeConnectionChanged) {
                if (Tile.ENode.IsConnected && EntityUtils.IsWithinMargin(Tile.PosIdx, false, false) && Ready.Gen) {
                    S.UIQuery.ShowTutorialPopup.Ask(TeleportersPage.PageId, 1f);
                }
            } else if (message == MsgIdH.ElectricNodeSwitchedOff) {
                OnPowerOff();
            }
        }

        private void OnPowerOff() {
            RemoveFromPath();
            if (Outbound != null) {
                Outbound.RemoveFromPath();
            }
        }

        private void AfterTickGrid() {
            if (!elNode.IsPowerable) {
                RemoveFromPath();
                return;
            }
            energyCost = S.Sys.Path.TeleporterEnergyCost() * costMultiplier;
            if (upgradeEnergyEfficiency > 1f) {
                energyCost *= 1f / upgradeEnergyEfficiency;
            }
            elNode.SetConsumerWantedInput(energyCost);
            if (elNode.IsConsuming) {
                if (Outbound?.CanTeleport == true) {
                    HideNotLinked();
                    AddToPath();
                } else {
                    ShowNotLinked();
                    RemoveFromPath();
                }
            } else {
                RemoveFromPath();
            }
        }

        private void ShowNotLinked() {
            if (isShowingNotLinkedIcon) { return; }
            isShowingNotLinkedIcon = true;
            if (Outbound != null) {
                ShowInfoIcon(IconId.CWarning, T.LinkInactive);
            } else {
                ShowInfoIcon(IconId.CNotConfigured, T.NotLinked);
            }
        }

        private void HideNotLinked() {
            if (!isShowingNotLinkedIcon) { return; }
            isShowingNotLinkedIcon = false;
            HideInfoIcon();
        }

        public void RemoveFromPath() {
            if (isAddedToPath) {
                isAddedToPath = false;
                S.Sig.RemoveTeleporter.Send(this, addedToPathPos);
            }
        }

        public void ResetAddedToPath() {
            isAddedToPath = false;
        }

        private int addedToPathPos;
        private int addedToPathPosOutbound;

        public void AddToPath() {
            if (Outbound == null) {
                D.Err("Trying to add a teleporter to path without outbound! {0}", Tile);
                return;
            }
            if (!isAddedToPath) {
                isAddedToPath = true;
                addedToPathPos = Tile.Transform.PosIdx;
                addedToPathPosOutbound = Outbound.Tile.PosIdx;
                S.Sig.AddTeleporter.Send(this, addedToPathPos, addedToPathPosOutbound);
            }
        }

        public override string ToString() {
            return $"* Teleporter[{Pos.String(Tile.PosIdx)}.OutId:{outboundId}.Active:{IsTeleporterActive}.CanTP:{CanTeleport}]";
        }

        public bool TryTeleportTo(Being b, TeleporterComp t2) {
            if (!CanTeleport) {
                D.Warn("This teleporter cannot teleport! {0}->{1}", this, t2, b);
                return false;
            }
            if (Outbound != t2) {
                D.Err("Wrong outbound teleporter! {0} -> {1}, expected {2}",
                    this, t2, Outbound, b);
                return false;
            }
            if (!t2.CanTeleport) {
                D.Warn("Outbound teleporter cannot teleport! {0}->{1}", this, t2, b);
                return false;
            }
            Enter(b, t2);
            return true;
        }
        public void Enter(Being b, TeleporterComp target) {
            // D.Warn("Entering teleporter: {0}->{1}, {2}", this, target, b);
            PlayTeleportVFX(target);
            Sounds.PlayTeleportIn(Tile.Position);
            b.Motion.Progress.Flags |= MotionProgressFlags.Teleporting;
            b.Graphics.SetHidden();
            if (b.Data.Species.IsBiological && Rng.UChance(0.1f)) {
                b.Data.Mind.Health?.AddEffect(S, MentalEffect.Create(
                    Tile.PosIdx, G.Ticks,
                    Consts.TicksPerHour, T.ObjectsDevicesTeleporter, -3));
            }
            S.Sig.EntityTeleportStarted.Send(b, this, target);
        }
        public void Exit(Being b) {
            var teleportTarget = Entity.Position;
            b.Motion.Progress.Flags &= ~MotionProgressFlags.Teleporting;
            b.Motion.TeleportTo(teleportTarget);
            Sounds.PlayTeleportOut(Tile.Position);
            b.Graphics.SetVisible();
            // D.Warn("Exiting teleporter: {0}, {1}", this, b);
            S.Sig.EntityTeleportEnded.Send(b, this);
        }

        public void PlayTeleportVFX(TeleporterComp target) {
            if (!The.Prefs.GetBool(Pref.GCosmeticParticles,
                    PrefH.GCosmeticParticles, true)) {
                return;
            }
            var vfx = ParticlesVFX.CreateWithBudget(S, "TeleportVFX", Consts.V3Zero,
                    BBKeyH.TeleporterVFXPlayingCount, Consts.TeleportMaxVFX)
                .WithoutOwnership();
            if (vfx.TryGetFX(out var fx)) {
                var pos = Tile.Position;
                var tPos = target.Tile.Position;
                var dist = Vector2.Distance(pos, tPos);
                var t = Time.timeSinceLevelLoad;
                fx.SetVector4(ShaderVars.Viewport, S.Grid.Bounds);
                fx.SetVector2(ShaderVars.From, pos);
                fx.SetFloat(ShaderVars.Distance, dist);
                fx.SetFloat(ShaderVars.Duration, 5f);
                fx.SetFloat(ShaderVars.EndTime, t + 5f);
                fx.SetVector2(ShaderVars.To, tPos);
            }
            vfx.SetClock(S.Clock);
            vfx.DestroyAfterRealTime(8f);
        }


        private bool isShowingNotLinkedIcon;

        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetInt("OutboundId", outboundId);
        }
        protected override void OnLoad(ComponentData data) {
            outboundId = data.GetInt("OutboundId", 0);
        }

        public void UnsetOutbound() {
            Outbound = null;
            RemoveFromPath();
            // may have shown "link inactive", so let's hide and reshow
            HideNotLinked();
            ShowNotLinked();
        }

        public void SetOutbound(TeleporterComp tp) {
            // Deliberate, we will remove and re-add it on next energy tick
            RemoveFromPath();
            HideNotLinked();
            Outbound = tp;
        }

        #if !PRODUCTION_BUILD
        public int DebugAddedToPathPos => addedToPathPos;
        #endif
    }
}
