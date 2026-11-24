using System;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Conditions;
using Game.Systems.Creatures;
using Game.Systems.Mental;
using Game.Systems.Stats;
using KL.Randomness;
using KL.Utils;
using Unity.Mathematics;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.AI {
    public sealed class AINeedFaction : AINeed {
        [SelfInit]
        private static void Register() {
            RegisterNeed(new AINeedFaction());
        }

        public override int[] RelatedStats => null;
        public override bool IsInternal => true;

        public override void Tick(GameState s, BeingData data, long ticks) {
            if (ticks % Consts.TicksPer15Minutes == 6) {
                if (data.Body.IsIncapacitated) { return; }
                var stats = data.StatsBeing;
                var alignment = stats.AIAlignment;
                var fac = data.Mind.Faction;
                if (!fac.IsPlayer && fac.IdH != FactionIdH.AntiPlayerTemp) {
                    var prevAlignment = alignment.BaseValue;
                    if (prevAlignment > 0.5f) {
                        // try to snap out of wrong AI alignment
                        alignment.UpdateBase(0f);
                        data.Mind.AfterSetFaction(true);
                    }
                }
            }
        }

        public override void InitializeFor(BeingData data) {
        }
        public override void InitializeForNew(BeingData data) {
        }

        public AINeedFaction() : base(NeedId.Faction) {}
    }
}
