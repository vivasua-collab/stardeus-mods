using System;
using System.Collections.Generic;
using System.Text;
using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Research;
using Game.Tools;
using Game.Utils;
using KL.Grid;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class CopyConfigAction : BaseEntityAction {
        public const string ActionId = "CopyConfig";
        [SelfInit]
        private static void Register() {
            RegisterAction(new CopyConfigAction());
        }
        public override string Id => ActionId;
        public override string HotkeyLabel => T.CopyConfigShort;
        public override string HotkeyDefault => "<Keyboard>/v";

        private readonly List<ICopyableComp> copyables = new();

        protected override bool ApplyFor(GameState s, Tile tile, out EntityActionData data) {
            copyables.Clear();
            if (tile.ENode != null && !tile.ENode.IsReachable) {
                data = EntityActionData.Unavailable;
                return false;
            }
            foreach (var comp in tile.Components) {
                if (comp is ICopyableComp copyable && copyable.IsCopyable) {
                    copyables.Add(copyable);
                }
            }
            if (copyables.Count > 0) {
                data = new EntityActionData(this, (l, p, i) => Create(l, p, i, tile), Priority.Low, 1);
                return true;
            }
            data = EntityActionData.Unavailable;
            return false;
        }

        private WBase Create(Lifetime lifetime, RectTransform p, int i, Tile tile) {
            var widget = SpawnButton(lifetime, true, p, i);
            var title = T.CopyConfigShort;
            // if (copyables.Count > 1) {
            //     title = $"{title} ({Units.XNum(copyables.Count)})";
            // } else {
            //     title = $"{title} ({copyables[0].CopyConfigText})";
            // }
            widget.Load(IconId.CCopySettings, title, () => {
                ToolCopyConfig.SwitchToCopy(copyables);
            });
            return widget;
        }

        protected override void Unload() {
            copyables.Clear();
        }
    }

}
