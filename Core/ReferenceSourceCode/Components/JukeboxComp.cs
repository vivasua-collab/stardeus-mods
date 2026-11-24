using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Utils;
using KL.Randomness;
using System;
using Game.Systems.Drama;
using Game.Systems.Mental;
using Game.Systems.AI;
using KL.Grid;
using UnityEngine;

namespace Game.Components {
    public sealed class JukeboxComp : FunProviderComp<JukeboxComp> {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new JukeboxComp());
        }
        public override string ActIcon => IconId.CSoundOn;
        public override string ActName => T.ActUsejukebox;
        public override bool CanUse(Being user) {
            return true;
        }

        public override bool ShouldContinueUsing(AIAgentComp agent, AIGoal goal,
                long ticks, float funValue) {
            if (funValue < FunSession.SatisfiedAt
                    && (ticks - goal.StateStartTick) < MaxSessionTicks) {
                return true;
            }
            return false;
        }

        // Dancers share the work spot, each on its own side of it: the first toward the
        // machine, the others spread evenly around. Random directions put two on top of each
        // other.
        protected override void PositionUser(Being worker, int num) {
            var toMachine = Tile.Position - Pos.ToVector(WorkSpot);
            var start = Mathf.Atan2(toMachine.y, toMachine.x) * Mathf.Rad2Deg;
            var step = 360f / Math.Max(userCapacity, 1);
            var angle = (start + num * step + Rng.URange(-15f, 15f)) * Mathf.Deg2Rad;
            worker.Graphics.SetOffset(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 0.4f);
        }

        protected override void CreateSocialInteraction(Being worker, List<Being> users) {
            if (curInteraction != null) {
                curInteraction.With(worker.Data);
                return;
            }
            var soc = new SocialInteraction();
            soc.AttitudeChange = 0.03f;
            soc.Icon = IconId.CSoundOn;
            soc.Title = T.AbilityDance;
            soc.Desc = T.InteractionDancedWith_V2.F(worker.Data.Mind.LinkToShortName,
                Texts.ListAnd(users, (u) => u.Data.Mind.LinkToShortName));
            soc.With(worker.Data);
            foreach (var u in users) {
                soc.With(u.Data);
            }
            curInteraction = soc;
        }

        protected override bool OnUse(Being worker) {
            if (Rng.UChance(0.1f)) {
                worker.Graphics.SetRotation(Rng.URange(-15f, 15f), true);
            }
            if (Rng.UChance(0.05f)) {
                worker.Graphics.SetFacing(Rng.UFrom(Facing.Types));
            }
            if (Rng.UChance(0.5f)) {
                worker.Graphics.Punch(Facing.ToOffset(Tile.Transform.Rotation, 1f),
                    0.15f, Rng.URange(0.8f, 1.2f), Rng.URange(0.8f, 1.2f));
            } else {
                worker.Graphics.Punch(Facing.ToOffset(
                        Facing.Rotate(Tile.Transform.Rotation, Facing.Type.Right), 1f),
                    0.15f, Rng.URange(0.8f, 1.2f), Rng.URange(0.8f, 1.2f));
            }
            return true;
        }

        public override string ToString() {
            return " * Jukebox";
        }

        public override void Reward(Being being, long ticks, int ticksUsing) {
            AddRewardMood(being, ticks, ticksUsing, IconId.CSoundOn, T.UsedAJukebox, +3);
        }

    }
}
