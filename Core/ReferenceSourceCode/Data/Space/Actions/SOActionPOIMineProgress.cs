using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Game.Constants;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;
using Game.Systems.UIUX;

namespace Game.Data.Space {
    public sealed class SOActionPOIMineProgress : SOAction, ITickListener {
        public const string ActionId = "POIMineProgress";
        [SelfInit]
        private static void Register() {
            Register(ActionId, new SOActionPOIMineProgress());
        }
        private B<string> textB;

        protected override void OnFlush() {
            textB = null;
        }


        public override bool IsAvailable(SOActionContext ctx) {
            if (ctx.S.Sys.Planets.TryGetResourceFor(ctx.SO, out var resource, out _)) {
                return resource.ManualMineFactor > 0f;
            }
            return false;
        }

        public override bool AddOptions(QuickChoice qc) {
            var prog = new QuickChoice.DynamicTextIcon {
                Icon = Images.Sprite(GraphicsId.ObjectsDevicesShuttleFlying_R),
                TextB = textB = new B<string>(string.Empty),
            };
            Tick(0);
            qc.AddDynamicTextIcon(textB.Value, prog);
            return true;
        }

        public void Tick(long currentTick) {
            if (textB == null) { return; }
            if (ctx.S.Sys.Planets.TryGetMiningRate(ctx.SO,
                    out _, out var rateWithOutputMul, out var mt, out var depth)) {
                var txt = mt.Deposit.MineText(rateWithOutputMul, Units.Depth(depth));
                textB.Set(txt);
            } else {
                textB.Set(string.Empty);
            }
        }

        public override string Id => ActionId;

        public override int UIPriority => 181;
    }
}
