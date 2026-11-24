using System;
using Game.Data;
using Game.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public abstract class BaseSearchResult {
        /// <summary>
        /// For saving / loading pinned results. For def results it will be def id, etc.
        /// </summary>
        public abstract string Id { get; }
        /// <summary>
        /// Hashes.S output of Id. For HashCode/Equals, etc
        /// </summary>
        public abstract int IdH { get; }
        public BaseSearchAdapter Adapter;
        public int Order;
        public virtual Def Def => null;
        public virtual bool IsPinnedAvailable => true;
        public abstract void SpawnWidget(Lifetime life, int idx, WGroup wContainer);
        public abstract void SpawnPinned(Lifetime life, int idx, WGroup wContainer);
        protected BaseSearchResult(BaseSearchAdapter adapter) {
            Adapter = adapter;
        }

        public abstract void OnSelect();

        public override int GetHashCode() {
            unchecked {
                return (IdH * 397) ^ (Adapter != null ? Adapter.GetHashCode() : 0);
            }
        }

        public override bool Equals(object obj) {
            if (obj == null || obj is not BaseSearchResult other) {
                return false;
            }
            return IdH == other.IdH && Adapter == other.Adapter;
        }
    }
}
