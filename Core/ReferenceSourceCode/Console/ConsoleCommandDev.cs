#if !STARDEUS_DEMO
using KL.Console;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;
using System.Globalization;
using Game.UI;
using Game.Visuals;
using Game.Constants;
using Game.Rendering;

using Game.Utils;
using Game.Systems;
using Game.Systems.AI.Combat;
using Game.Systems.Drama;
using Game.Systems.Quests;
using Game.Systems.Ships;
using Game.Systems.Story;
using Game.Systems.UIUX;
using Game.Systems.UIUX.Codex.Pages;
using Game.Systems.Creatures;
namespace Game.Console {
    public class ConsoleCommandDev : BaseConsoleCommand {
        [SelfInit]
        private static void Load() {
            Register(new ConsoleCommandDev());
        }

        protected override Dictionary<string, string> subCommands => new() {
            { "menu", "Menu" },
            { "stop_demo", "StopDemo" },
            { "start_demo", "StartDemo" },
            { "debug_tasks", "DebugTasks" },
            { "recording_mode", "RecordingMode" },
            #if !PRODUCTION_BUILD
            { "quick_test", "QuickTest" },
            { "viz_regions", "VizRegions" },
            { "realtime_postprocessing", "RealtimePostProcessing" },
            { "reload_looks", "ReloadLooks" },
            { "sky", "Sky" },
            { "sky_storm", "SkyStorm" },
            #endif
        };

        protected override HashSet<string> commandsThatDontBlockAchievements => new() {
            "menu", "stop_demo", "viz_regions", "debug_tasks", "realtime_postprocessing", "reload_looks",
            "sky", "sky_storm"
        };
        /// <summary>
        /// Put whatever you want to quick test here
        /// </summary>
        private ConsoleCommandResult QuickTest(ConsoleCommandArguments args) {
            // A.S.Sys.NPCShip.ForceShipClassChoice();
            A.S.UIQuery.ShowTutorialPopup.Ask(ElectricityConnectPage.PageId, 3f);
            // A.S.UIQuery.ShowTutorialPopup.Ask("ShipCombat/ClassPoints", 3f);
            return Close();
        }
        private ConsoleCommandResult DebugTasks(ConsoleCommandArguments args) {
            GoalDebugTab.Enabled = !GoalDebugTab.Enabled;
            A.S.UIQuery.ReloadCurrentEntity.Ask();
            return OK("Goal debug tab: {0}", GoalDebugTab.Enabled);
        }
        private ConsoleCommandResult RecordingMode(ConsoleCommandArguments args) {
            var s = A.S;
            // Recordings of turrets, traps and ship combat need combat running
            var keepCombat = HasOption(args, "keep_combat");
            // Recordings that hover build menu items or search results show their codex cards
            var keepTooltips = HasOption(args, "keep_tooltips");

            s.Sys.Get<StorySys>().IsDeactivated = true;
            s.Sig.SetStoryGen.Send(StoryGenDef.Get("none"));

            s.Sys.Get<DramaSys>().IsDeactivated = true;
            s.Prefs.SetBool(Pref.DisableDrama, true);

            if (!keepCombat) {
                s.Sys.Get<CombatSys>(CombatSys.SysId).IsDeactivated = true;
                s.Prefs.SetBool(Pref.Combat, false);
                s.Query.StartRaid.RemoveResponder();
                s.Query.StartCombatBetween.RemoveResponder();
                s.Query.StartFriendlyFightBetween.RemoveResponder();

                s.Sys.Get<ShipCombatSys>().IsDeactivated = true;
                s.Query.StartShipCombatWith.RemoveResponder();
                s.Query.StartNPCShipCombatBetween.RemoveResponder();
            }
            if (!keepTooltips) {
                s.UIQuery.ShowCodexTooltipFor.RemoveResponder();
            }

            var suggestions = s.Sys.Get<SuggestionSys>(SuggestionSys.SysId);
            suggestions.IsDeactivated = true;
            suggestions.IsDisabled = true;
            s.Sig.AddSuggestion.RemoveAllListeners();
            s.Query.CanAddSuggestionNow.RemoveResponder();
            s.Query.TryAddSuggestion.RemoveResponder();

            s.Sys.Get<QuestSys>(QuestSys.SysId).IsDeactivated = true;
            s.Query.AddQuest.RemoveResponder();
            s.Query.AcceptQuest.RemoveResponder();
            s.Query.RegisterAvailableQuest.RemoveResponder();
            s.Query.GetQuestFor.RemoveResponder();
            s.Query.ForceQuestFor.RemoveResponder();
            s.Query.WantsToOfferQuestToPlayer.RemoveResponder();
            s.UIQuery.ShowQuestHubFor.RemoveResponder();

            s.Sig.ShowFanfare.RemoveAllListeners();

            var registry = s.UIQuery.GetWidgetRegistry.Ask();
            registry?.Quest?.SetForceHidden(true);
            registry?.Quest?.Hide();

            // the setting is saved with the game, so a setup save made in recording mode
            // carries it switched off
            s.Prefs.SetBool(Pref.DisableCodexPopups, !keepTooltips);
            s.UIQuery.HideCodexTooltip.Ask();

            var kept = keepCombat && keepTooltips ? ", combat and codex cards kept"
                : keepCombat ? ", combat kept"
                : keepTooltips ? ", codex cards kept"
                : string.Empty;
            return OK($"Recording mode enabled{kept}");
        }

