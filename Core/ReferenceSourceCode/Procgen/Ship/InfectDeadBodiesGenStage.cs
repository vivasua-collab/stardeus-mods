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
using Game.Systems.Conditions;

namespace Game.Procgen {
    public class InfectDeadBodiesGenStage : BaseShipGenStage {
        private readonly Curve deadByDiffCurve = new float[] {
            0.1f, 0f,
            0.25f, 1f,
            0.5f, 2f,
            1.0f, 3f,
            2.0f, 4f,
            5.0f, 8f,
        };
        public override void Apply() {
            int wantDead = Mathf.RoundToInt(deadByDiffCurve.Evaluate(S.Difficulty.Level));
            if (wantDead == 0) { return; }
            foreach (var b in S.Beings.All.OrderBy(b => rng.Value)) {
                if (!b.Species.IsHumanoid) { continue; }
                if (!b.IsDead) { continue; }
                if (ConditionState.CreateFor(S, ConditionIdH.CrawlerInfected,
                        b.Data, Severity.Irrelevant, false)) {
                    if (--wantDead < 1) { break; }
                }
            }
        }
    }
}
