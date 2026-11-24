using System;
using Game.Achievements;
using Game.Commands;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Systems.Creatures;
using Game.Utils;
using Game.CodeGen;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Systems.Conditions;
using Game.Systems.Story;
using Game.Systems.Notify;
using Game.Systems.UIUX;
using Game.Systems.Naming;
using Game.Input;

namespace Game.Components {
    public sealed class StasisArrayComp : BaseComponent<StasisArrayComp>,
            ITickListener, IRelocatable, IDefPowerProvider {

        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new StasisArrayComp());
        }
        public const float PreventWakeUpThreshold = 0.90f;
        private const int DefaultCapacity = 100;
        // An array is billed for at least this many sleepers
        private const int MinBilledBeings = 10;
        private const float PreventWakeUpsMul = 1.5f;
        private const float PreventWakeUpsAdd = 150f;
        public bool IsSentToColony;
        private int beingCount;
        private int capacity;
        public int BeingCount => beingCount;
        public int Capacity => capacity;
        private float curConsumption {
            get {
                var bc = beingCount;
                if (bc < MinBilledBeings) {
                    bc = MinBilledBeings;
                }
                var energy = IsALSOn ?
                    bc * stasisALSEnergyPerBeing :
                    bc * stasisBasicEnergyPerBeing;
                if (IsALSOn && PreventWakeUps) {
                    energy *= PreventWakeUpsMul;
                    energy += PreventWakeUpsAdd;
                }
                return energy;
            }
        }

        // Basic life support for the minimum billed sleepers .. a full array on advanced
        // life support that prevents wake ups
        public void AddDefPower(Def def, ComponentConfig config, ref DefPower power) {
            var cap = Mathf.Max(MinBilledBeings, config.GetInt(PropertyIdH.MaxCapacity,
                DefaultCapacity));
            var min = MinBilledBeings * Tunables.StasisBasicStabilityEnergyPerBeing;
            var max = cap * Tunables.StasisAdvancedStabilityEnergyPerBeing
                * PreventWakeUpsMul + PreventWakeUpsAdd;
            power.AddConsumption(min, max);
        }
        public float Stability;
        private bool IsPowered;
        private long lastWakeUp;
        // ALS = advanced life support
        public bool IsALSOn;
        public bool PreventWakeUps;
        public bool IsPowerOK => IsPowered;
        public bool WakeUpsPrevented =>
            PreventWakeUps && IsALSOn
                && Stability >= PreventWakeUpThreshold;
        private bool isWakeUpRequested;
        private float wakeUpProgress;
        private float lastNotifiedWakeUpProgress;
        private BCallback stateChangedB;
        public BCallback StateChangedB => stateChangedB ??= new BCallback();
        // Separate from StateChangedB, which fires on every stability percent and wake-up
        // progress step: this one only fires when who is in the pods, or how they are
        // doing, changes - the only things a list of sleepers has to redraw for.
        private BCallback manifestChangedB;
        public BCallback ManifestChangedB => manifestChangedB ??= new BCallback();
        public void NotifyManifestChanged() {
            manifestChangedB?.Invoke();
        }
        public bool IsElysium;
        // Nobody in this array is harmed by low stability before this tick. Set for an
        // array that appears mid game (a derelict arrives already unstable), so the player
        // gets to reach it before named people start dying in it.
        public long HarmGraceUntilTick;
        // Damage does not throw anyone clear before this tick, see Receive
        private long throwClearUntilTick;

        private float stasisALSEnergyPerBeing;
        private float stasisBasicEnergyPerBeing;

        public void EnableElysium() {
            IsElysium = true;
            IsALSOn = false;
            RemoveStabilityWarning();
            if (Tile.TryGetComponent<HeaterComp>(out var heater)) {
                heater.SetDisabled(true);
            }
            if (Tile.TryGetComponent<OxygenProducerComp>(out var oxyPump)) {
                oxyPump.SetDisabled(true);
            }
        }

        private NotificationHandle noPowerEvent;

        private void ShowNoPowerEvent() {
            if (!noPowerEvent.IsValid) {
                noPowerEvent = Notification.BuildFor(Tile, Tile.Definition.Preview, T.LifeSupportNoPower, 0, NotificationType.Alert)
                    .WithTooltip(T.CryoNotEnoughPowerDesc)
                    .Group("sts_pwr")
                    .Show();
            }
        }

        private void HideNoPowerEvent() {
            if (noPowerEvent.IsValid) {
                Notification.Remove(ref noPowerEvent);
            }
        }

        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetBool("IsALSOn", IsALSOn);
            data.SetBool("IsPowered", IsPowered);
            data.SetBool("PreventWU", PreventWakeUps);
            data.SetFloat("Stability", Stability);
            data.SetInt("BeingCount", beingCount);
            data.SetBool("IsWURequested", isWakeUpRequested);
            data.SetFloat("WakeUpProgress", wakeUpProgress);
            data.SetLong("HarmGrace", HarmGraceUntilTick);
            data.SetLong("ThrowClearUntil", throwClearUntilTick);
        }

        protected override void OnLoad(ComponentData data) {
            IsALSOn = data.GetBool("IsALSOn", false);
            IsPowered = data.GetBool("IsPowered", false);
            PreventWakeUps = data.GetBool("PreventWU", false);

            // Capacity is not saved: it is a property of the device (OnConfig), so a retune
            // reaches every array, including ones in existing saves
            Stability = data.GetFloat("Stability", Stability);
            beingCount = data.GetInt("BeingCount", 0);
            // D.Warn("Loaded being count {0} for {1}", beingCount, Tile);
            #if !PRODUCTION_BUILD
            if (beingCount == 0) {
                // For tests
                D.Err("Recovering stasis 0 being count in broken relocation save");
                if (The.TestsAreRunning) {
                    beingCount = ManifestSys.DefaultHumans;
                }
            }
            #endif
            if (beingCount > capacity) {
                beingCount = capacity;
            }
            isWakeUpRequested = data.GetBool("IsWURequested", false);
            wakeUpProgress = data.GetFloat("WakeUpProgress", 0f);
            HarmGraceUntilTick = data.GetLong("HarmGrace", 0L);
            throwClearUntilTick = data.GetLong("ThrowClearUntil", 0L);
        }

        public override void OnLateReady(bool wasLoaded) {
            // Ready.Gen: during generation the stage that built the array decides who is
            // in it, this fill is only for an array the player places in sandbox
            if (!wasLoaded && beingCount == 0 && A.InstantBuild && Ready.Gen) {
                ManifestSys.For(S).Populate(this, ManifestSys.DefaultHumans,
                    Rng.URange(ManifestSys.DefaultMinPets, ManifestSys.DefaultMaxPets + 1),
                    Rng.Unseeded);
                D.Warn("Instant build-> filling stasis with beings: {0} | {1}",
                    beingCount, Tile);
            }
        }

        private ElectricNodeComp elNode;
        public ElectricNodeComp ENode => elNode ??= Tile.ENode;

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ElectricNodeAdded) {
                elNode = (ElectricNodeComp) sender;
                elNode.AddAfterTick(this, AfterTickGrid);
                if (!IsElysium) {
                    elNode.SetConsumerWantedInput(curConsumption);
                }
            } else if (message == MsgIdH.ElectricNodeConnectionChanged) {
                UpdateStabilityInfo(G.Ticks, (int) (Stability * 100));
            } else if (message == MsgIdH.TookDamage) {
                D.Warn("StasisArray taking damage! {0} -> {1}",
                    Tile.Health.LastDamageType, Tile.Health.LastDamageAmount);
                // Only a real blow throws people clear, and only once in a while: fire ticks
                // and streams of small hits would otherwise wake the whole array for free
                var health = Tile.Health;
                if (health.HealthPercent > 0.1f
                        && health.LastDamageAmount >= health.MaxHealth * Tunables.StasisThrowClearMinDamage
                        && G.Ticks >= throwClearUntilTick) {
                    if (S.Rng.Chance(0.66f)) {
                        throwClearUntilTick = G.Ticks
                            + (long) (Tunables.StasisThrowClearCooldownHours * Consts.TicksPerHour);
                        ForceEjectSurvivors(1, 4);
                    }
                }
            } else if (message == MsgIdH.HealthWentToZero) {
                D.Warn("StasisArray health went to zero, destroying!");
                AfterDestroy();
            }
        }

        private static Curve stabilityToSanityCurve = new float[] {
            0.0f, 0.05f,
            0.1f, 0.15f,
            0.2f, 0.3f,
            0.4f, 0.5f,
            0.5f, 0.7f,
            1.0f, 0.95f,
        };

        public Being SpawnBeing(BeingData data, bool allowRaid) {
            if (beingCount < 1) {
                D.Err("Trying to spawn a being from stasis array with 0 used slots! {0} -> {1}",
                    this, data);
                return null;
            }
            var being = SpawnAt(data);
            if (being == null) {
                return null;
            }
            ReleaseChamber();
            ApplyWakeUpEffects(data, being, true, allowRaid);
            return being;
        }

        // Wakes a colonist previously sent back into stasis (see StasisUtils) - they occupy
        // a chamber the same as anonymous cryo-mass does, so their return frees one too.
        public Being SpawnKnownBeing(BeingData data) {
            var being = SpawnAt(data);
            if (being == null) {
                return null;
            }
            ReleaseChamber();
            ApplyWakeUpEffects(data, being, false, false);
            return being;
        }

        // Shared "a chamber was freed" bookkeeping for both the anonymous-stranger and
        // known-sleeper wake-up paths - one occupancy counter, one place that unlocks
        // AchievementStasisEmptied on the last chamber clearing.
        private void ReleaseChamber() {
            if (beingCount < 1) { return; }
            beingCount--;
            if (beingCount == 0 && !isForceEjecting) {
                // Emptied by waking everyone, as opposed to being force-ejected by
                // AfterDestroy/OnRemove - those go through ForceEjectSurvivors, which sets
                // isForceEjecting, and zero beingCount themselves regardless of what gets
                // spawned here.
                The.AchievementTracker.Unlock(
                    Achievements.AchievementStasisEmptied.AchievementId);
            }
            stateChangedB?.Invoke();
        }

        // Called when a colonist reaches the array's InPort and is stored - occupies a
        // chamber the same as anonymous cryo-mass does. No-ops if the array is already full.
        public void StoreColonist() {
            if (beingCount >= capacity) { return; }
            beingCount++;
            stateChangedB?.Invoke();
        }

        private Being SpawnAt(BeingData data) {
            var cmd = new CmdSpawnBeing(data, Tile.Transform.OutPort);
            cmd.Execute(S);
            var being = cmd.SpawnedBeing;
            if (being == null) {
                return null;
            }
            // Everything a sleeper carried in was frozen with them, and a save/load in between
            // leaves it unregistered - spawning alone does not bring it back (Activate is not a
            // load). Same restore as every other way back aboard: DeserterUtils,
            // ExpeditionSys, BeingStorage. Without it a wound they had never heals, because
            // the stale ConditionInjured stops Body.CheckCondition making a new one.
            data.RestoreConditions(S, ignoreRegistered: true);
            S.Sys.Log.AddLine(being, T.BeingWokeUp_V1.F(
                being.Species.Name));
            lastWakeUp = G.Ticks;
            return being;
        }

        // isFreshStranger gates the effects that only make sense for someone the colony has
        // never met: resetting sanity/AI alignment to a stability-derived value, and the
        // low-stability "turns hostile and raids" roll. A returning known colonist
        // (isFreshStranger: false) still gets CryoSickness but keeps their existing mind
        // untouched. Nobody takes body damage at the moment of waking any more: what an
        // unstable array does to a body happens while they sleep, where the player can
        // see it coming (ManifestSys).
        private void ApplyWakeUpEffects(BeingData data, Being being, bool isFreshStranger, bool allowRaid) {
            var stability = Stability;
            if (IsElysium) {
                stability = S.Rng.Range(0.02f, 0.15f);
            }
            var cryoSev = stability >= 0.75f ? Severity.Mild
                        : stability >= 0.5f ? Severity.Moderate
                        : Severity.Severe;
            // The wake-up owns cryo sickness: always a fresh one from this array's stability.
            // StasisUtils.SendToStasis clears it on the way in, but a sleeper from an older
            // save can still be carrying one.
            being.Data.RemoveCondition(S, ConditionIdH.CryoSickness);
            ConditionState.CreateFor(S, ConditionIdH.CryoSickness, being.Data, cryoSev, false);
            if (isFreshStranger) {
                var sanity = stabilityToSanityCurve.Evaluate(Stability) * being.Data.StatsBeing.SanityBaseline.Value;
                being.Data.StatsBeing.Sanity.UpdateBase(sanity);
                // FIXME better ai alignment
                // A stranger starts wary of the AI, below the mutiny line, whatever their mood:
                // trust is earned afterwards through what the AI does for them (TrustSys)
                being.Data.StatsBeing.AIAlignment.UpdateBase(S.Rng.Range(0.35f, 0.5f));
            }
            if (stability < 0.95f) {
                if (isFreshStranger) {
                    if (allowRaid && stability < 0.3f && S.Rng.Chance(0.5f) && A.IsCombatOn) {
                        var antiPlayer = FactionDef.Get("AntiPlayer");
                        data.Mind.SetFaction(antiPlayer);
                        S.Sys.Combat.StartRaidBy(being);
                    } else if (stability < 0.5f) {
                        data.Body.AddExcrementsOnFloor();
                    }
                }
            }
        }

        public void LoadBeings(float beings) {
            // capacity = Mathf.CeilToInt(beings / 500) * 500;
            beingCount = Mathf.CeilToInt(beings);
            if (beingCount > capacity) {
                beingCount = capacity;
            }
            // D.Warn("Loading {0} beings to stasis array: {1}", beingCount, Tile);
        }
        // Only ManifestSys calls this: the count follows the bound sleepers, never the
        // other way round.
        public void SyncBeingCount(int count) {
            if (count > capacity) {
                count = capacity;
            }
            if (count < 0) {
                count = 0;
            }
            if (beingCount == count) { return; }
            beingCount = count;
            stateChangedB?.Invoke();
            manifestChangedB?.Invoke();
        }
        // The section holding this array is being abandoned, with everyone in it
        public void Purge() {
            ManifestSys.For(S).LoseSleepers(this, T.StasisLostCause);
            beingCount = 0;
            stateChangedB?.Invoke();
        }

        // Nothing in the game calls this today - there is no button for it - and it is kept
        // for mods and for whatever asks for a wake-up on demand next. A wake-up requested
        // this way is deliberately not announced ahead of time the way the scheduled one is
        // (see StasisWakeUpWarning): whoever asked has already watched the progress below.
        public bool TryRequestWakeUp() {
            if (!IsPowered || beingCount < 1 || Stability <= 0.9f) {
                return false;
            }
            isWakeUpRequested = true;
            wakeUpProgress = 0f;
            lastNotifiedWakeUpProgress = 0f;
            stateChangedB?.Invoke();
            return true;
        }

        public override string ToString() {
            return string.Format(" * StasisArray [{0} Beings]", beingCount);
        }

        protected override void OnConfig() {
            Stability = 1f;
            capacity = Config.GetInt(PropertyIdH.MaxCapacity, DefaultCapacity);
            unpoweredChangeCurve = Tunables.StasisUnpoweredStabilityChangeCurve;
            basicChangeCurve = Tunables.StasisBasicStabilityChangeCurve;
            advancedChangeCurve = Tunables.StasisAdvancedStabilityChangeCurve;
            stasisALSEnergyPerBeing = Tunables.StasisAdvancedStabilityEnergyPerBeing;
            stasisBasicEnergyPerBeing = Tunables.StasisBasicStabilityEnergyPerBeing;
        }

        public string StabilityTitle() {
            if (Stability > 0.9f) { return T.CryoStabilityGreat; }
            if (Stability > 0.8f) { return T.CryoStabilityGood; }
            if (Stability > 0.6f) { return T.CryoStabilityAverage; }
            if (Stability > 0.33f) { return T.CryoStabilityPoor; }
            return T.CryoStabilityUnstable;
        }
        private string StabilityTitleAndColor(out Color color) {
            if (Stability > 0.9f) {
                color = Consts.ColorGreenBar;
                return Texts.Green(T.CryoStabilityGreat);
            }
            if (Stability > 0.8f) {
                color = Consts.ColorGreenBar;
                return Texts.Green(T.CryoStabilityGood);
            }
            if (Stability > 0.6f) {
                color = Consts.YellowRectColor;
                return Texts.Yellow(T.CryoStabilityAverage);
            }
            if (Stability > 0.33f) {
                color = Consts.YellowRectColor;
                return Texts.Yellow(T.CryoStabilityPoor);
            }
            color = Consts.RedRectColor;
            return Texts.Red(T.CryoStabilityUnstable);
        }


        private void AfterDestroy() {
            if (Tile.Reinstallable.IsPending) { return; }
            if (!Tile.IsConstructed) { return; }
            if (IsSentToColony) { return; }
            if (beingCount > 0) {
                ForceEjectSurvivors(
                    Math.Min(10, beingCount),
                    Math.Max(20, beingCount));
                // Whoever did not make it out went down with the array
                ManifestSys.For(S).LoseSleepers(this, T.StasisLostCause);
                beingCount = 0;
                if (S.Components.CountMatching<StasisArrayComp>(
                        (a) => a != this && a.beingCount > 0, 1) == 0) {
                    The.AchievementTracker.Unlock(AchievementStasisDestroyed.AchievementId);
                }
            }
        }

        public override void OnRemove() {
            if (!Ready.Gen) { return; }
            if (Tile.Reinstallable.IsMidRelocation) { return; }
            if (IsSentToColony) { return; }
            RemoveStabilityWarning();
            // The stasis array was deconstructed
            if (beingCount > 0 && Tile.Health.HealthPercent > 0.1f) {
                if (A.InstantBuild && !Tile.Health.IsDamaged) {
                    // Won't eject - stasis was removed in sandbox mode
                    ManifestSys.For(S).DiscardSleepers(this);
                } else {
                    ForceEjectSurvivors(
                        Mathf.CeilToInt(beingCount * 0.05f),
                        Mathf.CeilToInt(beingCount * 0.15f) + 1);
                    ManifestSys.For(S).LoseSleepers(this, T.StasisLostCause);
                }
                beingCount = 0;
                if (Ready.Game) {
                    if (S.Components.CountMatching<StasisArrayComp>(
                            (a) => a != this && a.beingCount > 0, 1) == 0) {
                        The.AchievementTracker.Unlock(AchievementStasisDestroyed.AchievementId);
                    }
                }
            }
            // Whatever path led here, nobody stays bound to an array that no longer exists
            ManifestSys.For(S).LoseSleepers(this, T.StasisLostCause);
        }

        private bool isForceEjecting;
        private void ForceEjectSurvivors(int limitFrom, int limitTo) {
            if (beingCount < 1) { return; }
            if (limitTo > 9) {
                limitTo = 9;
            }
            if (limitFrom > limitTo / 2) {
                limitFrom = limitTo / 2;
            }
            var countToSpawn = S.Rng.Range(0,
                Mathf.Clamp(beingCount, 0, S.Rng.Range(limitFrom, limitTo)));
            var outPortPos = Tile.Transform.OutPort;
            if (!EntityUtils.IsWithinBounds(outPortPos)) {
                D.Err("Trying to force eject survivors from stasis array that is outside margin: {0}",
                    Entity);
                return;
            }
            // The people thrown clear are people who were in here, not new strangers
            var ejected = new System.Collections.Generic.List<BeingData>();
            ManifestSys.For(S).DrawCandidates(this, S.Rng, countToSpawn, ejected);
            isForceEjecting = true;
            foreach (var data in ejected) {
                StasisUtils.Wake(S, data, this, false, G.Ticks);
            }
            isForceEjecting = false;
        }

        private void AfterTickGrid() {
            if (IsElysium || !elNode.IsPowerable) { return; }
            elNode.SetConsumerWantedInput(curConsumption);
            if (elNode.IsConsuming) {
                var wasPowered = IsPowered;
                IsPowered = true;
                HideNoPowerEvent();
                if (!wasPowered) {
                    stateChangedB?.Invoke();
                }
                if (IsALSOn && isWakeUpRequested) {
                    wakeUpProgress += Tunables.StasisWakeUpSpeed;
                    if (wakeUpProgress > 1f) {
                        isWakeUpRequested = false;
                        wakeUpProgress = 0f;
                        lastNotifiedWakeUpProgress = 0f;
                        var evt = StoryEventDef.Get("OnDemand/StasisWakeUp");
                        var data = StoryEventData.CreateFor(evt);
                        data.Seed = Rng.UInt;
                        data.TargetTile = Tile;
                        if (!S.Query.StartStoryEvent.Ask(data)) {
                            D.Err("Failed to start stasis wake-up story event! {0}", data);
                        }
                        stateChangedB?.Invoke();
                    } else if (wakeUpProgress - lastNotifiedWakeUpProgress >= 0.1f) {
                        lastNotifiedWakeUpProgress = wakeUpProgress;
                        stateChangedB?.Invoke();
                    }
                }
            } else {
                var wasPowered = IsPowered;
                IsPowered = false;
                if (elNode.IsReachable) {
                    ShowNoPowerEvent();
                }
                if (wasPowered) {
                    stateChangedB?.Invoke();
                }
            }
        }

        private Curve unpoweredChangeCurve;
        private Curve basicChangeCurve;
        private Curve advancedChangeCurve;

        // The highest stability a powered array holds with advanced life support off: above it
        // the basic curve is negative. Generation has to use this and not a nicer looking
        // number, or a freshly generated array slides for the player's whole first week with
        // nothing actually wrong with it. Derived from the curve so a retune carries.
        // Goes through Curve rather than the raw float[] because Curve's conversion normalises
        // point direction - the unpowered curve is authored descending.
        public static float SettledBasicStability {
            get {
                Curve curve = Tunables.StasisBasicStabilityChangeCurve;
                var points = curve.Points;
                for (int i = points.Length - 1; i >= 0; i--) {
                    if (points[i].y >= 0f) {
                        return points[i].x;
                    }
                }
                return 0f;
            }
        }

        private NotificationHandle lowStabilityEvent;
        private int stabBefore = -1;

        public void Tick(long currentTick) {
            // ghost
            if (!Tile.IsConstructed) { return; }
            // Elysium feeds the grid rather than drawing from it, and AfterTickGrid skips it,
            // so nothing would ever set IsPowered back once a relocation or a grid blip
            // cleared it, and the array slid on the unpowered curve (STA-1922)
            var isSustained = IsPowered || IsElysium;
            var changeCurve = isSustained ? (IsALSOn ? advancedChangeCurve : basicChangeCurve) : unpoweredChangeCurve;
            var changeRate = isSustained ? Tunables.StasisPoweredStabilityChangeRate : Tunables.StasisUnpoweredStabilityChangeRate;
            var rate = changeCurve.Evaluate(Stability);
            rate *= changeRate;
            Stability = Mathf.Clamp01(Stability + rate);
            int stabAfter = (int) (Stability * 100);
            if (elNode == null || elNode.HasDeficit || !elNode.IsConnected) {
                IsPowered = false;
            }
            if (stabBefore != stabAfter) {
                UpdateStabilityInfo(currentTick, stabAfter);
                stabBefore = stabAfter;
                // D.FIXME_SOFT("Adjust wakeup time");
                // S.Clock.OnNextUpdate(S.Story.AdjustNextWakeUpTime);
            }
        }

        private void UpdateStabilityInfo(long currentTick, int stabAfter) {
            if (!IsElysium && beingCount > 0 && !ENode.IsConnected) {
                if (stabAfter <= 33) {
                    if (!lowStabilityEvent.IsValid) {
                        lowStabilityEvent = Notification.BuildFor(Tile, Tile.Graphics.GraphicId,
                                T.CryoStabilityUnstable, 0, NotificationType.Alert)
                            .WithTooltip(T.CryostabilityUnstableWarning)
                            .Group("sts_stb")
                            .Show();
                        ShowInfoIcon(IconId.CWarning, Texts.Red(T.CryostabilityUnstableWarning));
                    }
                } else if (stabAfter > 33) {
                    RemoveStabilityWarning();
                }
            } else {
                RemoveStabilityWarning();
            }
            var title = StabilityTitleAndColor(out var color);
            var dir = Mathf.Sign(stabAfter - stabBefore);
            var pct = Units.Percentage(Stability);
            if (dir > 0) {
                pct = $"{pct}{Texts.Green(GlyphMap.ArrowUp)}";
            } else if (dir < 0) {
                pct = $"{pct}{Texts.Red(GlyphMap.ArrowDown)}";
            }
            title = $"{title} ({pct})";

            ShowInfoBar(color, Stability, title);
            stateChangedB?.Invoke();
        }

        private void RemoveStabilityWarning() {
            if (lowStabilityEvent.IsValid) {
                Notification.Remove(ref lowStabilityEvent);
                HideInfoIcon();
            }
        }

        public void RelocateTo(Entity target) {
            if (target.TryGetComponent<StasisArrayComp>(out var array)) {
                // The relocated array is a new entity: the sleepers follow it, or they
                // would stay bound to an id that no longer exists. OriginalId, because a
                // relocation in place removes this entity first, and removal zeroes
                // EntityId: rebinding from 0 moved nobody and the array got an invented
                // roster on every turn (STA-1923)
                ManifestSys.For(S).RebindById(entity.OriginalId, array.EntityId);
                array.beingCount = beingCount;
                array.capacity = capacity;
                array.IsElysium = IsElysium;
                array.IsPowered = IsPowered;
                array.isWakeUpRequested = isWakeUpRequested;
                array.lastWakeUp = lastWakeUp;
                array.Stability = Stability;
                array.HarmGraceUntilTick = HarmGraceUntilTick;
                array.throwClearUntilTick = throwClearUntilTick;
                array.IsALSOn = IsALSOn;
                array.PreventWakeUps = PreventWakeUps;
                array.UpdateStabilityInfo(G.Ticks, (int) (Stability * 100));
            } else {
                D.Err("Target has no stasis array! {0}", target);
            }
        }

    }
}
