using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Randomness;
using UnityEngine;
using System;
using Game.Systems.Drama;
using Game.Systems.Mental;
using Game.Systems.AI;
using KL.Utils;

namespace Game.Components {
    public sealed class ArcadeCabinetComp : FunProviderComp<ArcadeCabinetComp> {

        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new ArcadeCabinetComp());
        }
        public override string ActIcon => IconId.CJoystick;
        public override string ActName => T.ActGaming;
        private Vector2[][] offsets;

        protected override void ConfigureProvider() {
            offsets = new Vector2[2][];
            offsets[0] = Config.GetVector2Set(PropertyIdH.Offsets_P1);
            offsets[1] = Config.GetVector2Set(PropertyIdH.Offsets_P2);
        }

        protected override void CreateSocialInteraction(Being worker, List<Being> users) {
            if (curInteraction != null) {
                curInteraction.With(worker.Data);
                return;
            }
            var soc = new SocialInteraction();
            soc.AttitudeChange = 0.03f;
            soc.Icon = IconId.CJoystick;
            soc.Title = T.PlayedVideoGame;
            soc.Desc = T.PlayedVideoGameWith_V2.F(worker.Data.Mind.LinkToShortName,
                Texts.ListAnd(users, (u) => u.Data.Mind.LinkToShortName));
            soc.With(worker.Data);
            foreach (var u in users) {
                soc.With(u.Data);
            }
            curInteraction = soc;
        }

        public override bool CanUse(Being user) {
            return user.Data.Mind.Species.IsHumanoid
                && user.Data.HasAbility(AbilityIdH.Manipulate);
        }
        protected override void PositionUser(Being worker, int num) {
            if (num >= offsets.Length) {
                D.Err("Arcade cabinet slot {0} has no configured offset! {1}", num, Tile);
                num = offsets.Length - 1;
            }
            var off = offsets[num][(int) Tile.Transform.Rotation];
            worker.Motion.TeleportTo(Tile.Position + off);
        }
        public override int MinSessionTicks => Consts.TicksPer4Hours;
        public override int MaxSessionTicks => Consts.TicksPer6Hours;

        public override bool ShouldContinueUsing(AIAgentComp agent, AIGoal goal,
                long ticks, float funValue) {
            if (agent.TicksInCurrentAction < MinSessionTicks) {
                return true;
            }
            if (funValue < FunSession.SatisfiedAt
                    && (ticks - goal.StateStartTick) < MaxSessionTicks) {
                return true;
            }
            return false;
        }

        protected override bool OnUse(Being worker) {
            // make sure we're facing the device
            var facing = Facing.Opposite(Tile.Transform.Rotation);
            worker.Graphics.SetFacing(facing);

            if (Rng.UChance(0.1f)) {
                worker.Graphics.SetRotation(Rng.URange(-10f, 10f), true);
            }

            worker.Graphics.Punch(Facing.ToOffset(Facing.Rotate(Tile.Transform.Rotation, Facing.Type.Right), 1f),
                0.01f, Rng.URange(0.25f, 0.5f), Rng.URange(0.15f, 0.3f));
            return true;
        }

        public override string ToString() {
            return " * Arcade Cabinet";
        }

        public override void Reward(Being being, long ticks, int ticksUsing) {
            AddRewardMood(being, ticks, ticksUsing, IconId.CJoystick, T.ActGaming, +3);
        }
    }
}
