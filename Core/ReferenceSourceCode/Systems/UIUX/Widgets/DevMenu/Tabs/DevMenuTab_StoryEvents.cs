#if !STARDEUS_DEMO
using System;
using System.Collections.Generic;
using System.Linq;
using Game.CodeGen;
using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Story.Events;
using Game.Systems.Quests;
using Game.Systems.Story;
using Game.Tools;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class DevMenuTab_StoryEvents : DevMenuTab {
        public const string TabIdConst = "StoryEvents";
        public override string TabId => TabIdConst;
        public override string TabName => "Story Events";

        public enum GroupMode { None, ByFlag, ByOutcome }
        public enum ListFilterMode { All, Available, Unavailable, CoolingDown }
        public enum OnDemandFilterMode { All, NotOnDemand, OnDemand }
        public enum SortMode { Id, LastSeen, LastStart, WeightDesc, WeightAsc, SeenCountDesc, SeenCountAsc, PointsCostDesc, PointsCostAsc }
        public enum OutcomeFilterMode { ShowAll, Negative, Positive, Neutral, Raid }

        private GroupMode groupMode;
        private OnDemandFilterMode onDemandFilter;
        private ListFilterMode listFilterMode;
        private StoryPickIgnore ignoreMode;
        private SortMode sortMode;
        private OutcomeFilterMode outcomeFilter;
        private bool showFlags;
        private string searchStr;
        private StoryEventDef selectedEvent;
        private Lifetime tabLifetime;
        private Lifetime detailsLifetime;
        private Lifetime eventsBlockLife;
        private WGroup currentGroup;
        private int eventsBlockGIdx;
        private SubPanelWidget currentSubPanel;
        private Action currentReload;

        public DevMenuTab_StoryEvents(GameState s) : base(s) { }

        public override void LoadContent(Lifetime life, WGroup group, ref int gIdx,
                SubPanelWidget subPanel, Action reload) {
            tabLifetime = life;
            currentGroup = group;
            currentSubPanel = subPanel;
            currentReload = reload;

            var ctx = S.Query.GetStoryContext.Ask();

            // --- Story Generator block ---
            var genBlock = UISpawn.Block(life, group, gIdx++);
            int gi = 0;
            UISpawn.Widget<WText>(life, UIPrefabId.WContent2Header, genBlock.Container, gi++).Load("Story Generator");

            var gen = S.Query.GetStoryGen.Ask();
            var pointAllowanceAspect = (PointAllowanceStoryAspect)
                gen.StoryAspects.First(a => a is PointAllowanceStoryAspect);
            int maxPoints = (int)pointAllowanceAspect.GetPointAllowance(ctx);

            var genRow = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button, genBlock.Container, gi++);
            genRow.Load(IconId.CDNA, gen.NameT, $"Points: {maxPoints}", () => {
                DebugOutputWidget.SpawnFor(new StoryDebugOutput(S.Query.GetStoryContext.Ask()));
            });

            var forcedSignalQuest = S.BB.Get(BBKeyH.DevForceSignalQuest, string.Empty);
            AddCycleButton(life, genBlock.Container, ref gi, IconId.CSignal,
                "Force Signal Quest",
                string.IsNullOrEmpty(forcedSignalQuest) ? "None" : forcedSignalQuest,
                () => CycleForcedSignalQuest(reload));

            if (ctx.ActiveHooks.Count > 0) {
                UISpawn.Widget<WText>(life, UIPrefabId.WContent2Header, genBlock.Container, gi++).Load("Active Hooks");
                foreach (var hook in ctx.ActiveHooks) {
                    UISpawn.Widget<WIconText>(life, UIPrefabId.WContent2Text, genBlock.Container, gi++).Load(IconId.CHaul, hook.ToString());
                }
            }
            if (ctx.OngoingEvents.Count > 0) {
                UISpawn.Widget<WText>(life, UIPrefabId.WContent2Header, genBlock.Container, gi++).Load("Ongoing Events");
                foreach (var ev in ctx.OngoingEvents) {
                    UISpawn.Widget<WIconText2>(life, UIPrefabId.WContent2Text2, genBlock.Container, gi++).Load(IconId.CInfo, ev.Def.Id, ev.State.ToString());
                }
            }

            // --- Story Schedules block ---
            var schedBlock = UISpawn.Block(life, group, gIdx++);
            int si = 0;
            UISpawn.Widget<WText>(life, UIPrefabId.WContent2Header, schedBlock.Container, si++).Load("Story Schedules");
            for (int i = 0; i < StorySchedule.Count; i++) {
                AddScheduleRow(life, schedBlock.Container, ref si, ctx.Schedule[i]);
            }

            // --- Flags block ---
            var flagsBlock = UISpawn.Block(life, group, gIdx++);
            int fli = 0;
            var flagsHeaderBtn = UISpawn.Widget<WButtonText2>(life, UIPrefabId.WContent2HeaderButton, flagsBlock.Container, fli++);
            flagsHeaderBtn.Load("Flags", () => { showFlags = !showFlags; reload(); }, showFlags ? "Hide" : "Show");
            if (showFlags) {
                var clearBtn = UISpawn.Widget<WButtonText>(life, UIPrefabId.WContent2ButtonSimple, flagsBlock.Container, fli++);
                clearBtn.Load("Clear All Flag Cooldowns", () => {
                    foreach (StoryEventFlags flag in Enum.GetValues(typeof(StoryEventFlags))) {
                        if (flag == StoryEventFlags.None || Enums.IsCompoundFlag(flag)) { continue; }
                        ctx.ClearCooldown(flag);
                    }
                    reload();
                });
                foreach (StoryEventFlags flag in Enum.GetValues(typeof(StoryEventFlags))) {
                    if (flag == StoryEventFlags.None || Enums.IsCompoundFlag(flag)) { continue; }
                    AddFlagRow(life, flagsBlock.Container, ref fli, ctx, flag, reload);
                }
            }

            // --- Filters block ---
            var filtersBlock = UISpawn.Block(life, group, gIdx++);
            int fi2 = 0;
            UISpawn.Widget<WText>(life, UIPrefabId.WContent2Header, filtersBlock.Container, fi2++).Load("Story Event Filters");

            AddCycleButton(life, filtersBlock.Container, ref fi2, IconId.WList, "Group", groupMode.ToString(), () => { groupMode = Enums.Cycle(groupMode); reload(); });
            AddCycleButton(life, filtersBlock.Container, ref fi2, IconId.WList, "Sort", sortMode.ToString(), () => { sortMode = Enums.Cycle(sortMode); reload(); });
            AddCycleButton(life, filtersBlock.Container, ref fi2, IconId.WFunnel, "Show", listFilterMode.ToString(), () => { listFilterMode = Enums.Cycle(listFilterMode); reload(); });
            AddCycleButton(life, filtersBlock.Container, ref fi2, IconId.WFunnel, "OnDemand", onDemandFilter.ToString(), () => { onDemandFilter = Enums.Cycle(onDemandFilter); reload(); });
            AddCycleButton(life, filtersBlock.Container, ref fi2, IconId.WFunnel, "Weight Ignore Mode", ignoreMode.ToString(), () => { ignoreMode = Enums.Cycle(ignoreMode); reload(); });
            AddCycleButton(life, filtersBlock.Container, ref fi2, IconId.WFunnel, "Outcome Filter", outcomeFilter.ToString(), () => { outcomeFilter = Enums.Cycle(outcomeFilter); reload(); });

            var searchWidget = UISpawn.Widget<WInputWithIcon>(life, UIPrefabId.WContent2Input, filtersBlock.Container, fi2++);
            searchWidget.Icon.Load(IconId.CMagnifier);
            UIUtils.LoadSearch(searchWidget.Input, newSearch => { searchStr = newSearch; RebuildEventsBlock(); }, placeholder: T.SearchPlaceholder, value: searchStr);

            // --- Events list block (has its own lifetime so search can rebuild it without full reload) ---
            eventsBlockGIdx = gIdx++;
            eventsBlockLife = life.CreateChild("EventsList");
            BuildEventsBlock();
        }

        private void RebuildEventsBlock() {
            eventsBlockLife?.Dispose();
            eventsBlockLife = tabLifetime.CreateChild("EventsList");
            BuildEventsBlock();
        }

        private void BuildEventsBlock() {
            var ctx = S.Query.GetStoryContext.Ask();
            var gen = S.Query.GetStoryGen.Ask();
            var pointAllowanceAspect = (PointAllowanceStoryAspect)
                gen.StoryAspects.First(a => a is PointAllowanceStoryAspect);
            int maxPoints = (int)pointAllowanceAspect.GetPointAllowance(ctx);

            var l = eventsBlockLife;
            var eventsBlock = UISpawn.Block(l, currentGroup, eventsBlockGIdx);
            int ei = 0;
            UISpawn.Widget<WText>(l, UIPrefabId.WContent2Header, eventsBlock.Container, ei++).Load("Story Events");

            WBase freshSelectedWidget = null;
            int added = 0;

            if (groupMode == GroupMode.None) {
                foreach (var def in FilteredEvents(StoryEventDef.All.Values, ctx, maxPoints)) {
                    if (AddEventRow(l, eventsBlock.Container, ref ei, ctx, def, maxPoints, out var w)) {
                        added++;
                        if (def == selectedEvent) { freshSelectedWidget = w; }
                    }
                }
            } else if (groupMode == GroupMode.ByOutcome) {
                for (int oi = 0; oi < StoryEventDef.ByOutcome.Length; oi++) {
                    var outcomeDefs = FilteredEvents(StoryEventDef.ByOutcome[oi], ctx, maxPoints).ToList();
                    if (outcomeDefs.Count == 0) { continue; }
                    UISpawn.Widget<WText>(l, UIPrefabId.WContent2Header, eventsBlock.Container, ei++).Load(((StoryEventOutcome)oi).ToString());
                    foreach (var def in outcomeDefs) {
                        if (AddEventRow(l, eventsBlock.Container, ref ei, ctx, def, maxPoints, out var w)) {
                            added++;
                            if (def == selectedEvent) { freshSelectedWidget = w; }
                        }
                    }
                }
            } else if (groupMode == GroupMode.ByFlag) {
                foreach (StoryEventFlags flag in Enum.GetValues(typeof(StoryEventFlags))) {
                    if (flag == StoryEventFlags.None) { continue; }
                    var flagDefs = FilteredEvents(StoryEventDef.All.Values, ctx, maxPoints).Where(d => d.Flags.HasFlag(flag)).ToList();
                    if (flagDefs.Count == 0) { continue; }
                    UISpawn.Widget<WText>(l, UIPrefabId.WContent2Header, eventsBlock.Container, ei++).Load(flag.ToString());
                    foreach (var def in flagDefs) {
                        if (AddEventRow(l, eventsBlock.Container, ref ei, ctx, def, maxPoints, out var w)) {
                            added++;
                            if (def == selectedEvent) { freshSelectedWidget = w; }
                        }
                    }
                }
            }

            UISpawn.Widget<WIconText2>(l, UIPrefabId.WContent2Text2, eventsBlock.Container, ei++).Load(IconId.CCount, "Count", Units.XNum(added));

            // Re-open sub-panel for previously selected event after rebuild
            if (selectedEvent != null && freshSelectedWidget != null) {
                LayoutRebuilder.ForceRebuildLayoutImmediate(currentGroup.Container);
                ShowEventDetails(freshSelectedWidget, selectedEvent, currentSubPanel, currentReload);
            }
        }

        private static void AddCycleButton(Lifetime life, RectTransform container, ref int idx,
                string icon, string label, string value, Action onClick) {
            var btn = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button, container, idx++);
            btn.Load(icon, label, value, onClick);
        }

        private void CycleForcedSignalQuest(Action reload) {
            var signalQuests = QuestDef.All
                .Where(q => (q.Providers & QuestProviderType.Signal) != QuestProviderType.None)
                .OrderBy(q => q.Id)
                .ToList();
            if (signalQuests.Count == 0) {
                UISounds.PlayDigitalCancel();
                return;
            }
            var current = S.BB.Get(BBKeyH.DevForceSignalQuest, string.Empty);
            var currentIndex = signalQuests.FindIndex(q => q.Id == current);
            if (currentIndex == signalQuests.Count - 1) {
                S.BB.Remove(BBKeyH.DevForceSignalQuest, current);
            } else {
                var next = signalQuests[currentIndex + 1];
                S.BB.Set(BBKeyH.DevForceSignalQuest, next.Id);
            }
            S.IsAchievementsBlocked = true;
            UISounds.PlayDigitalConfirm();
            reload();
        }

        // Returns false if row should be filtered out; out widget is the spawned row.
        private bool AddEventRow(Lifetime life, RectTransform container, ref int idx,
                StoryContext ctx, StoryEventDef def, int maxPoints, out WBase spawnedWidget) {
            spawnedWidget = null;
            var w = WeightOf(def, out var canStart);
            if (listFilterMode == ListFilterMode.Available && !canStart) { return false; }
            if (listFilterMode == ListFilterMode.Unavailable && canStart) { return false; }
            if (listFilterMode == ListFilterMode.CoolingDown && !ctx.IsCoolingDown(def)) { return false; }

            string searchQuery = Strings.ToSearchString(searchStr, true);
            if (searchQuery != null && !def.Id.ToLower().Contains(searchQuery)) { return false; }

            string icon = canStart ? IconId.CCheck : (ctx.IsCoolingDown(def) ? IconId.CSnowflake : IconId.CCross);
            string title = canStart ? Texts.Green(def.Id) : Texts.Grey(def.Id);
            if (def.IsDisabled) { title = Texts.Strike(title); }
            var seenCnt = SeenCount(def);
            string pts = def.Points <= maxPoints ? Texts.Green(def.Points.ToString()) : Texts.Red(def.Points.ToString());
            title = $"x{seenCnt}: {pts} {title}";
            if (selectedEvent == def) { title = $"{title} {Texts.Accented(GlyphMap.TriangleLeft)}"; }

            var row = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button, container, idx++);
            row.Load(icon, title, string.Format("{0:0.##}", w), () => {
                ShowEventDetails(row, def, currentSubPanel, currentReload);
            });
            UIUtils.AddTipFunc(life, row, () => {
                var seen2 = S.Query.GetStoryContext.Ask().SeenDataFor(def);
                return $"Seen: {seen2}";
            });
            spawnedWidget = row;
            return true;
        }

        private void ShowEventDetails(WBase anchor, StoryEventDef def, SubPanelWidget subPanel, Action reload) {
            if (subPanel.IsShowing && selectedEvent == def) {
                selectedEvent = null;
                subPanel.Close();
                reload();
                return;
            }
            selectedEvent = def;

            detailsLifetime?.Dispose();
            detailsLifetime = tabLifetime.CreateChild("EventDetails");
            var l = detailsLifetime;
            var ctx = S.Query.GetStoryContext.Ask();

            subPanel.SetTitle("Story Event");
            int bIdx = 0;
            var block = UISpawn.Block(l, subPanel.Container, bIdx++);
            int idx = 0;

            // Start Event button
            var startBtn = UISpawn.Widget<WButtonIconText2>(l, UIPrefabId.WContent2Button, block.Container, idx++);
            startBtn.Load(IconId.WPlay, def.Id, "Start Event", () => {
                if (CreateEventFrom(S, def)) { reload(); }
            });
            SetEventTooltip(l, startBtn, def);

            var prioritizeBtn = UISpawn.Widget<WButtonText>(l, UIPrefabId.WContent2ButtonSimple, block.Container, idx++);
            prioritizeBtn.Load("Prioritize for Scheduling", () => {
                S.Query.GetStoryContext.Ask().PrioritizeEvent(def, "Dev UI");
            });

            // Stats
            UISpawn.Widget<WText>(l, UIPrefabId.WContent2Header, block.Container, idx++).Load("Stats");

            var handler = S.Query.StoryEventHandlerFor.Ask(def.Outcome);
            var wt = handler.GetEventWeight(ctx, def, StoryPickIgnore.None);
            var wNoSchedule = handler.GetEventWeight(ctx, def, StoryPickIgnore.Schedule);
            var ignore = def.Flags.HasFlag(StoryEventFlags.OnDemand) ? StoryPickIgnore.All : StoryPickIgnore.None;
            var canStart = handler.CanStartNow(ctx.SeenDataFor(def), def, ctx, ignore);

            UISpawn.Widget<WIconText2>(l, UIPrefabId.WContent2Text2, block.Container, idx++).Load(IconId.CStarcreds, "Story Points", def.Points.ToString());

            var weightRow = UISpawn.Widget<WIconText2>(l, UIPrefabId.WContent2Text2, block.Container, idx++);
            weightRow.Load(canStart ? IconId.CCheck : IconId.CCross, "Weight", string.Format("{0:0.##}/{1:0.##}", wt, wNoSchedule));
            UIUtils.AddTip(l, weightRow, canStart ? $"Can pick. Weight: {wt}" : $"Can't pick. Weight: {wt}");

            string outcomeIcon = def.Outcome switch {
                StoryEventOutcome.Negative => IconId.CMoodBad,
                StoryEventOutcome.Neutral => IconId.CMoodNeutral,
                StoryEventOutcome.Positive => IconId.CMoodGood,
                StoryEventOutcome.Raid => IconId.CCombat,
                _ => IconId.CWarning
            };
            UISpawn.Widget<WIconText2>(l, UIPrefabId.WContent2Text2, block.Container, idx++).Load(outcomeIcon, "Outcome", def.Outcome.ToString());
            UISpawn.Widget<WIconText2>(l, UIPrefabId.WContent2Text2, block.Container, idx++).Load(IconId.CPercentGreen, "Rarity", def.Rarity.ToString());

            var jsonPath = $"{Game.Mods.ModLoader.CoreModFilePath($"Config/Story/Events/{def.Id}")}.json";
            var fileBtn = UISpawn.Widget<WButtonIconText2>(l, UIPrefabId.WContent2Button, block.Container, idx++);
            fileBtn.Load(IconId.CInfo, jsonPath.Split('/')[^1], Texts.ExternalLink("Open"), () => Files.OpenOSFile(jsonPath));
            UIUtils.AddTipFunc(l, fileBtn, () => Files.ReadText(jsonPath));

            // Seen
            var seen = ctx.SeenDataFor(def);
            UISpawn.Widget<WText>(l, UIPrefabId.WContent2Header, block.Container, idx++).Load("Seen");
            UISpawn.Widget<WIconText2>(l, UIPrefabId.WContent2Text2, block.Container, idx++).Load(IconId.CTime,
                seen.LastStart > 0 ? $"Last Start: {Units.TicksAgoFromNow(seen.LastStart)}" : "Never Started",
                Units.XNum(seen.StartCount));
            UISpawn.Widget<WIconText2>(l, UIPrefabId.WContent2Text2, block.Container, idx++).Load(IconId.CTime,
                seen.LastSeen > 0 ? $"Last Seen: {Units.TicksAgoFromNow(seen.LastSeen)}" : "Never Seen",
                Units.XNum(seen.SeenCount));

            // Event flags
            bool hasFlags = false;
            foreach (StoryEventFlags flag in Enum.GetValues(typeof(StoryEventFlags))) {
                if (flag == StoryEventFlags.None || !def.Flags.HasFlag(flag)) { continue; }
                if (!hasFlags) {
                    UISpawn.Widget<WText>(l, UIPrefabId.WContent2Header, block.Container, idx++).Load("Flags");
                    hasFlags = true;
                }
                UISpawn.Widget<WIconText>(l, UIPrefabId.WContent2Text, block.Container, idx++).Load(
                    ctx.IsCoolingDown(flag) ? IconId.CSnowflake : IconId.CCheck, flag.ToString());
            }

            // History
            UISpawn.Widget<WText>(l, UIPrefabId.WContent2Header, block.Container, idx++).Load("History");
            bool anyHistory = false;
            for (int i = ctx.OngoingEvents.Count - 1; i >= 0; i--) {
                if (AddHistoryRow(l, block.Container, ref idx, ctx.OngoingEvents[i], def)) { anyHistory = true; }
            }
            for (int i = ctx.PastEvents.Count - 1; i >= 0; i--) {
                if (AddHistoryRow(l, block.Container, ref idx, ctx.PastEvents[i], def)) { anyHistory = true; }
            }
            if (!anyHistory) {
                UISpawn.Widget<WIconText>(l, UIPrefabId.WContent2Text, block.Container, idx++).Load(IconId.CInfo, T.Empty);
            }

            subPanel.ShowFooter(false);
            subPanel.Show(anchor, true);
        }

        private bool AddHistoryRow(Lifetime l, RectTransform container, ref int idx,
                StoryEventData data, StoryEventDef def) {
            if (data.Def != def) { return false; }
            string title = data.LogEntry.IsValid ? data.LogEntry.Title : data.State.ToString();
            string icon = data.LogEntry.IsValid ? data.LogEntry.Icon : IconId.CMissing;
            if (!data.IsEnded) { title = Texts.Green(title); }
            var row = UISpawn.Widget<WIconText2>(l, UIPrefabId.WContent2Text2, container, idx++);
            row.Load(icon, title, Units.TicksAgoFromNow(data.StartTick));
            UIUtils.AddTipFunc(l, row, () => {
                var sb = Caches.StringBuilder;
                sb.Clear();
                if (!data.IsEnded) { sb.AppendLine("Ongoing event!"); }
                sb.AppendLine(data.LogEntry.Desc);
                sb.AppendLine(data.ToString());
                return sb.ToString();
            });
            return true;
        }

        private void AddScheduleRow(Lifetime l, RectTransform container, ref int idx, StorySchedule schedule) {
            var icon = schedule.IsAllowed ? IconId.CCheck : IconId.CCross;
            if (schedule.CooldownUntil - G.Ticks > 0) { icon = IconId.CSnowflake; }
            string title = schedule.IsAllowed ? Texts.Green(schedule.Outcome.ToString()) : Texts.Red(schedule.Outcome.ToString());
            var events = StoryEventDef.ByOutcome[(int)schedule.Outcome];
            int onDem = events.Count(ev => ev.Flags.HasFlag(StoryEventFlags.OnDemand));
            title = $"{title} x{events.Count - onDem} {Texts.Grey($"(+{onDem} on demand)")}";
            var row = UISpawn.Widget<WButtonIconText2>(l, UIPrefabId.WContent2Button, container, idx++);
            row.Load(icon, title, Units.XOutOfY(schedule.PointAllowance, schedule.MaxPointAllowance), () => {
                S.IsAchievementsBlocked = true;
                schedule.ChangeNextTick();
                if (S.Clock.Speed != ClockSpeed.Normal) { S.Clock.SetSpeed(ClockSpeed.Normal); }
            });
            UIUtils.AddTipFunc(l, row, () => {
                var sb = Caches.StringBuilder;
                sb.Clear();
                sb.AppendFormat("Points: {0}/{1}\n", schedule.PointAllowance, schedule.MaxPointAllowance);
                sb.AppendFormat("Until change: {0}\n", Units.TicksToTime(schedule.ChangeAfter - G.Ticks));
                var cd = schedule.CooldownUntil - G.Ticks;
                if (cd > 0) { sb.AppendFormat("Cooling down: {0}\n", Units.TicksToTime(cd)); }
                return sb.ToString();
            });
        }

        private void AddFlagRow(Lifetime l, RectTransform container, ref int idx,
                StoryContext ctx, StoryEventFlags flag, Action reload) {
            string icon = ctx.IsCoolingDown(flag) ? IconId.CSnowflake : IconId.CCheck;
            int count = StoryEventDef.All.Values.Count(d => d.Flags.HasFlag(flag));
            var row = UISpawn.Widget<WButtonIconText2>(l, UIPrefabId.WContent2Button, container, idx++);
            row.Load(icon, flag.ToString(), Texts.Grey(Units.XNum(count)), () => {
                if (ctx.IsCoolingDown(flag)) { ctx.ClearCooldown(flag); }
                else if (!ctx.AutoCoolDownFlag(flag)) { S.Sig.ShowErrorMessage.Send("This flag has no cooldown def!"); }
                reload();
            });
            UIUtils.AddTipFunc(l, row, () => {
                if (ctx.FlagCooldowns.TryGetValue(flag, out var cd) && cd - G.Ticks > 0) {
                    return $"{Texts.Accented(flag.ToString())}\nCooling down: {Units.TicksToTime(cd - G.Ticks)}";
                }
                return $"{Texts.Accented(flag.ToString())}\n{T.AdjAvailable}";
            });
        }

        private IEnumerable<StoryEventDef> FilteredEvents(IEnumerable<StoryEventDef> events,
                StoryContext ctx, int maxPoints) {
            switch (outcomeFilter) {
                case OutcomeFilterMode.Negative: events = events.Where(ev => ev.Outcome == StoryEventOutcome.Negative); break;
                case OutcomeFilterMode.Positive: events = events.Where(ev => ev.Outcome == StoryEventOutcome.Positive); break;
                case OutcomeFilterMode.Neutral: events = events.Where(ev => ev.Outcome == StoryEventOutcome.Neutral); break;
                case OutcomeFilterMode.Raid: events = events.Where(ev => ev.Outcome == StoryEventOutcome.Raid); break;
            }
            switch (onDemandFilter) {
                case OnDemandFilterMode.OnDemand: events = events.Where(ev => ev.Flags.HasFlag(StoryEventFlags.OnDemand)); break;
                case OnDemandFilterMode.NotOnDemand: events = events.Where(ev => !ev.Flags.HasFlag(StoryEventFlags.OnDemand)); break;
            }
            return sortMode switch {
                SortMode.Id => events.OrderBy(e => e.Id),
                SortMode.WeightAsc => events.OrderBy(e => WeightOf(e, out _)),
                SortMode.WeightDesc => events.OrderByDescending(e => WeightOf(e, out _)),
                SortMode.SeenCountAsc => events.OrderBy(e => SeenCount(e)),
                SortMode.SeenCountDesc => events.OrderByDescending(e => SeenCount(e)),
                SortMode.PointsCostAsc => events.OrderBy(e => e.Points),
                SortMode.PointsCostDesc => events.OrderByDescending(e => e.Points),
                SortMode.LastSeen => events.OrderByDescending(e => LastSeen(e)),
                SortMode.LastStart => events.OrderByDescending(e => LastStart(e)),
                _ => events.OrderBy(e => e.Id),
            };
        }

        private float WeightOf(StoryEventDef def, out bool canStart) {
            var ctx = S.Query.GetStoryContext.Ask();
            var handler = S.Query.StoryEventHandlerFor.Ask(def.Outcome);
            var ignore = def.Flags.HasFlag(StoryEventFlags.OnDemand) ? StoryPickIgnore.All : ignoreMode;
            canStart = handler.CanStartNow(ctx.SeenDataFor(def), def, ctx, ignore);
            return handler.GetEventWeight(ctx, def, ignoreMode);
        }

        private int SeenCount(StoryEventDef e) => S.Query.GetStoryContext.Ask().SeenDataFor(e).SeenCount;
        private long LastSeen(StoryEventDef e) => S.Query.GetStoryContext.Ask().SeenDataFor(e).LastSeen;
        private long LastStart(StoryEventDef e) => S.Query.GetStoryContext.Ask().SeenDataFor(e).LastStart;

        private static void SetEventTooltip(Lifetime l, WBase w, StoryEventDef def) {
            string tip = def.Id.StartsWith("Main/") || def.Id.StartsWith("Social/") ? "Click to start the event" :
                def.Flags.HasFlag(StoryEventFlags.Capsule) ? "Requires an unopened Storage Capsule." :
                def.Flags.HasFlag(StoryEventFlags.Egg) ? "Requires an unhatched Egg." :
                def.Flags.HasFlag(StoryEventFlags.Expedition) ? "Enqueues the event for the next expedition." :
                def.Flags.HasFlag(StoryEventFlags.StasisPod) ? "Requires an unopened Stasis Pod." :
                def.Flags.HasFlag(StoryEventFlags.Signal) ? "Enqueues for the next Signal reply." :
                def.Flags.HasFlag(StoryEventFlags.DerelictShip) ? "Requires an unapproached Derelict Ship nearby." :
                "Click to start the event.";
            UIUtils.AddTip(l, w, tip);
        }
        public static bool CreateEventFrom(GameState S, StoryEventDef meta) {
            if (meta.Id.StartsWith("Main/")) {
                return CreateMainEvent(S, meta);
            }
            if (meta.Id.StartsWith("Social/")) {
                return CreateMainEvent(S, meta);
            }
            if (meta.Flags.HasFlag(StoryEventFlags.Space)) {
                return CreateMainEvent(S, meta);
            }
            if (meta.Flags.HasFlag(StoryEventFlags.Capsule)) {
                return CreateCapsuleEvent(S, meta);
            }
            if (meta.Flags.HasFlag(StoryEventFlags.Signal)) {
                return CreateSignalEvent(S, meta);
            }
            if (meta.Flags.HasFlag(StoryEventFlags.StasisPod)) {
                return CreatePodEvent(S, meta);
            }
            if (meta.Flags.HasFlag(StoryEventFlags.Egg)) {
                return CreateEggEvent(S, meta);
            }
            if (meta.Flags.HasFlag(StoryEventFlags.DerelictShip)) {
                return CreateDerelictShipEvent(S, meta);
            }
            if (meta.Flags.HasFlag(StoryEventFlags.Expedition)) {
                return CreateExpeditionEvent(S, meta);
            }
            if (meta.Flags.HasFlag(StoryEventFlags.ColonistRequest)) {
                return CreateMainEvent(S, meta);
            }
            D.Err("Unhandled event! {0}", meta);
            return false;
        }

        private static bool CreateDerelictShipEvent(GameState S, StoryEventDef meta) {
            S.CmdQ.Enqueue(new CmdSpawnDerelictShip(null, meta));
            return true;
        }

        private static bool CreateMainEvent(GameState S, StoryEventDef meta) {
            try {
                var data = StoryEventData.CreateFor(meta);
                return TryStartEvent(S, data);
            } catch (Exception ex) {
                D.LogEx(ex, "Failed creating dev mode story event: {0}", meta.Id);
                NotifyFail($"Failed creating {Texts.Accented(meta.Id)} event. It may require special conditions that did not match",
                    $"Error:\n\n {ex.Message}");
                UISounds.PlayDigitalCancel();
                return false;
            }
        }

        private static bool TryStartEvent(GameState S, StoryEventData ev) {
            try {
                ev.Flags |= StoryEventDataFlags.Debug;
                if (ev.Seed == 0) {
                    ev.Seed = 420;
                }
                S.IsAchievementsBlocked = true;
                if (S.Query.StartStoryEvent.Ask(ev)) {
                    UISounds.PlayDigitalConfirm();
                    return true;
                } else {
                    WPopup.Create(false, IconId.CWarning, "Failed starting event", ev.ToString()).Spawn();
                }
            } catch (Exception ex) {
                D.LogEx(ex, "Failed executing dev mode story event: {0}", ev);
                NotifyFail($"Failed executing event. It may require special conditions that did not match: {ev.Id}.",
                    $"Error:\n\n {ex.Message}");
                UISounds.PlayDigitalCancel();
            }
            return false;
        }
        private static bool CreateExpeditionEvent(GameState S, StoryEventDef meta) {
            S.BB.Set(BBKeyH.DevForceExpeditionEvent, meta.Id);
            S.IsAchievementsBlocked = true;
            UISounds.PlayDigitalConfirm();
            return true;
        }
        private static bool CreateSignalEvent(GameState S, StoryEventDef meta) {
            S.BB.Set(BBKeyH.DevForceSignalEvent, meta.Id);
            S.IsAchievementsBlocked = true;
            UISounds.PlayDigitalConfirm();
            return true;
        }
        private static bool CreateEggEvent(GameState S, StoryEventDef meta) {
            var selObj = S.Query.GetSelectedObj.Ask(true);
            EggComp egg = null;
            selObj?.TryGetComponent(out egg);
            if (egg == null) {
                egg = S.Components.FindFirstByType<EggComp>();
                if (egg != null) {
                    EntityUtils.CameraFocusOn(egg.Entity);
                }
            }
            if (egg == null) {
                NotifyFail("Please spawn and optionally select an <b>Egg</b> to trigger Egg event types!",
                    "Switching to egg spawn tool now.");
                #if !STARDEUS_DEMO
                DevMenu.ToggleInstantBuild(true);
                #endif
                var tool = The.Toolbox.Get(ToolSpawnObj.ToolId);
                The.Toolbox.SetActive(tool, DefIds.ObjEggsEgg01);
                return false;
            }
            var ev = StoryEventData.CreateFor(meta);
            ev.TargetObj = egg.Obj.Data;
            ev.Seed = egg.Seed;
            return TryStartEvent(S, ev);
        }

        private static bool CreatePodEvent(GameState S, StoryEventDef meta) {
            var selObj = S.Query.GetSelectedParentTile.Ask(true);
            StasisPodComp obj = null;
            if (selObj?.TryGetComponent<StasisPodComp>(out obj) == true) {
                if (obj.IsOpened) {
                    NotifyFail("Selected stasis pod is <b>already open</b>, please select an unopened one", null);
                    return false;
                }
            }
            if (obj == null) {
                obj = S.Components.FindFirstMatching<StasisPodComp>(c => !c.IsOpened);
                if (obj != null) {
                    EntityUtils.CameraFocusOn(obj.Entity);
                }
            }
            if (obj == null) {
                NotifyFail("Please spawn and optionally select an unopened <b>Stasis Pod</b> to trigger Capsule event types!",
                    "Switching to capsule spawn tool now.");
                #if !STARDEUS_DEMO
                DevMenu.ToggleInstantBuild(true);
                #endif
                var tool = The.Toolbox.Get(ToolPlaceTiles.ToolId);
                The.Toolbox.SetActive(tool);
                tool.Configure(DefIds.ObjectsDevicesStasisPod);
                return false;
            }
            var ev = StoryEventData.CreateFor(meta);
            S.IsAchievementsBlocked = true;
            ev.Seed = obj.ContentsSeed;
            ev.TargetTile = obj.Tile;
            return TryStartEvent(S, ev);
        }
        private static bool CreateCapsuleEvent(GameState S, StoryEventDef meta) {
            var selObj = S.Query.GetSelectedObj.Ask(true);
            StorageCapsuleComp cap = null;
            if (selObj?.TryGetComponent<StorageCapsuleComp>(out cap) == true) {
                if (cap.IsOpen) {
                    NotifyFail($"{Texts.LinkToObj(selObj, "Selected capsule")} is <b>already open</b>, please select an unopened one", null);
                    return false;
                }
            }
            if (cap == null) {
                cap = S.Components.FindFirstMatching<StorageCapsuleComp>(c => !c.IsOpen);
                if (cap != null) {
                    EntityUtils.CameraFocusOn(cap.Entity);
                }
            }
            if (cap == null) {
                NotifyFail("Please spawn and optionally select an unopened <b>Storage Capsule</b> to trigger Capsule event types!",
                    "Switching to capsule spawn tool now.");
                #if !STARDEUS_DEMO
                DevMenu.ToggleInstantBuild(true);
                #endif
                var tool = The.Toolbox.Get(ToolSpawnObj.ToolId);
                The.Toolbox.SetActive(tool, DefIds.ObjContainersStorageCapsule);
                return false;
            }
            S.IsAchievementsBlocked = true;
            cap.RiggedEventId = meta.Id;
            NotifySuccess($"{Texts.LinkToObj(selObj, "Selected capsule")} rigged with story event:", Texts.Accented(meta.Id));
            return true;
        }

        private static void NotifyFail(string msg, string msg2) {
            WPopup.Create(false, IconId.CCross, "Failure!", msg).Spawn();
        }

        private static void NotifySuccess(string msg, string msg2) {
            WPopup.Create(false, IconId.CCheck, "Success!", msg, msg2).Spawn();
        }
    }
}
#endif
