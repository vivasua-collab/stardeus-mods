using Game.Constants;
using Game.Data;
using Game.Systems.Notify;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Story {
    // OnDemand and free: started by StructureCollapseSys when neglected damage gives way, and
    // by the opening's curated beat. The collapse itself is StructureCollapse
    public sealed class DamagedStructureCollapseStoryEvent : StoryEvent {
        public const string EventId = "DamagedStructureCollapse";
        public const string DefId = "Main/DamagedStructureCollapse";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new DamagedStructureCollapseStoryEvent());
        }

        public override bool CheckConditions(StoryContext ctx, StoryEventDef def) {
            return StructureCollapse.HasCandidate(ctx.S);
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            var s = ctx.S;
            var start = StructureCollapse.PickStart(s, data.SeededRng);
            if (start == null) {
                data.FailEvent(s, "Did not find any damaged structure for collapse", isHardFailure: false);
                return;
            }
            data.TargetTile = start.Tile;
            D.Warn("Collapsing damaged structure: {0}->{1}", start.HealthPercent, start.Tile);
            if (!StructureCollapse.Collapse(s, data.SeededRng, start, out var result)) {
                data.FailEvent(s, "Damaged structure could not collapse", isHardFailure: false);
                return;
            }
            Announce(s, data, result);
            data.SetState(s, StoryEventState.Ending);
        }

        private static void Announce(GameState s, StoryEventData data,
                StructureCollapse.Result result) {
            // Create log entry and notifications
            data.CreateLogEntry(IconId.WDamage, T.EvtDamagedStructureCollapsed,
                T.EvtDamagedStructureCollapsedDesc_V2.F(result.FirstName,
                    Texts.LinkToPos(result.FirstPos)));

            // Not tied to a tile: the hub drops a notification whose tile is gone, and the
            // collapse just removed it
            var rect = result.Rect;
            var center = rect.Center();
            var size = rect.Extents;
            Notification.Build(IconId.CWarning, data.LogEntry.Title, Consts.TicksPerDay)
                .Group("inc", T.NounIncident, IconId.CWarning)
                .WithArea(center, size)
                .WithOnClick(() => EntityUtils.CameraFocusOn(center, size))
                .WithTooltip(data.LogEntry.Desc)
                .Show();

            var logLine = Texts.LinkTo(Texts.Red(T.EvtDamagedStructureCollapsed),
                rect.CenterPos);
            s.Sys.Log.AddLine(logLine);
            s.Sig.ShowFanfare.Send(logLine);
        }
    }
}
