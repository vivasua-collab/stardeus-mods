using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Systems.Diplomacy;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class WFaction : WBase {
        [SerializeField] private WImage wIcon;
        [SerializeField] private WText wName;
        [SerializeField] private WText wAlliesLabel;
        [SerializeField] private WText wEnemiesLabel;
        [SerializeField] private WProgressText wProgress;
        [SerializeField] private WGroup wAlliesContainer;
        [SerializeField] private WGroup wEnemiesContainer;
        [SerializeField] private GameObject goAllies;
        [SerializeField] private GameObject goEnemies;
        [SerializeField] private GameObject goButtons;
        [SerializeField] private WButtonText btnRed;
        [SerializeField] private WButtonText btnAction;

        private readonly List<FactionDef> factionsCache = new();
        private GameState S;
        private FactionDef faction;
        private Action onActed;
        /// <summary>
        /// The player's own row is informational only: it lists who we are allied with and
        /// who we are at war with. Standing with ourselves and the diplomatic actions are
        /// both meaningless there.
        /// </summary>
        private bool isPlayerRow;

        public FactionDef Faction => faction;
        public WGroup Allies => wAlliesContainer;
        public WGroup Enemies => wEnemiesContainer;

        public void Load(GameState s, Lifetime life, FactionDef fac, Action onActed) {
            S = s;
            faction = fac;
            this.onActed = onActed;
            isPlayerRow = fac.IsPlayer;
            wIcon.Load(fac.FlagIcon);
            wName.Load(fac.NameT);
            // This widget is pooled, so both states must be set explicitly - an instance
            // that previously held the player row can be reused for an NPC row.
            wProgress.SetActive(!isPlayerRow);
            if (!isPlayerRow) {
                var standing = s.Query.FactionAttitude.Ask(fac, FactionDef.Player);
                var colorId = standing > 0.25f
                    ? "Green"
                    : standing < -0.25f
                        ? "Red"
                        : "Blue";
                wProgress.SetFillColor(The.UITheme.GetColor(colorId, this));
                wProgress.Load(Units.PercentageSigned(standing), (standing + 1f) * 0.5f);
                var pct = Units.PercentageSigned(standing, colored: true);
                UIUtils.AddTip(life, wProgress, $"{T.RelationshipWith_V1.F(fac.NameT)}: {pct}");
            }

            var relations = s.Sys.Diplomacy.Relations;
            relations.CollectAllies(fac, factionsCache);
            LoadFactionIcons(life, wAlliesContainer, goAllies, wAlliesLabel, T.NounAllies);
            relations.CollectEnemies(fac, factionsCache);
            LoadFactionIcons(life, wEnemiesContainer, goEnemies, wEnemiesLabel, T.NounEnemies);

            if (!isPlayerRow) {
                // Must be re-registered on every Load: tooltips belong to the lifetime,
                // which the widget disposes on rebuild and on close, while this pooled
                // WFaction instance survives. The factories are dynamic, so RefreshActions
                // never needs to touch them.
                UIUtils.AddTipFunc(life, btnAction, ActionTip);
                UIUtils.AddTipFunc(life, btnRed, BreakTip);
                btnAction.Load(T.DiplomacyProposeAlliance, OnProposeClicked);
                btnRed.Load(T.DiplomacyBreakAlliance, OnBreakClicked);
            }
            RefreshActions();
        }

        private void LoadFactionIcons(Lifetime life, WGroup container, GameObject block,
                WText label, string labelText) {
            if (factionsCache.Count == 0) {
                block.SetActiveFast(false);
                return;
            }
            block.SetActiveFast(true);
            label.Load(labelText);
            for (int i = 0; i < factionsCache.Count; i++) {
                var other = factionsCache[i];
                var icon = UISpawn.Widget<WImage>(life, UIPrefabId.WStatusIcon,
                    container.Container, i);
                icon.Load(other.FlagIcon);
                UIUtils.AddTip(life, icon, other.NameT);
            }
        }

        /// <summary>
        /// Re-evaluates only the two buttons. Cheap enough to run on a timer, and it does
        /// not respawn the icon grids or disturb the scroll position.
        /// </summary>
        public void RefreshActions() {
            if (faction == null) { return; }
            if (isPlayerRow) {
                btnAction.SetActive(false);
                btnRed.SetActive(false);
                goButtons.SetActiveFast(false);
                return;
            }
            var diplomacy = S.Sys.Diplomacy;
            var player = FactionDef.Player;
            var isAllied = diplomacy.Relations.Get(faction, player) == FactionRelation.Allied;
            // Propose stays visible while not allied even when it cannot be used, so its
            // tooltip can explain why.
            btnAction.SetActive(!isAllied);
            if (!isAllied) {
                btnAction.SetInteractable(diplomacy.CanProposeAlliance(faction, out _));
            }
            btnRed.SetActive(isAllied);
            if (isAllied) {
                btnRed.SetInteractable(
                    !diplomacy.Relations.IsAsimovAllianceActive(faction, player));
            }
            goButtons.SetActiveFast(btnAction.gameObject.activeSelf || btnRed.gameObject.activeSelf);
        }

        private string ActionTip() {
            var diplomacy = S.Sys.Diplomacy;
            if (!diplomacy.CanProposeAlliance(faction, out var reason)) {
                return reason;
            }
            return T.DiplomacyProposeChance_V1.F(
                Units.Percentage(diplomacy.AllianceAcceptanceChance(faction)));
        }

        private string BreakTip() {
            var diplomacy = S.Sys.Diplomacy;
            if (diplomacy.Relations.IsAsimovAllianceActive(faction, FactionDef.Player)) {
                return T.DiplomacyAsimovAlliance;
            }
            return T.DiplomacyBreakAllianceConfirm_V1.F(faction.NameT);
        }

        private void OnProposeClicked() {
            var diplomacy = S.Sys.Diplomacy;
            // The button is disabled when a proposal is not allowed, so a false result
            // below can only mean the faction turned us down.
            if (!diplomacy.CanProposeAlliance(faction, out _)) { return; }
            var accepted = diplomacy.TryProposeAlliance(faction);
            var response = accepted
                ? T.DiplomacyAllianceAcceptedPopup_V1.F(faction.NameT)
                : T.DiplomacyAllianceRejectedPopup_V1.F(faction.NameT);
            WPopup.Create(false, faction.FlagIcon, T.DiplomacyProposeAlliance, response)
                .Spawn();
            // A successful proposal changes a relation and rebuilds the list through
            // FactionRelationChanged. A rejected one changes nothing but the cooldown,
            // so only the buttons need refreshing.
            onActed?.Invoke();
        }

        private void OnBreakClicked() {
            var capturedFaction = faction;
            WPopup.Create(false, IconId.CWarning, T.DiplomacyBreakAlliance,
                    T.DiplomacyBreakAllianceConfirm_V1.F(capturedFaction.NameT))
                .WithAction(T.Confirm, () => {
                    S.Sys.Diplomacy.BreakAlliance(capturedFaction);
                    onActed?.Invoke();
                })
                .WithAction2(T.Cancel, null)
                .Spawn();
        }
    }
}
