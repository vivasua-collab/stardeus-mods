using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Game.Constants;
using Game.Data;
using Game.Rendering;
using Game.UI;
using Game.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class SituationHandler : BaseWidgetHandler<SituationWidget> {
        public SituationHandler(GameState s, SituationWidget widget) : base(s, widget) {
            widget.Initialize(s);
            s.UIQuery.ToggleSituation.SetResponder(ToggleSituation);
            The.SysSig.UIThemeReloaded.AddListener(OnThemeReloaded);
            widget.OnShow = OnShow;
        }

        private void OnThemeReloaded() {
            widget.OnThemeReloaded();
        }


        private bool ToggleSituation(bool show) {
            widget.Toggle(show);
            return true;
        }

        private void OnShow(Lifetime lifetime) {
            var situation = S.Situation;
            var fields = typeof(Situation).GetFields();

            widget.ShowChart(true);

            var block = UISpawn.Block(lifetime, widget.WItems, 0);
            int idx = 0;
            bool renderedFirst = false;

            foreach (var field in fields) {
                if (!CanShow(field)) { continue; }

                var capturedField = field;
                var nameT = $"situation.{field.Name}".T();
                var value = field.GetValue(situation)?.ToString() ?? "-";

                var row = UISpawn.Widget<WIconText2>(lifetime,
                    UIPrefabId.WContent2Text2, block.Container, idx++);
                row.Load(IconId.CInfo, nameT, value);

                UIUtils.AddHover(lifetime, row, on => {
                    if (!on) { return; }
                    RenderChartForField(capturedField);
                });

                if (!renderedFirst) {
                    RenderChartForField(capturedField);
                    renderedFirst = true;
                }
            }
        }

        private void RenderChartForField(FieldInfo field) {
            var hist = S.Map.SituationHistory;
            if (hist == null || hist.Count == 0) {
                widget.RenderSituationChart(null, null);
                return;
            }

            var data = new List<RTChartNode>();
            float prevX = 0;
            float prevY = 0;
            bool first = true;

            foreach (var entry in hist) {
                var val = ExtractField(field, entry.Value);
                if (first) {
                    prevX = entry.Key;
                    prevY = val;
                    first = false;
                    continue;
                }
                data.Add(new RTChartNode {
                    Type = RTChartNode.NodeType.Line,
                    From = new Vector2(prevX, prevY),
                    To = new Vector2(entry.Key, val),
                    Color = val >= prevY ? Color.green : Color.red,
                    Thickness = 3
                });
                prevX = entry.Key;
                prevY = val;
            }

            // Add current moment
            var now = ExtractField(field, S.Situation);
            data.Add(new RTChartNode {
                Type = RTChartNode.NodeType.Line,
                From = new Vector2(prevX, prevY),
                To = new Vector2(prevX + 1, now),
                Color = now >= prevY ? Color.green : Color.red,
                Thickness = 3
            });

            var fieldName = $"situation.{field.Name}".T();
            widget.RenderSituationChart(data, (xy, valXY, minX, minY, maxX, maxY, nodes) => {
                if (nodes.Count != 1) { return null; }
                var val = nodes[0].ValueAt(xy.x);
                if (val > 1000) { val = Mathf.RoundToInt(val); }
                var day = Mathf.FloorToInt(valXY.x);
                return $"{fieldName}: {val}\n{T.Day} {day}";
            });
        }

        private static bool CanShow(FieldInfo f) {
            if (!f.FieldType.IsPrimitive) { return false; }
            var name = f.Name;
            if (name == "Check") { return false; }
            if (name == "Difficulty") { return false; }
            return true;
        }

        private static float ExtractField(FieldInfo field, Situation s) {
            if (s == null) { return 0f; }
            if (field.FieldType == typeof(int)) {
                return (int)field.GetValue(s);
            }
            if (field.FieldType == typeof(float)) {
                return (float)field.GetValue(s);
            }
            if (field.FieldType == typeof(long)) {
                var val = (long)field.GetValue(s);
                return val > float.MaxValue ? float.MaxValue : (float)val;
            }
            if (field.FieldType == typeof(double)) {
                var val = (double)field.GetValue(s);
                return val > float.MaxValue ? float.MaxValue : (float)val;
            }
            return 0f;
        }

        public void Cleanup() {
            The.SysSig.UIThemeReloaded.RemoveListener(OnThemeReloaded);
        }
    }
}
