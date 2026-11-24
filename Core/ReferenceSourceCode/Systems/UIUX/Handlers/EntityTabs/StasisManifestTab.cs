using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    // Who is asleep in this array. Browse only: there is deliberately no way to wake or
    // eject a specific person from here, who wakes next is not the player's pick.
    public sealed class StasisManifestTab : BaseEntityTab {
        public const string TabId = "StasisManifest";

        [SelfInit]
        private static void Register() {
            RegisterTab(new StasisManifestTab());
        }

        private StasisArrayComp stasis;
        private GameState S;
        private WGroup contentsBlock;
        private Lifetime contentsLifetime;
        private readonly List<BeingData> sleepers = new();
        #if !PRODUCTION_BUILD
        // For tests: the list must not be rebuilt by stability or wake-up progress changes
        public static int RebuildCount;
        #endif

        public override string Id => TabId;
        public override int Order => 20;
        public override string TabName => T.StasisManifestTab;

        // Unlike the Stasis Array tab, this one does not wait for the array to be reachable
        // by the grid. It only reads, and the opening of a game - array not wired up yet,
        // stability ticking down - is exactly when the player should see who is in there.
        public override bool SuppressWarningOverlay => true;

        public override bool ShowFor(GameState s, Tile tile) {
            if (!tile.IsConstructed) { return false; }
            if (!tile.TryGetComponent(out stasis)) { return false; }
            S = s;
            return true;
        }

        public override bool LoadTopControls(WGroup group) {
            ManifestSys.For(S).EnsurePopulated(stasis);
            var block = UISpawn.Block(lifetime, group, 0);
            var header = UISpawn.Widget<WProgressWithLabel>(lifetime,
                UIPrefabId.WContent2HeaderProgress, block.Container, 0);
            header.Label.Load(T.StasisManifestAsleep);
            UIUtils.AddTip(lifetime, header.Progress, T.StasisManifestAsleepDesc);
            UpdateHeader(header);
            var array = stasis;
            array.ManifestChangedB.Bind(lifetime, () => UpdateHeader(header, array), false);
            return true;
        }

        private void UpdateHeader(WProgressWithLabel header) {
            UpdateHeader(header, stasis);
        }

        private static void UpdateHeader(WProgressWithLabel header, StasisArrayComp array) {
            float pct = array.Capacity > 0
                ? (float) array.BeingCount / array.Capacity
                : 0f;
            header.Progress.SetPercent(pct, false);
            header.Progress.SetText(Units.XOutOfY(array.BeingCount, array.Capacity));
        }

        public override bool LoadContent(WGroup group) {
            contentsBlock = UISpawn.Block(lifetime, group, 0);
            BuildList();
            // Not StateChangedB: that one fires on every stability percent and wake-up
            // progress step, and each rebuild redraws every avatar and resets the scroll
            stasis.ManifestChangedB.Bind(lifetime, BuildList, false);
            return true;
        }

        private void BuildList() {
            if (contentsBlock == null) { return; }
            #if !PRODUCTION_BUILD
            RebuildCount++;
            #endif
            if (contentsLifetime != null) {
                contentsLifetime.Dispose();
            }
            contentsLifetime = lifetime.CreateChild("StasisManifest.Contents");
            var container = contentsBlock.Container;
            int idx = 0;

            ManifestSys.For(S).FindSleepers(stasis, sleepers);
            if (sleepers.Count == 0) {
                var empty = UISpawn.Widget<WIconText>(contentsLifetime,
                    UIPrefabId.WContent2Text, container, idx++);
                empty.Load(IconId.CInfo, T.StasisManifestEmpty);
                return;
            }
            var mode = BeingData.BeingSortMode;
            if (mode == BeingData.SortMode.ConditionAsc || mode == BeingData.SortMode.ConditionDesc) {
                // The shared sort orders by body part quality, which is mostly age. Here
                // "condition" has to mean the same thing the bar on the row shows.
                int dir = mode == BeingData.SortMode.ConditionAsc ? 1 : -1;
                sleepers.Sort((a, b) => {
                    var byHealth = Health(a).CompareTo(Health(b)) * dir;
                    return byHealth != 0 ? byHealth : string.CompareOrdinal(
                        a.Mind.Name.ShortName, b.Mind.Name.ShortName);
                });
                foreach (var bd in sleepers) {
                    SpawnRow(contentsLifetime, container, ref idx, bd);
                }
            } else {
                foreach (var bd in BeingData.SortedBy(S, sleepers, mode)) {
                    SpawnRow(contentsLifetime, container, ref idx, bd);
                }
            }
            sleepers.Clear();
        }

        // How much of the body is unharmed. Not body part quality, which the being storage
        // tab shows: quality is driven by age, so a healthy 70 year old would sit at 40% from
        // the first minute and the damage an unstable array does would be lost in the noise.
        public static float Health(BeingData data) {
            return 1f - data.Body.InjuryPercent;
        }

        private static void SpawnRow(Lifetime life, RectTransform container, ref int idx,
                BeingData data) {
            var w = UISpawn.Widget<WButtonIconText2Progress>(life,
                UIPrefabId.WContent2ProgressButton, container, idx++);
            w.Load(null, BeingUtils.DecoratedBeingName(data, false),
                Health(data), () => BeingUtils.Select(data, false));
            data.Avatar.Load(w.Item.Image);
            UIUtils.AddTipFunc(life, w, () => BeingUtils.BodyTooltip(data, true));
        }

        public override bool LoadFooter(WGroup group) {
            var sort = UISpawn.Widget<WDropdown>(lifetime,
                UIPrefabId.WDropdownWithFrame, group.Container, 0);
            sort.Load(BeingData.SortModeOptions,
                (int) BeingData.BeingSortMode, (i) => {
                    BeingData.BeingSortMode = (BeingData.SortMode) i;
                    BuildList();
                });
            UIUtils.AddTip(lifetime, sort, T.Sort);
            return true;
        }

        protected override void Cleanup() {
            stasis = null;
            S = null;
            contentsBlock = null;
            contentsLifetime = null;
            sleepers.Clear();
        }
    }
}
