using System.Collections.Generic;
using System.Linq;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    // "Locked -> N" row under a choice list, its tooltip links each locked option to its research
    public static class ResearchLockedRow {
        public static WIconText Spawn(Lifetime life, RectTransform container, int idx,
                List<Def> locked) {
            // The caller's list is usually a shared cache that gets poisoned before the
            // tooltip runs, so keep a copy
            var lockedArr = locked.ToArray();
            var wgt = UISpawn.Widget<WIconText>(life,
                UIPrefabId.WContent3IconTextSimple, container, idx);
            wgt.Load(IconId.CLocked, $"{T.ResearchLocked} {GlyphMap.ArrowRight} {Units.Num(lockedArr.Length)}");
            UIUtils.AddTipFunc(life, wgt, () => {
                var sb = Caches.StringBuilder4;
                sb.Clear();
                sb.AppendFormat("{0}: {1}", T.CodexMResearch, Texts.Red(T.ResearchLocked));
                sb.Append(GlyphMap.BRHR);
                var cnt = lockedArr.Length;
                int num = 0;
                foreach (var l in lockedArr.OrderBy(l => l.NameT)) {
                    sb.Append(Texts.LinkToResearch(l, Texts.Yellow(l.NameT)));
                    if (num++ < cnt - 1) {
                        sb.Append(", ");
                    }
                }
                return sb.ToString();
            });
            return wgt;
        }
    }
}
