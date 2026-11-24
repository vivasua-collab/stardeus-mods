using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using KL.Utils;

namespace Game.Systems.UIUX {
    public struct FavMatTypeComparer : IComparer<MatType> {
        public static readonly FavMatTypeComparer Instance = new();
        public int Compare(MatType x, MatType y) {
            var favGroupX = x.GroupId;
            var favGroupY = y.GroupId;
            var orderX = FavGroupOrder(favGroupX);
            var orderY = FavGroupOrder(favGroupY);
            if (orderX != orderY) {
                return orderX - orderY;
            } else {
                return string.Compare(x.NameT, y.NameT);
            }
        }
        private static int FavGroupOrder(string id) {
            if (id == null) { return 0; }
            var key = $"UIFavMatGroupOrder_{id}";
            if (Tunable.TryInt(key, out var order)) {
                return order;
            }
            D.Err("Tunable int key not defined in Config/Tunables/UI.json: {0}", key);
            return 0;
        }
    }
}
