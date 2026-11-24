using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Systems.Motion;
using KL.Collections;
using KL.Grid;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Proxy {
    public sealed class EntityProxySys : GameSystem {
        public const string SysId = "EntityProxySys";
        public override string Id => SysId;

        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new EntityProxySys());
        }

        private readonly List<EntityProxy> proxies = new();

        protected override void OnInitialize() {
            S.Query.GetEntityProxiesInRect.SetResponder(GetProxiesInRect);
            S.Query.GetEntityProxiesAt.SetResponder(GetProxiesAt);
            S.Query.AddEntityProxy.SetResponder(AddProxy);
            S.Query.RemoveEntityProxy.SetResponder(RemoveProxy);
        }

        private int GetProxiesAt(int posIdx, List<EntityProxy> res) {
            if (proxies.Count == 0) { return 0; }
            var posFrom = Pos.ToVector(posIdx, 0f, 0f);
            var posTo = Pos.ToVector(posIdx, 1f, 1f);
            return GetProxiesInRect(posFrom, posTo, res);
        }

        private int GetProxiesInRect(Vector2 from, Vector2 to, List<EntityProxy> res) {
            if (proxies.Count == 0) { return 0; }
            int found = 0;
            var minX = Maths.Min(from.x, to.x);
            var minY = Maths.Min(from.y, to.y);
            var maxX = Maths.Max(from.x, to.x);
            var maxY = Maths.Max(from.y, to.y);
            for (int i = proxies.Count - 1; i >= 0; i--) {
                var pr = proxies[i];
                var p = pr.Position;
                if (p.x < minX || p.y < minY || p.x > maxX || p.y > maxY) {
                    continue;
                }
                res.Add(pr);
                found++;
            }
            return found;
        }

        private bool RemoveProxy(EntityProxy proxy) {
            if (!proxies.Remove(proxy)) {
                D.Err("EntityProxy was not added: {0}", proxy);
                return false;
            }
            return true;
        }

        private bool AddProxy(EntityProxy proxy) {
            if (!proxies.AddUniqueInefficient(proxy)) {
                D.Err("EntityProxy already added: {0}", proxy);
                return false;
            }
            return true;
        }

        public override void Unload() {
            proxies.Clear();
        }

        public string GetName() {
            return "EntityProxySys";
        }
    }
}
