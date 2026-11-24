using System;
using System.Collections.Generic;
using System.Linq;
using Game.Data;
using Game.Input;
using Game.Systems;
using Game.Systems.Proxy;
using Game.Utils;
using KL.Collections;
using KL.Grid;
using KL.Utils;
using UnityEngine;

namespace Game.Tools {
    public sealed class FreeSelectInfoHandler : FreeSelectHandler {
        public const string HandlerId = "FreeSelectInfo";

        [SelfInit]
        private static void Register() {
            Register(HandlerId, new FreeSelectInfoHandler());
        }

        public override int Order => 7;

        public override void Initialize(FreeSelectContext ctx) {
            base.Initialize(ctx);
        }

        private readonly Dictionary<string, List<EntityProxy>> proxiesByTooltip = new();

        public override void OnHover(Vector2 pos) {
            if (Pos.IsOOB(ctx.HoveredPos)) { return; }

            Caches.EntityProxyList.Clear();
            var proxyCount = S.Query.GetEntityProxiesAt.Ask(
                ctx.HoveredPos, Caches.EntityProxyList);


            if (proxyCount > 0) {
                proxiesByTooltip.Clear();
                for (int i = 0; i < proxyCount; i++) {
                    var pr = Caches.EntityProxyList[i];
                    if (pr.Tooltip != null) {
                        if (!proxiesByTooltip.TryGetValue(pr.Tooltip, out var list)) {
                            list = new();
                            proxiesByTooltip[pr.Tooltip] = list;
                        }
                        list.Add(pr);
                    }
                }
                foreach (var tp in proxiesByTooltip.OrderBy(t => t.Key)) {
                    var sb = ctx.BeginTooltipBlock();
                    sb.Append(Texts.SubTitle(tp.Key));
                    var count = tp.Value.Count;
                    var limit = Maths.Min(count, 5);
                    for (int i = 0; i < limit; i++) {
                        EntityProxy item = tp.Value[i];
                        if (i > 0) {
                            sb.AppendLine();
                        }

                        sb.Append(' ');
                        sb.Append(GlyphMap.ArrowRight);
                        sb.Append(' ');
                        sb.Append(Texts.LinkToEntity(item.Entity));
                    }
                    if (limit < count) {
                        sb.AppendLine();
                        sb.Append(Texts.Grey($"+{Units.Num(count - limit)}"));
                    }
                }
                proxiesByTooltip.Clear();
            }

            var infoCount = S.Sys.Info.InfoFor(ctx.HoveredPos, ctx.InfoItems);
            if (infoCount > 0) {
                var sb = ctx.Tooltip;
                for (int i = 0; i < infoCount; i++) {
                    var inf = ctx.InfoItems[i];
                    if (inf != null) {
                        string prefix = sb.Length > 0 ? GlyphMap.BRHR : null;
                        if (inf.AppendInfoText(sb, prefix) && ctx.HoveredBeing != null
                                && inf.Entity == ctx.HoveredBeing) {
                            FreeSelectBeingConditionsHandler.AppendConditions(sb,
                                ctx.HoveredBeing);
                            ctx.BeingConditionsShown = true;
                        }
                    }
                }
            }
        }
    }
}
