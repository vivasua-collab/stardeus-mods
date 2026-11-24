using System;
using System.Threading;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Story;
using Game.Story.Events;
using Game.Systems.Electricity;
using Game.Systems.UIUX;
using Game.UI;
using Game.Utils;

namespace Game {
    /// <summary>
    /// Commonly used shortcuts to save some typing here and there.
    /// </summary>
    public static class A {
        public static void Cleanup() {
            S = null;
            HasRuntime = false;
        }
        public static Thread MainThread;
        public static GameState S;
        public static ElectricitySys Grid => S.Sys.Electricity;
        public static bool IsMainThread => Thread.CurrentThread == MainThread;
        public static bool IsElysiumOn => S.BB.Get(BBKeyH.ElysiumMode, false);
        public static bool IsInHyperspace => S.Universe.HyperspaceLocation != null;
        public static SpaceObject HyperspaceLocation => S.Universe.HyperspaceLocation;
        public static SpaceObject Player => S.Universe.Player;

        public static int AutoRepairPriority => S.Prefs.GetInt(
            Pref.AutoRepairPriority, PrefH.AutoRepairPriority,
                Consts.DefaultAutoRepairPriority);
        public static int AutoRebuildPriority => S.Prefs.GetInt(
            Pref.AutoRebuildPriority, PrefH.AutoRebuildPriority,
                Consts.DefaultAutoRebuildPriority);
        public static int BodyPartProcedurePriority => S.Prefs.GetInt(
            Pref.BodyPartProcedurePriority, PrefH.BodyPartProcedurePriority,
                Consts.DefaultBodyPartProcPriority);
        public static SpaceObject PlayerParentInStarmap {
            get {
                if (UIShowing.Starmap) {
                    var ctx = StarmapCtx;
                    if (ctx.ParentSO != null) {
                        return ctx.ParentSO;
                    } else {
                        if (ctx.Layer == UniverseLayer.Universe) {
                            return S.Universe.Root;
                        } else if (ctx.Layer == UniverseLayer.Region) {
                            return PlayerSector.Parent;
                        } else {
                            return PlayerSector;
                        }
                    }
                } else {
                    return PlayerSector;
                }
            }
        }
        public static SpaceObject PlayerSector {
            get {
                if (IsInHyperspace) {
                    return HyperspaceLocation;
                } else {
                    return Player.Parent;
                }
            }
        }
        public static bool IsSaveTemplate => S.Vars.IsTemplateMode;
        public static bool IsCosmeticParticlesOn =>
            The.Prefs.GetBool(Pref.GCosmeticParticles,
                PrefH.GCosmeticParticles, true);
        public static bool IsCombatOn => S.Prefs.GetBool(Pref.Combat,
            PrefH.Combat, true);
        public static bool FocusOnEvents => The.Prefs.GetBool(Pref.GFocusOnEvents,
            PrefH.GFocusOnEvents, true);
        public static bool ShowHints => The.Prefs.GetBool(Pref.GShowHints,
            PrefH.GShowHints, true);
        public static bool GradualUIReveal => The.Prefs.GetBool(Pref.GGradualUIReveal,
            PrefH.GGradualUIReveal, true);

        public static bool FocusOnEventsAndIdle {
            get {
                if (!FocusOnEvents) { return false; }
                if (!The.Toolbox.IsFreeSelect) { return false; }
                var t = GameTimer.realtimeSinceStartupAsDouble;
                var lastMotion = S.Vars.LastInputTime;
                return t - lastMotion > 3f;
            }
        }
        public static bool HasCommunicator {
            get {
                var lastPing = S.BB.Get(BBKeyH.CommunicatorPing, (long) -Consts.TicksPerDay);
                if (lastPing == 0L) { return false; }
                return G.Ticks - lastPing <= Consts.TicksPerHour;
            }
        }
        public static bool HasAsimovOverride => S.BB.Get(BBKeyH.HasAsimovOverride, false);
        /// <summary>
        /// Set for good by the Main/EliminateSentinelHQ quest. Unlike HasAsimovOverride this
        /// is a quest bool, so it is part of QuestSys' own save data and needs no
        /// re-application on load. Gates every path that could introduce new Sentinel
        /// presence - see Docs/Architecture/Systems/RogueAIRoute.md.
        /// </summary>
        public static bool SentinelEliminated => Ready.Game
            && S.Query.GetQuestBool.Ask(Systems.RogueAIUtils.BoolSentinelEliminated);
        #if STARDEUS_DEMO
        public const bool InstantBuild = false;
        #else
        public static bool InstantBuild => S.Prefs.GetBool(
            Pref.DevInstantBuild, PrefH.DevInstantBuild, false);
        #endif
        #if PRODUCTION_BUILD
        public static bool GameCanBeAutoSaved => Ready.Gen && G.ClockSpeed < ClockSpeed.Unlimited;
        #else
        public static bool GameCanBeAutoSaved => Ready.Gen && G.ClockSpeed < ClockSpeed.Unlimited && !The.TestsAreRunning;
        #endif

        public static bool AddDevTools => InstantBuild || S.Scenario.IsSandbox;
        public static StarmapContext StarmapCtx => S.UIQuery.GetStarmapContext.Ask();
        public static int ShipSize => S.Sys.Areas.ShipSize;

        public static bool HasRuntime;
    }
}
