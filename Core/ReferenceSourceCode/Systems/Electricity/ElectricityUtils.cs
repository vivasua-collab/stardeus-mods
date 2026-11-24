using System.Collections.Generic;
using Game.Constants;
using Game.Utils;
using KL.Utils;
using Unity.Burst;
using UnityEngine;

namespace Game.Systems.Electricity {
    public static class ElectricityUtils {
        [BurstCompile]
        public static unsafe int AutoConnectCandidatesForPosition(
                float x, float y,
                int range,
                int nodeCount,
                int maxResults,
                ElectricNode* nodes,
                int* results,
                bool onlyReachable) {
            int resultSize = 0;
            var position = new Vector2(x, y);
            var rangeSqr = range * range;
            for (int i = 0; i < nodeCount; i++) {
                var nodeTo = nodes[i];
                if (!nodeTo.IsInitialized) { continue; }
                if (onlyReachable && !nodeTo.IsReachable) { continue; }
                if (nodeTo.IsConnector && nodeTo.NumConnections < nodeTo.MaxConnections
                        && Vectors.SqrDistance(position, nodeTo.Position)
                            <= Maths.Max(rangeSqr, nodeTo.ConnectorRangeSqr)) {
                    results[resultSize++] = nodeTo.Idx;
                    if (resultSize >= maxResults) {
                        return resultSize;
                    }
                } else if (range > 0 && Vectors.SqrDistance(position, nodeTo.Position)
                            <= Maths.Max(rangeSqr, nodeTo.ConnectorRangeSqr)) {
                    results[resultSize++] = nodeTo.Idx;
                    if (resultSize >= maxResults) {
                        return resultSize;
                    }
                }
            }
            return resultSize;
        }


        public static string GridState(in ElectricGrid grid) {
            if (!grid.HasShipComputer) { return T.Unknown; }
            var hasDeficit = grid.Deficit > 0f;
            var maxOut = grid.MaxOutputWithoutBat;
            var state = Units.ElectricityIO(maxOut, grid.Consumption);
            if (hasDeficit) {
                state = Texts.Red(state);
            }
            return state;
        }
    }
}
