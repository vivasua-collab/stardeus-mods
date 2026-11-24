using System;
using System.Collections.Generic;
using System.Linq;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Input;
using Game.Systems.Creatures;
using Game.Systems.Notify;
using Game.Systems.Quests;
using Game.UI;
using Game.Utils;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public class ExpeditionContext {
        public bool IsSetup => Expedition == null;
        public bool IsExploration => TargetSO.Value != null
            && TargetSO.Value.Type.IdH != SpaceObjectTypeIdH.Resource;
        public List<BeingData> crewCandidates;
        /// <summary>
        /// Mind IDs that must be in the crew due to an active colonist quest.
        /// Populated when the target SO is set.
        /// </summary>
        public HashSet<uint> RequiredCrewMindIds;
        private SpaceObject lastAutofillTarget;
        public readonly BCallback OnCrewChanged = new();
        public readonly BCallback OnOrdersChanged = new();
        public ShuttleComp Shuttle;
        public readonly B<SpaceObject> TargetSO = new(null);
        public Lifetime MainLifetime;
        public Lifetime MenuLifetime;
        public Expedition Expedition;
        private WPopupData actionPromptPopup;
        public ExpeditionMiningOrders MiningOrders;
        public bool IsActionRequired => requiredActionPrompt != null;
        /// <summary>
        /// True when the crew contains a guest (delivery-quest passenger). Such expeditions must
        /// also carry at least one non-guest escort and must abort if the escort fails to board.
        /// </summary>
        public bool HasGuestCrew {
            get {
                if (crewCandidates == null) { return false; }
                for (int i = 0; i < crewCandidates.Count; i++) {
                    if (crewCandidates[i].IsGuest) { return true; }
                }
                return false;
            }
        }
        public bool HadActionRequired;
        private Action<Expedition> requiredActionPrompt;
        private NotificationHandle actionRequiredHandle;
        public float EstimateMiningRate(out bool areMiningOrdersOK) {
            if (TargetSO.Value == null || crewCandidates == null || Shuttle == null) {
                areMiningOrdersOK = true;
                return 0f;
            }
            var shuttle = Shuttle;
            var miners = IsSetup ? crewCandidates : Expedition.Crew;
            var miningFactor = Expedition.MiningFactorFor(miners, shuttle);
            var so = TargetSO;
            if (A.S.Sys.Planets.TryEstimateExpeditionMiningRate(
                    so.Value, miningFactor, out var rate, out var mt)) {
                var orders = Expedition == null ?
                    MiningOrders : Expedition.MiningOrders;
                areMiningOrdersOK = orders.MinRate < rate * 0.95f;
                return rate;
            }
            areMiningOrdersOK = true;
            return 0f;
        }

        private void AddActionRequiredNotification() {
            if (actionRequiredHandle.IsValid) { return; }
            actionRequiredHandle = Notification.Build(IconId.CBell, $"{T.Expedition}: {T.ActionRequired}",
                0, NotificationType.Immediate)
                .Sticky()
                .Group("exped_action", T.Expedition)
                .WithTooltipFunc(() => T.ExpeditionActionPrompt_V1.F(Shuttle.Name))
                .WithOnClick(() => {
                    Notification.Remove(ref actionRequiredHandle);
                    requiredActionPrompt?.Invoke(Expedition);
                })
                .Show();
        }

        public void SetRequiredAction(Action<Expedition> prompt) {
            HadActionRequired = true;
            requiredActionPrompt = prompt;
            AddActionRequiredNotification();
            ShowActionRequiredPrompt();
            Expedition.CurrentStage.Subtitle.Set(Texts.Red(T.ActionRequired));
            Expedition.OnChange.Invoke();
        }

        public void ShowActionRequiredPrompt() {
            if (requiredActionPrompt == null) { return; }
            if (actionPromptPopup != null) {
                actionPromptPopup.Despawn();
            }
            actionPromptPopup = WPopup.Create(false, IconId.CWarning, T.ActionRequired, null,
                T.ExpeditionActionPrompt_V1.F(Shuttle.Name))
                .WithAction(T.VerbRespond, () => {
                    TryRequiredAction();
                })
                .WithAction2(T.Ignore, null)
                .Spawn();
        }

        public bool TryRequiredAction() {
            if (IsActionRequired) {
                requiredActionPrompt?.Invoke(Expedition);
                requiredActionPrompt = null;
                Notification.Remove(ref actionRequiredHandle);
                return true;
            }
            return false;
        }

        public void ClearRequiredAction() {
            requiredActionPrompt = null;
            if (actionPromptPopup != null) {
                actionPromptPopup.Despawn();
                actionPromptPopup = null;
            }
            Expedition.OnChange.Invoke();
            Expedition.CurrentStage.Subtitle.Set(null);
            Notification.Remove(ref actionRequiredHandle);
        }

        public bool Launch() {
            var S = A.S;
            // New players went straight for the shuttle, before a radar had shown them
            // anything worth flying to. Checked here rather than in CanLaunch, so the button
            // stays clickable and the click explains why nothing happens. Instant build waives
            // it, as it does for hyperjumps (HyperjumpQueue).
            if (!S.Sys.Sec.IsRadarActive() && !A.InstantBuild) {
                UISounds.PlayActionDenied();
                S.Sig.ShowErrorMessage.Send($"{T.OperationFailed}: {T.RadarNotAvailable}");
                return false;
            }
            if (!CanLaunch(true)) {
                return false;
            }
            var exp = new Expedition();
            // exp.Controls = this;
            exp.MiningOrders = MiningOrders;
            exp.Shuttle = Shuttle;
            exp.Context = this;
            exp.SetTarget(TargetSO.Value);
            exp.WithCrew(crewCandidates);
            if (HasGuestCrew || IsQuestCrewTarget(TargetSO.Value)) {
                // Other quest-required crew use the same per-expedition override.
                // Passenger must never fly alone: force-abort if the escort fails to board.
                // Not saved to prefs — this is a per-expedition override, not a settings change.
                exp.MiningOrders.AbortOnMissingCrew = true;
            }
            bool removedCrew = false;
            for (int i = exp.Crew.Count - 1; i >= 0; i--) {
                BeingData c = exp.Crew[i];
                if (S.Sys.Expedition.IsOnExpedition(c, exp)) {
                    exp.CrewRemove(c);
                    removedCrew = true;
                }
                // Guests (e.g. delivery quest passengers) are cargo, not workers — their
                // tiredness must not block the launch.
                if (BeingUtils.IsTired(c) && !c.IsGuest) {
                    S.Sig.ShowErrorMessage.Send($"{T.OperationFailed}: {T.ExpCrewTired}");
                    return false;
                }
            }
            if (removedCrew) {
                UISounds.PlayActionDenied();
                RebuildUI();
                return false;
            }
            var tick = G.Ticks;
            // foreach (var cm in exp.Crew) {
            //     // cm.ExpeditionMember.LastSelectTick = tick;
            // }
            exp.Seed = Rng.UInt;
            var owner = TargetSO.Value.Faction;
            if (!IsExploration && owner != null && !owner.IsPlayer
                    && !S.Sys.Diplomacy.IsAlliedWithPlayer(owner)) {
                // Mining a foreign deposit costs standing and brings their ships or police
                // after us (PlanetsSys.ApplyMiningConsequence, same gate), so ask first
                WPopup.Create(false, owner.FlagIcon, T.Warning, null,
                    T.ConfirmExpeditionToFactionTerritory_V2.F(
                        TargetSO.Value.StarmapLink, owner.NameWithGlyph))
                    .Unique(TerritoryWarningPopupId)
                    .WithAction2(T.Cancel, null)
                    .WithAction(T.LaunchExpedition, () => {
                        LaunchUnlessCrewCannotMine(exp);
                    }).Spawn();
                return false;
            }
            return LaunchUnlessCrewCannotMine(exp);
        }

        private const string TerritoryWarningPopupId = "exp.territory.warning";

        private bool LaunchUnlessCrewCannotMine(Expedition exp) {
            if (exp.MiningFactor < 1) {
                WPopup.Create(false, IconId.CWarning,
                    T.Warning, null, T.CrewIncapableOfMining)
                    .WithAction(T.Ok, () => {
                        DoLaunchExpedition(exp);
                    }).Spawn();
                return false;
            }
            DoLaunchExpedition(exp);
            return true;
        }

        public void RebuildUI() {
            A.S.UIQuery.RebuildExpeditions.Ask();
        }

        private void DoLaunchExpedition(Expedition exp) {
            // launches from a confirm popup arrive later - this setup may have launched since
            if (!IsSetup) { return; }
            if (crewCandidates.Count == 0 && (IsExploration || !exp.Shuttle.HasAutoPilot))  {
                UISounds.PlayActionDenied();
                RebuildUI();
                return;
            }

            if (!exp.Shuttle.Tile.IsActive || exp.Shuttle.Tile.Health.IsDamaged
                    || exp.Shuttle.IsLost) {
                UISounds.PlayActionDenied();
                RebuildUI();
                return;
            }

            // Only commit to this expedition once it is actually launching — earlier
            // aborts (tired crew, removed crew, cancelled mining-factor warning) must
            // leave the setup context untouched so IsSetup stays true and the crew
            // selection UI remains usable.
            Expedition = exp;

            // prophylactic
            exp.Shuttle.Tile.ClearGoal("Launching expedition");

            exp.Shuttle.SaveLastCrew(exp.Crew);
            exp.Shuttle.SetInUse(true);
            A.S.Sys.Expedition.Launch(exp);
            crewCandidates.Clear();
            UISounds.PlayShowDialog();
            RebuildUI();
        }

        // FIXME refactor this to output a struct instead
        public bool CheckDepth(out float curDepth, out float maxDepth, out string depthText,
                out string diffTxt, out int ownedCnt,
                SpaceObject target = null) {
            target ??= TargetSO.Value;
            depthText = null;
            diffTxt = null;
            if (IsExploration || target == null) {
                curDepth = maxDepth = 0f;
                ownedCnt = 0;
                return true;
            }
            maxDepth = Shuttle?.MiningDepth ?? 0f;
            var isDepthOK = true;
            curDepth = 0f;
            ownedCnt = 0;
            if (A.S.Sys.Planets.TryGetResourceFor(target, out var resource, out var mt)) {
                ownedCnt = A.S.Sys.Inventory.AvailableCount(mt);
                diffTxt = $"{T.MiningDifficulty}: {resource.MiningDifficulty:0.#}";
                if (mt.Deposit.IsWithDepth) {
                    curDepth = resource.MiningDepth;
                    depthText = Units.Depth(curDepth);
                    if (maxDepth < resource.MiningDepth) {
                        depthText = Texts.Red(depthText);
                        isDepthOK = false;
                    } else if (maxDepth < curDepth * 0.8f) {
                        depthText = Texts.Yellow(depthText);
                    } else {
                        depthText = Texts.Green(depthText);
                    }
                    depthText = $"{GlyphMap.ArrowDown}{depthText}";
                }
            }
            return isDepthOK;
        }

        public bool CanLaunch(bool showErrors = false) {
            var S = A.S;
            var target = TargetSO.Value;
            if (target.Parent != S.Universe.Player.Parent) {
                if (showErrors) {
                    S.Sig.ShowErrorMessage.Send($"{T.OperationAborted}: {T.ExpeditionTargetTooFarAway}<br><br>{T.Location}: {S.Universe.Player.ParentPath(false)}<br>{T.NounTarget}: {target.ParentPath(false)}");
                }
                return false;
            }
            if (S.Sys.Expedition.HasExpeditionTo(TargetSO.Value)) {
                if (showErrors) {
                    S.Sig.ShowErrorMessage.Send($"{T.OperationFailed}: {T.ExpeditionAlreadyInProgress_V1.F(target.StarmapLink)}");
                }
                return false;
            }
            if (Arrays.IsEmpty(crewCandidates)) {
                if (showErrors) {
                    S.Sig.ShowErrorMessage.Send($"{T.OperationFailed}: {T.ExpeditionCrew} {T.NotSelected}");
                }
                return false;
            }
            if (HasGuestCrew && !crewCandidates.Any(c => !c.IsGuest)) {
                if (showErrors) {
                    S.Sig.ShowErrorMessage.Send($"{T.OperationFailed}: {T.ExpeditionGuestNeedsEscort}");
                }
                return false;
            }
            if (!Shuttle.IsAvailableForExpedition) {
                if (showErrors) {
                    S.Sig.ShowErrorMessage.Send($"{T.OperationFailed}: {T.Shuttle} {GlyphMap.ArrowRight} {T.NotAvailable}");
                }
                return false;
            }
            // ExpeditionStagePrep aborts the run over this anyway - refuse it here so the crew
            // isn't hailed across the ship first
            if (Shuttle.Tile.Health.IsDamaged) {
                if (showErrors) {
                    S.Sig.ShowErrorMessage.Send($"{T.OperationFailed}: {T.ShuttleDamagedDesc}");
                }
                return false;
            }
            if (!CheckDepth(out var curDepth, out var maxDepth, out var depthText, out _, out _)) {
                if (showErrors) {
                    S.Sig.ShowErrorMessage.Send($"{T.OperationFailed}: {T.ExpDepositTooDeep_V2.F(Units.Depth(maxDepth), Units.Depth(curDepth))}");
                }
                return false;
            }
            var rate = EstimateMiningRate(out var ordersOK);
            if (!ordersOK) {
                if (showErrors) {
                    S.Sig.ShowErrorMessage.Send(
                        $"{T.TwitchSettingsInvalid}: {T.MiningOrders}{GlyphMap.BRHR}{T.MinMineRate}: {Texts.Red(MiningOrders.MinRate)}<br>({T.Max}: {(int) rate})");
                }
                return false;
            }
            return true;
        }

        /// <summary>
        /// True when <paramref name="target"/> auto-locks crew via a quest step (a colonist quest
        /// provider or a being required by a quest step target, e.g. a delivery-quest passenger).
        /// Mirrors the detection logic in <see cref="CollectRequiredCrew"/>.
        /// </summary>
        public bool IsQuestCrewTarget(SpaceObject target) {
            if (target == null) { return false; }
            var targets = A.S.Query.GetQuestStepTargets.Ask(target);
            if (targets.Length == 0) { return false; }
            for (int i = 0; i < targets.Length; i++) {
                var t = targets[i];
                if (t.Step?.Data?.Provider is ColonistQuestProvider) { return true; }
                if (t.RequiredCrewMindId != 0) { return true; }
            }
            return false;
        }

        /// <summary>
        /// Clears the current crew selection so the next <see cref="AutofillCrew"/> reselects
        /// from scratch. Used when the target crosses a quest/non-quest crew boundary.
        /// </summary>
        public void ResetCrewSelection() {
            crewCandidates?.Clear();
            lastAutofillTarget = null;
        }

        private void CollectRequiredCrew(GameState S) {
            RequiredCrewMindIds?.Clear();
            if (TargetSO.Value == null) { return; }
            var targets = S.Query.GetQuestStepTargets.Ask(TargetSO.Value);
            for (int i = 0; i < targets.Length; i++) {
                var t = targets[i];
                if (t.Step?.Data?.Provider is ColonistQuestProvider cp) {
                    RequiredCrewMindIds ??= new HashSet<uint>();
                    RequiredCrewMindIds.Add(cp.Mind.Id);
                }
                // A target may require a specific being (e.g. a delivery quest passenger).
                if (t.RequiredCrewMindId != 0) {
                    RequiredCrewMindIds ??= new HashSet<uint>();
                    RequiredCrewMindIds.Add(t.RequiredCrewMindId);
                }
            }
        }

        public void AutofillCrew() {
            if (Shuttle == null) { return; }
            var S = A.S;
            var target = TargetSO.Value;
            bool targetChanged = target != lastAutofillTarget;
            lastAutofillTarget = target;
            CollectRequiredCrew(S);
            bool hasRequired = RequiredCrewMindIds != null && RequiredCrewMindIds.Count > 0;
            if (hasRequired) {
                // Quest target: pre-fill with exactly the required member(s). On a
                // genuine target change, strip any previously auto-filled crew so
                // nobody other than the quest member(s) is pre-loaded. On a
                // same-target reload, keep the player's manual additions.
                crewCandidates ??= new List<BeingData>();
                if (targetChanged) {
                    crewCandidates.RemoveAll(c => !RequiredCrewMindIds.Contains(c.Mind.Id));
                }
                EnsureRequiredCrewPresent(S);
                return;
            }
            if (!Arrays.IsEmpty(crewCandidates)) {
                return;
            }
            var lastCrewIds = Shuttle.GetLastCrewIds();
            if (!Arrays.IsEmpty(lastCrewIds)) {
                TryRestoreLastCrew(S, lastCrewIds);
            } else {
                AutofillCrewDefault(S);
            }
        }

        private void EnsureRequiredCrewPresent(GameState S) {
            if (RequiredCrewMindIds == null || RequiredCrewMindIds.Count == 0) { return; }
            crewCandidates ??= new List<BeingData>();
            foreach (var mindId in RequiredCrewMindIds) {
                bool alreadyIn = false;
                foreach (var c in crewCandidates) {
                    if (c.Mind.Id == mindId) { alreadyIn = true; break; }
                }
                if (alreadyIn) { continue; }
                foreach (var b in S.Beings.Alive) {
                    if (b.Data.Mind.Id == mindId && b.IsAlive) {
                        crewCandidates.Insert(0, b.Data);
                        break;
                    }
                }
            }
        }

        // A crew member may only be auto-preloaded if they can actually depart right now:
        // eligible for the expedition, not already committed elsewhere, not tired/hungry, and
        // able to physically path to the shuttle's boarding port to board it.
        private bool CanAutofillCrew(GameState S, Being b) {
            return BeingUtils.CanBeSentOnExpedition(b, IsExploration)
                && !S.Sys.Expedition.IsOnExpedition(b.Data, null)
                && !BeingUtils.IsTired(b.Data)
                && Shuttle != null
                && EntityUtils.CanReach(b, Shuttle.InPort);
        }

        private void TryRestoreLastCrew(GameState S, int[] lastCrewIds) {
            crewCandidates ??= new List<BeingData>();

            var replacementPool = BuildReplacementPool(S);
            var committed = new HashSet<uint>();

            foreach (var savedIdInt in lastCrewIds) {
                if (crewCandidates.Count >= Shuttle.Capacity) { break; }
                var savedId = (uint) savedIdInt;
                Being found = null;
                foreach (var b in S.Beings.Alive) {
                    if (b.Data.Id == savedId) {
                        found = b;
                        break;
                    }
                }
                bool canUse = found != null && CanAutofillCrew(S, found);
                if (canUse && committed.Add(savedId)) {
                    crewCandidates.Add(found.Data);
                } else {
                    for (int i = 0; i < replacementPool.Count; i++) {
                        if (committed.Add(replacementPool[i].Data.Id)) {
                            crewCandidates.Add(replacementPool[i].Data);
                            break;
                        }
                    }
                }
            }
        }

        private List<Being> BuildReplacementPool(GameState S) {
            var pool = new List<Being>();
            foreach (var b in S.Beings.Alive) {
                if (!CanAutofillCrew(S, b)) { continue; }
                pool.Add(b);
            }
            if (IsExploration) {
                return pool.OrderByDescending(
                    b => S.Sys.Expedition.LastExpeditionTimeFor(b.Data.Id)).ToList();
            }
            return pool.OrderByDescending(
                    b => S.Sys.Expedition.LastExpeditionTimeFor(b.Data.Id))
                .ThenByDescending(b => b.Data.Mind.Jobs.LevelOf(JobTypeIdH.Mining))
                .ToList();
        }

        private void AutofillCrewDefault(GameState S) {
            var candidates = S.Beings.Alive;
            var validCandidates = new List<Being>();
            foreach (var b in candidates) {
                if (!CanAutofillCrew(S, b)) {
                    continue;
                }
                validCandidates.Add(b);
            }
            var validCount = validCandidates.Count;
            if (validCount < 3) { return; }
            var sortedCandidates = validCandidates.OrderByDescending(
                b => S.Sys.Expedition.LastExpeditionTimeFor(b.Data.Id));
            if (!IsExploration) {
                sortedCandidates = sortedCandidates.ThenByDescending(
                    w => w.Data.Mind.Jobs.LevelOf(JobTypeIdH.Mining));
            }
            crewCandidates ??= new List<BeingData>();
            foreach (var b in sortedCandidates) {
                crewCandidates.Add(b.Data);
                var sc = crewCandidates.Count;
                if (sc == Shuttle.Capacity) {
                    break;
                }
                // Always leave at least 2 space capable workers
                if (crewCandidates.Count == validCount - 2) {
                    break;
                }
            }
        }

        public string LaunchTip() {
            var sb = Caches.StringBuilder3;
            sb.Clear();
            sb.Append(Texts.SubTitle(T.NounChecklist));
            var S = A.S;
            bool hasIssues = false;
            var target = TargetSO.Value;
            if (target?.Parent != null) {
                if (target.Parent != S.Universe.Player.Parent) {
                    sb.Append(Texts.Accented(GlyphMap.ArrowRight));
                    sb.Append(' ');
                    sb.AppendFormat("{0}! {1}: {2}, {3}: {4}\n",
                        T.ExpeditionTargetTooFarAway, T.Location,
                            S.Universe.Player.ParentPath(false),
                            T.NounTarget,
                            target.ParentPath(false));
                    hasIssues = true;
                }
            } else {
                sb.Append(Texts.Accented(GlyphMap.ArrowRight));
                sb.Append(' ');
                sb.AppendLine(T.NoDestination);
                hasIssues = true;
            }
            if (Arrays.IsEmpty(crewCandidates)) {
                sb.Append(Texts.Accented(GlyphMap.ArrowRight));
                sb.Append(' ');
                sb.AppendLine($"{T.ExpeditionCrew}: {T.NotSelected}");
                hasIssues = true;
            }
            if (HasGuestCrew && !crewCandidates.Any(c => !c.IsGuest)) {
                sb.Append(Texts.Accented(GlyphMap.ArrowRight));
                sb.Append(' ');
                sb.AppendLine(T.ExpeditionGuestNeedsEscort);
                hasIssues = true;
            }
            if (S.Sys.Expedition.HasExpeditionTo(TargetSO.Value)) {
                sb.Append(Texts.Accented(GlyphMap.ArrowRight));
                sb.Append(' ');
                sb.AppendLine(T.ExpeditionAlreadyInProgress_V1.F(target.StarmapLink));
                hasIssues = true;
            }
            if (!Shuttle.IsAvailableForExpedition) {
                sb.Append(Texts.Accented(GlyphMap.ArrowRight));
                sb.Append(' ');
                sb.AppendFormat("{0}: {1}\n", T.Shuttle, T.NotAvailable);
                hasIssues = true;
            }
            if (Shuttle.Tile.Health.IsDamaged) {
                sb.Append(Texts.Accented(GlyphMap.ArrowRight));
                sb.Append(' ');
                sb.AppendLine(T.ShuttleDamaged);
                hasIssues = true;
            }
            if (!CheckDepth(out var curDepth, out var maxDepth, out var depthText, out _, out _)) {
                sb.Append(Texts.Accented(GlyphMap.ArrowRight));
                sb.Append(' ');
                sb.AppendLine($"{T.ExpDepositTooDeep_V2.F(Units.Depth(maxDepth), depthText).Replace("<br><br>", "\n")}");
                hasIssues = true;
            }
            var rate = EstimateMiningRate(out var ordersOK);
            if (!ordersOK) {
                sb.Append(Texts.Accented(GlyphMap.ArrowRight));
                sb.Append(' ');
                sb.AppendLine($"{T.AdjIncompatible}: {T.MinMineRate} ({Texts.Red(MiningOrders.MinRate)}) > {T.ExpectedMineRate} ({(int) rate})");
                hasIssues = true;
            }

            if (!hasIssues) {
                sb.Append(Texts.Green(GlyphMap.ArrowRight));
                sb.Append(' ');
                sb.Append(T.Ready);
            }

            return sb.ToString();
        }

        public string DestinationTip(SpaceObject so) {
            CheckDepth(out _, out _, out var depthText,
                    out var diffTxt, out int ownedCnt, so);
            var sb = Caches.StringBuilder4;
            sb.Clear();
            sb.AppendLine(T.AvailableCount_V1.F(Units.NumColoredAbbr(ownedCnt)));
            if (diffTxt != null) {
                sb.Append(diffTxt);
            }
            if (depthText != null) {
                sb.Append(' ');
                sb.Append(depthText);
            }
            return sb.ToString();
        }
    }
}
