using System;
using System.Buffers;
using System.Linq;
using Game.Data;
using Game.Mods;
using Game.Systems.BodyParts;
using Game.Systems.Items;
using KL.Pool;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Creatures {
    public sealed class GenBeingInventoryStep : GenBeingDataStep {
        [SelfInit]
        private static void Register() {
            RegisterStep(new GenBeingInventoryStep());
        }
        public const string StepId = "Inventory";
        public override string Id => StepId;
        public override int Order => 0;
        public override bool Generate(GameState S, BeingData data, Rng rng,
                GenBeingVars vars) {
            GenerateFor(S, data, rng);
            return true;
        }

        private void GenerateFor(GameState S, BeingData data, Rng rng) {
            if (data.Inventory == null) {
                data.Inventory = BeingInventory.BlankFor(S, data);
            }
        }
    }
}
