using Game.Utils;
using KL.Pool;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public static class UIDraw {
        // Line from Top Left
        public static T SpawnLineTL<T>(Lifetime life, T prefab,
                RectTransform parent, float thickness, Vector2 from, Vector2 to) where T : WBase{
            T line;
            if (life == null) {
                line = UISpawn.WidgetPermanent(prefab, parent);
            } else {
                line = UISpawn.Widget(life, prefab, parent, -1);
            }
            var rt = line.Rect;
            // rt.SetParent(parent);
            rt.localScale = Vector3.one;
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0.5f, 0.5f);
            UpdateLineTL(thickness, from, to, rt);
            return line;
        }

        public static void UpdateLineTL(float thickness, Vector2 from, Vector2 to, RectTransform rt) {
            rt.sizeDelta = new Vector2(Vectors.Distance(from, to), thickness);

            // position the line
            rt.anchoredPosition = (from + to) / 2;
            var adj = from - to;
            float angle = Mathf.Atan2(adj.y, adj.x) * Mathf.Rad2Deg;
            rt.localRotation = Quaternion.Euler(0, 0, angle);
            rt.localPosition = Vectors.SetZ(rt.localPosition, 0);
        }

        public static void MoveLine(RectTransform rt, float thickness, Vector2 from, Vector2 to) {
            rt.sizeDelta = new Vector2(Vectors.Distance(from, to), thickness);
            // position the line
            rt.anchoredPosition = (from + to) / 2;
            var adj = from - to;
            float angle = Mathf.Atan2(adj.y, adj.x) * Mathf.Rad2Deg;
            rt.localRotation = Quaternion.Euler(0, 0, angle);
            rt.localPosition = Vectors.SetZ(rt.localPosition, 0);
        }
    }
}
