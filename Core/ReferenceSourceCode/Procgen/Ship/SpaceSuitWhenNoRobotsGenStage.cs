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
    public class SpaceSuitWhenNoRobotsGenStage : BaseShipGenStage {
        public override void Apply() {
            var aliveRobots = S.Beings.Alive.Count(b => !b.Species.IsBiological);
            if (aliveRobots > 0) { return; }
            D.Warn("No robots at start, trying to relocate one space suit near humans");

            // with alignment
            var human = S.Beings.FindFirstMatching(b => b.IsAlive && b.Species.IsHumanoid && b.Data.StatsBeing.AIAlignment.Value > 0.5f);
            // anyone
            human ??= S.Beings.FindFirstMatching(b => b.IsAlive && b.Species.IsHumanoid);

            if (human == null) { return; }

            var suit = S.Objs.FindFirstMatching(o => o.Definition.IdH == DefIdsH.ObjEquipmentClothingSpaceSuit01);
            var helm = S.Objs.FindFirstMatching(o => o.Definition.IdH == DefIdsH.ObjEquipmentHatsSpaceHelmet01);
            var pos = human.Position + Rng.UInsideUnitCircle();
            if (suit != null) {
                suit.Transform.TeleportTo(pos);
                suit.Graphics.TeleportTo(pos);
            }
            pos = human.Position + Rng.UInsideUnitCircle();
            if (helm != null) {
                helm.Transform.TeleportTo(pos);
                helm.Graphics.TeleportTo(pos);
            }
        }
    }
}
