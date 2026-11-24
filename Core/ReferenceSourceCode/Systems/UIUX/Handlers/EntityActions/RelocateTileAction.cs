using System;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Tools;
using Game.Utils;
using KL.Grid;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class RelocateTileAction : BaseEntityAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new RelocateTileAction());
        }
        public const string ActionId = "RelocateTile";
        public override string Id => ActionId;
        public override string HotkeyLabel => T.Relocate;
        public override string HotkeyId => "Relocate";
        public override string HotkeyDefault => "<Keyboard>/g";
        protected override bool ApplyFor(GameState s, Tile tile,
                out EntityActionData data) {
            tile = EntityUtils.ParentObj(tile);

            if (!tile.IsConstructed || tile.Reinstallable == null) {
                data = EntityActionData.Unavailable;
                return false;
            }
            var re = tile.Reinstallable;
            Priority priority;
            var applicable = true;
            if (re.IsAdvanced && !s.Research.IsUnlocked(
                    DefIdsH.ResearchConstructionRelocationAdvanced)) {
                applicable = false;
            } else if (!re.CanOrderRelocationNow) {
                applicable = false;
            }
            if (applicable) {
                priority = Priority.Normal;
            } else {
                // can't relocate now
                priority = Priority.Low;
            }
            data = new EntityActionData(this, (l, p, i) => Create(l, p, i, applicable, re),
                priority);
            return true;
        }

        private WBase Create(Lifetime l, RectTransform p, int i, bool applicable, ReinstallableComp re) {
            var widget = SpawnButton(l, applicable, p, i);
            var text = T.Relocate;
            if (!applicable) {
                text = Texts.Strike(text);
            }
            widget.Load(IconId.CMove, text, () => re.BeginRelocation());
            return widget;
        }

        private WBase CreateMulti(Lifetime l, RectTransform p, int i, List<Tile> tiles, int cnt) {
            var widget = SpawnButtonMulti(l, true, p, i);
            widget.LoadMulti(cnt, IconId.CMove, T.Relocate,
                () => RelocateUtils.Relocate(tiles, false, out _));
            return widget;
        }

        protected override bool ApplyForMany(GameState s, List<Tile> tiles,
                out EntityActionData data) {
            if (!RelocateUtils.Relocate(tiles, true, out var cnt)) {
                data = EntityActionData.Unavailable;
                return false;
            }

            data = new EntityActionData(this,
                (l, p, i) => CreateMulti(l, p, i, new List<Tile>(tiles), cnt));

            return true;
        }


    }
}
