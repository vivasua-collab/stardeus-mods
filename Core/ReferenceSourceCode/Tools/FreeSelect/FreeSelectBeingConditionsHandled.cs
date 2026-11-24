using System;
using System.Collections.Generic;
using System.Text;
using Game.Data;
using Game.Input;
using Game.Systems;
using Game.Systems.Conditions;
using Game.Utils;
using KL.Collections;
using KL.Grid;
using KL.Utils;
using UnityEngine;

namespace Game.Tools {
    public sealed class FreeSelectBeingConditionsHandler : FreeSelectHandler {
        public const string HandlerId = "FreeSelectBeingConditions";

        [SelfInit]
        private static void Register() {
            Register(HandlerId, new FreeSelectBeingConditionsHandler());
        }

        // Right after info items
        public override int Order => 8;

        public override void OnHover(Vector2 pos) {
            // FreeSelectInfoHandler puts them under the being's own info lines when it has any
            if (ctx.HoveredBeing != null && !ctx.BeingConditionsShown) {
                AddConditionsTooltip();
            }
        }

        // Continues the being's block. Appending at the end of the tooltip put them under
        // whatever block came last, the bed a downed being lies in.
        public static void AppendConditions(StringBuilder sb, Being being) {
            foreach (var cond in being.Data.Conditions.Values) {
                if (!Shows(cond, being)) { continue; }
                sb.AppendLine();
                sb.AppendFormat(" {0} {1}", GlyphMap.ArrowRight, cond.Condition.NameT);
            }
        }

        private static bool Shows(ConditionState cond, Being being) {
            return cond.Condition.ShowOnHoverFor(being) && !cond.IsUndetected;
        }

        private void AddConditionsTooltip() {
            StringBuilder sb = null;
            foreach (var cond in ctx.HoveredBeing.Data.Conditions.Values) {
                if (!Shows(cond, ctx.HoveredBeing)) { continue; }
                if (sb == null) {
                    sb = ctx.BeginTooltipBlock();
                    sb.Append(Texts.SubTitle(ctx.HoveredBeing.Data.Name));
                } else {
                    sb.AppendLine();
                }
                sb.AppendFormat(" {0} {1}", GlyphMap.ArrowRight, cond.Condition.NameT);
            }
        }
    }
}
