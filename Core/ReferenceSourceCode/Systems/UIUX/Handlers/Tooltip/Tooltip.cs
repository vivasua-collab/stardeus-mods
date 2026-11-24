using System;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public class Tooltip : IWithLifetime {
        public static float DelaySeconds => The.Prefs.GetFloat(Pref.GTooltipDelay,
            PrefH.GTooltipDelay, Consts.DefaultTooltipDelay);

        public TooltipFlags Flags;
        public readonly WBase Owner;
        public readonly RectTransform Target;
        public Def Def;
        public string Tip;
        public Lifetime Life;
        /// <summary>
        /// WARNING!
        /// Never set this after adding a hover, always add tooltip first using
        /// tip = UIUtils.AddTipFun, then do tip.HoverFun = (on) => ...
        /// </summary>
        public Func<string> TooltipFun { get; private set; }
        public Action<bool> HoverFun;
        public bool IsDisposed { get; private set;}

        private Tooltip(WBase owner, RectTransform target) {
            Owner = owner;
            Target = target;
        }
        public static Tooltip CreateFor(Lifetime life, WBase owner,
                RectTransform target, Func<string> func,
                TooltipFlags flags = TooltipFlags.None) {
            var t = new Tooltip(owner, target);
            t.TooltipFun = func;
            t.Flags = flags | TooltipFlags.Function;
            return Register(life, t);
        }
        public static Tooltip CreateFor(Lifetime life, WBase owner,
                RectTransform target, Def def,
                TooltipFlags flags = TooltipFlags.None) {
            var t = new Tooltip(owner, target);
            t.Def = def;
            t.Flags = flags;
            return Register(life, t);
        }
        public static Tooltip CreateFor(Lifetime life, WBase owner,
                RectTransform target, string tip,
                TooltipFlags flags = TooltipFlags.None) {
            if (owner == null) {
                D.Err("Attempting to create a tooltip for a null owner! {0}", tip);
                return null;
            }
            var t = new Tooltip(owner, target);
            t.Tip = tip;
            t.Flags = flags;
            return Register(life, t);
        }

        private static Tooltip Register(Lifetime life, Tooltip t) {
            A.S.UIQuery.AddTooltipData.Ask(life, t, true);
            // Tooltips are found through the top raycast hit, which a clickable text only is
            // while something on it needs the pointer
            TMP_Text_Clickable.HoldForTooltip(life, t.Target);
            return t;
        }

        public bool IsParentOf(GameObject go, out int depth) {
            if (go == Target.gameObject) {
                depth = 0;
                return true;
            }
            return MiscUtils.IsParentOf(Target, go.transform, out depth, 6);
        }

        public override bool Equals(object obj) {
            if (obj == null || GetType() != obj.GetType()) {
                return false;
            }
            var other = (Tooltip) obj;
            return Owner == other.Owner && Target == other.Target;
        }

        public override int GetHashCode() {
            unchecked {
                int hash = 17;
                hash = hash * 31 + (Owner != null ? Owner.GetHashCode() : 0);
                hash = hash * 31 + (Target != null ? Target.GetHashCode() : 0);
                return hash;
            }
        }

        public override string ToString() {
            var tip = Tip;
            if (tip?.Length > 10) {
                tip = tip[0..10];
            }
            return $"Tip[{tip}|{Owner}|{Target}]";
        }

        public void Dispose() {
            Life = null;
            IsDisposed = true;
            Flags |= TooltipFlags.Destroyed;
        }
    }
}
