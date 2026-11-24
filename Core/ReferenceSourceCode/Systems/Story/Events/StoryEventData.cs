#if !PRODUCTION_BUILD
// #define DEBUG_FLAGS
// #define DEBUG_STATE
#endif
using System;
using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Data.Space;
using Game.Input;
using Game.Systems.Creatures;
using Game.Systems.Items;
using Game.Systems.Mental;
using Game.Systems.Notify;
using Game.Systems.Space;
using Game.Utils;
using KL.Collections;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Story {
    /// <summary>
    /// Contains data about a story event, ongoing or past
    /// </summary>
    public sealed class StoryEventData {

        public static StoryEventData CreateCustom(string iconId, string title, string desc) {
            var d = new StoryEventData();
            d.Id = ++LastId;
            d.CreateLogEntry(iconId, title, desc);
            d.State = StoryEventState.Ending;
            return d;
        }
        public static StoryEventData CreateFor(StoryEventDef def)  {
            var d = new StoryEventData();
            d.Id = ++LastId;
            d.Def = def;
            if (def == null) {
                D.Err("Trying to create story event data for null def!");
                return null;
            }
            if (def.Event == null) {
                D.Err("Trying to create story event data for def that has null event! {0}", def);
                return null;
            }
            return d;
        }
        public static uint LastId;
        public uint Id;
        public StoryEventDef Def;
        public StoryEventState State { get; private set; }
        #if DEBUG_FLAGS
        private StoryEventDataFlags flags;
        public StoryEventDataFlags Flags {
            get => flags;
            set => D.SetFieldWithDebug(ref flags, value, $"StoryEventDataFlags {this}");
        }
        #else
        public StoryEventDataFlags Flags;
        #endif
        public long StartTick;
        public long EndTick;
        public int PointsCostActual;
        /// <summary>
        /// Points actually deducted from the schedule when this event started (may differ from
        /// Def.Points if the allowance was below the cost and got clamped to 0). Used to refund
        /// exactly what was taken — no more, no less — in Finalize.
        /// </summary>
        public int PointsReserved;
        public StoryEventFlags EventFlagsActual;
        public Tile TargetTile;
        public BeingData TargetBeing;
        public ObjData TargetObj;
        public SpaceObject TargetSO;
        public FactionDef Faction;
        public SpaceSector Location;
        public List<Mind> Participants;
        /// <summary>
        /// Not save-loadable, so only useful in OnStart!
        /// </summary>
        public object TargetOther;
        public string FailReason;
        public int Seed;
        public int SeedForSave {
            get {
                if (seededRng != null) {
                    return (int) seededRng.State;
                }
                return Seed;
            }
        }
        private Rng seededRng;
        public Rng SeededRng {
            get {
                if (seededRng != null) {
                    return seededRng;
                }
                if (Seed == 0) {
                    D.Err("Trying to get seeded RNG but StoryEventData has no Seed! {0}", this);
                    seededRng = Rng.Unseeded.Fork();
                } else {
                    seededRng = Rng.Load((uint) Seed);
                }
                return seededRng;
            }
        }

        /// <summary>
        /// Any persistent state for ongoing story event
        /// </summary>
        public ComponentData Data;
        public ComponentData GetOrCreateData() => Data ??= new ComponentData();
        /// <summary>
        /// Any non-persistent state for ongoing story event
        /// </summary>
        private ComponentData temp;
        public ComponentData Temp => temp ??= new ComponentData();

        /// <summary>
        /// Non-persistent temp object for specific event implementations
        /// to keep the state between ticks.
        /// </summary>
        public object TempObj;

        /// <summary>
        /// If event requires listening to some signal, this should be set to true.
        /// It will not be persisted so in event's OnLoad resubscription should happen.
        /// </summary>
        public object EventListener;

        public NotificationHandle Event;
        /// <summary>
        /// Crate this using CreateLogEntry() method, or use HideLogEntry() if
        /// you don't want this event to show up in the story event log.
        /// <see cref="CreateLogEntry(string, string, string)"/>
        /// <see cref="HideLogEntry"/>
        /// </summary>
        public StoryLogEntry LogEntry;
        public bool IsEnded => State == StoryEventState.Ended || State == StoryEventState.Failed;
        public bool IsEndedSuccess => State == StoryEventState.Ended;
        public bool IsFailed => State == StoryEventState.Failed;

        /// <summary>
        /// Must assign data.Event to the result if you want this to be cleaned up
        /// automatically!
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="icon"></param>
        /// <param name="title"></param>
        /// <param name="type"></param>
        /// <param name="showPopup"></param>
        /// <param name="autoHide"></param>
        /// <param name="addLogLine"></param>
        /// <param name="showFanfare"></param>
        /// <param name="onClick"></param>
        /// <param name="onHover"></param>
        /// <param name="area"></param>
        /// <returns></returns>
        public NotificationHandle AddEventNotification(
                StoryContext ctx, string icon = null, string title = null,
                NotificationType type = NotificationType.Warning,
                bool showPopup = true, bool autoHide = true,
                bool addLogLine = false, bool showFanfare = false, Action onClick = null,
                Action<bool> onHover = null,
                (Vector2 pos, Vector2 size)? area = null, string voice = null) {
            icon ??= LogEntry.Icon;
            title ??= LogEntry.Title;
            if (string.IsNullOrEmpty(icon) || string.IsNullOrEmpty(title)) {
                D.Err("Trying to show notification with empty icon or title! {0} -> {1}",
                    this, LogEntry);
                return default;
            }

            if (Event.IsValid) {
                D.Err("Event already added! Trying to update!");
                Notification.Remove(ref Event);
            }
            if (showPopup) {
                ShowPopup();
            }
            if (showFanfare) {
                ctx.S.Sig.ShowFanfare.Send(LogEntry.Desc);
            }
            if (addLogLine) {
                ctx.S.Sys.Log.AddLine(LogEntry.Desc);
            }
            var not = Notification.Build(icon, title, autoHide ? Consts.TicksPer8Hours : 0, type)
                .WithOnClick(onClick ?? ShowPopup)
                .WithOnHover(onHover)
                .WithVoice(voice);
            if (onHover == null && area.HasValue) {
                not.WithArea(area.Value.pos, area.Value.size);
            }
            return not.Show();
        }

        public void RemoveNotification() {
            Notification.Remove(ref Event);
        }

        /// <summary>
        /// Initialized and not failed
        /// </summary>
        public static bool IsOK(StoryEventData data) {
            return data != null && data.State != StoryEventState.Failed
                && data.State != StoryEventState.Undefined;
        }

        /// <param name="needsFlagsCooldown">should almost always be false! StasisWakeUp is an example where this is true</param>
        public void FailEvent(GameState s, string reason,
                bool needsFlagsCooldown = false, bool isHardFailure = true) {
            FailReason = reason;
            if (isHardFailure) {
                D.Err("StoryEvent failed (hard): {0} -> {1}", FailReason, this);
            } else {
                Flags |= StoryEventDataFlags.SoftFailure;
                D.Warn("StoryEvent failed (soft): {0} -> {1}", FailReason, this);
            }
            if (needsFlagsCooldown) {
                if (EventFlagsActual == StoryEventFlags.None) {
                    EventFlagsActual = Def.Flags;
                }
                Flags |= StoryEventDataFlags.NeedsCooldown;
            }
            SetState(s, StoryEventState.Failed);
        }

        public void LoadState(StoryEventState state) {
            State = state;
        }

        public void SetState(GameState s, StoryEventState newState) {
            if (State == newState) { return; }
            if (newState == StoryEventState.Ended && State != StoryEventState.Ending && State != StoryEventState.Failed) {
                D.Err("Disallowing story event state change to Ended without going through Ending/Failed! {0}", this);
                newState = StoryEventState.Ending;
            }
            #if DEBUG_STATE
            D.Warn("Setting state: {0}->{1} | {2}", State, state, this);
            #endif
            State = newState;
            s.Sig.StoryEventStateChanged.Send(this);
            if (newState == StoryEventState.Ended) {
                s.Situation.StoryEvents++;
            }
        }
        public void HideLogEntry() {
            if (LogEntry.IsValid) {
                D.Err("Hiding a valid log entry! {0} -> {1}", this, LogEntry);
            }
            LogEntry.Hide = true;
        }
        public void CreateLogEntry(string icon, string title, string desc) {
            var newEntry = new StoryLogEntry() {
                Icon = icon,
                Title = title,
                Desc = desc,
            };
            if (LogEntry.IsValid) {
                D.Err("Trying to overwrite a valid log entry! {0} -> {1} with {2}",
                    this, LogEntry, newEntry);
            }
            LogEntry = newEntry;
        }
        public void ShowPopup() {
            if (!LogEntry.IsValid) {
                D.Err("Trying to show invalid log entry for story event: {0} | {1}",
                    this, D.DumpInstance(LogEntry));
                return;
            }
            //     WPopup.Spawn(LogEntry.Icon, LogEntry.Title, date {ago}", LogEntry.Desc);
            var date = Texts.TicksToDate(StartTick);
            if (StartTick < G.Ticks) {
                date = $"{date} / {Units.TicksAgoFromNow(StartTick)}";
            }
            var details = $"{Texts.Title(LogEntry.Title)}{LogEntry.Desc}";
            var hash = Hashes.Quick(LogEntry.Title.GetHashCode(), LogEntry.Desc.GetHashCode());
            The.Dialog.ShowSimple(LogEntry.Icon, details, T.Ok, sideText: date);
        }
        public override string ToString() {
            return $"StoryEventData[{Id}|{Def?.Id},State:{State},Start:{StartTick},End:{EndTick}]";
        }

        public void AddParticipant(Mind mind) {
            Participants ??= new();
            if (!Participants.AddUniqueInefficient(mind)) {
                D.Err("Trying to add participant to the story event more than once: {0} -> {1} -> {2}",
                    mind, mind.Owner, this);
            }
        }
        public void AddParticipant(BeingData data) {
            AddParticipant(data.Mind);
        }
        // A participant who left the ship (sent away, sold, abandoned ship) keeps the Mind but
        // has no Being any more, and after a load no Owner either
        public bool HasParticipantsAboard(int count) {
            if (Arrays.Count(Participants) < count) { return false; }
            for (int i = 0; i < count; i++) {
                if (Participants[i].Owner?.Being == null) { return false; }
            }
            return true;
        }

        public void SetTarget(Entity target) {
            if (target == null) { return; }
            if (target is Tile tile) {
                TargetTile = tile;
            } else if (target is Obj obj) {
                TargetObj = obj.Data;
            } else if (target is Being b) {
                TargetBeing = b.Data;
            } else {
                D.Err("Target type unhandled: {0}", target);
            }
        }

        #if !PRODUCTION_BUILD
        public string DebugInfo {
            get {
                var sb = new System.Text.StringBuilder();
                sb.AppendFormat("Id: {0} | ", Id);
                sb.Append(Def?.Id);
                sb.Append(" | ");
                sb.AppendFormat("State: {0}; ", State);
                sb.AppendFormat("Flags: {0} (Actual: {1}); ", Flags, EventFlagsActual);
                sb.AppendFormat("Point Cost: {0} (Actual: {1}); ",
                    Def?.Points, PointsCostActual);
                sb.AppendFormat("StartTime: {0}; ", Units.TicksAgoFromNow(StartTick));
                if (State == StoryEventState.Failed) {
                    sb.AppendFormat("Duration: {0}; ", Units.TicksToTime(G.Ticks - EndTick));
                    sb.AppendFormat("Fail Reason: {0}; ", FailReason);
                } else if (State == StoryEventState.Ended) {
                    sb.AppendFormat("Duration: {0}; ", Units.TicksToTime(G.Ticks - EndTick));
                }
                sb.AppendFormat("Flags: {0}; ", State);
                sb.AppendFormat("TargetTile: {0}; ", TargetTile);
                sb.AppendFormat("TargetObj: {0}; ", TargetObj);
                sb.AppendFormat("TargetBeing: {0}; ", TargetBeing);
                sb.AppendFormat("Seed: {0}; ", Seed);
                return sb.ToString().Trim();
            }
        }
        #endif
    }
}
