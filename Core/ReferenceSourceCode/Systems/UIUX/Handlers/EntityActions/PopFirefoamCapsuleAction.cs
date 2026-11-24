using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    // Pops Firefoam Capsules by hand after a confirm, the way DetonateNowAction sets off
    // explosives, with the same "Detonate now" label
    public sealed class PopFirefoamCapsuleAction : BaseEntityAction {
        public const string ActionId = "PopFirefoamCapsule";

        [SelfInit]
        private static void Register() {
            RegisterAction(new PopFirefoamCapsuleAction());
        }

        public override string Id => ActionId;

        protected override bool ApplyFor(GameState s, Tile tile, out EntityActionData data) {
            tile = EntityUtils.ParentObj(tile);
            if (!CanPop(tile, out _)) {
                data = EntityActionData.Unavailable;
                return false;
            }
            // the capsule's own action, in front of the generic ones (Build a Copy, Relocate)
            data = new EntityActionData(this,
                (l, p, i) => Create(l, p, i, s, tile), Priority.First);
            return true;
        }

        protected override bool ApplyForMany(GameState s, List<Tile> tiles,
                out EntityActionData data) {
            if (!Pop(tiles, true, out var cnt)) {
                data = EntityActionData.Unavailable;
                return false;
            }
            data = new EntityActionData(this,
                (l, p, i) => CreateMulti(l, p, i, s, new List<Tile>(tiles), cnt),
                Priority.First);
            return true;
        }

        private WBase Create(Lifetime l, RectTransform p, int i, GameState s, Tile tile) {
            var widget = SpawnButton(l, true, p, i);
            widget.Load(IconId.CFire, T.BombDetonateNow, () => {
                var one = new List<Tile>(1) { tile };
                ShowConfirm(s, one, widget);
            });
            return widget;
        }

        private WBase CreateMulti(Lifetime l, RectTransform p, int i, GameState s,
                List<Tile> tiles, int cnt) {
            var widget = SpawnButtonMulti(l, true, p, i);
            widget.LoadMulti(cnt, IconId.CFire, T.BombDetonateNow,
                () => ShowConfirm(s, tiles, widget));
            return widget;
        }

        private static void ShowConfirm(GameState s, List<Tile> tiles, WBase anchor) {
            var qc = QuickChoice.Create(T.BombDetonateNow);
            qc.Widget = anchor.Rect;
            qc.AddButton(T.Confirm, () => {
                // the spray grows in on the game clock
                if (s.Clock.Speed == ClockSpeed.Stopped) {
                    var sp = s.Clock.SetSpeed(ClockSpeed.Normal);
                    UISounds.PlayClockChange(sp);
                }
                Pop(tiles, false, out _);
            });
            qc.Show();
        }

        private static bool Pop(List<Tile> tiles, bool dryRun, out int cnt) {
            int c = 0;
            foreach (var t in tiles) {
                if (!CanPop(EntityUtils.ParentObj(t), out var capsule)) { continue; }
                c++;
                if (!dryRun) {
                    capsule.Pop();
                }
            }
            cnt = c;
            if (cnt > 0 && !dryRun) {
                UIUtils.RefreshSelectedEntityUI();
            }
            return cnt > 0;
        }

        private static bool CanPop(Tile tile, out FirefoamCapsuleComp capsule) {
            capsule = null;
            if (tile == null || !tile.IsActive || !tile.TryGetComponent(out capsule)) {
                return false;
            }
            return capsule.CanPop;
        }
    }
}
