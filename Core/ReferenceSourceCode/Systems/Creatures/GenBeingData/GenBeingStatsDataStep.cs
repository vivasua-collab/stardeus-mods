using System;
using System.Buffers;
using System.Linq;
using Game.Data;
using Game.Mods;
using Game.Systems.BodyParts;
using KL.Pool;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Creatures {
    public sealed class GenBeingStatsDataStep : GenBeingDataStep {
        [SelfInit]
        private static void Register() {
            RegisterStep(new GenBeingStatsDataStep());
        }
        public const string StepId = "Stats";
        public override string Id => StepId;
        public override int Order => 100;
        public override bool Generate(GameState S, BeingData data, Rng rng,
                GenBeingVars vars) {
            return true;
        }
    }
}
