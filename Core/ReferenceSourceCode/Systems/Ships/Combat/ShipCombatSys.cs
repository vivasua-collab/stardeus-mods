using System;
using System.Collections.Generic;
using System.Linq;
using Game.Achievements;
using Game.Audio;
using Game.CodeGen;
using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Data.Space;
using Game.Input;
using Game.Rendering;
using Game.Story.Events;
using Game.Systems.Firex;
using Game.Systems.Ships;
using Game.UI;
using Game.Utils;
using KL.Pool;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Systems.Notify;
using Game.Systems.UIUX;
using Game.Systems.Voice;
using Game.Systems.UIUX.Codex.Pages;

namespace Game.Systems.Ships {
    public sealed class ShipCombatSys : GameSystem, ISaveableSpecial {
        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new ShipCombatSys());
        }
        private const string SysId = "ShipCombat";
        public override string Id => SysId;

        // private ShipCombatUI ui;
        // public for tutorial
        // public ShipCombatUI UI => ui;
        private ShipCombatAI ai;
        /// <summary>
        /// The player's side: the player's ship plus any NPC ships fighting for it
        /// </summary>
        public readonly List<NPCShip> Allies = new();
        /// <summary>
        /// List of active player enemies in the game map
        /// </summary>
        public readonly List<NPCShip> Enemies = new();
        public long CombatStartTick;
        /// <summary>
        /// Whether the current (or most recently ended) combat was started by a player
        /// action (SOActionAttackShip, ShipHailAction_Extort, or JoinNPCBattle), as
        /// opposed to an NPC's own decision to attack. Gates whether the enemy side may
        /// reinforce at all - random NPC aggression must never escalate a fight the
        /// player didn't pick. Same lifecycle as CombatStartTick: set only when a fresh
        /// fight starts (Enemies.Count == 0), left stale (harmless) between fights,
        /// persisted the same way. See IsShipCombatPlayerInitiated.
        /// </summary>
        public bool PlayerInitiatedCombat;
        private ShipCombatSysData data;
        private NPCShip playerShip;
        private NotificationHandle combatEvent;
        public readonly List<NPCShipBattle> NPCShipBattles = new();
        private readonly Dictionary<SpaceObject, long> soRiggedForDestruction = new();
        private bool needsClassPointValidation;
        private bool needsFluxCapValidation;
        private bool needsFluxVentValidation;
        // Not saved: only used to spot the tick a space threat ends
        private bool hadSpaceThreats;
        /// <summary>
        /// Whether the player's ship can be in a restored combat at all. Decided once in
        /// restore stage A and reused by stage B, so the two stages cannot disagree.
        /// </summary>
        private bool restoredCombatIsValid;
        // Reused across calls to avoid a per-tick allocation; GetShipsAgainstPlayer's
        // contract is that the caller reads it before asking again.
        private readonly List<NPCShip> shipsAgainstPlayer = new();

        public override void Unload() {
        }

        protected override void OnInitialize() {
            S.Sig.AfterLoadState.AddListener(OnLoad);
            // ui = new ShipCombatUI(S, this);
            ai = new ShipCombatAI();
        }

        private void OnLoad(GameState state) {
            state.Query.StartShipCombatWith.SetResponder(StartCombatWith);
            state.Query.StartNPCShipCombatBetween.SetResponder(StartCombatBetween);
            state.Query.JoinShipCombatAsAlly.SetResponder(JoinCombatAsAlly);
            state.Query.JoinShipCombatAgainstPlayer.SetResponder(JoinCombatAgainstPlayer);
            state.Query.GetShipCombatEnemySlots.SetResponder(EnemySlotsLeft);
            state.Query.GetShipsAgainstPlayer.SetResponder(GetShipsAgainstPlayer);
            state.Query.IsShipCombatPlayerInitiated.SetResponder(IsCombatPlayerInitiated);
            state.Query.GetNPCShipBattleFor.SetResponder(GetNPCBattleFor);
            state.Query.CanJoinNPCShipBattle.SetResponder(CanJoinNPCBattle);
            state.Query.JoinNPCShipBattle.SetResponder(JoinNPCBattle);
            state.Query.GetEnemyShips.SetResponder(GetEnemies);
            state.Query.GetAllyShips.SetResponder(GetAllies);
            state.Query.RestoreShipCombatData.SetResponder(RestoreCombatData);
            state.Query.EndShipCombatWith.SetResponder(EndCombatWith);
            state.Sig.ShipViewVisibilityChanged.AddListener(OnShipViewVisibilityChange);
            state.Sig.EnteredHyperspace.AddListener(OnPlayerEnterHyperspace);
            state.Sig.ShipTurnedDerelict.AddListener(OnShipTurnedDerelict);
            state.Clock.OnTick.AddListener(OnTick);
            state.Sig.EntityAdded.AddListener(OnEntityAdded);
            state.Sig.EntityRemoved.AddListener(OnEntityRemoved);
            state.Sig.ConstructionCompleted.AddListener(OnConstructionCompleted);
            state.Sig.PlayerShipClassChanged.AddListener(OnShipClassChange);
            state.Query.RigSpaceObjectForDestruction.SetResponder(RigSpaceObjectForDestruction);
            Ready.When(WhenH.ShipDataRestored, OnShipDataRestored);
        }

        // A derelict is never a combatant. The hack that walks the crew off and the sector
        // nuke derelict ships outside EndCombatWith, and an enemy left on the roster kept
        // fighting as a derelict (STA-1907). EndCombatWith takes ships off the roster before
        // derelicting them, so this is a no-op for its own kills.
        private void OnShipTurnedDerelict(NPCShip ship) {
            if (ship.IsPlayer || !IsPlayerCombatant(ship)) { return; }
            EndCombatWith(ship, ShipStateFlags.None);
        }

        private bool RigSpaceObjectForDestruction(SpaceObject so, int timeout) {
            D.Warn("Rigging space object for destruction: {0} with timeout: {1}", so, Units.TicksToTime(timeout));
            soRiggedForDestruction[so] = G.Ticks + timeout;
            S.Sig.ShowFanfare.Send(T.SectorNukeTimer_V1.F(Units.TicksToTime(timeout, shortVersion: true)));
            return true;
        }

        private void OnShipClassChange() {
            ValidatePlayerClassPoints(false);
            // ui.Rebuild(false);
        }

        private bool StartCombatBetween(NPCShip attacker, NPCShip defender) {
            if (attacker.Faction == defender.Faction) {
                D.Err("Cannot start combat between two ships of same faction: {0} -> {1} | {2}",
                    attacker, defender, attacker.Faction);
                return false;
            }
            if (attacker.IsPlayer || defender.IsPlayer) {
                D.Err("Cannot start combat between player and NPC: {0} -> {1}",
                    attacker, defender);
                return false;
            }
            if (attacker.IsInCombat) {
                if (!defender.IsInCombat) {
                    D.Err("Attacker already in combat, cannot start combat between {0} and {1}",
                        attacker, defender);
                }
                return false;
            }
            if (!attacker.IsActive || !defender.IsActive) {
                D.Err("Either attacker or defender is not active: {0} and {1}",
                    attacker, defender);
                return false;
            }
            if (Enemies.Contains(defender)) {
                D.Err("Defender is already fighting with player. cannot attack: {0} -> {1}",
                    attacker, defender);
                // let's fuck the player up instead
                StartCombatWith(attacker, false);
                return false;
            }
            if (defender.IsInCombat) {
                D.Err("Defender is already in combat, cannot start combat between {0} and {1}",
                    attacker, defender);
                return false;
            }
            if (IsInAnyNPCBattle(attacker) || IsInAnyNPCBattle(defender)) {
                D.Err("Either attacker or defender is already in an NPC ship battle: {0} and {1}",
                    attacker, defender);
                return false;
            }
            // D.Warn("Starting NPC ship combat between {0} and {1}", attacker, defender);
            // D.Warn("Attacker CombatState: {0}", attacker.CombatState);
            // D.Warn("Defender CombatState: {0}", defender.CombatState);

            var powerDiff = attacker.StrengthDifferenceAgainst(defender);
            var absPowerDiff = Math.Abs(powerDiff);
            var ticksFrom = Mathf.Lerp(Consts.TicksPer24Hours, Consts.TicksPer12Hours, absPowerDiff);
            var ticksTo = Mathf.Lerp(Consts.TicksPer2Days, Consts.TicksPer18Hours, absPowerDiff);
            var maxDuration = Rng.URange(ticksFrom, ticksTo);

            var battle = new NPCShipBattle {
                StartTick = G.Ticks,
                EndTick = G.Ticks + (int) maxDuration,
                Attacker = attacker,
                Defender = defender,
                PowerDiff = powerDiff,
                Rng = S.Rng.Fork(),
            };
            attacker.State |= ShipStateFlags.InCombat;
            defender.State |= ShipStateFlags.InCombat;
            attacker.CombatState.LastAttacker = null;
            defender.CombatState.LastAttacker = null;
            attacker.SetWantsHyperspace(false);
            defender.SetWantsHyperspace(false);
            NPCShipBehavior_Sector_Follow.AssignTo(attacker, defender.SO);
            NPCShipBehavior_Sector_Follow.AssignTo(defender, attacker.SO);
            NPCShipBattles.Add(battle);
            return true;
        }

        private void OnConstructionCompleted(Tile tile) {
            if (tile.HasComponent<IShipDeviceComp>()) {
                needsClassPointValidation = true;
                if (tile.HasComponent<ShipCapacitorComp>() && tile.Reinstallable?.IsMidRelocation == true) {
                    needsFluxCapValidation = true;
                }
                if (tile.HasComponent<ShipVentComp>() && tile.Reinstallable?.IsMidRelocation == true) {
                    needsFluxVentValidation = true;
                }
            }
        }

        private void OnEntityRemoved(Entity entity) {
            if (entity.HasComponent<IShipDeviceComp>()) {
                if (EntityUtils.IsWithinMargin(entity.PosIdx, false, false)) {
                    needsClassPointValidation = true;
                    if (entity.HasComponent<ShipWeaponComp>() && playerShip.IsInCombat) {
                        ShipCombatAI.RecalculateBattleStationAssignments(playerShip.CombatState);
                    }
                }
            }
        }

        private void OnEntityAdded(Entity entity) {
            if (entity.HasComponent<IShipDeviceComp>()) {
                if (entity.TryGetComponent<ReinstallableComp>(out var reinstallable)) {
                    if (reinstallable.IsPending || reinstallable.IsMidRelocation) {
                        return;
                    }
                }
                if (EntityUtils.IsWithinMargin(entity.PosIdx, false, false)) {
                    if (entity is Tile tile) {
                        if (!tile.IsConstructed) {
                            ValidatePlayerClassPoints(true);
                            if (entity.HasComponent<ShipCapacitorComp>()) {
                                ValidateFluxCapacity();
                            }
                            if (entity.HasComponent<ShipVentComp>()) {
                                ValidateFluxVents();
                            }
                        } else {
                            needsClassPointValidation = true;
                        }
                    } else {
                        D.Err("Entity is not a tile: {0}", entity);
                    }
                }
            }
        }

        private NotificationHandle cpWarning;

        private void ValidatePlayerClassPoints(bool future) {
            if (!Ready.Gen) { return; }
            // prevent early game warning
            if (G.Ticks < Consts.TicksPerHour) { return; }
            // prevent warning when devices are auto-rebuilt mid combat
            if (future && The.Toolbox.ActiveTool != The.Toolbox.BuildTool
                    && Enemies.Count > 0) {
                return;
            }
            S.Situation.ShipClassPoints = playerShip.CombatState.CurrentClassPoints;
            CalculateCP(out var currentCP, out var nonConstructedCP, out var maxCP);
            if (currentCP > maxCP) {
                if (!cpWarning.IsValid) {
                    cpWarning = Notification.Build(IconId.CSpaceShip,
                        T.ShipViolatesClassLimit,
                        0, NotificationType.Warning)
                        .WithOnClick(() => {
                            S.UIQuery.ToggleShipConfig.Ask(!UIShowing.ShipConfig);
                        })
                        .WithTooltipFunc(() => {
                            CalculateCP(out var currentCP,
                                out var nonConstructedCP,
                                out var maxCP);
                            return ShipCombatWidgetUtils.ShipClassOverLimitTip(
                                playerShip, currentCP, maxCP);
                            })
                        .Sticky()
                        .Show();
                    // only on crossing the limit, the sticky notification covers the rest
                    S.Sig.ShowFanfare.Send(Texts.Red(T.ShipViolatesClassLimit));
                }
            } else {
                if (cpWarning.IsValid) {
                    S.Sig.ShowFanfare.Send($"{T.NounShipClassLimit}: {Texts.GreenOK}");
                    Notification.Remove(ref cpWarning);
                }
                if (future && currentCP < maxCP && currentCP + nonConstructedCP > maxCP) {
                    D.Warn("Future class points violation!");
                    if (CanShowLimitPopup(Pref.ShowClassPointsWarning, PrefH.ShowClassPointsWarning)) {
                        var sb = Caches.StringBuilder3;
                        sb.Clear();
                        ShipCombatWidgetUtils.AddClassPointsDesc(playerShip, currentCP,
                            nonConstructedCP, maxCP, sb);
                        ShowLimitPopup(Pref.ShowClassPointsWarning,
                            T.NounShipClassLimit,
                            T.ShipClassLimitFutureViolationTip,
                            sb.ToString());
                    }

                    S.UIQuery.ShowTutorialPopup.Ask(ClassPointsPage.PageId, 3f);
                }
            }
        }

        private void ValidateFluxCapacity() {
            if (!Ready.Gen) { return; }
            if (G.Ticks < Consts.TicksPerHour) { return; }
            var maxCap = playerShip.Class.MaxFluxCapacity;
            if (maxCap <= 0) { return; }
            CalculateFluxCap(out var currentBonus, out var pendingBonus);
            var baseCapacity = Tunables.ShipBaseFlux + playerShip.Class.FluxBonus;
            if (baseCapacity + currentBonus + pendingBonus > maxCap
                    && CanShowLimitPopup(Pref.ShowFluxCapacityWarning, PrefH.ShowFluxCapacityWarning)) {
                ShowLimitPopup(Pref.ShowFluxCapacityWarning, T.MaxFluxCapacity,
                    T.TooManyFluxCapacitors_V3.F(Texts.LinkToCodex(DefIdsH.ObjectsDevicesFluxCapacitor, T.NounFluxCapacitors),
                        playerShip.Class.CodexLink, maxCap), null);
            }
        }

        // Class limit popups are advice, so they follow Show Hints as well as their own
        // per save Don't Show Again
        private bool CanShowLimitPopup(string pref, int prefH) {
            return A.ShowHints && S.Prefs.GetBool(pref, prefH, true);
        }

        // Unique, because every blueprint placed in one drag validates again
        private void ShowLimitPopup(string pref, string title, string subtitle, string text) {
            UISounds.PlayActionDenied();
            WPopup.Create(false, IconId.CWarning, title, subtitle, text)
                .WithAction(T.Ok, null)
                .WithAction2(T.DoNotShowAgain, () => {
                    S.Prefs.SetBool(pref, false);
                })
                .Unique(pref)
                .Spawn();
        }

        private void CalculateFluxCap(out int currentBonus, out int pendingBonus) {
            currentBonus = 0;
            pendingBonus = 0;
            var cache = Caches.ShipDeviceComps;
            var found = S.Components.FindAllByInterface(cache);
            for (int i = 0; i < found; i++) {
                if (cache[i] is not ShipCapacitorComp comp) { continue; }
                if (!EntityUtils.IsWithinMargin(comp.Entity.PosIdx, false, false)) { continue; }
                if (comp.Entity is Tile tile) {
                    if (tile.Reinstallable.IsMidRelocation || tile.Reinstallable.IsPending) { continue; }
                }
                if (comp.IsConstructed) {
                    currentBonus += comp.FluxCapacity;
                } else {
                    pendingBonus += comp.FluxCapacity;
                }
            }
        }

        private void ValidateFluxVents() {
            if (!Ready.Gen) { return; }
            if (G.Ticks < Consts.TicksPerHour) { return; }
            var maxVents = playerShip.Class.MaxFluxVents;
            if (maxVents <= 0) { return; }
            CalculateFluxVentCount(out var currentCount, out var pendingCount);
            if (currentCount + pendingCount > maxVents
                    && CanShowLimitPopup(Pref.ShowFluxVentsWarning, PrefH.ShowFluxVentsWarning)) {
                ShowLimitPopup(Pref.ShowFluxVentsWarning, T.NounFluxVents,
                    T.TooManyFluxVents_V3.F(Texts.LinkToCodex(DefIdsH.ObjectsDevicesFluxVent, T.NounFluxVents),
                        playerShip.Class.CodexLink, maxVents), null);
            }
        }

        private void CalculateFluxVentCount(out int currentCount, out int pendingCount) {
            currentCount = 0;
            pendingCount = 0;
            var cache = Caches.ShipDeviceComps;
            var found = S.Components.FindAllByInterface(cache);
            for (int i = 0; i < found; i++) {
                if (cache[i] is not ShipVentComp comp) { continue; }
                if (!EntityUtils.IsWithinMargin(comp.Entity.PosIdx, false, false)) { continue; }
                if (comp.Entity is Tile tile) {
                    if (tile.Reinstallable.IsMidRelocation || tile.Reinstallable.IsPending) { continue; }
                }
                if (comp.IsConstructed) {
                    currentCount++;
                } else {
                    pendingCount++;
                }
            }
        }

        private void CalculateCP(out int currentCP, out int nonConstructedCP, out int maxCP) {
            var cache = Caches.ShipDeviceComps;
            var found = S.Components.FindAllByInterface(cache);
            currentCP = 0;
            nonConstructedCP = 0;
            maxCP = S.Universe.Player.Ship.Class.MaxClassPoints;
            for (int i = 0; i < found; i++) {
                var comp = cache[i];
                if (!EntityUtils.IsWithinMargin(comp.Entity.PosIdx, false, false)) {
                    continue;
                }
                if (comp.DeviceDef == null) {
                    D.Err("Device def not linked for comp: {0} on entity {1}", comp, comp.Entity);
                    continue;
                }
                if (comp.IsConstructed) {
                    currentCP += comp.DeviceDef.ClassPoints;
                } else if (IsPendingPlayerDevice(comp)) {
                    nonConstructedCP += comp.DeviceDef.ClassPoints;
                }
            }
        }

        // Shared with the Class Points UI tab so the listed unbuilt devices always
        // match what the future class points violation warning counts.
        public static bool IsPendingPlayerDevice(IShipDeviceComp comp) {
            if (comp.DeviceDef == null || comp.IsConstructed) { return false; }
            if (!EntityUtils.IsWithinMargin(comp.Entity.PosIdx, false, false)) { return false; }
            if (comp.Entity is Tile tile) {
                if (tile.Reinstallable.IsMidRelocation || tile.Reinstallable.IsPending) {
                    return false;
                }
            }
            return true;
        }

        private void OnPlayerEnterHyperspace(bool enter) {
            if (enter && Enemies.Count > 0) {
                for (int i = Enemies.Count - 1; i >= 0; i--) {
                    NPCShip e = Enemies[i];
                    EndCombatWith(e, ShipStateFlags.None);
                }
            }
        }

        private void OnShipDataRestored() {
            playerShip = S.Universe.Player.Ship;
            if (playerShip == null) {
                D.Err("ShipCombatSys restore has PlayerShip null!");
            }
            ShipCombatAI.RecalculateBattleStationAssignments(playerShip.CombatState);
            ValidatePlayerClassPoints(false);
        }

        private bool IsInAnyNPCBattle(NPCShip ship) {
            for (int i = NPCShipBattles.Count - 1; i >= 0; i--) {
                var battle = NPCShipBattles[i];
                if (battle.Attacker == ship || battle.Defender == ship) { return true; }
            }
            return false;
        }

        private bool IsPlayerCombatant(NPCShip ship) {
            return Enemies.Contains(ship) || Allies.Contains(ship);
        }

        // A ship is in the player's fight or in an NPC battle, never both: the end of the NPC
        // battle took the ship out of combat while it was still our enemy, and the fight could
        // never end (STA-1898). The player's fight takes over; the opponent goes back to its
        // business, and a friendly one can still join us through ScanForJoiners.
        private void LeaveNPCBattle(NPCShip ship) {
            if (!TryGetNPCBattleFor(ship, out var battle)) { return; }
            NPCShipBattles.Remove(battle);
            var opponent = battle.Attacker == ship ? battle.Defender : battle.Attacker;
            if (!IsPlayerCombatant(opponent)) {
                ReleaseFromCombat(opponent);
            }
        }

        private static void EndNPCBattleParticipation(NPCShip ship) {
            ship.State &= ~ShipStateFlags.InCombat;
            if (ship.IsActive) {
                ship.CombatState.LastAttacker = null;
                ship.ChangeBehavior(NPCShipBehaviorScope.Sector, false);
            }
        }

        /// <summary>
        /// Mirrors the cleanup TickNPCBattles does when a battle ends, for ships that
        /// are no longer fighting anyone.
        /// </summary>
        private void ReleaseFromCombat(NPCShip ship) {
            ship.State &= ~ShipStateFlags.InCombat;
            ship.CombatState.LastAttacker = null;
            ship.CombatState.FocusedEnemy = null;
            ship.Memory.SetFollowTarget(null);
            ship.Memory.ChaseTargetId = 0;
            if (ship.IsActive) {
                ship.ChangeBehavior(NPCShipBehaviorScope.Sector, false);
            }
        }

        private List<NPCShip> GetEnemies() {
            return Enemies;
        }

        private List<NPCShip> GetAllies() {
            return Allies;
        }

        private int NPCAllyCount {
            get {
                var count = 0;
                for (int i = Allies.Count - 1; i >= 0; i--) {
                    if (!Allies[i].IsPlayer) { count++; }
                }
                return count;
            }
        }

        /// <summary>
        /// Enemy with the most firepower it can actually use, ties broken by lowest Id
        /// </summary>
        private NPCShip StrongestEnemy() {
            NPCShip strongest = null;
            var strongestStr = -1;
            for (int i = 0; i < Enemies.Count; i++) {
                var enemy = Enemies[i];
                var str = enemy.CombatState.WeaponCombatStrength;
                if (str > strongestStr || (str == strongestStr && enemy.Id < strongest.Id)) {
                    strongestStr = str;
                    strongest = enemy;
                }
            }
            return strongest;
        }

        /// <summary>
        /// Makes an NPC ship fight on the player's side. Membership in Allies is what
        /// makes a ship an ally, so it is set before anything else can observe the ship.
        /// </summary>
        private bool JoinCombatAsAlly(NPCShip ship) {
            if (Enemies.Count == 0) {
                D.Err("Cannot join as ally, player is not fighting anyone: {0}", ship);
                return false;
            }
            if (ship.IsPlayer) {
                D.Err("Player's ship cannot join combat as an ally!");
                return false;
            }
            if (!ship.IsActive || ship.IsDerelict) {
                D.Err("Cannot join as ally, ship is not active: {0}", ship);
                return false;
            }
            if (ship.IsInCombat || Allies.Contains(ship) || Enemies.Contains(ship)
                    || IsInAnyNPCBattle(ship)) {
                D.Err("Cannot join as ally, ship is already fighting: {0}", ship);
                return false;
            }
            if (ship.IsInHyperspace) {
                D.Err("Cannot join as ally from hyperspace: {0}", ship);
                return false;
            }
            if (ship.Sector != A.PlayerSector.Sector) {
                D.Err("Cannot join as ally from another sector: {0}", ship);
                return false;
            }
            if (NPCAllyCount >= Tunables.ShipCombatMaxNPCAllies) {
                D.Warn("Not joining as ally, ally limit is reached: {0}", ship);
                return false;
            }
            if (Allies.Count == 0) {
                Allies.Add(playerShip);
            }
            Allies.Add(ship);

            PrepareForCombat(ship, StrongestEnemy(), isPlayerSide: true);

            S.Query.SpawnShipView.Ask(ship);
            ai.ResolveFocus(ship.CombatState, Enemies, FocusResolveReason.RosterChange);
            BroadcastEnemyRosterChange();

            // The ally follows the player, not an enemy: an enemy follow target goes
            // stale the moment it dies or leaves, and what the ally shoots at is a
            // separate, view level decision.
            NPCShipBehavior_Sector_Follow.AssignTo(ship, A.Player);

            AnnounceAllyJoined(ship);
            S.Sig.ShipCombatAllyChanged.Send(ship, true);
            return true;
        }

        /// <summary>
        /// Everything an NPC ship needs internally before it can fight. Emits nothing and
        /// spawns nothing, so a caller can finish setting up every participant before
        /// anything observable happens.
        /// </summary>
        private static void PrepareForCombat(NPCShip ship, NPCShip opponent,
                bool isPlayerSide) {
            var cs = ship.CombatState;
            ship.HailUI?.EndCall();
            ship.State |= ShipStateFlags.InCombat;
            ship.SetWantsHyperspace(false);
            cs.LastAttacker = null;
            cs.SetStrategies(opponent, isPlayerSide);
            cs.SetNextBlunder();
            cs.ResetAmmoState();
            cs.Flux.SetShip(ship);
            cs.Flux.UpdateCapacity(cs);
        }

        /// <summary>
        /// The player's own entry into a fight: bookkeeping plus everything the start of
        /// a combat announces. Must run before the rosters are populated, the guard
        /// below depends on Allies still being empty.
        ///
        /// Reinforcements (common now that enemies can reinforce too, not just allies)
        /// route through StartCombatWith -> BeginPlayerCombat same as the first enemy, so
        /// only the actual start-of-combat effects are gated on Enemies.Count == 0 here -
        /// otherwise NumShipCombats, the ship log and the music switch would all re-fire
        /// per reinforcement.
        /// </summary>
        /// <param name="playerInitiated">
        /// Whether a player action caused this. Only meaningful - and only stored, into
        /// PlayerInitiatedCombat - for the first enemy of a fresh fight; a reinforcement
        /// joining an ongoing one cannot retroactively change how the fight started.
        /// </param>
        private void BeginPlayerCombat(bool playerInitiated) {
            The.AchievementTracker.Unlock(AchievementFirstContact.AchievementId);
            if (Allies.Count == 0) {
                Allies.Add(playerShip);
            }
            // the alarm is a per-arrival cue and stays ungated
            PlayRaidSound();
            if (Enemies.Count > 0) {
                // battle stations and the mini UI still have to react to the new roster
                ShowCombatEvent(announce: false);
                return;
            }
            PlayerInitiatedCombat = playerInitiated;
            CombatStartTick = G.Ticks;
            RestartWeaponWindups();
            CreateCombatEvent(G.Ticks);
            Sounds.PlayMusic(SoundtrackMood.Combat, true);
        }

        // A fight starts with every weapon winding up, also those a manned battle station held
        // ready for space threats: a held volley landed at minute 0, before the enemy's shields
        // (30 min) were up. Inactive weapons start a full wind-up on their next tick
        private void RestartWeaponWindups() {
            foreach (var w in playerShip.CombatState.Weapons) {
                if (w.IsPointDefense) { continue; }
                switch (w.Phase) {
                    case ShipWeaponPhase.Windup:
                    case ShipWeaponPhase.Ready:
                    case ShipWeaponPhase.Charging:
                    case ShipWeaponPhase.Firing: {
                        w.SetPhase(S, ShipWeaponPhase.Inactive);
                        break;
                    }
                }
            }
        }

        private void AnnounceAllyJoined(NPCShip ally) {
            var msg = T.ShipCombatAllyJoined_V1.F(ally.NameLinked);
            S.Sys.Log.AddLine(msg);
            S.Sig.ShowFanfare.Send(msg);
        }

        /// <param name="killer">who gets the kill, null to use the last attacker</param>
        private bool RemoveAlly(NPCShip ally, ShipStateFlags newState, NPCShip killer) {
            if (ally.IsPlayer) {
                D.Err("Player's ship cannot be removed from allies!");
                return false;
            }
            // CleanupAfterCombat below clears the attribution
            killer ??= ally.CombatState.LastAttacker;
            if (!Allies.Remove(ally)) {
                D.Err("Failed removing ally: {0} | {1}",
                    ally, D.DumpCollection(Allies));
            }
            ally.HailUI?.EndCall();
            S.Query.DespawnShipView.Ask(ally);
            ally.State &= ~ShipStateFlags.InCombat;
            for (int i = Enemies.Count - 1; i >= 0; i--) {
                ClearWeaponsTargeting(Enemies[i], ally);
            }
            BroadcastEnemyRosterChange();
            ally.CombatState.CleanupAfterCombat();
            if (newState.HasFlag(ShipStateFlags.Derelict)) {
                ally.TurnIntoDerelict(killer);
            } else if (!ally.IsDerelict) {
                ally.ChangeBehavior(NPCShipBehaviorScope.Sector, false);
            }
            S.Sig.ShipCombatAllyChanged.Send(ally, false);
            return true;
        }

        private void DismissNPCAllies() {
            for (int i = Allies.Count - 1; i >= 0; i--) {
                var ally = Allies[i];
                if (ally.IsPlayer) { continue; }
                RemoveAlly(ally, ShipStateFlags.Active, null);
            }
        }

        private static void ClearWeaponsTargeting(NPCShip shooter, NPCShip target) {
            foreach (var w in shooter.CombatState.Weapons) {
                if (w.TargetShip == target) {
                    w.ClearTarget_Ship();
                }
            }
        }

        private void BroadcastPlayerSideRosterChange() {
            ai.ResolveFocus(playerShip.CombatState, Enemies,
                FocusResolveReason.RosterChange);
            for (int i = Allies.Count - 1; i >= 0; i--) {
                var ally = Allies[i];
                if (ally.IsPlayer) { continue; }
                ai.ResolveFocus(ally.CombatState, Enemies,
                    FocusResolveReason.RosterChange);
            }
        }

        private void BroadcastEnemyRosterChange() {
            for (int i = Enemies.Count - 1; i >= 0; i--) {
                ai.ResolveFocus(Enemies[i].CombatState, Allies,
                    FocusResolveReason.RosterChange);
            }
        }

        private void OnTick(long ticks) {
            if (playerShip == null) {
                D.Err("Player ship not initialized yet");
                return;
            }
            if (Enemies.Count > 0) {
                // D.Warn("Ticking ai: {0}", D.DumpCollection(Enemies));
                TickShip(playerShip, ticks);
                ai.Tick(S, playerShip, Enemies, ticks, CombatStartTick, isPlayerSide: true);
                for (int i = Enemies.Count - 1; i >= 0; i--) {
                    var enemy = Enemies[i];
                    TickShip(enemy, ticks);
                    ai.Tick(S, enemy, Allies, ticks, CombatStartTick, isPlayerSide: false);
                    enemy.CombatState.RepairDevices(ticks);
                }
                TickNPCAllies(ticks);
                if (ticks % 15 == 0) {
                    NPCShipBehavior_Sector_JoinCombat.ScanForJoiners(S,
                        Tunables.ShipCombatMaxNPCAllies - NPCAllyCount,
                        CombatStartTick);
                    // random NPC aggression must never escalate into a gang-up - only a
                    // fight the player picked may draw reinforcements
                    if (PlayerInitiatedCombat) {
                        NPCShipBehavior_Sector_JoinCombatAgainst.ScanForAttackers(S,
                            CombatStartTick);
                    }
                }
            } else {
                // Flux Vents have no charge phase, so their IsActive can flip live
                // (damage, depower, disconnect, repair) without any add/remove event
                // firing, same as the in-combat case in TickShip. Recalculate every
                // tick so the vent rate never goes stale out of combat either.
                if (playerShip.CombatState.Vents.Count > 0) {
                    playerShip.CombatState.Flux.UpdateCapacity(playerShip.CombatState);
                }
                var flux = playerShip.CombatState.Flux;
                var hasThreats = S.Sys.Sec.HasActiveSpaceThreats;
                if (flux.Soft > 0f || flux.Hard > 0f) {
                    // Out of combat, hard flux vents whenever shields are down (same
                    // rule as combat). With no threat, shields are down, so
                    // accumulated hard flux dissipates. While a space threat is
                    // active, venting is left entirely to the player: auto-opening
                    // here would immediately re-trigger every tick (opening the vent
                    // disables the shield, which flips !IsShieldUp right back to
                    // true), permanently overriding a manual close. Soft flux always
                    // vents via ShipFlux.Tick regardless.
                    if (!hasThreats && !playerShip.CombatState.IsShieldUp) {
                        if (flux.State == ShipFluxState.OK) {
                            flux.SetState(ShipFluxState.Venting);
                        }
                    }
                    flux.Tick(S, ticks);
                    if (!UIShowing.ShipCombatMini || !UIShowing.ShipCombatMode) {
                        S.UIQuery.ToggleShipCombatMini.Ask(true);
                    }
                }
                // A player who opened the full combat view to manage venting during a
                // threat shouldn't have to remember to close it once the threat passes.
                // Only on the tick it passes, so the view can also be opened in peace time
                // (Battle Stations app) and stay open.
                if (hadSpaceThreats && !hasThreats && UIShowing.ShipCombatMode) {
                    S.UIQuery.ToggleShipCombat.Ask(false);
                }
                hadSpaceThreats = hasThreats;
            }

            TickNPCBattles(ticks);
            TickRiggedSOs(ticks);

            if (needsClassPointValidation) {
                needsClassPointValidation = false;
                ValidatePlayerClassPoints(false);
            }
            if (needsFluxCapValidation) {
                needsFluxCapValidation = false;
                ValidateFluxCapacity();
            }
            if (needsFluxVentValidation) {
                needsFluxVentValidation = false;
                ValidateFluxVents();
            }
        }

        private void TickRiggedSOs(long ticks) {
            if (soRiggedForDestruction.Count == 0) { return; }
            foreach (var kv in soRiggedForDestruction) {
                int left = (int) (kv.Value - ticks);
                if (left <= 0) {
                    soRiggedForDestruction.Remove(kv.Key);
                    NukeSO(kv.Key);
                    break;
                } else {
                    D.Log("left % hour = {0} % {1} = {2}", left,
                        Consts.TicksPerHour, left % Consts.TicksPerHour);
                    if (left % Consts.TicksPerHour == 0) {
                        S.Sig.ShowFanfare.Send(T.SectorNukeTimer_V1.F(
                            Units.TicksToTime(left, true)));
                    }
                }
            }
        }

        private void NukeSO(SpaceObject so) {
            if (so.Sector != null) {
                S.Sig.ShowFanfare.Send(T.SectorNuked_V1.F(so.StarmapLink));
                if (so.HasChildren) {
                    for (int i = so.Children.Count - 1; i >= 0; i--) {
                        SpaceObject child = so.Children[i];
                        if (child == A.Player) {
                            Explosions.CreateExplosionFX(
                                S, S.Grid.Center, S.Grid.Width, 10000, true);
                            new GameOverCutscene(S, NukedShipDestruction.Destroy(S));
                            continue;
                        }
                        if (child.IsShip) {
                            child.Ship.TurnIntoDerelict(A.Player.Ship);
                        } else {
                            SOUtils.RemoveSO(child);
                        }
                    }
                }
                so.POIs?.Clear();
                if (so.HasKnowledge) {
                    so.Knowledge.Mats = null;
                    so.Knowledge.POIs?.Clear();
                }
                so.SetType(SpaceObjectType.Find(SpaceObjectTypeIdH.Space));
                so.Data.SetBool(SOUtils.NukedKey, true);
            } else {
                D.Err("Unhandled NukeSO type: {0}", so.Type);
                SOUtils.RemoveSO(so);
            }
            S.Sig.SpaceObjectNuked.Send(so);
            The.AchievementTracker.Unlock(AchievementSectorNuked.AchievementId);
        }

        private void TickNPCBattles(long ticks) {
            if (NPCShipBattles.Count <= 0) { return; }
            for (int i = NPCShipBattles.Count - 1; i >= 0; i--) {
                var battle = NPCShipBattles[i];
                // End the battle early if either ship was pulled out of combat
                // externally (e.g. player defeated one of the participants).
                // A ship that left combat will have IsActive=false (derelict)
                // or IsInCombat=false (fled from player combat).
                var endedExternally =
                    !battle.Attacker.IsActive || !battle.Defender.IsActive
                    || !battle.Attacker.IsInCombat || !battle.Defender.IsInCombat;
                if (endedExternally || !battle.Tick(S, ticks)) {
                    NPCShipBattles.RemoveAt(i);
                    // a participant that is also in the player's fight stays in it
                    if (!IsPlayerCombatant(battle.Attacker)) {
                        EndNPCBattleParticipation(battle.Attacker);
                    }
                    if (!IsPlayerCombatant(battle.Defender)) {
                        EndNPCBattleParticipation(battle.Defender);
                    }
                }
            }
        }

        private void TickNPCAllies(long ticks) {
            for (int i = Allies.Count - 1; i >= 0; i--) {
                var ally = Allies[i];
                if (ally.IsPlayer) { continue; }
                if (ally.CombatState.IsNPCDefeated) {
                    RemoveAlly(ally, ShipStateFlags.Derelict, null);
                    continue;
                }
                TickShip(ally, ticks);
                ai.Tick(S, ally, Enemies, ticks, CombatStartTick, isPlayerSide: true);
                if (Enemies.Count == 0) { return; }
                ally.CombatState.RepairDevices(ticks);
            }
        }

        private void TickShip(NPCShip ship, long ticks) {
            // D.Warn("Ticking enemy: {0}: {1}", enemy, ticks);
            // Flux Vents have no charge phase, so their IsActive can flip live
            // (damage, depower, disconnect, repair) without any add/remove event
            // firing. Recalculate every tick so the vent rate never goes stale.
            if (ship.CombatState.Vents.Count > 0) {
                ship.CombatState.Flux.UpdateCapacity(ship.CombatState);
            }
            ship.CombatState.Flux.Tick(S, ticks);
        }


        private void AddEnemy(NPCShip enemy) {
            D.Warn("Adding enemy: {0} | {1}", enemy, D.DumpCollection(Enemies));
            if (Enemies.Contains(enemy)) {
                D.Err("Trying to add enemy that's already in the list: {0}", enemy);
                return;
            }
            enemy.CombatState.SetStrategies(playerShip, isPlayerSide: false);
            enemy.CombatState.SetNextBlunder();
            enemy.CombatState.ResetAmmoState();
            Enemies.Add(enemy);
            enemy.State |= ShipStateFlags.InCombat;
            S.Vars.ShipCombatEnemyCount = Enemies.Count;
            ai.ResolveFocus(enemy.CombatState, Allies, FocusResolveReason.RosterChange);
            BroadcastPlayerSideRosterChange();
            // ui.OnAddEnemy(enemy);
            S.Sig.ShipCombatEngagedWith.Send(enemy, true);
            if (!playerShip.IsInCombat) {
                S.Sig.ShipCombatChanged.Send(true);
                playerShip.State |= ShipStateFlags.InCombat;
                playerShip.UpdateSize(S.Sys.Areas.ShipSize);
            }
        }

        /// <summary>
        /// Load only counterpart of AddEnemy / JoinCombatAsAlly: puts a ship back on its
        /// side without re-initializing anything that was serialized (strategies, weapon
        /// and flux settings, ammo, blunder timing).
        /// </summary>
        private static void AttachCombatantOnLoad(NPCShip ship, List<NPCShip> side) {
            side.Add(ship);
            ship.State |= ShipStateFlags.InCombat;
        }

        private void RemoveEnemy(NPCShip enemy) {
            enemy.HailUI?.EndCall();
            if (!Enemies.Remove(enemy)) {
                D.Err("Failed removing enemy: {0} | {1}",
                    enemy, D.DumpCollection(Enemies));
            }
            S.Query.DespawnShipView.Ask(enemy);
            foreach (var w in enemy.CombatState.Weapons) {
                S.Query.RegisterShipWeapon.Ask(w, false);
            }
            foreach (var sh in enemy.CombatState.Shields) {
                S.Query.RegisterShipShield.Ask(sh, false);
            }
            foreach (var cap in enemy.CombatState.Capacitors) {
                S.Query.RegisterShipCapacitor.Ask(cap, false);
            }
            ClearWeaponsTargeting(playerShip, enemy);
            for (int i = Allies.Count - 1; i >= 0; i--) {
                var ally = Allies[i];
                if (ally.IsPlayer) { continue; }
                ClearWeaponsTargeting(ally, enemy);
            }
            enemy.State &= ~ShipStateFlags.InCombat;
            S.Vars.ShipCombatEnemyCount = Enemies.Count;
            // ui.OnRemoveEnemy(enemy);
            if (Enemies.Count == 0) {
                // allies have to be gone before anyone hears that combat ended,
                // or listeners see dismissed ships still flagged as in combat
                DismissNPCAllies();
            }
            BroadcastPlayerSideRosterChange();
            if (Enemies.Count == 0) {
                playerShip.State &= ~ShipStateFlags.InCombat;
                playerShip.UpdateSize(S.Sys.Areas.ShipSize);
                S.Sig.ShipCombatChanged.Send(false);
                RemoveCombatEvent();
                foreach (var w in playerShip.CombatState.Weapons) {
                    w.AccuracyBonusFromBattleStation = 1.0f;
                }
            }
            enemy.CombatState.CleanupAfterCombat();
            S.Sig.ShipCombatEngagedWith.Send(enemy, false);
        }

        public bool EndCombatWith(NPCShip ship, ShipStateFlags newState) {
            if (!ship.IsPlayer && Allies.Contains(ship)) {
                return RemoveAlly(ship, newState, null);
            }
            if (!Enemies.Contains(ship)) {
                // Not in the player's fight (an NPC battle participant, a ransom paid out of
                // combat): ending "its" fight here must not end the player's
                if (newState.HasFlag(ShipStateFlags.Derelict) && !ship.IsDerelict) {
                    ship.TurnIntoDerelict(ship.CombatState.LastAttacker ?? A.Player.Ship);
                }
                return false;
            }
            // Captured before RemoveEnemy, which dismisses the NPC allies as soon as the
            // last enemy is gone - by the time we know we won, Allies is already empty.
            var hadNPCAllies = NPCAllyCount > 0;
            // RemoveEnemy runs CleanupAfterCombat, which clears the attribution
            var killer = ship.CombatState.LastAttacker;
            RemoveEnemy(ship);
            S.Query.DespawnShipView.Ask(ship);
            ship.State &= ~ShipStateFlags.InCombat;
            if (newState.HasFlag(ShipStateFlags.Derelict)) {
                var actualKiller = killer ?? A.Player.Ship;
                ship.TurnIntoDerelict(actualKiller);
                // Hooked here rather than in TurnIntoDerelict, which is also the shared
                // derelict path for sector nukes and hacked-into-derelict ships - neither
                // of those is a ship combat win.
                ShipAchievements.OnShipDefeated(actualKiller, ship);
            } else if (!ship.IsDerelict) {
                ship.ChangeBehavior(NPCShipBehaviorScope.Sector, false);
            }
            if (Enemies.Count == 0) {
                // RemoveEnemy dismissed the NPC allies, only the player is left
                Allies.Clear();
                // EndCombatWith is also the responder for surrender, flight and defeat -
                // only a kill (the same Derelict flag TurnIntoDerelict above tests) counts
                // as a win.
                if (hadNPCAllies && newState.HasFlag(ShipStateFlags.Derelict)) {
                    The.AchievementTracker.Unlock(AchievementShipAllyWin.AchievementId);
                }
                // ui.Cleanup();
            }
            return true;
        }

        private void OnShipViewVisibilityChange(ShipView view, bool isVisible) {
            // ui.OnShipViewVisibilityChange(view, isVisible);
        }
        private void PlayRaidSound() {
            UISounds.Play("FX/RaidAlert");
            The.SysSig.GamepadRumble.Send(0.3f, 0.3f, 0.2f);
        }

        private bool StartCombatWith(NPCShip ship, bool affectRelation) {
            if (Enemies.Contains(ship)) {
                // it's ok, sometimes player's ship can be escaping
                return false;
            }
            // A ship can turn derelict while the player flies over to attack it (a hack, a
            // battle of its own), and a derelict never fights (STA-1865)
            if (!ship.IsActive || ship.IsDerelict) {
                D.Warn("Not starting combat with a ship that is not active: {0}", ship);
                return false;
            }
            var isFirstEnemy = Enemies.Count == 0;
            ship.HailUI?.EndCall();
            if (A.IsInHyperspace || ship.IsInHyperspace) {
                D.Err("Trying to start combat with {0} while either player or enemy is in hyperspace!. Player in HS: {1}",
                    ship, A.IsInHyperspace);
                return false;
            }
            LeaveNPCBattle(ship);
            BeginPlayerCombat(affectRelation);
            ship.State |= ShipStateFlags.InCombat;
            ship.CombatState.LastAttacker = null;
            // if the combat was initiated by the NPC, we likely won't need to
            // to assign the combat behavior again, so let's not do that
            // if combat behavior is already set.
            if (ship.BehaviorSector is not NPCShipBehavior_Sector_Combat) {
                NPCShipBehavior_Sector_Combat.AssignTo(ship, A.Player.Ship);
            }
            var view = S.Query.SpawnShipView.Ask(ship);
            // update size and all that
            ship.CombatState.Flux.SetShip(ship);
            ship.CombatState.Flux.UpdateCapacity(ship.CombatState);
            if (affectRelation) {
                ship.ChangeAttitudeTowards(FactionDef.Player,
                    ship.CombatState.Rng.Range(-0.01f, -0.02f));
            }
            if (isFirstEnemy) {
                PlayStats.TrackShipCombatStarted(ship);
            } else {
                PlayStats.TrackShipCombatReinforcement(ship, Enemies.Count + Allies.Count + 1);
            }
            AddEnemy(ship);
            if (affectRelation) {
                // run after AddEnemy, so ship is unambiguously on the Enemies roster and
                // GetShipsAgainstPlayer counts it exactly once - retaliators dispatched
                // from here land in the same enemy-slot budget as everyone else
                ShipHailAction_Insult.SectorShipsTryRetaliateFor(ship, false);
            }
            return true;
        }


        public void SaveSpecial(SystemsDataSpecial sd) {
            var d = ShipCombatSysData.Serialize(this);
            if (soRiggedForDestruction.Count > 0) {
                d.RiggedSOs = new Dictionary<int, long>(soRiggedForDestruction.Count);
                foreach (var kv in soRiggedForDestruction) {
                    d.RiggedSOs[kv.Key.Id] = kv.Value;
                }
            }
            sd.ShipCombat = d;
        }

        public void LoadSpecial(SystemsDataSpecial sd) {
            data = sd.ShipCombat;
        }
        private void CreateCombatEvent(long ticks) {
            // D.Warn("Creating combat event");
            S.Situation.NumShipCombats++;
            D.Warn("Starting ship combat! #{0} at {1}",
                S.Situation.NumShipCombats, Units.TicksToTime(ticks));
            ShowCombatEvent(announce: true);
        }

        /// <summary>
        /// Everything the ongoing combat notification drags along. A restored combat
        /// replays it with announce=false: the fanfare and the log line already happened
        /// when the fight started, the rest of it has to be put back.
        /// </summary>
        private void ShowCombatEvent(bool announce) {
            if (!combatEvent.IsValid) {
                if (combatEvent.IsValid) {
                    Notification.Remove(ref combatEvent);
                }
                combatEvent = Notification.Build(IconId.CAttackTarget,
                    T.ShipCombatOngoing,
                    0, NotificationType.Critical)
                    .WithOnClick(() => {
                        var on = UIShowing.ShipCombatMode;
                        S.UIQuery.ToggleShipCombat.Ask(!on);
                    })
                    .Sticky()
                    .Emergency(true)
                    .WithVoice(VoiceLine.EnemyShip)
                    .Show();
                if (announce) {
                    string title = Texts.Red(T.ShipCombatOngoing);
                    S.Sys.Log.AddLine(title);
                    S.Sig.ShowFanfare.Send(title);
                }
                new CmdRestrictClock(ClockSpeed.Normal).Execute(S);
                Sounds.PlayMusic(SoundtrackMood.Combat, true);
                S.Clock.DangerPause();
            }
            ShipCombatAI.RecalculateBattleStationAssignments(playerShip.CombatState);
            // ui.Rebuild(false);
            if (!UIShowing.ShipCombatMode) {
                S.UIQuery.ToggleShipCombatMini.Ask(true);
            }
        }
        private void RemoveCombatEvent() {
            // ui.Rebuild(false);
            if (combatEvent.IsValid) {
                Notification.Remove(ref combatEvent);
                Sounds.PlayMusic(SoundtrackMood.Calm, true);
                S.Sys.Log.AddLine(T.ShipCombatEnded, Texts.TMPColorGreen);
            }
            if (UIShowing.ShipCombatMode) {
                UIShowing.Unset(UIShowingFlags.ShipCombatMode);
                S.UISig.ToggleShipCombatMode.Send(false);
            }
        }

        // ---- NPC ship battle joining (formerly ShipCombatSys.JoinBattle.cs) ----
        // Turning a statistical NPC vs NPC battle into a real time combat the player is
        // part of. Structured as a pure preflight followed by a commit that cannot fail:
        // the individual steps of starting a combat all have immediate side effects that
        // could not be rolled back halfway through.

        public bool TryGetNPCBattleFor(NPCShip ship, out NPCShipBattle battle) {
            for (int i = NPCShipBattles.Count - 1; i >= 0; i--) {
                var candidate = NPCShipBattles[i];
                if (candidate.Attacker == ship || candidate.Defender == ship) {
                    battle = candidate;
                    return true;
                }
            }
            battle = null;
            return false;
        }

        private NPCShipBattle GetNPCBattleFor(NPCShip ship) {
            TryGetNPCBattleFor(ship, out var battle);
            return battle;
        }

        /// <summary>
        /// The one and only answer to "can the player join this battle on this side".
        /// Pure - availability, the recheck after flying over, and the commit preflight
        /// all ask this, so they can never disagree.
        /// </summary>
        public bool CanJoinNPCBattle(NPCShipBattle battle, NPCShip sideToHelp) {
            if (battle == null || sideToHelp == null) { return false; }
            // never assume which side the caller meant
            if (sideToHelp != battle.Attacker && sideToHelp != battle.Defender) {
                return false;
            }
            if (!NPCShipBattles.Contains(battle)) { return false; }
            // v1 does not merge a battle into a fight the player is already in
            if (Enemies.Count > 0) { return false; }
            if (A.IsInHyperspace) { return false; }
            // Active is not maintained for the player's ship, only Derelict is
            if (playerShip.IsDerelict) { return false; }
            if (playerShip.CombatState.WeaponCombatStrength <= 0) { return false; }
            if (NPCAllyCount >= Tunables.ShipCombatMaxNPCAllies) { return false; }
            if (S.Clock.IsForceStopped) { return false; }
            return CanConvertBattleParticipant(battle.Attacker, battle)
                && CanConvertBattleParticipant(battle.Defender, battle);
        }

        private bool CanConvertBattleParticipant(NPCShip ship, NPCShipBattle battle) {
            if (ship == null) { return false; }
            if (!ship.IsActive || ship.IsDerelict || ship.IsInHyperspace) { return false; }
            // A statistical battle only ends at HP <= 0, so a participant can already be
            // down to no working devices. Converting one would let the first
            // incapacitation sweep derelict it right after joining.
            if (ship.CombatState.IsNPCDefeated) { return false; }
            if (ship.Sector != A.PlayerSector.Sector) { return false; }
            if (IsPlayerCombatant(ship)) { return false; }
            for (int i = NPCShipBattles.Count - 1; i >= 0; i--) {
                var other = NPCShipBattles[i];
                if (other == battle) { continue; }
                if (other.Attacker == ship || other.Defender == ship) { return false; }
            }
            return true;
        }

        /// <param name="sideToHelp">the participant that becomes the player's ally</param>
        /// <returns>false, having changed nothing, when the preflight fails</returns>
        public bool JoinNPCBattle(NPCShipBattle battle, NPCShip sideToHelp) {
            if (!CanJoinNPCBattle(battle, sideToHelp)) {
                D.Warn("Refusing to join NPC ship battle: {0} helping {1}",
                    battle, sideToHelp);
                return false;
            }
            var ally = sideToHelp;
            var enemy = battle.Attacker == sideToHelp ? battle.Defender : battle.Attacker;
            NPCShipBattles.Remove(battle);

            // the player joins first, while both rosters are still empty. Deliberately
            // choosing to help a side is the player picking the fight, same as clicking
            // Attack.
            BeginPlayerCombat(true);
            playerShip.State |= ShipStateFlags.InCombat;
            playerShip.UpdateSize(S.Sys.Areas.ShipSize);

            Allies.Add(ally);
            Enemies.Add(enemy);
            S.Vars.ShipCombatEnemyCount = Enemies.Count;
            PrepareForCombat(ally, enemy, isPlayerSide: true);
            PrepareForCombat(enemy, playerShip, isPlayerSide: false);
            NPCShipBehavior_Sector_Follow.AssignTo(ally, A.Player);
            NPCShipBehavior_Sector_Combat.AssignTo(enemy, playerShip);
            PlayStats.TrackShipCombatStarted(enemy);

            S.Query.SpawnShipView.Ask(ally);
            S.Query.SpawnShipView.Ask(enemy);
            ai.ResolveFocus(playerShip.CombatState, Enemies, FocusResolveReason.RosterChange);
            ai.ResolveFocus(ally.CombatState, Enemies, FocusResolveReason.RosterChange);
            ai.ResolveFocus(enemy.CombatState, Allies, FocusResolveReason.RosterChange);

            // set up, so the rest of the game can be told about it
            S.Sig.ShipCombatChanged.Send(true);
            S.Sig.ShipCombatEngagedWith.Send(enemy, true);
            S.Sig.ShipCombatAllyChanged.Send(ally, true);
            AnnounceAllyJoined(ally);

            // taking a side is what the two factions actually react to
            enemy.ChangeAttitudeTowards(FactionDef.Player,
                enemy.CombatState.Rng.Range(-0.01f, -0.02f));
            ShipHailAction_Insult.SectorShipsTryRetaliateFor(enemy, false);
            S.Query.ChangeFactionAttitude.Ask(ally.Faction, FactionDef.Player, 0.03f);
            return true;
        }

        // ---- Save/load restore (formerly ShipCombatSys.Load.cs) ----
        // Restoring a saved ship combat. Split into two stages, because ship views must
        // only spawn for ships that survived reconciliation, and everything that reads
        // views must only run once they all exist.

        /// <summary>
        /// Asked by the ship load sequencer once ships are restored and the player's
        /// ship has been validated. Owns the whole restore chain: rosters, then views
        /// (spawned from the ShipCombatDataRestored gate, along with the weapon and
        /// shield state that depends on them), then focus and signal replay.
        /// </summary>
        private bool RestoreCombatData() {
            RestoreStageA();
            Ready.Set(WhenH.ShipCombatDataRestored, true);
            RestoreStageB();
            RestoreRiggedSOs();
            data = null;
            return true;
        }

        // Armed sector nukes keep their absolute detonation tick across save/load, so the
        // fuse resumes where it was and TickRiggedSOs detonates it as usual. Written directly
        // rather than through RigSpaceObjectForDestruction, which would re-announce the nuke.
        private void RestoreRiggedSOs() {
            if (data?.RiggedSOs == null) { return; }
            foreach (var kv in data.RiggedSOs) {
                if (!S.Universe.TryFind(kv.Key, out var so)) {
                    D.Warn("Rigged space object {0} no longer exists, dropping its nuke", kv.Key);
                    continue;
                }
                if (so.Sector == null || so.Type.IdH == SpaceObjectTypeIdH.Space) {
                    D.Warn("Rigged space object is no longer a nukeable sector, dropping its nuke: {0}", so);
                    continue;
                }
                soRiggedForDestruction[so] = kv.Value;
            }
        }

        /// <summary>
        /// Rebuilds the two sides from the save and reconciles them. No signals, no
        /// views, no UI: anything observable happens in stage B.
        /// </summary>
        private void RestoreStageA() {
            if (Allies.Count > 0) {
                D.Err("Allies was not zero when restoring data!");
                Allies.Clear();
            }
            if (Enemies.Count > 0) {
                D.Err("Enemies was not zero when restoring data!");
                Enemies.Clear();
            }
            restoredCombatIsValid = false;
            if (data == null) { return; }
            // cleared only when there is saved combat to rebuild from, so a save
            // without a ship combat section cannot yank the player out of a fight
            playerShip.State &= ~ShipStateFlags.InCombat;
            playerShip.CombatState.FocusedEnemy = null;

            // every ship the save listed, so the ones that don't make it into a side
            // can be released instead of being left flagged as fighting
            var restored = new List<NPCShip>();
            var enemies = new List<NPCShip>();
            var allies = new List<NPCShip>();
            CollectRestoredCombatants(data.Enemies, enemies, restored);
            CollectRestoredCombatants(data.Allies, allies, restored);

            // Runtime combat refuses to start in hyperspace and ends when the player
            // enters it, so a restored one has to obey the same rule. Player validation
            // has already run by now and can have turned the ship into a derelict.
            // ShipStateFlags.Active is deliberately NOT checked: ValidatePlayerShip only
            // ever toggles Derelict, so Active is not maintained for the player's ship
            // and testing it would drop perfectly valid combats.
            restoredCombatIsValid = !playerShip.IsDerelict && !A.IsInHyperspace;
            if (!restoredCombatIsValid) {
                if (enemies.Count > 0 || allies.Count > 0) {
                    D.Warn("Dropping restored ship combat, player ship cannot fight. Derelict: {0}, Hyperspace: {1}",
                        playerShip.IsDerelict, A.IsInHyperspace);
                }
                enemies.Clear();
                allies.Clear();
            } else {
                // the player is never an enemy, and for NPCs the enemy side wins
                enemies.Remove(playerShip);
                for (int i = allies.Count - 1; i >= 0; i--) {
                    var ally = allies[i];
                    if (ally != playerShip && enemies.Contains(ally)) {
                        D.Err("Restored ship combat participant was on both sides, keeping it as an enemy: {0}",
                            ally);
                        allies.RemoveAt(i);
                    }
                }
                DropUnfitCombatants(enemies);
                DropUnfitCombatants(allies);
                if (enemies.Count == 0) {
                    // nothing left to fight, so restored allies are dismissed
                    // instead of restoring a combat without opponents
                    allies.Clear();
                } else {
                    allies.Remove(playerShip);
                    allies.Insert(0, playerShip);
                }
            }

            foreach (var enemy in enemies) {
                AttachCombatantOnLoad(enemy, Enemies);
            }
            foreach (var ally in allies) {
                AttachCombatantOnLoad(ally, Allies);
            }
            EnsureTargetStrategies();
            RestoreNPCShipBattles(data.NPCShipBattles);
            foreach (var ship in restored) {
                if (ship.IsPlayer) { continue; }
                if (IsPlayerCombatant(ship) || IsInAnyNPCBattle(ship)) { continue; }
                ReleaseFromCombat(ship);
            }
            // load attach bypasses AddEnemy, which normally keeps these in sync
            CombatStartTick = Enemies.Count > 0 ? data.CombatStartTick : 0;
            PlayerInitiatedCombat = Enemies.Count > 0 && data.PlayerInitiatedCombat;
            S.Vars.ShipCombatEnemyCount = Enemies.Count;
        }

        /// <summary>
        /// Runs once every combatant has a view and its weapons and shields are back.
        /// Focus resolution comes after weapon target restoration, so nothing overwrites
        /// the target a ship ends up with.
        /// </summary>
        private void RestoreStageB() {
            ai.ResolveFocus(playerShip.CombatState, Enemies, FocusResolveReason.Restore);
            for (int i = Allies.Count - 1; i >= 0; i--) {
                var ally = Allies[i];
                if (ally.IsPlayer) { continue; }
                ai.ResolveFocus(ally.CombatState, Enemies, FocusResolveReason.Restore);
            }
            for (int i = Enemies.Count - 1; i >= 0; i--) {
                ai.ResolveFocus(Enemies[i].CombatState, Allies, FocusResolveReason.Restore);
            }
            // stage B runs on every load, but combat UI and effects must not
            if (!restoredCombatIsValid || Enemies.Count == 0) { return; }

            S.Sig.ShipCombatChanged.Send(true);
            for (int i = 0; i < Enemies.Count; i++) {
                S.Sig.ShipCombatEngagedWith.Send(Enemies[i], true);
            }
            for (int i = 0; i < Allies.Count; i++) {
                var ally = Allies[i];
                if (ally.IsPlayer) { continue; }
                S.Sig.ShipCombatAllyChanged.Send(ally, true);
            }
            ShowCombatEvent(announce: false);
        }

        /// <summary>
        /// Saves made before target strategies existed have none, and neither do ships
        /// whose strategy def a mod removed. Only the target strategy is chosen here:
        /// everything else was restored and must not be re-rolled.
        /// </summary>
        private void EnsureTargetStrategies() {
            for (int i = Enemies.Count - 1; i >= 0; i--) {
                var enemy = Enemies[i];
                if (enemy.CombatState.StrategyTarget != null) { continue; }
                ShipCombatTargetStrategyDef.ChooseFor(enemy, playerShip,
                    isPlayerSide: false);
            }
            for (int i = Allies.Count - 1; i >= 0; i--) {
                var ally = Allies[i];
                if (ally.IsPlayer || ally.CombatState.StrategyTarget != null) { continue; }
                ShipCombatTargetStrategyDef.ChooseFor(ally, StrongestEnemy(),
                    isPlayerSide: true);
            }
        }

        private void CollectRestoredCombatants(List<uint> ids, List<NPCShip> side,
                List<NPCShip> restored) {
            if (ids == null) { return; }
            foreach (var id in ids) {
                var ship = S.Query.NPCShipById.Ask(id);
                if (ship == null) {
                    D.Err("Restored ship combat participant no longer exists: {0}", id);
                    continue;
                }
                if (side.Contains(ship)) {
                    D.Err("Restored ship combat participant was listed twice: {0}", ship);
                    continue;
                }
                side.Add(ship);
                if (!restored.Contains(ship)) {
                    restored.Add(ship);
                }
            }
        }

        private static void DropUnfitCombatants(List<NPCShip> side) {
            for (int i = side.Count - 1; i >= 0; i--) {
                var ship = side[i];
                if (ship.IsPlayer) { continue; }
                if (!ship.IsActive || ship.IsDerelict || ship.IsInHyperspace) {
                    D.Warn("Dropping restored ship combat participant that cannot fight: {0}",
                        ship);
                    side.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// A ship can only ever be in one battle at a time, but older saves could
        /// store overlapping ones. Battles that reference a ship which is already
        /// fighting elsewhere are dropped, and participants left without any combat
        /// are released back into their sector.
        /// </summary>
        private void RestoreNPCShipBattles(List<NPCShipBattleData> battleDatas) {
            NPCShipBattles.Clear();
            if (battleDatas == null) { return; }
            var orphans = new List<NPCShip>();
            foreach (var battleData in battleDatas) {
                if (!NPCShipBattleData.TryDeserialize(S, battleData, out var battle)) {
                    continue;
                }
                if (IsInAnyNPCBattle(battle.Attacker) || IsInAnyNPCBattle(battle.Defender)
                        || IsPlayerCombatant(battle.Attacker)
                        || IsPlayerCombatant(battle.Defender)) {
                    D.Err("Dropping restored NPC ship battle, a participant is already fighting elsewhere: {0} vs {1}",
                        battle.Attacker, battle.Defender);
                    if (!orphans.Contains(battle.Attacker)) {
                        orphans.Add(battle.Attacker);
                    }
                    if (!orphans.Contains(battle.Defender)) {
                        orphans.Add(battle.Defender);
                    }
                    continue;
                }
                NPCShipBattles.Add(battle);
            }
            foreach (var ship in orphans) {
                if (IsInAnyNPCBattle(ship) || IsPlayerCombatant(ship)) { continue; }
                ReleaseFromCombat(ship);
            }
        }

        // ---- Enemy reinforcement (formerly ShipCombatSys.JoinAgainst.cs) ----
        // NPC ships reinforcing the player's opponents: same-faction ships, allies of a
        // ship the player is fighting, and police defending a lawful ship from an
        // outlaw player. Mirrors the NPC battle joining section above, on the opposing
        // side.
        //
        // The enemy roster is capped (Tunables.ShipCombatMaxEnemies) for reinforcement
        // paths only - never for a player-initiated attack, a raid, or a story spawn.
        // Both reinforcement dispatchers (NPCShipBehavior_Sector_JoinCombatAgainst's
        // scan, and ShipHailAction_Insult.DoRetaliate's pile-on) draw from the single
        // budget below rather than tracking what they have spent. Both are also gated
        // by PlayerInitiatedCombat above OnTick's scan call (and, for the kill-revenge
        // half of DoRetaliate, by IsShipCombatPlayerInitiated checked in
        // NPCShip.TurnIntoDerelict) - random NPC aggression must never draw in help.

        /// <summary>
        /// Live enemies plus every ship in the player's sector currently flying over to
        /// attack the player, each counted exactly once. This is what makes a hail
        /// insult's dispatch and a direct-attack target land in the same budget: both
        /// end up on Sector_Combat following the player (or, for the scan,
        /// Sector_JoinCombatAgainst) before anything asks how many slots are left.
        /// </summary>
        private List<NPCShip> GetShipsAgainstPlayer() {
            shipsAgainstPlayer.Clear();
            shipsAgainstPlayer.AddRange(Enemies);
            if (S.Sys.NPCShip.SectorShips.TryGetValue(A.PlayerSector.Sector, out var ships)) {
                for (int i = ships.Count - 1; i >= 0; i--) {
                    var ship = ships[i];
                    if (!ship.IsActive || ship.IsDerelict) { continue; }
                    if (shipsAgainstPlayer.Contains(ship)) { continue; }
                    if (ship.BehaviorSector is NPCShipBehavior_Sector_JoinCombatAgainst) {
                        shipsAgainstPlayer.Add(ship);
                    } else if (ship.BehaviorSector is NPCShipBehavior_Sector_Combat
                            && ship.Memory.FollowTarget == A.Player) {
                        shipsAgainstPlayer.Add(ship);
                    }
                }
            }
            return shipsAgainstPlayer;
        }

        /// <summary>
        /// Enemy cap minus GetShipsAgainstPlayer count. Can be negative. Callers re-ask
        /// this after every dispatch rather than tracking spent slots themselves.
        /// </summary>
        private int EnemySlotsLeft() {
            return Tunables.ShipCombatMaxEnemies - GetShipsAgainstPlayer().Count;
        }

        private bool IsCombatPlayerInitiated() {
            return PlayerInitiatedCombat;
        }

        /// <summary>
        /// The single capped commit both reinforcement arrival paths call: the scan's
        /// own behavior, and NPCShipBehavior_Sector_Combat's arrival when the ship was
        /// dispatched as a reinforcement. Deliberately has no Enemies.Count > 0
        /// precondition - a hail-insult reinforcement can legitimately arrive before the
        /// ship that was insulted, and refusing it would silently cancel retaliation
        /// that already happens today.
        /// </summary>
        private bool JoinCombatAgainstPlayer(NPCShip ship) {
            if (ship.IsPlayer) {
                D.Err("Player's ship cannot join combat against itself!");
                return false;
            }
            if (!ship.IsActive || ship.IsDerelict) {
                D.Err("Cannot join against player, ship is not active: {0}", ship);
                return false;
            }
            if (IsPlayerCombatant(ship) || IsInAnyNPCBattle(ship)) {
                D.Err("Cannot join against player, ship is already fighting: {0}", ship);
                return false;
            }
            if (ship.IsInHyperspace) {
                D.Err("Cannot join against player from hyperspace: {0}", ship);
                return false;
            }
            if (ship.Sector != A.PlayerSector.Sector) {
                D.Err("Cannot join against player from another sector: {0}", ship);
                return false;
            }
            // The authoritative recheck: both of these can have changed while the ship
            // was flying over (the fight it was sent for could have ended and a new,
            // NPC-initiated one started; a diplomatic change could have made it an ally
            // mid-flight). This is the one commit both arrival paths share - the scan's
            // own behavior already rechecks GetMotive, but DoRetaliate's reinforcement
            // dispatch has no motive system at all, so this is its only revalidation.
            if (!PlayerInitiatedCombat) {
                D.Warn("Refusing to join against player, combat is no longer player-initiated: {0}",
                    ship);
                return false;
            }
            if (NPCShipBehavior_Sector_JoinCombatAgainst.IsAlliedWithPlayerSide(S, ship)) {
                D.Warn("Refusing to join against player, ship is allied with the player's side: {0}",
                    ship);
                return false;
            }
            // the live roster only - the arriving ship is itself already counted in the
            // against-player set, so asking EnemySlotsLeft() here would let two arrivals
            // in the same tick block each other
            if (Enemies.Count >= Tunables.ShipCombatMaxEnemies) {
                return false;
            }
            var wasFirstEnemy = Enemies.Count == 0;
            if (!StartCombatWith(ship, affectRelation: false)) {
                return false;
            }
            if (!wasFirstEnemy) {
                // the first enemy is already covered by the normal combat-start fanfare
                AnnounceEnemyJoined(ship);
            }
            return true;
        }

        private void AnnounceEnemyJoined(NPCShip enemy) {
            var msg = T.ShipCombatEnemyJoined_V1.F(enemy.NameLinked);
            S.Sys.Log.AddLine(msg);
            S.Sig.ShowFanfare.Send(msg);
        }
    }
}
