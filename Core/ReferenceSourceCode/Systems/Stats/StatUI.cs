using System;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.UI;
using Game.Systems.UIUX;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Stats {
    public static class StatUI {
        public static (WGroup, int) AddStat(Stat stat, Lifetime life,
                WGroup parent, WGroup block, int catId,
                ref int blockIdx, ref int idx) {
            if (stat == null) {
                return (block, catId);
            }

            if (block == null || catId != stat.CategoryId) {
                block = UISpawn.Block(life, parent, blockIdx++);
                catId = stat.CategoryId;
                idx = 0;

                var header = UISpawn.Widget<WText>(life,
                    A.InstantBuild ? UIPrefabId.WContent2Header : UIPrefabId.WContentHeader, block.Container, idx++);
                header.Load(Stat.CategoryFor(stat));
            }

            WBase wBase = null;
            if (A.InstantBuild) {
                var w = UISpawn.Widget<WButtonIconText2>(life,
                    UIPrefabId.WContent2Button, block.Container, idx++);
                wBase = w;
                w.Load(Stat.IconFor(stat), Stat.NameFor(stat), stat.Display(), () => {
                    var qc = QuickChoice.Create(Stat.NameFor(stat));
                    qc.Widget = w.Rect;
                    qc.AddInput(T.Change, new QuickChoice.Input() {
                        OnChange = (v) => {
                            if (float.TryParse(v, out var vf)) {
                                stat.UpdateBase(vf);
                                stat.Apply();
                            } else {
                                A.S.Sig.ShowErrorMessage.Send("Invalid format, must be a number: " + v);
                            }
                        },
                        OnSubmit = (v) => qc.Close(),
                    });
                    qc.Show();
                });
                stat.BFloat.Bind(life, v => {
                    w.SetText2(stat.Display(v));
                });

            } else {
                var w = UISpawn.Widget<WIconText2>(life,
                    UIPrefabId.WContentText2, block.Container, idx++);
                wBase = w;
                w.Load(Stat.IconFor(stat), Stat.NameFor(stat), stat.Display());
                stat.BFloat.Bind(life, v => {
                    w.SetText2(stat.Display(v));
                });
            }


            UIUtils.AddTipFunc(life, wBase, () => BuildStatTooltip(stat));
            return (block, catId);
        }

        private static string BuildStatTooltip(Stat stat) {
            var sb = Caches.StringBuilder3;
            sb.Clear();
            var desc = Stat.DescFor(stat);
            if (desc != null) {
                sb.Append(desc);
                sb.Append(GlyphMap.BRHR);
            }
            var baseStr = T.StatValueBase_V1.F(stat.DisplayBase());
            bool addedAny = false;
            if (!Arrays.IsEmpty(stat.Sources)) {
                float limit = 0f;
                for (int i = 0; i < stat.Sources.Count; i++) {
                    IStatSource src = stat.Sources[i];
                    if (src.GetEffectFor(stat, out var toAdd, out float lim)) {
                        if (addedAny) {
                            sb.Append('\n');
                        } else {
                            addedAny = true;
                        }
                        if (lim > 0f) {
                            sb.AppendFormat(Texts.Grey("{0}: {1} ({2}: {3})"), src.StatSourceNameT,
                                stat.DisplaySigned(toAdd), T.Max, stat.Display(lim));
                            limit = Maths.Max(limit, lim);
                        } else {
                            sb.AppendFormat(Texts.Grey("{0}: {1}"), src.StatSourceNameT,
                                stat.DisplaySigned(toAdd));
                        }
                    }
                    limit = Maths.Max(limit, lim);
                }
                if (addedAny) {
                    sb.Append('\n');
                }
                sb.Append(baseStr);
                // the base line is a line too: with no source in effect (a skill at its neutral
                // level) the next line ran into it
                addedAny = true;
                if (limit > 0f) {
                    sb.AppendFormat(Texts.Grey(" ({0}: {1})"), T.Max, stat.Display(limit));
                }
            } else {
                addedAny = true;
                sb.Append(T.StatValueBase_V1.F(stat.DisplayBase()));
            }
            bool hasModifiers = false;
            if (stat.InfluencedBy != null) {
                hasModifiers = true;
                foreach (var inf in stat.InfluencedBy) {
                    addedAny = inf.AppendInfo(sb, addedAny);
                }
            }
            if (!Arrays.IsEmpty(stat.Modifiers)) {
                hasModifiers = true;
                foreach (var mod in stat.Modifiers) {
                    addedAny = mod.AppendInfo(sb, stat, addedAny, nameOverride: mod.NameT);
                }
            }
            if (!hasModifiers) {
                if (addedAny) {
                    sb.Append('\n');
                }
                sb.Append(Texts.Grey(T.StatNoModifiers));
                addedAny = true;
            }
            if (addedAny) {
                sb.Append('\n');
            }
            sb.Append(T.StatValueEffective_V1.F(stat.Display()));
            if (stat.IsDirty) {
                sb.Append('*');
            }
            if (stat.Influences != null) {
                addedAny = false;
                foreach (var infStat in stat.Influences) {
                    foreach (var inf in infStat.InfluencedBy) {
                        if (inf.DependsOn == stat) {
                            addedAny = inf.AppendInfluenceInfo(sb, infStat, addedAny);
                        }
                    }
                }
            }
            return sb.ToString();
        }
    }
}
