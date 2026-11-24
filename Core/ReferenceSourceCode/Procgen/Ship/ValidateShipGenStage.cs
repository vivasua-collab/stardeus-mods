using System.Collections.Generic;
using System.Linq;
using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using System;

namespace Game.Procgen {
    public class ValidateShipGenStage : BaseShipGenStage {
        private readonly Dictionary<Type, int> requiredObjects;

        public ValidateShipGenStage(Dictionary<Type, int> required) {
            requiredObjects = required;
        }
        public override void Apply() {
            bool needRetry = false;
            var compState = typeof(ComponentState);
            var meth = compState.GetMethod("CountOfConstructed");
            foreach (var req in requiredObjects) {
                try {
                    var genMeth = meth.MakeGenericMethod(req.Key);
                    var count = (int) genMeth.Invoke(S.Components, new object[] { req.Value });
                    if (count < req.Value) {
                        D.Err("Too few {0} for scenario: {1} seed: {2}. Wanted {3}. Got: {4}",
                            req.Key.Name, S.Scenario.Id, S.SeedStr, req.Value, count);
                        needRetry = true;
                    }
                } catch (Exception ex) {
                    D.LogEx(ex, "Failed calling S.Components.CountOfConstructed<{0}>({1})",
                        req.Key.Name, req.Value);
                }
            }
            if (needRetry) {
                D.Err("Will start over!");
                StartOver = true;
            }
        }
    }
}
