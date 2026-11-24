using Game.CodeGen;
using Game.Constants;
using Game.Utils;
using KL.Randomness;
using KL.Utils;

namespace Game.Systems.Story {
    // Keeps the storyteller's events apart across outcomes. Each outcome paces only itself
    // (window, points, picker cooldown, cap), and the pickers' slots are a game minute apart,
    // so outcomes that became eligible together fired back to back: in STA-1654 a comet shower
    // and an egg from the next window came 3.5 game hours apart, seconds at high speed.
    // A notable start opens a quiet period that the periodic pickers, which have no moment of
    // their own, wait out. Scripted beats and hooks keep their timing and only stamp. See
    // StorySystem.md, "Spacing between events".
    public sealed class SpacingStoryAspect : StoryAspect {
        [SelfInit]
        private static void Register() {
            Register(AspectId, Factory);
        }

        private static StoryAspect Factory() {
            return new SpacingStoryAspect();
        }

        public const string AspectId = "Spacing";
        public override string Id => AspectId;

        private const string K_QuietUntil = "Spacing.QuietUntil";
        // Raids keep their own cadence and neither open nor wait out a quiet period. Social
        // interactions and breakdowns are colony life, started by hooks on their own clocks,
        // and come often enough in a busy colony that stamping them would hold the storyteller
        // back for good. Drama is not here: its events (fights, mutiny) are periodic picks
        private const StoryEventFlags Unpaced = StoryEventFlags.Raid | StoryEventFlags.Social
            | StoryEventFlags.MentalBreakdown;

        private Curve quietHoursCurve;
        // Chance that a start opens no quiet period, so two events still land together now
        // and then. Public so tests can make the roll deterministic; reset from the def on
        // every Initialize
        public float ClumpChance;

        public override void Initialize(StoryContext ctx, bool isLoading) {
            LoadCurve(PropertyIdH.QuietHoursCurve, ref quietHoursCurve);
            ClumpChance = Def.Vars.Get(PropertyIdH.ClumpChance, 0f);
            ctx.Spacing = this;
        }

        public override void Unload(StoryContext ctx) {
            if (ctx.Spacing == this) {
                ctx.Spacing = null;
            }
        }

        public override void OnTick(StoryContext ctx) {
        }

        public long QuietUntil(StoryContext ctx) {
            return ctx.AspectData.GetLong(K_QuietUntil, 0L);
        }

        public bool IsQuiet(StoryContext ctx) {
            return ctx.Ticks < QuietUntil(ctx);
        }

        public void OnEventStarted(StoryContext ctx, StoryEventData data) {
            var def = data.Def;
            if (def.Outcome == StoryEventOutcome.Raid
                    || (def.Flags & Unpaced) != StoryEventFlags.None) {
                return;
            }
            if (quietHoursCurve == null || ctx.Rng.Chance(ClumpChance)) {
                return;
            }
            var until = ctx.Ticks + (long) (ctx.Rng.Curve(quietHoursCurve) * Consts.TicksPerHour);
            if (until > QuietUntil(ctx)) {
                ctx.AspectData.SetLong(K_QuietUntil, until);
            }
        }
    }
}