        private static bool HasOption(ConsoleCommandArguments args, string option) {
            for (int i = 2; args.HasArgument(i); i++) {
                if (args.GetString(i) == option) { return true; }
            }
            return false;
        }
        private ConsoleCommandResult Menu(ConsoleCommandArguments args) {
            DevMenu.ShowMenu();
            return Close();
        }
        private ConsoleCommandResult StartDemo(ConsoleCommandArguments args) {
            SandboxBlock_Camera.StartDemoFlyThrough();
            return Close();
        }
        private ConsoleCommandResult StopDemo(ConsoleCommandArguments args) {
            G.IsDemoMode = false;
            A.S.UIQuery.CloseMainMenu.Ask(false);
            return Close();
        }
        #if !PRODUCTION_BUILD
        private ConsoleCommandResult VizRegions(ConsoleCommandArguments args) {
            A.S.Sig.ToggleOverlayById.Send(OverlayId.Reg);
            return Close();
        }

        // Forces the active camera's URP volume stack to re-evaluate every frame instead of
        // once (see Docs/Architecture/Systems/PostProcessing.md), so tweaking a VolumeProfile's
        // values in the inspector shows up live. Meant for editor iteration - turn back off
        // when done, since it costs a per-frame volume blend.
        private ConsoleCommandResult RealtimePostProcessing(ConsoleCommandArguments args) {
            var cam = RenderUtils.Camera;
            if (cam == null) {
                return Error("No active camera - load into a game or the main menu first");
            }
            bool on;
            if (args.HasArgument(2)) {
                on = args.GetBool(2, out var isValidBool);
                if (!isValidBool) {
                    return Error("{0} is not a valid bool", args.GetString(2));
                }
            } else {
                on = cam.GetVolumeFrameworkUpdateMode() != VolumeFrameworkUpdateMode.EveryFrame;
            }
            cam.SetVolumeFrameworkUpdateMode(
                on ? VolumeFrameworkUpdateMode.EveryFrame : VolumeFrameworkUpdateMode.UsePipelineSettings);
            return OK("Realtime post processing: {0}", on);
        }

        // Dev iteration helper for tuning Config/Species/Looks JSON (surgery card/line layout)
        // without restarting - see SpeciesLooksDef.ReloadBodyPartSlotLayouts.
        private ConsoleCommandResult ReloadLooks(ConsoleCommandArguments args) {
            SpeciesLooksDef.ReloadBodyPartSlotLayouts();
            return OK("Body part slot layouts reloaded. Reopen the surgery view or switch tabs to see them");
        }

        // Tuning helper for Config/Visuals/SpaceSky. No argument: what the current sector
        // resolves to. A number previews that difficulty, a def id previews that def, and
        // "reload" re-reads the sky JSON and re-applies the current sector.
        private ConsoleCommandResult Sky(ConsoleCommandArguments args) {
            var s = A.S;
            if (s == null) {
                return Error("Load a game first");
            }
            var location = s.Universe.Player.Parent;
            if (!args.HasArgument(2)) {
                var biome = location?.Sector?.Biome ?? location?.Parent?.Sector?.Biome;
                return OK("{0} -> {1}", biome, SpaceSky.Target);
            }
            var arg = args.GetString(2);
            if (arg == "reload") {
                SpaceSkyDef.Unload();
                SpaceSkyStormDef.Unload();
                SpaceSky.Apply(location, true);
                return OK("Reloaded: {0}", SpaceSky.Target);
            }
            if (float.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture,
                    out var difficulty)) {
                SpaceSky.SetPalette(SpaceSkyDef.Resolve(difficulty, null), true);
                return OK("Difficulty {0}: {1}", difficulty, SpaceSky.Target);
            }
            if (SpaceSkyDef.TryGet(arg, out var def)) {
                SpaceSky.SetPalette(def.Palette, true);
                return OK("{0}: {1}", def, SpaceSky.Target);
            }
            return Error("No sky def or difficulty: {0}", arg);
        }

        // Storm sky preview: a storm id (Config/Visuals/SpaceSkyStorms) or "off" for none
        private ConsoleCommandResult SkyStorm(ConsoleCommandArguments args) {
            if (!args.HasArgument(2)) {
                return OK("Active sky storms: {0}", string.Join(", ", SpaceSky.ActiveStorms));
            }
            var arg = args.GetString(2);
            if (arg == "off") {
                var active = new List<string>(SpaceSky.ActiveStorms);
                foreach (var id in active) {
                    SpaceSky.SetStorm(id, false);
                }
                return OK("Sky storms off");
            }
            if (!SpaceSkyStormDef.TryGet(arg, out var def)) {
                return Error("No sky storm def: {0}", arg);
            }
            SpaceSky.SetStorm(def.Id, true);
            return OK("Sky storm: {0}", def);
        }
        #endif

        public override void Initialize() {
            Name = "dev";
            HelpLine = "Development command";
        }
    }
}
#endif
