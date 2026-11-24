#if UNITY_EDITOR
#define DEBUG_EXPEDITIONS
#endif

using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Utils;
using Game.CodeGen;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Systems.Creatures;
using Game.Systems.Quests;
using Game.Commands;
using Unity.Mathematics;
using Game.Systems.Mental;
using KL.Grid;

namespace Game.Systems {
    public sealed class ExpeditionSys : GameSystem,
            ISaveableSpecial {
        public const string SysId = "ExpeditionSys";
        public override string Id => SysId;

        [SelfInit]
        private static void Register() {
            sanityCostCurve = null;
            GameSystems.Register(SysId, () => new ExpeditionSys());
        }

        private readonly List<Expedition> expeditions = new();
        public List<Expedition> Expeditions => expeditions;
        private readonly List<OverlayInfo> overlays = new();

        public List<OverlayInfo> Overlays => overlays;
        private static Curve sanityCostCurve;

        public Entity Entity => null;
        private int completed;
        /// <summary>
        /// key = being data id, value = last expedition start time
        /// </summary>
        private Dictionary<uint, long> crewExpeditionTimes;
        public long LastExpeditionTimeFor(uint beingId) {
            if (crewExpeditionTimes == null) { return 0L; }
            if (crewExpeditionTimes.TryGetValue(beingId, out var time)) {
                return time;
            }
            return 0L;
        }

        protected override void OnInitialize() {
            overlays.Clear();
            overlays.Add(new OverlayInfo(OverlayType.Controls,
                OverlayId.Expedition, IconId.WPickaxe));
            S.Sig.AfterLoadState.AddListener(OnLoadSave);
        }

        private void RestoreData() {
            RestoreExpeditions();
            if (!Arrays.IsEmpty(expeditions)) {
                for (int i = expeditions.Count - 1; i >= 0; i--) {
                    Expedition ex = expeditions[i];
                    ex.S = S;
                    ex.AfterLoad();
                    if (!ex.IsValid) {
                        D.Err("Removing invalid expedition!");
                        expeditions.RemoveAt(i);
                    }
                }
            }
            ValidateShuttles();
            ValidateExpeditionCrew();
            TickExpeditions();
            Ready.Set(WhenH.ExpeditionsRestored, true);
        }

        private void ValidateExpeditionCrew() {
            var beings = S.Query.AllBeingData.Ask();
            HashSet<BeingData> onExpedition = null;
            foreach (var b in beings.Values) {
                if (b.HasStateFlag(BeingState.Expedition)) {
                    onExpedition ??= new();
                    onExpedition.Add(b);
                }
            }
            if (onExpedition == null) {
                return;
            }

            foreach (var exp in expeditions) {
                if (exp.Crew != null) {
                    foreach (var mem in exp.Crew) {
                        onExpedition.Remove(mem);
                    }
                }
            }

            foreach (var stuck in onExpedition) {
                D.Err("Being is marked as on expedition, but is not part of any crew! Fixing: {0}", stuck);
                RemoveExpeditionState(stuck, null);
            }
        }

        private void RemoveExpeditionState(BeingData beingData, ShuttleComp shuttle) {
            beingData.StateFlags &= ~BeingState.Expedition;
            beingData.StateFlags &= ~BeingState.Hidden;
            if (beingData.Being != null) { return; }
            int pos;
            if (shuttle != null) {
                pos = shuttle.OutPort;
                shuttle = null;
            } else {
                pos = beingData.LastPosition.Idx;
            }
            if (pos < 1) {
                shuttle = S.Components.FindFirstByType<ShuttleComp>();
                if (shuttle != null) {
                    pos = shuttle.OutPort;
                }
                if (pos < 1) {
                    // final fallback
                    pos = S.Sys.Areas.GenRandomSolidPos(Rng.Unseeded);
                }
                if (pos < 1) {
                    // final final fallback
                    pos = S.Grid.CenterPos;
                }
                D.Err("Will respawn expedition member at fallback pos: {0} | {1}",
                    Pos.String(pos), beingData);
            }
            var cmd = new CmdSpawnBeing(beingData, pos);
            S.CmdQ.Enqueue(cmd);
        }

        private void OnLoadSave(GameState state) {
            sanityCostCurve = Tunables.ExpeditionSanityCostDaysCurve;
                // Tunable.FloatSet(TunableKey.ExpeditionSanityCostDaysCurve);
            state.Clock.OnTick.AddListener(OnTick);
            state.Clock.OnTickProgress.AddListener(OnTickProgress);
            S.Sig.EnteredHyperspace.AddListener(OnEnterHyperspace);
            S.Sig.FailedToEnterShuttle.AddListener(OnFailEnterShuttle);
            Ready.When(WhenH.BeingsLoaded, RestoreData);
        }

        private void OnEnterHyperspace(bool entered) {
            S.UIQuery.RebuildExpeditions.Ask();
        }

        private void OnFailEnterShuttle(BeingData crewMember, string reason) {
            foreach (var exp in expeditions) {
                if (exp.Crew.Contains(crewMember)) {
                    // found the exp
                    if (exp.MiningOrders.AbortOnMissingCrew
                            || IsRequiredQuestCrew(exp, crewMember)) {
                        var msg = T.ExpeditionFailMissingCrew_V2.F(crewMember.Name, exp.Target?.StarmapLink ?? T.Unknown);
                        if (!string.IsNullOrEmpty(reason)) {
                            msg = $"{msg}\n{reason}";
                        }
                        The.Dialog.ShowSimple(IconId.CComputing, msg);
                        AbortExpedition(exp);
                        break;
                    } else {
                        if (crewMember.HasStateFlag(BeingState.Expedition) && crewMember.Being?.IsActive != true
                                && crewMember.LastPosition.Idx == exp.Shuttle.InPort) {
                            // crew member did not fail!?
                            D.Err("Crew member claims to have failed to enter shuttle, but is inside the shuttle! Ignoring failure: {0}",
                                crewMember);
                        } else {
                            RemoveExpeditionState(crewMember, exp.Shuttle);
                            exp.CrewRemove(crewMember);
                        }
                    }
                    return;
                }
            }
        }

        private bool IsRequiredQuestCrew(Expedition exp, BeingData crewMember) {
            if (exp.Target == null) { return false; }
            // player's ship can become expedition target at some point, and then
            // targets will be blank anyway
            if (exp.Target.Type.IdH == SpaceObjectTypeIdH.Player) {
                return false;
            }
            var targets = S.Query.GetQuestStepTargets.Ask(exp.Target);
            for (int i = targets.Length - 1; i >= 0; i--) {
                var target = targets[i];
                if (target.Step?.Data?.Provider is ColonistQuestProvider provider
                        && provider.Mind.Id == crewMember.Mind.Id) {
                    return true;
                }
                if (target.RequiredCrewMindId == crewMember.Mind.Id) { return true; }
            }
            return false;
        }

        private void ValidateShuttles() {
            // Validate all shuttles
            var shuttles = S.Components.GenFind<ShuttleComp>();
            foreach (var shuttle in shuttles) {
                ValidateShuttle(shuttle);
            }
        }

        private void ValidateShuttle(ShuttleComp shuttle) {
            if (shuttle.IsLost) {
                if (shuttle.SpaceObject != null) {
                    D.Err("Shuttle is lost but has a space object! {0}", shuttle.SpaceObject);
                }
                return;
            }
            // Moved from shuttle late ready
            if (shuttle.IsOut) { shuttle.Hide(); }
            // This os OK, it happens when we save while shuttle is landing on the ship
            if (!shuttle.IsOut && shuttle.SpaceObject != null) {
                var exp = FindForShuttle(shuttle.EntityId);
                if (exp == null || exp.StageType != ExpeditionStageTypeH.LandingOnBase) {
                    D.Err("Shuttle is not out, but has a space object! {0}. Expedition: {1} | Stage: {2}",
                        shuttle.Tile, exp, exp?.CurrentStage);
                }
            }
            if (shuttle.IsInUse || shuttle.IsOut) {
                var exp = FindForShuttle(shuttle.EntityId);
                if (exp == null) {
                    D.Err("Shuttle is marked as out or in use, and has no expedition! {0}", shuttle.Tile);
                    shuttle.Show();
                    shuttle.SetInUse(false);
                }
            }
        }

        private void OnTick(long ticks) {
            if (ticks % 13 == 0) {
                // TODO switch back to background task?
                TickExpeditions();
            }
        }

        private void OnTickProgress(float progress) {
            for (int i = expeditions.Count - 1; i >= 0; i--) {
                Expedition exp = expeditions[i];
                switch (exp.StageType) {
                    case ExpeditionStageTypeH.TravelToDest: {
                        MoveShuttleTo(exp.Shuttle, exp.Target, exp.TargetOffset);
                        break;
                    }
                    case ExpeditionStageTypeH.TravelToBase: {
                        MoveShuttleTo(exp.Shuttle, S.Universe.Player, Consts.V2Zero);
                        break;
                    }
                }
            }
        }

        // FIXME this should be handled by TrafficSys
        private void MoveShuttleTo(ShuttleComp shuttle, SpaceObject target, Vector2 offset) {
            var shuttleSO = shuttle.SpaceObject;
            if (shuttleSO == null) {
                D.Err("Trying to move shuttle that has no space object! {0}",
                    shuttle);
                return;
            }

            var scale = Tunables.StarmapSpaceObjectSpread;
            var dist = Vectors.Distance(shuttleSO.Location, target.Location + offset) * scale;
            // This is pixels in starmap UI
            if (dist > 3) {
                var shutSpeed = S.Sys.Ship.SpeedInCurrentLayer
                    * Tunables.ShuttleSpeedBase;
                shuttleSO.Location = Vector2.MoveTowards(
                    shuttleSO.Location, target.Location + offset,
                        shutSpeed / S.Clock.TickDuration);
            }
        }

        private void TickExpeditions() {
            var ticks = G.Ticks;
            // first iteration - let's reset all mining factors for all expedition targets
            UpdatePlanetResources();
            for (int i = expeditions.Count - 1; i >= 0; i--) {
                var exped = expeditions[i];
                if (ValidateIntegrity(exped)) {
                    exped.Tick(ticks);
                    if (exped.IsAborted) {
                        AbortExpedition(exped);
                    }
                }
                if (exped.IsDone) {
                    expeditions.Remove(exped);
                    EndExpedition(exped);
                }
                // exped.Controls.UpdateUI();
            }
            S.Situation.ExpeditionsOngoing = expeditions.Count;
        }

        private void UpdatePlanetResources() {
            for (int i = expeditions.Count - 1; i >= 0; i--) {
                if (S.Sys.Planets.TryGetResourceForUpdate(expeditions[i].Target,
                        out var idx, out var res)) {
                    if (res.ManualMineFactor > 0f) {
                        res.ManualMineFactor = 0f;
                        S.Sys.Planets.UpdateResource(idx, res);
                    }
                }
            }
        }

        private bool ValidateIntegrity(Expedition exp) {
            if (exp.Shuttle?.Entity.IsActive != true) {
                D.Err("Shuttle was destroyed! Aborting expedition: {0}", exp);
                exp.IsDone = true;
                return false;
            }
            if (exp.Shuttle.IsLost) {
                return true;
            }
            if (exp.Crew.Count == 0 && !exp.Shuttle.HasAutoPilot) {
                if (exp.Shuttle.IsOut) {
                    exp.Shuttle.SetInUse(false);
                    exp.Shuttle.Show();
                }
                D.Err("Aborting expedition that has 0 members! {0}", exp);
                exp.IsDone = true;
                return false;
            }
            return true;
        }

        public override void Unload() {
            expeditions.Clear();
        }

        public string GetName() {
            return Id;
        }

        public void SaveSpecial(SystemsDataSpecial sd) {
            var data = new ExpeditionsData();
            data.Expeditions = new ExpeditionData[expeditions.Count];
            for (int i = 0; i < expeditions.Count; i++) {
                Expedition exp = expeditions[i];
                data.Expeditions[i] = Expedition.Serialize(exp);
            }
            data.CrewExpeditionTimes = crewExpeditionTimes;
            data.Completed = completed;
            sd.ExpeditionsData = data;
        }

        private ExpeditionsData data;

        public void LoadSpecial(SystemsDataSpecial sd) {
            if (sd == null) { return; }
            data = sd.ExpeditionsData;
        }

        private void RestoreExpeditions() {
            if (data == null) { return; }
            expeditions.Clear();
            var s = S.Situation;
            if (data.Expeditions != null) {
                for (int i = 0; i < data.Expeditions.Length; i++) {
                    ExpeditionData exp = data.Expeditions[i];
                    var e = Expedition.Deserialize(exp);
                    e.S = S;
                    expeditions.Add(e);
                }
                s.ExpeditionsOngoing = expeditions.Count;
            } else {
                s.ExpeditionsOngoing = 0;
            }
            crewExpeditionTimes = data.CrewExpeditionTimes;
            completed = data.Completed;
            s.ExpeditionsCompleted = completed;
            data = null;
        }

        public Expedition FindForShuttle(int id) {
            for (int i = 0; i < expeditions.Count; i++) {
                Expedition exp = expeditions[i];
                if (exp.IsValid && exp.Shuttle.EntityId == id) {
                    return exp;
                }
            }
            return null;
        }

        public bool AbortExpedition(Expedition exp) {
            if (exp.CurrentStage?.CanCallBack(exp) != false) {
                CallExpeditionBack(exp);
                return false;
            }
            expeditions.Remove(exp);
            exp.IsAborted = true;
            EndExpedition(exp);
            return true;
        }

        // Ends every expedition at once (the universe they flew into is being regenerated). Each
        // leaves the list before it ends, like everywhere else: ending one drops its crew's goals,
        // a crew member who was boarding reports a failure to enter the shuttle, and that would
        // abort the same expedition again while the caller still walked the list
        public void EndAll() {
            while (expeditions.Count > 0) {
                var last = expeditions.Count - 1;
                var exp = expeditions[last];
                expeditions.RemoveAt(last);
                if (exp.Shuttle != null) {
                    exp.Shuttle.SetSO(null);
                    exp.Shuttle.IsOut = false;
                }
                EndExpedition(exp);
            }
        }

        public void EndExpedition(Expedition exp) {
            exp.IsDone = true;
            if (exp.StageType != ExpeditionStageTypeH.Prep) {
                completed++;
                S.Situation.ExpeditionsCompleted = completed;
            }
            if (exp.Shuttle == null) {
                D.Err("Ending an expedition that has no shuttle! {0}", exp);
            } else {
                var so = exp.Shuttle.SpaceObject;
                if (so != null) {
                    D.Err("Ending an expedition for a shuttle that has a space object! {0}", so);
                    SOUtils.RemoveSO(so);
                    exp.Shuttle.SetSO(null);
                }
                exp.Shuttle.SetInUse(false);
            }
            if (!exp.Shuttle.IsLost) {
                exp.Shuttle.HideInfoIcon();
                long duration;
                if (exp.StartTick > 0) {
                    duration = G.Ticks - exp.StartTick;
                } else {
                    duration = 0;
                }
                var durationDays = duration / (float) Consts.TicksPerDay;
                for (int i = 0; i < exp.Crew.Count; i++) {
                    BeingData member = exp.Crew[i];
                    if (member.Being?.IsActive == true) {
                        member.Being.AIAgent.DropGoal();
                    } else {
                        member.StateFlags &= ~BeingState.Expedition;
                        member.StateFlags &= ~BeingState.Hidden;
                        var cmd = new CmdSpawnBeing(member, exp.Shuttle.OutPort);
                        cmd.Execute(S);
                        member.RestoreConditions(S, ignoreRegistered: true);
                    }
                    if (durationDays > 0.1f) {
                        AdjustExpeditionMemberSanity(exp, member, duration, durationDays);
                    }
                }
            }
            exp.OnChange.Invoke();

            S.Sig.ExpeditionEnded.Send(exp);
            S.UIQuery.RebuildExpeditions.Ask();
        }

        private void AdjustExpeditionMemberSanity(Expedition exp, BeingData member,
                long duration, float durationDays) {
            if (member.Mind.Health == null) { return; }
            if (member.StateFlags.HasFlag(BeingState.Dead)) {
                return;
            }
            var sanityChange = SanityChangeFor(exp, member, durationDays);
            if (sanityChange != 0f) {
                var eff = MentalEffect.CreateWithIcon(
                    member.Being?.PosIdx ?? exp.Shuttle?.Tile.PosIdx ?? -1,
                    icon: IconId.CPickaxe,
                    start: G.Ticks - duration,
                    duration: (long) math.round(duration * 1.1f), T.Expedition,
                    // Same magnitude formula as TickCrewSanity: replaces the ongoing
                    // effect (same Reason), settles it with a clean post-return duration.
                    effect: (int) math.round(sanityChange * 100f)
                );
                S.Sig.AddMentalEffectWithLog.Send(member, eff, 0.25f, true);
            }
        }

        public static float SanityChangeFor(Expedition exp, BeingData member, float durationDays) {
            var sanityCost = sanityCostCurve.Evaluate(durationDays);
            if (exp.Context.IsExploration) {
                if (member.Mind.Jobs.TryJob(JobTypeIdH.Expedition, out var expSlot)) {
                    if (expSlot.Loves) {
                        return sanityCost;
                    }
                }
            } else if (member.Mind.Jobs.TryJob(JobTypeIdH.Mining, out var miningSlot)) {
                if (miningSlot.Loves) {
                    return sanityCost;
                }
            }
            return -sanityCost;
        }

        public bool CallExpeditionBack(Expedition exp) {
            exp.MiningOrders.IsAutoUnload = false;
            if (exp.CurrentStage != null) {
                if (exp.CurrentStage.IsReturning) {
                    return true;
                }
                if (!exp.CurrentStage.CanCallBack(exp)) {
                    A.S.Sig.ShowErrorMessage.Send(T.ExpeditionCallBackFailed_V1.F(
                            exp.Target.StarmapLink));
                    return false;
                }
            }
            exp.ForceStage(ExpeditionStageTypeH.PrepForReturn);
            return true;
        }

        public void Launch(Expedition exp) {
            exp.S = S;
            exp.Launch();
            expeditions.Add(exp);
            // Deposit is marked visited only once actually mined (ExpeditionStageMineResource) —
            // Prep can still abort before takeoff (damaged shuttle / missing crew), and we don't
            // want to permanently blacklist a deposit that was never touched.
            crewExpeditionTimes ??= new();
            var ticks = G.Ticks;
            foreach (var mem in exp.Crew) {
                crewExpeditionTimes[mem.Id] = ticks;
            }
            S.Sig.ExpeditionStarted.Send(exp);
        }

        public Expedition FindForSpaceObject(SpaceObject so) {
            for (int i = expeditions.Count - 1; i >= 0; i--) {
                Expedition exp = expeditions[i];
                if (exp.Target == so && !exp.IsDone) {
                    return exp;
                }
            }
            return null;
        }
        public Expedition ForShuttleSO(SpaceObject so) {
            for (int i = expeditions.Count - 1; i >= 0; i--) {
                Expedition exp = expeditions[i];
                if (exp.Shuttle.SpaceObject == so && !exp.IsDone) {
                    return exp;
                }
            }
            return null;
        }

        public bool HasExpeditionTo(SpaceObject so) {
            for (int i = expeditions.Count - 1; i >= 0; i--) {
                Expedition exp = expeditions[i];
                if (exp.Target == so && !exp.IsDone) {
                    return true;
                }
            }
            return false;
        }

        public bool HasExpeditionIn(SpaceObject parent) {
            for (int i = expeditions.Count - 1; i >= 0; i--) {
                Expedition exp = expeditions[i];
                if (exp.Target?.Parent == parent && !exp.IsDone) {
                    return true;
                }
            }
            return false;
        }

        public Expedition FindForCrew(BeingData b) {
            for (int i = expeditions.Count - 1; i >= 0; i--) {
                Expedition exp = expeditions[i];
                if (!exp.IsDone && exp.Crew.Contains(b)) {
                    return exp;
                }
            }
            return null;
        }

        public bool IsOnExpedition(BeingData b, Expedition except) {
            for (int i = expeditions.Count - 1; i >= 0; i--) {
                Expedition exp = expeditions[i];
                if (exp == except) { continue; }
                if (exp.Crew.Contains(b)) {
                    return true;
                }
            }
            return false;
        }
    }
}
