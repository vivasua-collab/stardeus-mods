using System;
using System.Collections.Generic;
using System.Linq;
using Game.Data.Space;
using Game.Utils;
using Game.CodeGen;
using Kl.Algos;
using KL.Utils;
using UnityEngine;
using Game.Constants;
using KL.Randomness;

namespace Game.Procgen.Space {
    public static class GenUniverseUtils {
        public static Vector2[] GeneratePoissonDiscChildPoints(Universe u, SpaceObject ob,
                int childrenCount, object ctx) {
            var t = ob.Type;
            var rng = new Rng(Hashes.Quick(ob.Seed, Primes.Prime013));
            // var childrenCount = rng.IntCurve(ob.Biome.NumSubObjects);
            var radius = t.MinDistanceBetweenSubObjects;
            if (radius < 1f) {
                D.Err("MinDistanceBetweenSubObjects too small for {0}. Setting to 1", t);
                radius = 1f;
            }
            var size = Mathf.Sqrt(childrenCount) * radius * 2;

            List<Vector2> points = null;
            try {
                int attempts = 100;
                do {
                    points = PoissonSpajusDiscSampling.GeneratePoints(radius, radius * 2f,
                        new Vector2(size, size), rng, numAttempts: 100);
                    if (points.Count < childrenCount) {
                        D.Warn("Failed generating {0} poisson points, increasing size: {1}->{2}. Attempt: {3}",
                            childrenCount, size, size + 5, attempts);
                        size += 5;
                    }
                } while (points.Count < childrenCount && attempts-- > 0);
            } catch (System.Exception ex) {
                D.LogEx(ex, "Failed generating child points for space object: {0}. {1}. Radius: {2}. Size: {3}",
                    ctx, t, radius, size);
                return null;
            }

            if (points.Count < childrenCount) {
                D.Err("Point count: {0}, Children count {1}. Radius {2}. Size {3}",
                    points.Count, childrenCount, radius, size);
                D.Err("Not enough points when generating children for obj {0}",
                    ob.Type.Id);
            }

            var orderedPoints = points.OrderBy(p => rng.Value).ToArray();
            if (childrenCount < orderedPoints.Length) {
                Array.Resize(ref orderedPoints, childrenCount);
            }
            var off = new Vector2(-size / 2, -size / 2);
            for (int i = 0; i < orderedPoints.Length; i++) {
                var p = orderedPoints[i];
                orderedPoints[i] = p + off;
            }
            return orderedPoints;
        }
    }
}
