using System.Collections.Generic;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Procgen {
    public sealed class CurveGenSpawnCheck : IGenSpawnCheck {
        private const string id = "Curve";
        [SelfInit]
        private static void Register() {
            GenSpawnChecks.Add(id, new CurveGenSpawnCheck());
        }
        public string Id => id;

        public int Quantity(GameState s, Def def, ComponentConfig config, Rng rng) {
            float[] curveKeys = config.GetFloatSet(PropertyIdH.Keys);
            int[] curveValues = config.GetIntSet(PropertyIdH.Values);
            if (curveKeys.Length != curveValues.Length) {
                D.Err("Curve[{0}] Quantites and Chances count is not equal!", def.Id);
                return 1;
            }

            Vector2[] points = new Vector2[curveKeys.Length];
            for (int i = curveKeys.Length - 1; i >= 0; i--) {
                points[i] = new Vector2(curveKeys[i], curveValues[i]);
            }
            Curve curve = points;
            return Mathf.RoundToInt(curve.Evaluate(rng.Value));
        }
    }
}
