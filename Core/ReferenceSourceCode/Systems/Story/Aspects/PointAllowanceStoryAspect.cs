#if !PRODUCTION_BUILD
// #define DEBUG_POINTS
#endif
using System;
using System.Text;
using Game.CodeGen;
using Game.Data;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Story {
    public sealed class PointAllowanceStoryAspect : StoryAspect {
        [SelfInit]
        private static void Register() {
            Register(AspectId, Factory);
        }

        private static StoryAspect Factory() {
            return new PointAllowanceStoryAspect();
        }

        public const string AspectId = "PointAllowance";
        public override string Id => AspectId;
        public StoryEventOutcome Outcome => outcome;
        private StoryEventOutcome outcome;
        private int outcomeIdx;
        private float multiplier;
        private Curve colonistsCurve;
        private Curve workersCurve;
        private Curve energyCurve;
        private Curve daysCurve;
        private Curve shipClassPointsCurve;
        private Curve netWorthCurve;

        public override void OnTick(StoryContext ctx) {
            var schedule = ctx.Schedule[outcomeIdx];
            if (schedule.IsAllowed && schedule.PointAllowance < 0) {
                SetPointAllowanceFor(schedule, ctx);
            }
        }

        private void SetPointAllowanceFor(StorySchedule schedule, StoryContext ctx) {
            float points = GetPointAllowance(ctx);
            #if DEBUG_POINTS
            D.Warn("Changing point allowance for schedule: {0}->{1} (mul: {2}) ({3})",
                points, schedule, multiplier, Def);
            #endif
            var pointsBefore = schedule.PointAllowance;
            schedule.PointAllowance = (int) points;
            schedule.MaxPointAllowance = (int) points;
            ctx.S.Sig.StoryScheduleAllowanceChanged.Send(schedule, pointsBefore, (int)points);
        }

        public float GetPointAllowance(StoryContext ctx, StringBuilder sb = null) {
            var sit = ctx.S.Situation;
            var points = 0f;
            points += AddPoints_Colonists(ctx, sit, sb);
            points += AddPoints_Workers(ctx, sit, sb);
            points += AddPoints_Energy(ctx, sit, sb);
            points += AddPoints_Time(ctx, sit, sb);
            points += AddPoints_ShipClassPoints(ctx, sit, sb);
            points += AddPoints_NetWorth(ctx, sit, sb);
            if (multiplier != 1f) {
                if (sb != null) {
                    sb.AppendFormat("*{0} {1}", multiplier, Texts.Grey("(aspect mul)"));
                }
                points *= multiplier;
            }
            return points;
        }

        private float AddPoints_Colonists(StoryContext ctx, Situation sit, StringBuilder sb) {
            var pts = AddPointsFromCurve(colonistsCurve, sit.Colonists, sb, "Colonists");
            return pts;
        }
        private float AddPoints_Workers(StoryContext ctx, Situation sit, StringBuilder sb) {
            return AddPointsFromCurve(workersCurve, sit.Workers, sb, "Workers");
        }
        private float AddPoints_Energy(StoryContext ctx, Situation sit, StringBuilder sb) {
            return AddPointsFromCurve(energyCurve, sit.GridMaxOutput, sb, "Energy");
        }
        private float AddPoints_Time(StoryContext ctx, Situation sit, StringBuilder sb) {
            return AddPointsFromCurve(daysCurve, sit.Days, sb, "Days");
        }
        private float AddPoints_ShipClassPoints(StoryContext ctx, Situation sit, StringBuilder sb) {
            return AddPointsFromCurve(shipClassPointsCurve, sit.ShipClassPoints, sb, "ShipClassPoints");
        }
        private float AddPoints_NetWorth(StoryContext ctx, Situation sit, StringBuilder sb) {
            double nw = sit.BaseNetWorth;
            #if DEBUG_POINTS
            D.Warn("Ship net worth (for points): {0:0.##}M", nw);
            #endif
            return AddPointsFromCurve(netWorthCurve, (float) nw, sb, "NetWorth");
        }

        private float AddPointsFromCurve(Curve curve, float val, StringBuilder sb, string context) {
            if (curve == null) {
                D.Err("No curve for {0}", context);
                return 0f;
            }
            var p = curve.Evaluate(val);
            #if DEBUG_POINTS
            D.Warn("Adding points {0}: {1} -> {2} ({3})", context, val, p, Def);
            #endif
            if (sb != null) {
                sb.AppendFormat("\n +{0} for {1} {2}",
                    Texts.Green(p.ToString()),
                    Texts.Yellow(val.ToString()),
                    context);
            }
            return p;
        }

        public override void Initialize(StoryContext ctx, bool isLoading) {
            outcome = ParseOutcome();
            outcomeIdx = (int) outcome;
            multiplier = Def.Vars.Get(PropertyIdH.Multiplier, 1f);
            if (multiplier == 0) {
                D.Err("Multiplier is 0 for {0}. Setting to 1f", Def);
                multiplier = 1f;
            }
            var sched = ctx.ScheduleFor(outcome);
            var paMul = sched.DifficultyMod.PointAllowanceMul;
            if (paMul != 1f) {
                if (paMul == 0f) {
                    D.Err("PointAllowanceMul is 0f for diff: {0}", ctx.S.Difficulty.Id);
                } else {
                    // D.Warn("PointAllowanceMul for {0} with diff {1}: {2}. Overall mul: {3} -> {4}",
                    //     ctx.Generator.Id, outcome, paMul, multiplier, multiplier * paMul);
                    multiplier *= paMul;
                }
            }
            LoadCurve(PropertyIdH.ColonistsCurve, ref colonistsCurve);
            LoadCurve(PropertyIdH.WorkersCurve, ref workersCurve);
            LoadCurve(PropertyIdH.EnergyCurve, ref energyCurve);
            LoadCurve(PropertyIdH.DaysCurve, ref daysCurve);
            LoadCurve(PropertyIdH.ShipClassPointsCurve, ref shipClassPointsCurve);
            LoadCurve(PropertyIdH.NetWorthCurve, ref netWorthCurve);
        }

        public override void Unload(StoryContext ctx) {
        }

    }
}
