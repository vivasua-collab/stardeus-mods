using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Systems.AI.Combat;
using Game.Systems.BodyParts;
using Game.Systems.Creatures;
using Game.UI;
using Game.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    // Code-only popup (no scene prefab), so it is owned by a widgetless handler like
    // CodexPopupHandler rather than a BaseWidget.
    public sealed class CombatControlsHandler {
        private const float refreshInterval = 0.5f;

        private readonly GameState S;
        private Lifetime lifetime;
        private Lifetime contentLife;
        private WPanel widget;
        private Vector2? widgetPos;
        private Vector2 widgetSize;
        private float nextRefreshAt;
        private BeingsSys beings;

        private readonly List<CombatControlsRow> rows = new();
        private readonly HashSet<uint> shownIds = new();
        private readonly List<BeingData> eligible = new();
        private readonly List<Species> speciesOrder = new();
        private readonly Dictionary<Species, List<BeingData>> beingsBySpecies = new();

        public CombatControlsHandler(GameState s) {
            S = s;
            s.UIQuery.ToggleCombatControls.SetResponder(Toggle);
        }

        public void Cleanup() {
            Close();
        }

        private bool Toggle(bool show) {
            if (show) {
                Show();
            } else {
                Close();
            }
            return true;
        }

        private void Show() {
            if (lifetime != null) {
                widget.Rect.SetAsLastSibling();
                return;
            }
            beings = S.Sys.Get<BeingsSys>(BeingsSys.SysId);
            lifetime = Lifetime.Create("CombatControls");
            widget = UISpawn.Widget<WPanel>(lifetime, UIPrefabId.WListChoicePopup,
                UIPoolRoot.PopupContainer, 0);
            widget.Rect.SetAsLastSibling();
            widget.Load(T.CombatControls, Close);
            widget.ShowFooter(false);
            UIShowing.Set(UIShowingFlags.CombatControls, true);

            CollectEligible();
            RebuildContent(true);

            if (widgetPos.HasValue) {
                widget.Rect.anchoredPosition = widgetPos.Value;
                widget.Rect.sizeDelta = widgetSize;
            } else {
                UIUtils.AfterShowPopup(widget.Rect, true);
            }
            nextRefreshAt = Time.unscaledTime + refreshInterval;
            S.Sig.EveryFrame.AddListener(EveryFrame);
        }

        private void Close() {
            if (lifetime == null) {
                return;
            }
            S.Sig.EveryFrame.RemoveListener(EveryFrame);
            widgetPos = widget.Rect.anchoredPosition;
            widgetSize = widget.Rect.sizeDelta;
            rows.Clear();
            shownIds.Clear();
            contentLife = null;
            widget = null;
            lifetime.Dispose();
            lifetime = null;
            UIShowing.Set(UIShowingFlags.CombatControls, false);
        }

        private void EveryFrame() {
            // unscaled, so the list keeps up with changes made while the game is paused
            var now = Time.unscaledTime;
            if (now < nextRefreshAt) {
                return;
            }
            nextRefreshAt = now + refreshInterval;
            Refresh();
        }

        private void Refresh() {
            CollectEligible();
            if (!IsSameAsShown()) {
                RebuildContent(false);
                return;
            }
            for (int i = 0; i < rows.Count; i++) {
                rows[i].Update();
            }
        }

        private void CollectEligible() {
            eligible.Clear();
            foreach (var b in beings.All.Values) {
                if (IsEligible(b)) {
                    eligible.Add(b);
                }
            }
        }

        // On-map player units only: a reaction lives on the CombatUnit, which exists only
        // while the being is spawned (see BeingRoster.md)
        private static bool IsEligible(BeingData b) {
            if (!b.IsPlayer || b.IsGuest) { return false; }
            if (b.HasStateFlag(BeingState.Dead)) { return false; }
            if (b.Body.HasFlag(BodyFlags.Missing)) { return false; }
            var being = b.Being;
            if (being == null || !being.IsActive) { return false; }
            return being.TryGetComponent<CombatUnitComp>(out _);
        }

        private bool IsSameAsShown() {
            if (eligible.Count != shownIds.Count) {
                return false;
            }
            for (int i = 0; i < eligible.Count; i++) {
                if (!shownIds.Contains(eligible[i].Id)) {
                    return false;
                }
            }
            return true;
        }

        private void RebuildContent(bool scrollToTop) {
            contentLife?.Dispose();
            contentLife = lifetime.CreateChild("CombatControlsContent", capacity: 128);
            rows.Clear();
            shownIds.Clear();

            GroupBySpecies();
            int blockIdx = 0;
            if (speciesOrder.Count == 0) {
                var block = UISpawn.Block(contentLife, widget.Content, blockIdx);
                var empty = UISpawn.Widget<WIconText2>(contentLife,
                    UIPrefabId.WContent2Text2, block.Container, 0);
                empty.Load(IconId.CInfo, T.NotAvailable, null);
            }
            foreach (var species in speciesOrder) {
                var block = UISpawn.Block(contentLife, widget.Content, blockIdx++);
                int idx = 0;
                var header = UISpawn.Widget<WText>(contentLife, UIPrefabId.WContent2Header,
                    block.Container, idx++);
                header.Load(species.Name);
                foreach (var b in beingsBySpecies[species]) {
                    rows.Add(new CombatControlsRow(contentLife, block.Container, idx++, b));
                    shownIds.Add(b.Id);
                }
            }
            UIUtils.RebuildScroll(widget.Scroll, scrollToTop);
        }

        private void GroupBySpecies() {
            speciesOrder.Clear();
            foreach (var list in beingsBySpecies.Values) {
                list.Clear();
            }
            foreach (var b in eligible) {
                if (!beingsBySpecies.TryGetValue(b.Species, out var list)) {
                    list = new List<BeingData>();
                    beingsBySpecies[b.Species] = list;
                }
                if (list.Count == 0) {
                    speciesOrder.Add(b.Species);
                }
                list.Add(b);
            }
            speciesOrder.Sort(CompareSpecies);
            foreach (var sp in speciesOrder) {
                beingsBySpecies[sp].Sort(CompareBeing);
            }
        }

        private static int CompareSpecies(Species a, Species b) {
            int cmp = b.IsBiological.CompareTo(a.IsBiological);
            if (cmp != 0) { return cmp; }
            return string.Compare(a.Name, b.Name, StringComparison.Ordinal);
        }

        private static int CompareBeing(BeingData a, BeingData b) {
            return string.Compare(a.Name, b.Name, StringComparison.Ordinal);
        }
    }
}
