using System;
using Game.Constants;
using Game.Data;
using Game.Systems;
using Game.Systems.Electricity;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using Game.VFXR;
using KL.Utils;
using UnityEngine;
using Game.Systems.AI;
using Game.Rendering;
using Game.Systems.Path;
using Game.Systems.Stats;
using Game.Systems.UIUX;

namespace Game.Components {
    public sealed class ElectricNodeComp : BaseComponent<ElectricNodeComp>,
            IEntityPanelGoalProvider, IRelocatable, IRanged, IRotationDependent, IStatProvider {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new ElectricNodeComp());
        }
        public int PosIdx => Tile.Transform.PosIdx;
        private int rangeSqr;
        public int RangeSqr => rangeSqr;
        private ElectricitySys sys;
        #if UNITY_EDITOR
        public bool DebugNode;
        #endif

        public bool IsConnectableNow(out string failReason) {
            if (!Tile.IsConstructed) {
                failReason = T.ElDeviceNotConstructed;
                return false;
            }
            if (Tile.Health.IsHeavilyDamaged) {
                failReason = T.ElDeviceHeavilyDamaged;
                return false;
            }
            if (!Tile.IsActive) {
                failReason = T.AdRejTargetUnavailable;
                return false;
            }
            failReason = null;
            return true;
        }
        public bool CanShowConnectableInfo => Tile.IsConstructed;
        /// <summary>
        /// Use IsPowerable if you also want to check if the device is switched on
        /// </summary>
        public bool IsConnected => Node.NumConnections > 0 || Node.HasWorkerLink;
        public bool IsConnector => Node.IsConnector;
        public bool IsFullyConnected => Node.NumConnections >= Node.MaxConnections;
        public bool IsFullConnector => Node.IsConnector && Node.NumConnections >= Node.MaxConnections;
        public bool IsShipComputer;
        public bool IsSwitchedOn => Node.IsOn;
        public bool IsReachable => Node.IsReachable;
        public bool IsPowerable => Node.IsPowerable;// IsSwitchedOn && IsConnected;
        public bool IsPowered => IsSwitchedOn && IsConnected
            && sys.IsGridProducing(Node.GridId);
        public bool HasDeficit => Node.Deficit > 0f;
        public bool IsProducing => Node.Produced > 0f && Node.GridId != -1;
        public bool IsConsuming => Node.Consumed > 0f;
        public int TryConnectPosAfterBuild;
        public ElectricGrid Grid {
            get {
                if (sys.TryGetGrid(Node.GridId, out var grid)) {
                    return grid;
                }
                return default;
            }
        }

        public bool IsRegistered;
        public bool IsInitialized;
        public int SlotIdx;
        public ElectricNode Node;
        public bool IsSwitchable;
        public int[] ConnectionsToRestore;
        private bool isListeningToConnectorTextShow;
        private bool isAfterNodeUpdateEnqueued;
        private Vector2[] particleOffsets;
        private bool createdParticles;
        private bool isParticlesAdded;
        private bool hasTransmittedAdded;
        private bool isShowingInfoText;
        private int textSizePct;
        private Vector2 textOffset = new(0, -0.5f);
        private float lastRate = float.MinValue;
        private string lastRateText;
        private bool showZeroProduction;
        private string switchText => IsSwitchedOn ? T.SwitchOff : T.SwitchOn;
        private string switchIcon => IsSwitchedOn ? IconId.CTurnOff : IconId.CTurnOn;
        private bool wasShowingDeficit;
        private bool isPlayingAmbience;
        private bool hasAmbience;
        private float ambienceMinEnergy;
        private bool hasParticles;
        // For tests
        public bool HasParticles => hasParticles;
        private bool hasVFX;
        private ParticleSlot vfx;
        private float vfxMinEnergy;
        private ParticlesSys.Data particles;
        public ParticlesSys.Data Particles => particles;
        public float Range => Node.ConnectorRange;
        public Color RadiusColor => Consts.EnergyGridConnectorReachColor;
        public bool RadiusSameArea => false;

        public string StatsCategoryName => Node.IsShipComputer ? null : T.EnergyNode;
        public int StatsOrder => 10;

        private B<float> nodeProducedB;
        private B<float> nodeConsumedB;
        private B<float> nodeDeficitB;

        protected override void OnConfig() {
            Config.MarkUsed(PropertyIdH.Particles);
            Config.MarkUsed(PropertyIdH.Particles_D);
            Config.MarkUsed(PropertyIdH.Particles_L);
            Config.MarkUsed(PropertyIdH.Particles_R);
            Config.MarkUsed(PropertyIdH.Particles_U);
            Config.MarkUsed(PropertyIdH.ParticlesEmitChance);
            Config.MarkUsed(PropertyIdH.ParticlesAutoplay);
            Config.MarkUsed(PropertyIdH.ParticlesEmitOnly);
            Config.MarkUsed(PropertyIdH.ParticlesEmitChance);
            Config.MarkUsed(PropertyIdH.ParticleOffsets);
            IsSwitchable = Config.GetBool(PropertyIdH.IsSwitchable, true);
            var conRange = Config.GetInt(PropertyIdH.ConnectorRange, 0);
            showZeroProduction = Config.GetBool(PropertyIdH.ShowZeroProduction, true);
            rangeSqr = conRange * conRange;
            Node = new ElectricNode {
                EntityId = entity.Id,
                GridId = -1,
                IsOn = true,
                // Connector data
                IsConnector = Config.GetBool(PropertyIdH.IsConnector, false),
                ConnectorRange = conRange,
                ConnectorRangeSqr = rangeSqr,
                MaxConnections = Config.GetInt(PropertyIdH.MaxConnections, 0),
                // Battery data
                IsBattery = Config.GetBool(PropertyIdH.IsBattery, false),
                BatteryMaxCharge = Config.GetInt(PropertyIdH.BatteryMaxCharge, 0),
                // Consumer data
                IsConsumer = Config.GetBool(PropertyIdH.IsConsumer, false),
                IsConsumerHighPriority = Config.GetBool(PropertyIdH.IsConsumerHighPriority, false),
                IsConsumerLowPriority = Config.GetBool(PropertyIdH.IsConsumerLowPriority, false),
                // Producer data
                IsProducer = Config.GetBool(PropertyIdH.IsProducer, false),
                IsProducerSmart = Config.GetBool(PropertyIdH.IsProducerSmart, false),
                // Ship CPU data
                IsShipComputer = Config.GetBool(PropertyIdH.IsShipComputer, false),
            };
            if (IsSwitchable && Node.IsConnector) {
                D.Err("Connectors cannot be switchable! Fixing for {0}",
                    Entity.DefinitionId);
                Node.IsConnector = false;
            }
            IsShipComputer = Node.IsShipComputer;
            if (IsShipComputer) {
                Node.IsReachable = true;
            }
        }

        private void RegisterConnectorListener() {
            if (!isListeningToConnectorTextShow) {
                if (!Node.IsConnector) {
                    D.Err("Trying to register connector listener on a node that is not a connector! {0}",
                        Entity);
                }
                isListeningToConnectorTextShow = true;
                S.Sig.ShowConnectorCountsNew.AddListener(ShowConnectableInfoText);
            }
        }

        private void UnregisterConnectorListener() {
            if (isListeningToConnectorTextShow) {
                isListeningToConnectorTextShow = false;
                S.Sig.ShowConnectorCountsNew.RemoveListener(ShowConnectableInfoText);
            }
        }

        public override void OnReady(bool wasLoaded) {
            sys = S.Sys.Electricity;
        }

        public void Update(in ElectricNode node) {
            #if UNITY_EDITOR
            if (DebugNode) {
                D.Warn("Updating node for {0}: {1}->{2}",
                    Tile, D.DumpInstance(Node), D.DumpInstance(node));
            }
            #endif
            var wasConnected = Node.NumConnections > 0 || Node.HasWorkerLink;
            var wasOn = Node.IsOn;
            IsInitialized = true;
            var reachabilityChanged = node.IsReachable != Node.IsReachable;
            if (reachabilityChanged) {
                S.UIQuery.ReloadEntityIfSelected.Ask(Tile);
            }
            Node = node;
            nodeProducedB?.Set(Node.Produced);
            nodeConsumedB?.Set(Node.Consumed);
            nodeDeficitB?.Set(Node.Deficit);
            if (Ready.Gen && G.ClockSpeed < ClockSpeed.Unlimited) {
                if (!isAfterNodeUpdateEnqueued) {
                    isAfterNodeUpdateEnqueued = true;
                    S.ActQ.Enqueue(AfterNodeUpdate);
                }
            } else {
                AfterNodeUpdate();
            }
            var isConnected = node.NumConnections > 0 || node.HasWorkerLink;
            if (wasConnected != isConnected) {
                Transmit(this, MsgIdH.ElectricNodeConnectionChanged);
            }
            // Only BuildGridJob recomputes IsReachable, so a link alone does not tell a device
            // whether the ship computer can reach it - this lands after the rebuild syncs it
            if (reachabilityChanged) {
                Transmit(this, MsgIdH.ElectricNodeReachabilityChanged);
            }
        }
        private void AfterNodeUpdate() {
            if (Tile.Health.IsDestroyed) {
                return;
            }
            if (!IsRegistered) {
                #if !PRODUCTION_BUILD
                if (The.TestsAreRunning) {
                    if (S.Sys.Sec.HasThreats || S.Sys.Firex.IsAnythingOnFire) {
                        return;
                    }
                }
                D.Warn("Unregistered node received AfterNodeUpdate(). OK in slow situations.");
                #endif
                return;
            }

            isAfterNodeUpdateEnqueued = false;
            UpdateConnIcon();
            if (Node.Produced > 0f || Node.Consumed > 0f) {
                PlayAmbience();
                UpdateVFX();
            } else {
                StopAmbience();
            }
            UpdateUIBlock(true);
        }

        public void ToggleWorkerLink(bool on) {
            if (Node.HasWorkerLink == on) { return; }
            Node.HasWorkerLink = on;
            if (Node.NumConnections == 0) {
                if (on) {
                    Node.IsReachable = true;
                } else {
                    Node.IsReachable = false;
                    Node.Produced = 0f;
                    Node.Consumed = 0f;
                    Node.Deficit = 0f;
                }
            }
            D.Warn("Setting worker link {0}", on);
            sys.Update(this, Node);
            sys.RebuildGrid(true);
            S.UIQuery.ReloadEntityIfSelected.Ask(Tile);
        }

        public void SetProducerMaxOutput(float maxOut) {
            #if !PRODUCTION_BUILD
            if (!Node.IsProducer && !Node.IsProducerSmart && !Node.IsBattery) {
                D.Err("Trying to set ProducerMaxOutput for a non-producer! {0}",
                    Tile);
            }
            #endif
            if (maxOut == Node.ProducerMaxOutput) { return; }
            Node.ProducerMaxOutput = maxOut;
            sys.Update(this, Node);
            UpdateUIBlock(true);
        }

        public void AutoConnectIfEnabled() {
            bool linkedByOrder = false;
            if (TryConnectPosAfterBuild > 0) {
                var otherConn = OrderedConnectorAt(TryConnectPosAfterBuild);
                TryConnectPosAfterBuild = 0;
                if (otherConn != null) {
                    linkedByOrder = otherConn.TryConnectTo(this, out _);
                }
            }
            if (S.BB.Get(BBKeyH.AutoConnectUpgradeCount, 0) == 0) { return; }
            // A connector the Connect tool's build order just linked still picks up what is
            // broken around it, like any connector getting its first link
            if (IsConnected && !(linkedByOrder && IsConnector)) {
                // D.Warn("Device already connected, won't auto connect: {0}", Tile);
                return;
            }
            // D.Warn("Auto connecting device: {0}", Tile);
            A.Grid.TryAutoConnect(this, true);
        }

        // The Connect tool's build order names its source by position: by the time it is built,
        // something without a node can stand there, and wall sockets are on the Walls layer
        private static ElectricNodeComp OrderedConnectorAt(int pos) {
            var node = EntityUtils.ParentObjAt(pos)?.ENode;
            if (node?.IsConnector == true) { return node; }
            node = EntityUtils.WallAt(pos)?.ENode;
            return node?.IsConnector == true ? node : null;
        }

        public override void OnSave() {
            if (!Tile.IsConstructed) {
                if (ConnectionsToRestore != null) {
                    GetOrCreateData().SetIntSet("ReconnectAfterBuild",
                        ConnectionsToRestore);
                }
                if (TryConnectPosAfterBuild > 0) {
                    GetOrCreateData().SetInt("ConnAfterBuild", TryConnectPosAfterBuild);
                }
            }
        }
        protected override void OnLoad(ComponentData data) {
            TryConnectPosAfterBuild = data.GetInt("ConnAfterBuild", 0);
            ConnectionsToRestore = data.GetIntSet("ReconnectAfterBuild", null);
        }

        public void SetBatteryCharge(float input) {
            if (input == Node.BatteryCharge) { return; }
            Node.BatteryCharge = input;
            sys.Update(this, Node);
            UpdateUIBlock(true);
        }

        public void SetConsumerWantedInput(float input) {
            #if UNITY_EDITOR
            if (DebugNode) {
                if (Node.ConsumerWantedInput != input) {
                    D.Warn("Setting consumer wanted input {0}->{1} on {2}",
                        Node.ConsumerWantedInput, input, Tile);
                }
            }
            #endif
            if (!IsRegistered) {
                D.Warn("Trying to set consumer wanted input for a non-registered node! {0}",
                    this);
            }
            if (input == Node.ConsumerWantedInput) { return; }
            Node.ConsumerWantedInput = input;
            sys.Update(this, Node);
            UpdateUIBlock(true);
        }
        public void ProvideCPU(int amt) {
            if (amt == Node.ProvidesCPU) { return; }
            Node.ProvidesCPU = amt;
            sys.Update(this, Node);
            UpdateUIBlock(true);
        }
        public void ProvideGPU(int amt) {
            if (amt == Node.ProvidesGPU) { return; }
            Node.ProvidesGPU = amt;
            sys.Update(this, Node);
            UpdateUIBlock(true);
        }
        public void ProvideMemory(int amt) {
            if (amt == Node.ProvidesMem) { return; }
            Node.ProvidesMem = amt;
            sys.Update(this, Node);
            UpdateUIBlock(true);
        }
        public void ProvideDisk(int amt) {
            if (amt == Node.ProvidesDisk) { return; }
            Node.ProvidesDisk = amt;
            sys.Update(this, Node);
            UpdateUIBlock(true);
        }

        // I hate this so much
        private void CreateParticles() {
            var particlesName = Config.GetString(PropertyIdH.Particles, null);
            if (particlesName == null && Tile.Transform.IsRotatable) {
                switch (Tile.Transform.Rotation) {
                    case Facing.Type.Down: {
                        particlesName = Config.GetString(PropertyIdH.Particles_D, null);
                        break;
                    }
                    case Facing.Type.Left: {
                        particlesName = Config.GetString(PropertyIdH.Particles_L, null);
                        break;
                    }
                    case Facing.Type.Right: {
                        particlesName = Config.GetString(PropertyIdH.Particles_R, null);
                        break;
                    }
                    case Facing.Type.Up: {
                        particlesName = Config.GetString(PropertyIdH.Particles_U, null);
                        break;
                    }
                }
            }
            if (!string.IsNullOrEmpty(particlesName) && !RenderUtils.DisableCosmeticVFX) {
                var isAutoplay = Config.GetBool(PropertyIdH.ParticlesAutoplay, false);
                var isEmitOnly = Config.GetBool(PropertyIdH.ParticlesEmitOnly, true);
                var emitChance = Config.GetFloat(PropertyIdH.ParticlesEmitChance, 1);
                particleOffsets = Config.GetVector2Set(PropertyIdH.ParticleOffsets, null);
                hasParticles = true;
                particles = new ParticlesSys.Data() {
                    Owner = Entity,
                    IsEmitOnly = isEmitOnly,
                    EmitChance = emitChance,
                    IsStatic = true,
                    PrefabName = particlesName,
                    Play = isAutoplay,
                };
                isParticlesAdded = true;
                if (particleOffsets != null) {
                    particles.Offset = particleOffsets[(int) Tile.Transform.Rotation];
                }
                S.Sys.Particles.AddParticles(particles);
            }
        }

        public override void OnLateReady(bool wasLoaded) {
            if (IsShipComputer || Tile.Transform.IsMulti) {
                textOffset = new Vector2(0, -0.5f);
                textSizePct = 100;
            } else {
                textOffset = new Vector2(0, -0.33f);
                textSizePct = 70;
            }
            if (!Tile.IsConstructed) { return; }
            if (!IsRegistered && !wasLoaded) {
                // register this node with the ElectricSys
                sys.AddNode(this);
            }
            LoadAudioVisuals();
        }

        private void LoadAudioVisuals() {
            hasAmbience = Entity.AudioSource?.IsLinkedToEnergy == true;
            if (hasAmbience) {
                ambienceMinEnergy = Entity.AudioSource.MinEnergy;
            }
            if (!createdParticles) {
                createdParticles = true;
                CreateParticles();
            }
        }

        public void SetSlot(int idx) {
            SlotIdx = idx;
            IsRegistered = SlotIdx >= 0;
            if (IsRegistered) {
                if (Node.IsConnector) {
                    RegisterConnectorListener();
                }
                if (!hasTransmittedAdded) {
                    hasTransmittedAdded = true;
                    if (!Tile.IsConstructed && Tile.Reinstallable?.IsMidRelocation != true) {
                        D.Err("Tile is not constructed / relocated but being added to grid! {0}", Tile);
                    }
                    Transmit(this, MsgIdH.ElectricNodeAdded);
                } else {
                    D.Err("Transmitting AddedToGrid more than once! {0}", Tile);
                }
                UpdateConnIcon();
            } else {
                UnregisterConnectorListener();
            }
        }
        public void CreateManageGoal() {
            if (HasGoal) { return; }
            if (HasAnyGoal) {
                AIGoal.Cancel(Tile.Goal, G.Ticks, T.TempConnect);
            }
            var g = CreateGoal(JobTypeIdH.Task, IconId.CConnected, T.TempConnect, this)
                .WithoutRetry()
                .WithPriority(5)
                .WithPathFlags(PathReqFlags.AdjNearby)
                .WithRequiredAbilities(AbilityIdH.Recharge, AbilityIdH.SkilledLabor)
                .ByPlayer(true)
                .WithWorldToDesired(AIVarsH.TempConnectionProvided, false, true);
            if (!entity.TrySetGoal(g)) {
                UISounds.PlayActionDenied();
            }
        }

        public void AddAfterTick(IComponent comp, ElectricitySys.AfterTickGrid hook) {
            sys.AddAfterTick(comp, hook);
        }
        public void RemoveAfterTick(IComponent comp, ElectricitySys.AfterTickGrid hook) {
            sys.RemoveAfterTick(comp, hook);
        }

        public ElectricNode LoadNode(in ElectricNode node) {
            #if UNITY_EDITOR
            if (DebugNode) {
                D.Warn("Loading saved node for {0}: {1}", Tile, D.DumpInstance(node));
            }
            #endif
            if (IsRegistered) {
                D.Err("Loading a node to a comp that is already registered! {0}. CN: {1}, Loading: {2}",
                    this, Node, node);
            }
            if (node.EntityId != EntityId) {
                D.Err("Entity id mismatch in LoadNode! {0}->{1}", EntityId, node);
            }
            Node.RestoreFromSaved(node);
            Node.PosIdx = Tile.PosIdx;
            Node.Position = Tile.Position;
            if (!IsSwitchable && !Node.IsOn) {
                Node.IsOn = true;
            }
            SetSlot(node.Idx);
            Update(Node);
            return Node;
        }

        public override void Receive(IComponent sender, int message) {
            // When damaged, disconnect or update node stuff (status)
            if (message == MsgIdH.ConstructionFinished) {
                if (!IsRegistered && Entity.IsActive) {
                    sys.AddNode(this);
                }
                LoadAudioVisuals();
                if (!TryRestoreConnections()) {
                    AutoConnectIfEnabled();
                }
            } else if (message == MsgIdH.RepairedAllDamage) {
                // Nothing unbuilt can link, so the pending links wait for ConstructionFinished
                if (Tile.IsConstructed && !TryRestoreConnections()) {
                    AutoConnectIfEnabled();
                }
            } else if (message == MsgIdH.HealthWentToZero) {
                if (IsRegistered) {
                    SaveConnectionsForRestore();
                    sys.RemoveNode(this);
                }
            } else if (message == MsgIdH.TookDamage) {
                if (IsConnected && Tile.Health.IsHeavilyDamaged) {
                    SaveConnectionsForRestore();
                    sys.DisconnectAll(this);
                    //StopAmbience(); - should be automatic after disconnecting
                }
            } else if (message == MsgIdH.TilePosChanged) {
                if (IsRegistered) {
                    Node.Position = Tile.Position;
                    Node.PosIdx = Tile.PosIdx;
                    sys.Update(this, Node);
                }
            }
        }

        private void SaveConnectionsForRestore() {
            if (ConnectionsToRestore != null) {
                // already saved
                return;
            }
            var numConns = sys.ConnectionsFor(EntityId, Caches.IntArray128);
            if (numConns > 0) {
                ConnectionsToRestore = Arrays.Copy(Caches.IntArray128, numConns);
            }
        }

        public void AfterRotationUpdated() {
            if (!hasParticles) { return; }
            var wasParticlesAdded = isParticlesAdded;
            RemoveParticles();
            if (wasParticlesAdded) {
                CreateParticles();
            }
        }

        private bool TryRestoreConnections() {
            if (ConnectionsToRestore == null) {
                return false;
            }
            int wantedToRestore = ConnectionsToRestore.Length;
            int restored = 0;
            for (int i = 0; i < wantedToRestore; i++) {
                var oldTile = S.FindEntity<Tile>(ConnectionsToRestore[i]);
                if (oldTile?.IsActive == true) {
                    if (A.Grid.TryConnect(this, oldTile.ENode, out var failReason)) {
                        D.Warn("Reconnected: {0}", oldTile);
                        restored++;
                    } else {
                        D.Warn("Did not reconnect: {0} -> {1}", oldTile, failReason);
                    }
                } else {
                    D.Warn("Old tile not available! {0}", ConnectionsToRestore[i]);
                }
            }
            ConnectionsToRestore = null;
            D.Warn("Restores {0}/{1} connections for {2}", restored, wantedToRestore, Tile);
            return wantedToRestore == restored;
        }

        public override void OnRemove() {
            // Remove from ElectricSys, rebuild grid
            if (IsRegistered) {
                sys.RemoveNode(this);
                SlotIdx = -1;
                IsRegistered = false;
            }
            RemoveParticles();
            isShowingInfoText = false;
            HideAllInfo();
        }

        private void RemoveParticles() {
            if (hasParticles) {
                hasParticles = false;
                if (isParticlesAdded) {
                    isParticlesAdded = false;
                    S.Sys.Particles.RemoveParticles(particles);
                }
            }
        }

        public bool IsConnectedTo(ElectricNodeComp other) {
            if (other == null || other == this) { return false; }
            return sys.AreConnected(entity.Id, other.EntityId);
        }

        public void ShowConnectableInfoText(bool show) {
            // ShowInfoText($"{Grid.Id}|{Node.GridId}|{Node.Idx}", D.DumpInstance(Node));
            // return;
            if (show) {
                if (!IsRegistered) {
                    D.Err("Unregistered node trying to show connectable info text! {0}, {1}",
                        Entity, Node);
                }
                if (Node.IsConnector) {
                    isShowingInfoText = true;
                    ShowInfoText(
                        Texts.Green(Units.XOutOfY(
                        Node.NumConnections, Node.MaxConnections)),
                            Node.NumConnections < Node.MaxConnections ?
                            T.InfoConnectorSlotsAvailable : T.InfoConnectorFull);
                } else {
                    ShowNodeInfoText(show);
                }
            } else {
                isShowingInfoText = false;
                HideInfoText();
            }
        }
        public void ShowNodeInfoText(bool show) {
            // ShowInfoText($"{Grid.Id}|{Node.GridId}|{Node.Idx}", D.DumpInstance(Node));
            // return;

            // Connectors show their own
            if (!show || !IsSwitchedOn || !Node.IsReachable) {
                HideInfoText();
                isShowingInfoText = false;
                return;
            }
            var rateNow = Math.Abs(Node.Produced - Node.Consumed);
            bool changed = false;
            if (lastRateText == null || !Maths.Approximately(rateNow, lastRate)) {
                lastRateText = Units.Electricity(rateNow);
                lastRate = rateNow;
                changed = true;
            }
            if (!isShowingInfoText || changed) {
                if ((Node.IsProducer || Node.IsProducerSmart)
                        && Node.Produced > Node.Consumed) {
                    if (Node.IsProducing) {
                        ShowInfoText(Texts.WithColor(lastRateText,
                            Texts.TMPColorGreen, textSizePct),
                            $"{T.InfoEnergyNodeProducing}: {lastRateText}", textOffset);
                    } else {
                        if (showZeroProduction) {
                            ShowInfoText(Texts.WithColor(lastRateText,
                                Texts.TMPColorRed, textSizePct),
                                T.InfoEnergyNodeNotProducing, textOffset);
                        } else {
                            if (Node.IsConsuming) {
                                // Most likely battery charing
                                ShowInfoText(Texts.WithColor(lastRateText,
                                    Texts.TMPColorWhite, textSizePct),
                                    $"{T.InfoEnergyNodeConsuming}: {lastRateText}", textOffset);
                            } else {
                                HideInfoText();
                            }
                        }
                    }
                } else if (Node.IsConsuming) {
                    ShowInfoText(Texts.WithColor(lastRateText,
                        Texts.TMPColorWhite, textSizePct),
                        $"{T.InfoEnergyNodeConsuming}: {lastRateText}", textOffset);
                } else if (Node.Deficit > 0) {
                    ShowInfoText(Texts.WithColor($"{Units.Electricity(Node.Deficit)}!!!",
                        Texts.TMPColorRed, textSizePct),
                        $"{T.InfoEnergyDeficit}: {Texts.Red(Units.Electricity(Node.Deficit))}", textOffset);
                } else {
                    HideInfoText();
                }
            }
            isShowingInfoText = true;
        }

        public bool CanConnectTo(ElectricNodeComp other, out string failReason) {
            if (other == this) {
                failReason = T.OperationForbidden;
                return false;
            }
            if (other == null) {
                failReason = T.ElDeviceNotPowerable;
                return false;
            }
            //D.Warn("Estimating connectivity between {0} and {1}", Tile, other?.Tile);
            if (!IsConnectableNow(out failReason)) {
                return false;
            }
            if (!other.IsConnectableNow(out failReason)) {
                return false;
            }
            if (!Node.IsConnector && !other.Node.IsConnector) {
                failReason = T.ElNotAConnector;
                return false;
            }
            if (Node.IsConnector && Node.NumConnections >= Node.MaxConnections) {
                failReason = T.ElConnectorFullyUtilized;
                return false;
            }
            if (other.Node.IsConnector && other.Node
                    .NumConnections >= other.Node.MaxConnections) {
                failReason = T.ElConnectorFullyUtilized;
                return false;
            }

            if (other.Node.IsShipComputer && other.Node.NumConnections == 0
                    || Node.IsShipComputer && Node.NumConnections == 0) {
                if (!sys.CanAddAnotherShipComputer(out failReason)) {
                    return false;
                }
            }

            var range = 0;
            if (Node.IsConnector) {
                range = Node.ConnectorRange;
            }
            if (other.Node.IsConnector) {
                range = Maths.Max(range, other.Node.ConnectorRange);
            }
            if (Vectors.SqrDistance(Node.Position, other.Node.Position) > range * range) {
                failReason = T.ElConnectorOutOfRange;
                return false;
            }
            return true;
        }

        public bool TryConnectTo(ElectricNodeComp other, out string failReason) {
            if (!CanConnectTo(other, out failReason)) {
                return false;
            }
            if (other is ElectricNodeComp othere) {
                // Notifies ElectricNodeConnectionChanged on both sides when it flips
                return sys.TryConnect(this, othere, out failReason);
            } else {
                D.Err("Other is not an electric node (connect)! {0}", other);
            }
            return false;
        }

        public bool TryDisconnectFrom(ElectricNodeComp other, out string failReason) {
            if (other is ElectricNodeComp othere) {
                // Notifies ElectricNodeConnectionChanged on both sides when it flips
                return sys.TryDisconnect(this, othere, out failReason);
            } else {
                failReason = T.AdRejTargetUnavailable;
                D.Err("Other is not an electric node (disconnect)! {0}", other);
            }
            return false;
        }

        private void UpdateUIBlock(bool wasUpdated) {
            if (wasUpdated) {
                if (isShowingInfoText) {
                    //refresh
                    ShowConnectableInfoText(true);
                }
            }

            if (!IsReachable) { return; }
        }
        public void Switch(bool on, bool batch = false) {
            if (!IsSwitchable) { return; }
            if (IsSwitchedOn == on) { return; }
            Node.IsOn = on;
            if (!on) {
                // Make sure inactive devices won't consume / produce
                Node.Consumed = 0f;
                Node.Produced = 0f;
                Node.Deficit = 0f;
                Transmit(this, MsgIdH.ElectricNodeSwitchedOff);
            } else {
                Transmit(this, MsgIdH.ElectricNodeSwitchedOn);
            }
            sys.Update(this, Node);
            if (!on) {
                StopAmbience();
            }
            HideInfoText();
            if (!batch) {
                sys.EnqueueRebuild();
                UpdateUIBlock(true);
            }
            if (on) {
                HideInfo();
            }
            UpdateConnIcon();
        }
        public void UpdateConnIcon() {
            // Don't update any icons until we properly deserialized shit
            if (!Tile.IsConstructed) { return; }
            if (!IsConnected && !IsShipComputer && !Node.HasWorkerLink) {
                ShowInfo(IconId.CNoEnergy, T.InfoEnergyNotConnected);
                StopAmbience();
            } else {
                if (!IsReachable) {
                    bool validate = G.ClockSpeed < ClockSpeed.Fast1
                        && S.Sys.Sec.HasThreats;
                    #if !PRODUCTION_BUILD
                    validate = true;
                    #endif
                    if (validate && Node.GridId == -1 && !Tile.Health.IsDamaged) {
                        if (!A.Grid.ValidateConnectionsFor(this)) {
                            if (Node.NumConnections == 0) {
                                // perhaps explosion blew other connections away?
                                ShowInfo(IconId.CNoEnergy, T.InfoEnergyNotConnected);
                                StopAmbience();
                                return;
                            }
                            #if !PRODUCTION_BUILD
                            #if UNITY_EDITOR
                            D.Err("Node claims to be unreachable, while grid id is -1. {0}: {1}",
                                Tile, Node);
                            #else
                            D.ErrSoft("Node claims to be unreachable, while grid id is -1. {0}: {1}",
                                Tile, Node);
                            #endif
                            #endif
                        }
                    }
                    ShowInfo(IconId.CNoConnection, T.InfoEnergyNodeUnreachable);
                } else {
                    if (!IsSwitchedOn) {
                        ShowInfo(IconId.CPowerOffWarning, T.InfoEnergyNodeOff);
                    } else {
                        if (Node.Deficit > 0f) {
                            UpdateDeficitIcon();
                        } else {
                            HideInfo();
                        }
                    }
                }
            }
        }
        private void UpdateDeficitIcon() {
            if (Node.Deficit > 0 && !wasShowingDeficit) {
                ShowInfo(IconId.CElectricityDeficit, T.ElectricityDeficit);
                S.UIQuery.ReloadEntityIfSelected.Ask(Tile);
            }
            if (Node.Deficit < 0.001f && wasShowingDeficit) {
                S.UIQuery.ReloadEntityIfSelected.Ask(Tile);
                HideInfo();
            }
        }
        private void ShowInfo(string iconId, string text) {
            wasShowingDeficit = false;
            ShowInfoIcon(iconId, text);
        }

        private void HideInfo() {
            wasShowingDeficit = false;
            HideInfoIcon();
        }

        public void SetVFX(ParticleSlot vfx, float minEnergy) {
            this.vfx = vfx;
            vfxMinEnergy = minEnergy;
            hasVFX = vfx != null;
            if (hasVFX) {
                UpdateVFX();
            }
        }

        private void UpdateVFX() {
            if (!hasVFX) { return; }
            vfx.IsVisible = isPlayingAmbience &&
                (vfxMinEnergy == 0f ||
                    Node.Consumed >= vfxMinEnergy ||
                    Node.Produced >= vfxMinEnergy);
        }

        public override void UpdatePosIdx(int pos, int dx, int dy) {
            if (particles == null) { return; }
            particles.Position += new Vector2(dx, dy);
            particles.EmitParams.position += new Vector3(dx, dy, 0);
        }

        private void StopAmbience() {
            if (isPlayingAmbience) {
                isPlayingAmbience = false;
            } else {
                return;
            }
            if (hasAmbience) {
                Tile.AudioSource.Stop();
            }
            if (hasVFX) {
                vfx.IsVisible = false;
            }
            if (hasParticles) {
                particles.Play = false;
            }
        }
        private void PlayAmbience() {
            var shouldPlay = hasAmbience || hasParticles || hasVFX;
            if (!shouldPlay) { return; }
            if (ambienceMinEnergy > 0f) {
                shouldPlay = Node.Consumed >= ambienceMinEnergy
                    || Node.Produced >= ambienceMinEnergy;
            }
            if (!shouldPlay) {
                StopAmbience();
                return;
            }
            if (!isPlayingAmbience) {
                isPlayingAmbience = true;
            } else {
                return;
            }
            if (hasAmbience) {
                Tile.AudioSource.Play();
            }
            if (hasParticles) {
                particles.Play = true;
            }
        }

        public void RelocateTo(Entity target) {
            if (target.TryGetComponent<ElectricNodeComp>(out var other)) {
                sys.TryRelocateConnections(this, other);
                if (!Node.IsOn) {
                    other.Switch(false);
                }
            }
        }

        public void OnGoalChange(AIGoal goal, AIGoalState from, AIGoalState to) {
            if (!AIGoal.IsActive(to)) {
                UpdateConnIcon();
            }
        }

        public void OnGoalLoad(AIGoal goal) {
            UpdateConnIcon();
        }
        public override string ToString() {
            return $" * ElectricNode[{Tile}]";
        }

        public void AddStats(Lifetime life, WGroup block, ref int idx) {
            if (Node.IsShipComputer) { return; }
            if (Node.IsProducer || Node.IsProducerSmart || Node.IsBattery) {
                nodeProducedB ??= new B<float>(Node.Produced);
                var w = UISpawn.Widget<WIconText2Progress>(life,
                    UIPrefabId.WContentProgress, block.Container, idx++);
                w.Load(IconId.CEnergy, T.EnergyProduction, 0f);
                nodeProducedB.Bind(life, (_) => {
                    var max = Node.ProducerMaxOutput;
                    var produced = Node.Produced;
                    w.SetPercent(max > 0f ? produced / max : 0f, false);
                    w.Item.SetText2(Units.ElectricityIO(Math.Max(produced, max), produced));
                });
            }
            if ((!Node.IsProducer && !Node.IsProducerSmart) || Node.IsBattery) {
                nodeConsumedB ??= new B<float>(Node.Consumed);
                nodeDeficitB ??= new B<float>(Node.Deficit);
                var w = UISpawn.Widget<WIconText2Progress>(life,
                    UIPrefabId.WContentProgress, block.Container, idx++);
                w.Load(IconId.CEnergy, T.EnergyConsumption, 0f);
                nodeConsumedB.Bind(life, (_) => {
                    var consumed = Node.Consumed;
                    var max = consumed + Node.Deficit;
                    w.SetPercent(max > 0f ? consumed / max : 0f, false);
                    w.Item.SetText2(Units.ElectricityIO(Math.Max(consumed, max), consumed));
                });
                if (!Node.IsBattery) {
                    var dw = UISpawn.Widget<WIconText2>(life,
                        UIPrefabId.WContentText2, block.Container, idx++);
                    dw.Load(IconId.CNoEnergy, T.EnergyDeficit, Units.Electricity(Node.Deficit));
                    nodeDeficitB.Bind(life, (deficit) => {
                        dw.SetText2(Units.Electricity(deficit));
                    });
                }
            }
        }
    }
}
