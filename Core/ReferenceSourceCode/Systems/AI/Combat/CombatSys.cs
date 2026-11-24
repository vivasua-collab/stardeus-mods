#if UNITY_EDITOR
// #define DEBUG_COMBAT
// #define DEBUG_STRENGTH
#endif
using System;
using System.Collections.Generic;
using Game.Audio;
using Game.Commands;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;
using Game.Systems.Mental;
using Game.Systems.Creatures;
using KL.Grid;
using Game.Achievements;
using Game.Systems.Story;
using Game.Systems.Notify;
using Game.Systems.Voice;
using Game.Systems.Motion;

namespace Game.Systems.AI.Combat {
    public sealed class CombatSys : GameSystem, ISaveableSpecial, IOverlayProvider {
        public const string SysId = "CombatSys";
        public override string Id => SysId;
        // Balance tests only: the crew hunts raiders like AI raiders do, standing in for a player
        // who drafts everyone and sends them in. Undrafted crew otherwise only responds from
        // where it stands (CombatObjectiveDefend.RespondsInPlace), and raid pricing measured
        // against that would price raids for a crew that never goes to meet them.
        public static bool DefendersHuntForBalanceTests;

        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new CombatSys());
        }

        private CombatFlags flags;
        public bool IsAnyCombatInProgress => flags != CombatFlags.None;
        public bool IsHostileCombatInProgress => (flags & CombatFlags.Hostile) == CombatFlags.Hostile;
        public bool IsFriendlyCombatInProgress => (flags & CombatFlags.Friendly) == CombatFlags.Friendly;
        // What the player was told about the hostile fight (CombatAlerts): the raid alert or
        // Combat in progress is up. A fight may run before either, with nobody the wiser.
        public bool IsHostileAlertUp => IsHostileCombatInProgress
            && alerts.Stage != HostileAlert.Quiet;
        // Our side and an enemy have met: Combat in progress
        public bool IsHostileCombatEngaged => IsHostileCombatInProgress
            && alerts.Stage == HostileAlert.InCombat;
        private readonly CombatAlerts alerts;
        public CombatAlerts Alerts => alerts;
        private readonly List<OverlayInfo> overlays = new();
        public List<OverlayInfo> Overlays => overlays;
        private NotificationHandle combatEvent;
        private readonly GatheringRaidNotice gatheringNotice = new();
        public bool IsGatheringNoticeShown => gatheringNotice.IsShown;
        private NotificationData combatEventData;
        private bool combatEventHostile;
        // the hostile notification shown is Combat in progress, not the raid alert
        private bool combatEventIsCombat;
        private bool boardingAnnounced;
        // When a Signal Intrusion hack last turned a hostile robot to us; 0 = never.
        // ConditionHacked spaces conversions out by it, so one raid can't be taken whole.
        public long LastHackConversionTick;

        #if DEBUG_COMBAT
        public int NumCombats;
        #endif

        public readonly List<Army> Armies = new();
        public readonly HostileBodies Bodies = new();

        public CombatSys() {
            alerts = new CombatAlerts(this);
        }

        protected override void OnInitialize() {
            S.Sig.AfterLoadState.AddListener(OnLoadSave);
            S.Query.StartRaid.SetResponder(OnStartRaid);
            S.Query.StartCombatBetween.SetResponder(OnStartCombatBetween);
            S.Query.StartFriendlyFightBetween.SetResponder(OnStartFriendlyFightBetween);
            S.Query.GetArmies.SetResponder(GetArmies);
        }

        private List<Army> GetArmies() {
            return Armies;
        }

        private bool OnStartFriendlyFightBetween(CombatUnit unit1, CombatUnit unit2) {
            if (!A.IsCombatOn) { return false; }
            // A fistfight would be disbanded by the real one at once, and a unit can only
            // fight in one army: one already drafted would be pulled into both
            if (IsHostileCombatInProgress || unit1.Army != null || unit2.Army != null) {
                return false;
            }
            if (unit1.Agent.Goal?.IsForceAssigned == true
                    && !AIGoal.Interrupt(unit1.Agent.Goal, G.Ticks)) {
                return false;
            }
            var army1 = Army.Create(unit1.Agent.BeingData.EffectiveFaction, CombatFlags.Friendly)
                .WithObjective(CombatObjectiveIdH.FistFight);
            var army2 = Army.Create(unit2.Agent.BeingData.EffectiveFaction, CombatFlags.Friendly)
                .WithObjective(CombatObjectiveIdH.FistFight);
            Armies.Add(army1);
            Armies.Add(army2);
            army1.WithUnit(unit1);
            army2.WithUnit(unit2);
            army1.Organize();
            army2.Organize();
            if (!G.IsDemoMode) {
                UISounds.PlayMentalBreakdown();
            }
            ShowFightEvent();
            SetCombatFlags(flags | CombatFlags.Friendly);
            unit1.SetAttackTarget(unit2, true);
            unit2.SetAttackTarget(unit1, true);
            unit1.SetState(CombatUnitState.Aggressive);
            var line = T.FightBetween_V2.F(unit1.Data.Mind.LinkToShortName,
                unit2.Data.Mind.LinkToShortName);
            S.Sig.ShowFanfare.Send(line);
            S.Sys.Log.AddLine(line);
            if (unit1.Data.Mind.Health != null) {
                unit1.Agent.AddMentalEffect(MentalEffect.CreateWithIcon(
                    unit1.Agent.PosIdx,
                    IconId.CFist, G.Ticks, Consts.TicksPerDay, T.FightWith_V1.F(unit2.Name), -6));
            }
            if (unit2.Data.Mind.Health != null) {
                unit2.Agent.AddMentalEffect(MentalEffect.CreateWithIcon(
                    unit2.Agent.PosIdx,
                    IconId.CFist, G.Ticks, Consts.TicksPerDay, T.FightWith_V1.F(unit1.Name), -6));
            }
            return true;
        }

        private bool OnStartCombatBetween(CombatUnit attacker, CombatUnit target) {
            if (!A.IsCombatOn) { return false; }
            // Checked before the target changes sides: bailing out after that would leave
            // it AntiPlayerTemp with no army to hand it back its faction.
            if (attacker.Agent.Goal != null
                    && !AIGoal.Interrupt(attacker.Agent.Goal, G.Ticks)) {
                D.Warn("Can't start combat between {0} and {1} - attacker has non-interruptable goal! {1}",
                    attacker, target, attacker.Agent.Goal);
                return false;
            }
            var targetData = target.Agent.BeingData;
            var attackerIsPlayer = attacker.Agent.BeingData.EffectiveFaction.IsPlayer;
            // One of ours attacks or is attacked: the fight is on at once. A fight between
            // others (a predator after a raider) starts quietly, as a raid does.
            var engaged = attackerIsPlayer || targetData.EffectiveFaction.IsPlayer
                || targetData.IsProtectedGuest;
            if (attackerIsPlayer && IsGuestBetrayal(targetData)) {
                TurnGuestHostile(target);
            } else if (attackerIsPlayer
                    && (targetData.EffectiveFaction.IsPlayer || targetData.IsGuest)) {
                TurnAgainstPlayer(target);
            }
            DisbandFriendlyArmies();
            AutoCreateArmies();
            if (!attackerIsPlayer && target.Army == null && targetData.IsProtectedGuest) {
                ShelterWithCrew(target);
            }
            StartHostileFight();
            if (engaged && alerts.Stage != HostileAlert.InCombat) {
                RaiseCombatAlert(attackerIsPlayer ? target.PosIdx : attacker.PosIdx);
            }
            foreach (var a in Armies) {
                // initiate objectives and all that
                a.Tick(S, G.Ticks);
            }
            attacker.SetState(CombatUnitState.Aggressive);
            attacker.SetAttackTarget(target, true);
            // the crew went for a gathering raid (an order, or walking into it): it attacks
            EndStagingOf(attacker);
            EndStagingOf(target);
            return true;
        }

        private void EndStagingOf(CombatUnit unit) {
            if (unit.Army?.IsStaging == true) {
                unit.Army.EndStaging(S, G.Ticks);
            }
        }

        // A gathering raid attacks (Army.EndStaging). OnStartRaid kept it out of hostile
        // combat while it only waited; the fight starts now, as any raid's does, or it joins
        // the one that is on.
        public void OnStagingEnded(Army army) {
            gatheringNotice.Update(S, Armies, arrived: false);
            if (!A.IsCombatOn) { return; }
            if (!IsHostileCombatInProgress) {
                DisbandFriendlyArmies();
                AutoCreateArmies();
            }
            StartHostileFight();
        }

        // A hostile fight starts, or a new army joins one, and nobody is told yet: the raid
        // alert comes when an enemy that can reach us advances, Combat in progress at the first
        // contact (CombatAlerts)
        private void StartHostileFight() {
            if (IsHostileCombatInProgress) {
                alerts.OnArmyJoined(G.Ticks);
            } else {
                alerts.Begin(G.Ticks);
            }
            SetCombatFlags(flags | CombatFlags.Hostile);
        }

        // The player attacks one of their own (breakdown, crawler, Asimov override) or a
        // guest: the target fights back as AntiPlayerTemp, with the Survive objective, until
        // it is beaten or the fight ends. Army.RemoveUnit then hands its faction back.
        // A guest is not drafted into an army of its own faction: that one would raid the
        // ship computer and pull in every other guest of the faction.
        public void TurnAgainstPlayer(CombatUnit target) {
            var data = target.Agent.BeingData;
            // Already restrained: keep the faction remembered the first time. A chipped raider
            // goes back to its own faction too - Player would make the chip's hold permanent.
            if (data.Mind.Faction.IdH != FactionIdH.AntiPlayerTemp) {
                data.Mind.ReturnFaction = data.Mind.Faction;
            }
            data.Mind.SetFaction(FactionDef.FastGet(FactionIdH.AntiPlayerTemp));
            if (target.Army != null) {
                // Leave the player army now, so AutoCreateArmies gives the target an army
                // of its new faction before SetAttackTarget needs its army.
                // Army.Tick would evict it anyway, but only after SetAttackTarget.
                target.Army.RemoveUnit(target, CombatUnitState.Aware, keepFaction: true);
            }
            if (IsHostileCombatInProgress) {
                // mid-fight nobody else enrolls the target before the attack order needs it
                AutoCreateArmies();
            }
        }

        // A guest is restrained, as one of our own would be, only when it is out of its mind
        // or in a fistfight. The Asimov override is no reason here: it lets the AI harm its
        // own crew, and would make every attack on a guest a free restraint.
        public static bool CanRestrainGuest(BeingData data) {
            return data.Being?.AIAgent?.CombatUnit?.Army?.Flags == CombatFlags.Friendly
                || data.HasCondition(ConditionIdH.CrawlerAttached)
                || data.HasCondition(ConditionIdH.MentalBreakdown);
        }

        // The player attacks a calm guest: it and its people aboard turn on us for good
        public static bool IsGuestBetrayal(BeingData data) {
            return data.IsProtectedGuest && !CanRestrainGuest(data);
        }

        // Every guest of the attacked guest's faction defects, as on a raid of that faction:
        // the army they fall into is that faction's, and StaysNeutral would draft a guest
        // left behind into it while still a guest. The faction holds it against us.
        public void TurnGuestHostile(CombatUnit target) {
            var data = target.Agent.BeingData;
            var faction = data.Mind.ReturnFaction ?? data.Mind.Faction;
            var beings = S.Beings.Alive;
            for (int i = 0; i < beings.Count; i++) {
                var guest = beings[i].Data;
                if (guest == null || !guest.IsGuest) { continue; }
                if ((guest.Mind.ReturnFaction ?? guest.Mind.Faction) != faction) { continue; }
                DefectGuest(guest);
            }
            S.Query.ChangeFactionAttitude.Ask(faction, FactionDef.Player,
                Tunables.AttackGuestStandingPenalty);
            if (IsHostileCombatInProgress) {
                // mid-fight nobody else enrolls the target before the attack order needs it
                AutoCreateArmies();
            }
        }

        // A guest stops being one and fights for its own faction. Leaving its army first
        // (sheltering with the crew, or being restrained) hands a restrained guest its own
        // faction back (Army.RemoveUnit).
        private void DefectGuest(BeingData guest) {
            var cu = guest.Being?.AIAgent?.CombatUnit;
            if (cu?.Army != null) {
                cu.Army.RemoveUnit(cu, CombatUnitState.Aware);
            }
            PassengerUtils.EndGuestRole(S, guest, despawn: false);
            S.Sig.BeingDefected.Send(guest);
        }

        // A predator went for a guest, which sits in no army and so could not be targeted
        // at all. Combat is free-for-all, so an army of the guest's own faction would be the
        // crew's enemy too: the guest shelters with the crew instead, keeping its faction
        // (Army.Belongs), and fights back once it is hit (CombatUnit.ValidateConfig).
        private void ShelterWithCrew(CombatUnit guest) {
            var crew = ArmyFor(FactionDef.Player);
            if (crew.IsOrganized) {
                crew.Absorb(Army.Create(FactionDef.Player).WithUnit(guest));
            } else {
                crew.WithUnit(guest);
                crew.Organize();
            }
        }

        // A downed raider came to. During a raid it simply rejoins (EnsureInCombat). After it,
        // nobody would engage it (pain shock wakes raiders up hours after the fight), so it
        // starts its own raid: one that can get off the map leaves, one that
        // cannot fights on until it is down or dead. One that came to where it can reach
        // nothing of ours (a derelict) stays there: the crew meets it if it goes there
        // (BeingCluster), as it meets a derelict's crew.
        public void OnHostileRecovered(Being being) {
            if (!A.IsCombatOn || IsHostileCombatInProgress) { return; }
            if (being == null || !being.IsActive || !being.CanFight) { return; }
            var data = being.Data;
            if (data.IsFriendly || data.EffectiveFaction.IsPlayer || data.Species.IsWild) {
                return;
            }
            // a neutral being that came to (a derelict's survivor in a jet suit) is no raider
            if (!S.Sys.Diplomacy.IsHostileTowardsPlayer(data.EffectiveFaction)) { return; }
            var cu = being.AIAgent.CombatUnit;
            if (cu.Army != null) { return; }
            alerts.Reach.Refresh(G.Ticks);
            if (alerts.Reach.IsStranded(being)) { return; }
            if (StartRaidBy(being) == null || cu.Army == null) { return; }
            if (EscapeRoute.Exists(cu)) {
                cu.IsLeaving = true;
                cu.ValidateConfig();
            }
        }

        // A hyperjump leaves behind the hostiles flying outside the hull that are not attacking:
        // on their way out, or in no fight at all (a raider the fight ended around). They go the
        // way a raider that reaches the map edge goes. Attacking raiders keep up, or a charged
        // drive would cancel any raid in space (STA-1754). Floating ones drift off with the loose
        // objects as before.
        public void LeaveBehindOnJump() {
            var beings = S.Beings.Alive;
            for (int i = beings.Count - 1; i >= 0; i--) {
                var b = beings[i];
                if (!b.IsActive || b.Carrier != null || b.Motion.WillFloat) { continue; }
                if (!b.Motion.Flags.HasFlag(MotionFlags.InSpace)) { continue; }
                var data = b.Data;
                if (data.IsFriendly || data.EffectiveFaction.IsPlayer) { continue; }
                if (!S.Sys.Diplomacy.IsHostileTowardsPlayer(data.EffectiveFaction)) { continue; }
                var cu = b.AIAgent?.CombatUnit;
                if (cu?.Army != null && !cu.IsLeaving && !cu.IsFleeing) { continue; }
                // hidden and gone together: the AI must never run a hidden being
                S.Clock.OnNextTick(() => {
                    if (!b.IsActive) { return; }
                    b.Data.StateFlags |= BeingState.Hidden;
                    S.Beings.Destroy(b);
                });
            }
        }

        public Army StartRaidBy(Being raider) {
            if (!A.IsCombatOn) {
                return null;
            }
            if (raider == null) {
                D.Err("Trying to start a raid without raider!");
                return null;
            }

            var army = Army.Create(raider.Data.EffectiveFaction)
                .WithObjective(CombatObjectiveIdH.Raid);
            army.WithUnit(raider.AIAgent.CombatUnit);
            OnStartRaid(army);
            return army;
        }
        public Army StartRaidBy(FactionDef raidingFaction, List<Being> raiders) {
            if (!A.IsCombatOn) {
                return null;
            }
            for (int i = raiders.Count - 1; i >= 0; i--) {
                Being r = raiders[i];
                if (r.IsDead) {
                    D.Err("Trying to start a raid by a dead raider: {0}", r);
                    raiders.RemoveAt(i);
                    continue;
                }
                if (r.Data.Body.IsIncapacitated) {
                    D.Err("Trying to start a raid by an incapacitated raider: {0}", r);
                    raiders.RemoveAt(i);
                    continue;
                }
            }
            if (Arrays.IsEmpty(raiders)) {
                D.Err("Trying to start a raid without raiders! {0}", raidingFaction);
                return null;
            }

            var army = Army.Create(raidingFaction)
                .WithObjective(CombatObjectiveIdH.Raid);
            foreach (var b in raiders) {
                b.Data.Mind.SetFaction(raidingFaction);
                army.WithUnit(b.AIAgent.CombatUnit);
            }
            OnStartRaid(army);
            return army;
        }

        private Army OnStartRaid(Army army) {
            if (!A.IsCombatOn) {
                return null;
            }
            if (army == null || army.Units == null) {
                D.Err("Trying to start a raid with null or incorrectly initialized army! {0}", army);
                return null;
            }
            // A gathering raid (a staged raid) is no fight yet: no alert, no clock
            // limit, nobody called to arms while it only waits. OnStagingEnded starts the fight.
            var startsStaging = army.IsStaging;
            if (!startsStaging) {
                DisbandFriendlyArmies();
            }
            bool absorbed = false;
            TryAbsorbArmy(ref army, ref absorbed);
            if (!absorbed) {
                // this is a new army. let's try to add all others from same faction
                // before organizing
                foreach (var b in S.Beings.Alive) {
                    var cu = b.AIAgent.CombatUnit;
                    if (cu.State != CombatUnitState.Incapacitated) {
                        if (!army.Units.Contains(cu)) {
                            if (cu.Agent.BeingData.EffectiveFaction == army.Faction) {
                                // A guest whose own faction is now raiding defects: it stops
                                // being a guest (frees its bed, re-groups as an enemy) and
                                // fights for its faction. The owning quest fails via its
                                // watchdog step once it notices the passenger is no longer a guest.
                                if (cu.Agent.BeingData.IsGuest) {
                                    DefectGuest(cu.Agent.BeingData);
                                }
                                if (cu.Army != null) {
                                    cu.Army.RemoveUnit(cu, CombatUnitState.Aware,
                                        keepFaction: true);
                                }
                                army.WithUnit(cu);
                            }
                        }
                    }
                }
                army.Organize();
                Armies.Add(army);
                if (!army.IsStaging) {
                    AutoCreateArmies();
                }
                RegisterEncounterWith(army.Faction);
            }
            if (army.IsStaging) {
                if (startsStaging) {
                    gatheringNotice.Update(S, Armies, arrived: true);
                    return army;
                }
                // a raid of the same faction joined the gathering one: they attack together
                army.EndStaging(S, G.Ticks);
            }
            var enemyCount = 0;
            foreach (var a in Armies) {
                if (a.Faction.IsPlayer) { continue; }
                enemyCount += a.Units.Count;
            }
            // S.Situation.AddTension(enemyCount, "Combat");
            StartHostileFight();
            return army;
        }

        private void TryAbsorbArmy(ref Army army, ref bool absorbed) {
            foreach (var a in Armies) {
                if (a.Faction == army.Faction) {
                    // we will join the army
                    a.Absorb(army);
                    army = a;
                    absorbed = true;
                    return;
                }
            }
        }

        private void SetCombatFlags(CombatFlags next) {
            var prev = flags; flags = next;
            if (!prev.HasFlag(CombatFlags.Hostile) && next.HasFlag(CombatFlags.Hostile)) {
                PlayStats.TrackHostileCombatStarted();
            } else if (prev.HasFlag(CombatFlags.Hostile) && !next.HasFlag(CombatFlags.Hostile)) {
                PlayStats.TrackHostileCombatEnded();
            }
            if (!prev.HasFlag(CombatFlags.Friendly) && next.HasFlag(CombatFlags.Friendly)) {
                PlayStats.TrackFriendlyCombatStarted();
            } else if (prev.HasFlag(CombatFlags.Friendly) && !next.HasFlag(CombatFlags.Friendly)) {
                PlayStats.TrackFriendlyCombatEnded();
            }
        }

        private void DisbandFriendlyArmies() {
            for (int i = Armies.Count - 1; i >= 0; i--) {
                Army army = Armies[i];
                if ((army.Flags & CombatFlags.Friendly) == CombatFlags.Friendly) {
                    #if DEBUG_COMBAT
                    D.Warn("Disbanding friendly army: hostile combat started");
                    #endif
                    army.Disband();
                    Armies.RemoveAt(i);
                }
            }
            SetCombatFlags(flags & ~CombatFlags.Friendly);
            RemoveFightEvent();
        }

        // A fistfight broken up by a hostile fight takes its notification with it. The hostile
        // alerts come later, or never, and used to replace it at once.
        private void RemoveFightEvent() {
            if (!combatEvent.IsValid || combatEventHostile) { return; }
            Notification.Remove(ref combatEvent);
            combatEventData = null;
        }

        private void AutoCreateArmies() {
            #if DEBUG_COMBAT
            D.Warn("Combat#{0}: Autocreating Armies! Before: {1}",
                NumCombats, D.DumpCollection(Armies));
            #endif
            foreach (var b in S.Beings.Alive) {
                if (!b.IsActive) {
                    D.Err("Being inactive! {0}", b);
                }
                #if DEBUG_COMBAT
                D.Warn("Combat#{0}: Handling being: {1}", NumCombats, b);
                #endif
                if (b.AIAgent.CombatUnit.State == CombatUnitState.Incapacitated) {
                    continue;
                } else {
                    if (b.Data.Body.IsIncapacitated) {
                        D.Err("BeingDataBody is incapacitated but combat unit is not! {0}->{1}",
                            b.Data, b.AIAgent.CombatUnit);
                        b.AIAgent.CombatUnit.SetState(CombatUnitState.Incapacitated);
                        continue;
                    }
                }
                EnsureInCombat(b);
            }
            foreach (var a in Armies) {
                if (!a.IsOrganized) {
                    a.Organize();
                }
            }
        }

        /// <summary>
        /// True if there is an active hostile (non-friendly) army of the given faction.
        /// Used to decide whether a guest's own faction is currently raiding.
        /// </summary>
        private bool HasHostileArmyOfFaction(FactionDef faction) {
            for (int i = 0; i < Armies.Count; i++) {
                var a = Armies[i];
                if ((a.Flags & CombatFlags.Friendly) == CombatFlags.Friendly) { continue; }
                if (a.Faction == faction) { return true; }
            }
            return false;
        }

        /// <summary>
        /// A guest passenger stays neutral (is not drafted into a hostile army) unless their
        /// own faction is raiding. We check the guest's real faction (Mind.Faction), not
        /// EffectiveFaction, which can collapse to Player.
        /// </summary>
        private bool StaysNeutral(Being b) {
            return b.Data.IsProtectedGuest && !HasHostileArmyOfFaction(b.Data.Mind.Faction);
        }

        private void EnsureInCombat(Being b) {
            var unit = b.AIAgent.CombatUnit;
            if (unit.Army != null || !b.CanFight || unit.State == CombatUnitState.Incapacitated) {
                return;
            }
            if (StaysNeutral(b)) {
                // A guest aboard does not get pulled into combat just by being present.
                return;
            }
            var army = ArmyFor(b.Data.EffectiveFaction);
            if (army.IsOrganized) {
                #if !PRODUCTION_BUILD
                D.Warn("Combat#{0}: Army exists and is organized for unit that we are trying to auto-create army for: {1} ({2}), {3}",
                    S.Situation.NumRaids, army, b.Data, b.AIAgent.CombatUnit);
                #endif
                // we have to create a new army instead!
                var newArmy = Army.Create(b.Data.EffectiveFaction);
                newArmy.WithUnit(b.AIAgent.CombatUnit);
                army.Absorb(newArmy);
            } else {
                army.WithUnit(b.AIAgent.CombatUnit);
            }
        }

        private Army ArmyFor(FactionDef faction) {
            foreach (var a in Armies) {
                // friendly fights created separately and they will end when real fight begins
                if ((a.Flags & CombatFlags.Friendly) == CombatFlags.Friendly) { continue; }
                if (a.Faction == faction) {
                    return a;
                }
            }
            var army = Army.Create(faction);
            Armies.Add(army);
            return army;
        }

        private void OnLoadSave(GameState state) {
            state.Clock.OnTick.AddListener(OnTick);
            state.Query.ColonyCombatStrength.SetResponder(UpdateCombatStrength);
            Ready.When(WhenH.BeingsLoaded, RestoreData);
        }


        private CombatSysData data;

        private void OnTick(long ticks) {
            var armyCount = Armies.Count;

            if (ticks % Consts.TicksPerHour == 37) {
                UpdateCombatStrength();
            }
            // where the fighters stand now, for this tick's steps and path searches
            Bodies.Rebuild(S, Armies, IsHostileCombatInProgress);
            if (ticks % 10 == 3) {
                // a gathering raid beaten, disbanded or gone before it attacked
                gatheringNotice.Update(S, Armies, arrived: false);
            }
            if (armyCount == 0) { return; }

            var flagsBefore = flags;

            if (ticks % 10 == 7) {
                CheckBoardingCallout();
            }

            if (ticks % 10 == 3) {
                int friendlyArmiesCount = 0;
                bool mustDisbandFriendly = false;
                int hostileArmiesCount = 0;
                for (int i = Armies.Count - 1; i >= 0; i--) {
                    Army army = Armies[i];
                    if ((army.Flags & CombatFlags.Friendly) == CombatFlags.Friendly)  {
                        friendlyArmiesCount++;
                        long maxFriendlyTimeoutTicks = (long) (Tunables.FriendlyFightMaxDurationHours * Consts.TicksPerHour);
                        if (ticks - army.FormedTick > maxFriendlyTimeoutTicks) {
                            D.Warn("Disbanding friendly fight after {0}",
                                Units.TicksToTime(ticks - army.FormedTick));
                            mustDisbandFriendly = true;
                        }
                    } else if (!army.IsStaging) {
                        hostileArmiesCount++;
                    }
                }
                if (mustDisbandFriendly || hostileArmiesCount > 0) {
                    if (friendlyArmiesCount > 0) {
                        // disband all friendly fights because real shit is happening
                        for (int i = Armies.Count - 1; i >= 0; i--) {
                            Army army = Armies[i];
                            if ((army.Flags & CombatFlags.Friendly) == CombatFlags.Friendly) {
                                #if DEBUG_COMBAT
                                D.Warn("Disbanding friendly army: real combat started");
                                #endif
                                army.Disband();
                                Armies.RemoveAt(i);
                            }
                        }
                        SetCombatFlags(flags & ~CombatFlags.Friendly);
                        if (hostileArmiesCount > 0) {
                            RemoveFightEvent();
                        }
                    }
                    if (hostileArmiesCount > 0) {
                        #if !PRODUCTION_BUILD
                        if ((flags & CombatFlags.Hostile) == CombatFlags.None) {
                            D.Err("Armies exist without hostile flag!");
                        }
                        #endif
                        foreach (var b in S.Beings.Alive) {
                            EnsureInCombat(b);
                        }
                    }
                }
            }
            int hostileArmiesLeft = 0;
            int crawlerArmies = 0;
            int restraintArmies = 0;
            int armiesLeft = 0;
            int friendlyFightsLeft = 0;
            int stagingArmiesLeft = 0;
            for (int i = Armies.Count - 1; i >= 0; i--) {
                Army army = Armies[i];
                army.Tick(S, ticks);
                if (!army.IsDefeated && !army.IsDisbanded) {
                    if (army.IsStaging) {
                        // waiting, not fighting: it neither keeps a fight going nor ends one
                        stagingArmiesLeft++;
                        continue;
                    }
                    armiesLeft++;
                    if ((army.Flags & CombatFlags.Friendly) == CombatFlags.Friendly) {
                        friendlyFightsLeft++;
                    } else if (army.IsHostileTowards(FactionDef.Player)) {
                        hostileArmiesLeft++;
                    }
                    if (army.Faction.IdH == FactionIdH.Crawler) {
                        crawlerArmies++;
                    } else if (army.Faction.IdH == FactionIdH.AntiPlayerTemp) {
                        restraintArmies++;
                    }
                }
            }
            bool disband = false;
            if (armiesLeft == 0 && stagingArmiesLeft > 0 && flags == CombatFlags.None) {
                // only gathering raids, and no fight to end
            } else if (hostileArmiesLeft == 0 ||
                    (armiesLeft == 1 && S.Situation.ShipComputers == 0)) {
                if (friendlyFightsLeft > 1) {
                    // not disbanding, two non-hostile armies fighting
                } else {
                    #if DEBUG_COMBAT
                    D.Warn("Disband 1");
                    #endif
                    disband = true;
                }
            } else if (hostileArmiesLeft == armiesLeft
                    && hostileArmiesLeft == crawlerArmies + restraintArmies) {
                // Nobody on our side is standing, and neither crawlers nor a being we
                // tried to restrain go for the ship computer, so nothing is at stake any
                // more. Ending it hands the restrained being its faction back.
                #if DEBUG_COMBAT
                D.Warn("Disband 2");
                #endif
                disband = true;
            } else if (ticks % 10 == 3 && IsHostileCombatInProgress
                    && alerts.ShouldEndQuietly(Armies, ticks, out var why)) {
                // The enemies cannot carry on (CombatAlerts). They stay where they are.
                D.Warn("Hostile combat ends quietly ({0}), no contact for {1}", why,
                    Units.TicksToTime(ticks - alerts.LastContactTick));
                disband = true;
            }
            if (disband) {
                // a gathering raid stays: it attacks later, in a fight of its own
                for (int i = Armies.Count - 1; i >= 0; i--) {
                    if (Armies[i].IsStaging && !Armies[i].IsDefeated) { continue; }
                    Armies[i].Disband();
                    Armies.RemoveAt(i);
                }
                SetCombatFlags(CombatFlags.None);
                alerts.End();
                // a gathering raid that was beaten before it attacked started no fight to end
                if (flagsBefore != CombatFlags.None || combatEvent.IsValid) {
                    RemoveCombatEvent(flagsBefore);
                }
            } else if (IsHostileCombatInProgress) {
                UpdateAlerts(ticks);
            }
        }

        // Raises what the hooks and checks of CombatAlerts asked for, after the armies ticked:
        // never from inside a unit's attack or a story event's call
        private void UpdateAlerts(long ticks) {
            if (alerts.TakeContact(out var contactPos)) {
                if (alerts.Stage != HostileAlert.InCombat) {
                    RaiseCombatAlert(contactPos);
                }
                return;
            }
            if (alerts.Stage != HostileAlert.Quiet || !alerts.TakeRaidCheck(ticks)) { return; }
            var enemy = alerts.FirstAdvancingEnemy(Armies, ticks);
            if (enemy != null) {
                RaiseRaidAlert(enemy.PosIdx);
            }
        }

        private float UpdateCombatStrength() {
            float newStr = 0f;
            float newThreatWeight = 0f;
            var beings = S.Query.AllBeingData.Ask();
            using (D.Timer("UpdateCombatStrength")) {
                foreach (var b in beings.Values) {
                    if (b.EffectiveFaction.IsPlayer) {
                        newStr += CombatRaid.StrengthOf(b);
                        newThreatWeight += RaidThreatWeight.Of(b);
                    }
                }
            }

            #if DEBUG_STRENGTH
            D.Warn("Updating CombatStrength: {0}",
                S.Situation.CombatStrength, newStr);
            #endif
            S.Situation.CombatStrength = newStr;
            S.Situation.ThreatWeight = newThreatWeight;
            return newStr;
        }

        // A fistfight among our own, shown as it starts
        private void ShowFightEvent() {
            if (!combatEvent.IsValid || combatEventHostile) {
                if (combatEvent.IsValid) {
                    Notification.Remove(ref combatEvent);
                }
                combatEventHostile = false;
                combatEventIsCombat = false;
                combatEventData = Notification.Build(IconId.CFist, T.VerbFight,
                    0, NotificationType.Warning)
                    .WithOnClick(SelectEnemiesOrFriendlyFighters)
                    .Sticky()
                    .Emergency(false);
                combatEvent = combatEventData.Show();
                new CmdRestrictClock(ClockSpeed.Normal)
                    .Execute(S);
                S.Clock.DangerPause();
            }
            S.Sig.CombatChanged.Send(true, CombatFlags.Friendly);
        }

        // The CrawlerCombatAlert pref turns off every alert of a fight with crawlers alone
        private bool HidesHostileAlerts() {
            if (S.Prefs.GetBool(Pref.CrawlerCombatAlert, PrefH.CrawlerCombatAlert, true)) {
                return false;
            }
            foreach (var army in Armies) {
                if (army.Faction.IdH == FactionIdH.Player
                        || army.Faction.IdH == FactionIdH.Crawler) {
                    continue;
                }
                return false;
            }
            return true;
        }

        // The raid alert: an enemy that can reach us is on its way, and the cue for arming up.
        // For most raids that is as they arrive; Combat in progress waits for contact.
        private void RaiseRaidAlert(int enemyPos) {
            alerts.Stage = HostileAlert.Raid;
            if (HidesHostileAlerts()) { return; }
            PlayRaidSound();
            ShowHostileEvent(false, enemyPos, true);
            ArmUpNudge.Send(S);
        }

        // First contact: Combat in progress. Straight from a quiet fight (a predator on a
        // colonist, an order) it also brings what the raid alert would have.
        private void RaiseCombatAlert(int enemyPos) {
            var wasQuiet = alerts.Stage == HostileAlert.Quiet;
            alerts.Stage = HostileAlert.InCombat;
            if (HidesHostileAlerts()) { return; }
            if (wasQuiet) {
                PlayRaidSound();
            }
            ShowHostileEvent(true, enemyPos, wasQuiet);
            if (wasQuiet) {
                ArmUpNudge.Send(S);
            }
        }

        // A fight saved with an alert up gets it back. The beings keep the goals they saved,
        // so nobody is sent to arm up again.
        private void RestoreHostileAlert() {
            if (alerts.Stage == HostileAlert.Quiet || HidesHostileAlerts()) { return; }
            PlayRaidSound();
            ShowHostileEvent(alerts.Stage == HostileAlert.InCombat, Pos.Invalid, true);
        }

        private void ShowHostileEvent(bool inCombat, int enemyPos, bool callout) {
            // D.Warn("Creating combat event");
            // NOTE: NumRaids is incremented in CombatRaid.CreateArmy (raid-only, no friendly fights).
            if (combatEvent.IsValid) {
                Notification.Remove(ref combatEvent);
            }
            combatEventHostile = true;
            combatEventIsCombat = inCombat;
            // The raid alert and the callout must describe where the fight actually starts:
            // raiders already standing on the ship (one who came to, mutineers, boarders) are
            // aboard, raiders at the map edge or on a derelict are inbound (STA-1770)
            var aboard = (!inCombat || callout) && AnyHostileAboard();
            var raidTitle = aboard ? T.HostilesAboard : T.EvRaidMapEdgeTitle;
            var raidLine = aboard ? T.HostilesAboard : T.EvRaidMapEdgeDesc;
            combatEventData = Notification.Build(IconId.CCombat,
                    inCombat ? T.CombatOngoing : raidTitle, 0,
                    inCombat ? NotificationType.Critical : NotificationType.Alert)
                .WithOnClick(SelectEnemiesOrFriendlyFighters)
                .Sticky()
                .Emergency(false);
            if (callout) {
                boardingAnnounced = aboard;
                combatEventData.WithVoice(boardingAnnounced
                    ? VoiceLine.Boarding : VoiceLine.Raid);
            }
            combatEvent = combatEventData.Show();
            var title = Texts.Red(inCombat ? T.CombatOngoing : raidLine);
            var pos = enemyPos != Pos.Invalid ? enemyPos : FirstEnemyPos();
            if (pos != Pos.Invalid) {
                title = Texts.LinkTo(title, pos);
            }
            title = Texts.Red(title);
            S.Sys.Log.AddLine(title);
            S.Sig.ShowFanfare.Send(title);
            new CmdRestrictClock(ClockSpeed.Normal)
                .Execute(S);
            #if DEBUG_COMBAT
            D.Warn("Combat starting!");
            #endif
            S.Clock.DangerPause();
            if (inCombat) {
                if (!G.IsDemoMode) {
                    Sounds.PlayMusic(SoundtrackMood.Combat, true);
                }
                S.Sig.CombatChanged.Send(true, CombatFlags.Hostile);
            }
        }

        private int FirstEnemyPos() {
            foreach (var army in Armies) {
                if (army.Units.Count > 0 && army.IsHostileTowards(FactionDef.Player)) {
                    return army.Units[0].PosIdx;
                }
            }
            return Pos.Invalid;
        }

        private void SelectEnemiesOrFriendlyFighters() {
            var beings = Caches.BeingList;
            beings.Clear();
            foreach (var army in Armies) {
                bool include = combatEventHostile
                    ? army.IsHostileTowards(FactionDef.Player)
                    : (army.Flags & CombatFlags.Friendly) == CombatFlags.Friendly;
                if (!include) { continue; }
                foreach (var unit in army.Units) {
                    beings.Add(unit.Agent.Being);
                }
            }
            S.Query.SelectBeings.Ask(beings);
        }

        private void RemoveCombatEvent(CombatFlags flags) {
            S.Sig.CombatChanged.Send(false, flags);
            boardingAnnounced = false;
            if (combatEvent.IsValid) {
                Notification.Remove(ref combatEvent);
                combatEventData = null;
                // the raid alert alone never started the combat music
                if (!G.IsDemoMode && (combatEventIsCombat || !combatEventHostile)) {
                    Sounds.PlayMusic(SoundtrackMood.Calm, true);
                }
                S.Sys.Log.AddLine(T.CombatEnded, Texts.TMPColorGreen);
            }
            combatEventIsCombat = false;
            #if DEBUG_COMBAT
            D.Warn("Combat ending!");
            #endif
        }

        public override void Unload() {
        }

        public string GetName() {
            return Id;
        }

        private void RegisterEncounterWith(FactionDef raidFaction) {
            if (raidFaction.IsInternal || raidFaction.IdH == FactionIdH.AntiPlayer) {
                return;
            }
            The.AchievementTracker.Unlock(AchievementFirstContact.AchievementId);
            S.Query.ChangeFactionAttitude.Ask(
                raidFaction, FactionDef.Player, -0.01f);
        }

        private void PlayRaidSound() {
            if (!G.IsDemoMode) {
                UISounds.Play("FX/RaidAlert");
                The.SysSig.GamepadRumble.Send(0.3f, 0.3f, 0.2f);
            }
        }

        // True if a living, hostile-to-player combat unit is within radius of posIdx.
        // Used to suppress auto-repair task creation in active danger zones.
        public bool IsHostileNear(int posIdx, float radius) {
            if (Armies.Count == 0) { return false; }
            var radiusSqr = radius * radius;
            foreach (var a in Armies) {
                if (!a.IsHostileTowards(FactionDef.Player)) { continue; }
                foreach (var unit in a.Units) {
                    if (unit.State == CombatUnitState.Incapacitated) { continue; }
                    if (Pos.SqrDistance(posIdx, unit.PosIdx) <= radiusSqr) {
                        return true;
                    }
                }
            }
            return false;
        }

        // A unit is "aboard" when it stands on a floor inside the player's buildable map
        // margin. IsWithinMargin rejects derelicts, map-edge spawns and OOB positions and
        // must be evaluated first: IsFloorAt does no bounds check. An exposed hull deck
        // still has a floor, so a raider on an unpressurised outer deck counts as aboard.
        private static bool IsAboardShip(int posIdx) {
            return EntityUtils.IsWithinMargin(posIdx, false, false)
                && EntityUtils.IsFloorAt(posIdx);
        }

        private bool AnyHostileAboard() {
            if (Armies.Count == 0) { return false; }
            foreach (var a in Armies) {
                if (!a.IsHostileTowards(FactionDef.Player)) { continue; }
                foreach (var unit in a.Units) {
                    if (unit.State == CombatUnitState.Incapacitated) { continue; }
                    if (IsAboardShip(unit.PosIdx)) { return true; }
                }
            }
            return false;
        }

        // One-shot follow-up callout: a raid that began outside announced "Raiders Inbound"
        // with its notification, so once the first hostile crosses onto the ship the
        // "Raiders Aboard" warning fires on its own. Gated on the live hostile notification
        // because a crawler raid suppressed by the CrawlerCombatAlert pref sets the hostile
        // flags without creating a notification, and must stay silent.
        private void CheckBoardingCallout() {
            if (!IsHostileCombatInProgress || boardingAnnounced
                    || !combatEvent.IsValid || !combatEventHostile) {
                return;
            }
            if (!AnyHostileAboard()) { return; }
            boardingAnnounced = true;
            VoiceAlerts.Say(VoiceLine.Boarding);
        }

        // The nearest enemy still standing, from every army hostile to this unit's side - what a
        // fleeing unit runs from when nobody targets it
        public CombatUnit ClosestStandingEnemy(CombatUnit unit) {
            var faction = unit.Army?.Faction ?? unit.Data.EffectiveFaction;
            var pos = unit.PosIdx;
            CombatUnit closest = null;
            float closestSqr = float.MaxValue;
            for (int i = 0; i < Armies.Count; i++) {
                var a = Armies[i];
                if (!a.IsHostileTowards(faction)) { continue; }
                for (int j = 0; j < a.Units.Count; j++) {
                    var enemy = a.Units[j];
                    if (enemy.State == CombatUnitState.Incapacitated) { continue; }
                    var sqr = Pos.SqrDistance(enemy.PosIdx, pos);
                    if (sqr < closestSqr) {
                        closestSqr = sqr;
                        closest = enemy;
                    }
                }
            }
            return closest;
        }

        // FirstEnemyMatching for a distance check, without a closure: cover searches ask this
        // for every candidate tile
        public CombatUnit FirstEnemyWithin(CombatUnit unit, Vector2 pos, float rangeSqr) {
            if (unit.Army == null) {
                D.Err("Trying to find an enemy near a unit without an army! {0}", unit);
                return null;
            }
            var unitArmy = unit.Army;
            for (int i = 0; i < Armies.Count; i++) {
                var a = Armies[i];
                if (!a.IsHostileTowards(unitArmy.Faction)) { continue; }
                for (int j = 0; j < a.Units.Count; j++) {
                    var enemy = a.Units[j];
                    if (Vectors.SqrDistance(pos, enemy.Position) < rangeSqr) {
                        return enemy;
                    }
                }
            }
            return null;
        }

        public CombatUnit FirstEnemyMatching(CombatUnit unit, Func<CombatUnit, bool> enemyCheck) {
            if (unit.Army == null) {
                D.Err("Trying to see if unit without an army has enemy matching a check! {0} / {1}", unit, enemyCheck);
                return null;
            }
            var unitArmy = unit.Army;
            foreach (var a in Armies) {
                if (a.IsHostileTowards(unitArmy.Faction)) {
                    foreach (var enemy in a.Units) {
                        if (enemyCheck(enemy)) {
                            return enemy;
                        }
                    }
                }
            }
            return null;
        }



        public void RemoveUnit(CombatUnit unit) {
            if (unit == null) { return; }
            // Army.AddUnit sets Army, so a unit in an army's list always knows it
            unit.Army?.RemoveUnit(unit, CombatUnitState.Unaware);
        }

        public void SaveSpecial(SystemsDataSpecial sd) {
            sd.Combat ??= new CombatSysData();
            sd.Combat.Flags = flags;
            sd.Combat.Armies = ArmyData.Serialize(Armies);
            sd.Combat.LastHackConversionTick = LastHackConversionTick;
            alerts.Save(sd.Combat);
        }

        public void LoadSpecial(SystemsDataSpecial sd) {
            data = sd.Combat;
            LastHackConversionTick = data?.LastHackConversionTick ?? 0L;
        }

        private void RestoreData() {
            if (data == null) { return; }
            flags = data.Flags;
            alerts.Load(data, G.Ticks);
            ArmyData.Deserialize(S, data.Armies, Armies);
            if (Armies.Count > 0 && !HasOnlyStagingArmies()) {
                if (HasOnlyStaleEnemyArmies()) {
                    // Saved with enemy armies whose every unit has since changed sides
                    // (e.g. hacked raiders). Army.Tick evicts them and ends the combat on
                    // the first tick - don't raise a raid alert and pause for that.
                } else if (Armies.Count > 1 || !Armies[0].Faction.IsPlayer) {
                    // we saved mid combat. A fistfight between colonists is two friendly
                    // armies, and must not come back as a raid alert
                    if (HasHostileArmy()) {
                        RestoreHostileAlert();
                    } else {
                        ShowFightEvent();
                    }
                } else if (Armies.Count == 1) {
                    D.Err("Saved combat with just 1 army (player)! Or failed to deserialize all armies! {0}",
                        D.Dump(data.Armies));
                }
            }
            gatheringNotice.Update(S, Armies, arrived: false);
            if (Armies.Count > 0 && flags == CombatFlags.None && !HasOnlyStagingArmies())  {
                D.ErrSoft("Armies, but no flags! {0}", D.Dump(Armies));
                flags |= Armies[0].Flags;
            }
            data = null;
        }

        // saved while a raid gathered and nothing else fought: no fight to restore
        private bool HasOnlyStagingArmies() {
            for (int i = 0; i < Armies.Count; i++) {
                if (!Armies[i].IsStaging) { return false; }
            }
            return true;
        }

        private bool HasHostileArmy() {
            for (int i = 0; i < Armies.Count; i++) {
                if ((Armies[i].Flags & CombatFlags.Friendly) != CombatFlags.Friendly) {
                    return true;
                }
            }
            return false;
        }

        private bool HasOnlyStaleEnemyArmies() {
            bool hasEnemyArmy = false;
            for (int i = 0; i < Armies.Count; i++) {
                var a = Armies[i];
                if (a.Faction.IsPlayer) { continue; }
                hasEnemyArmy = true;
                if (a.HasUnitOfOwnFaction()) { return false; }
            }
            return hasEnemyArmy;
        }
        public static void StartBreachCapsuleRaid(GameState s, FactionDef fac) {
            if (!A.IsCombatOn) {
                return;
            }
            D.Warn("Will start breach capsule raid from faction: {0}", fac);
            var evt = StoryEventDef.Get("Raid/BreachCapsuleRaid");
            var data = StoryEventData.CreateFor(evt);
            data.Faction = fac;
            // Started outside the story picker (which seeds what it picks), and the event
            // generates its raiders from data.SeededRng, so seed it here.
            data.Seed = s.Rng.Int;
            if (!s.Query.StartStoryEvent.Ask(data)) {
                D.Err("Failed to trigger faction raid! {0}", fac);
            }
        }
    }
}
