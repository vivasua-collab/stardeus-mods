using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Story;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems {
    // Damaged structure collapses as ship physics rather than a story pick: an hourly roll
    // whose odds follow how much damage is left unrepaired, under every storyteller, at no
    // story points. Why: Docs/Architecture/Systems/StructureCollapse.md
    public sealed class StructureCollapseSys : GameSystem {
        public const string SysId = "StructureCollapseSys";
        public override string Id => SysId;

        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new StructureCollapseSys());
        }

        public static StructureCollapseSys For(GameState s) {
            return s.Sys.Get<StructureCollapseSys>(SysId);
        }

        private Curve mtbCurve;

        protected override void OnInitialize() {
            mtbCurve = CurveDef.FastGet(CurveIdH.Structure_CollapseMtbDays);
            S.Clock.OnTick.AddListener(OnTick);
        }

        public override void Unload() {
        }

        private void OnTick(long ticks) {
            if (ticks % Consts.TicksPerHour != 29) { return; }
            RollHour(ticks);
        }

        // Public for tests
        public bool RollHour(long ticks) {
            if (!CanRoll(ticks)) { return false; }
            var count = StructureCollapse.CountCandidates(S);
            if (!S.Rng.Chance(ChancePerHour(count))) { return false; }
            return TryStartNow();
        }

        // Public for tests. The opening has its own scripted collapse, and the gap counts any
        // start of the event (that beat, a console trigger), so two never come back to back
        public bool CanRoll(long ticks) {
            if (ticks < (long) (Tunables.StructureCollapseGraceDays * Consts.TicksPerDay)) {
                return false;
            }
            var def = StoryEventDef.Get(DamagedStructureCollapseStoryEvent.DefId);
            if (def == null || def.IsDisabled) { return false; }
            var ctx = S.Query.GetStoryContext.Ask();
            if (ctx == null) { return false; }
            var lastStart = ctx.SeenDataFor(def).LastStart;
            var gap = (long) (Tunables.StructureCollapseMinGapHours * Consts.TicksPerHour);
            return lastStart <= 0 || ticks - lastStart >= gap;
        }

        public float ChancePerHour(int damagedCount) {
            if (damagedCount < 1 || mtbCurve == null) { return 0f; }
            var mtbHours = mtbCurve.Evaluate(damagedCount) * 24f;
            if (mtbHours <= 0f) { return 1f; }
            return 1f - Mathf.Exp(-1f / mtbHours);
        }

        // Public for tests: starts a collapse now, skipping the roll, grace and gap
        public bool TryStartNow() {
            var def = StoryEventDef.Get(DamagedStructureCollapseStoryEvent.DefId);
            var ctx = S.Query.GetStoryContext.Ask();
            if (def == null || ctx == null) { return false; }
            return ctx.TryStartOnDemand(def, "neglected damage");
        }
    }
}
