using KL.Console;
using KL.Utils;
using Game.Constants;
using Game.Commands;
using KL.Grid;
using UnityEngine;
using Game.Data;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using KL.Randomness;
using KL.Collections;
using Game.Procgen.Space;
using Game.Data.Space;
using Game.Input;
using System.Text;
using Game.CodeGen;
using Game.Systems.Space;
using Game.Systems;
using Game.Utils;

namespace Game.Console {
    public class ConsoleCommandSpace : ConsoleCommand {
        [SelfInit]

        static void Load() {
            Register(new ConsoleCommandSpace());
        }
        private readonly Dictionary<string, string> subCommands = new() {
            { "teleport_to", "TeleportTo" },
            { "regenerate_universe", "RegenerateUniverse" },
            { "reset_telescope", "ResetTelescope" },
            { "reset_terraforming", "ResetTerraforming" },
            { "telescope_find", "TelescopeFind" },
            { "scan_region", "ScanRegion" },
            { "scan_universe", "ScanUniverse" },
            { "add_effect", "AddEffect" },
            { "remove_effects", "RemoveEffects" },
            { "add_beacon", "AddBeacon" },
            { "list_effects", "ListEffects" },
            { "find", "Find" }
        };
        private ConsoleCommandResult Find(ConsoleCommandArguments args) {
            var query = args.GetString(2, true);
            if (string.IsNullOrWhiteSpace(query)) {
                return OK("Usage: space find <query>");
            }
            var search = Strings.ToSearchString(query);
            var sb = Caches.StringBuilder;
            sb.Clear();
            int found = 0;
            foreach (var so in The.R.State.Universe.ObjectsById.Values) {
                if (Strings.ToSearchString(so.TypeName).StartsWith(search)) {
                    found = AddFindResult(sb, found, so);
                } else if (Strings.ToSearchString(so.DisplayName).StartsWith(search)) {
                    found = AddFindResult(sb, found, so);
                } else if (so.Sector?.Biome.Template != null
                        && Strings.ToSearchString(so.Sector.Biome.Template.Id).StartsWith(search)) {
                    found = AddFindResult(sb, found, so);
                }
            }
            if (found == 0) {
                foreach (var so in The.R.State.Universe.ObjectsById.Values) {
                    if (Strings.ToSearchString(so.TypeName).Contains(search)) {
                        found = AddFindResult(sb, found, so);
                    } else if (Strings.ToSearchString(so.DisplayName).Contains(search)) {
                        found = AddFindResult(sb, found, so);
                    } else if (so.Sector?.Biome.Template != null
                            && Strings.ToSearchString(so.Sector.Biome.Template.Id).Contains(search)) {
                        found = AddFindResult(sb, found, so);
                    }
                }
            }
            sb.Insert(0, $"Found: {found}\n");
            return OK(sb.ToString());
        }

        private static int AddFindResult(StringBuilder sb, int found, SpaceObject so) {
            sb.AppendFormat(" {0} ", GlyphMap.ArrowRight);
            if (so.Ship != null) {
                sb.AppendFormat("{0} | {1} | {2}\n", so.Ship.NameFull,
                    so.Ship.Sector.SO.ParentPath(false), so.Ship);

            } else {
                sb.AppendFormat("{0} | {1}", so.ParentPath(false), so);
            }
            found++;
            return found;
        }

        private ConsoleCommandResult ListEffects(ConsoleCommandArguments args) {
            var res = "All space effects:";
            foreach (var cfg in SpaceEffectConfig.All) {
                var text = cfg.Key;
                res += $"\n - {text}";
            }
            return OK(res);
        }

        private ConsoleCommandResult AddEffect(ConsoleCommandArguments args) {
            var sector = The.R.State.Universe.Player.Parent.Sector;
            if (sector == null) {
                return Error("This command works only when your ship is inside a sector");
            }
            var id = args.GetString(2);
            if (id == null) {
                return Error("Please provide effect ID. Use 'list_effects' to see what the ids are available.");
            }
            id = id.ToLowerInvariant();
            SpaceEffectConfig found = null;
            foreach (var cfg in SpaceEffectConfig.All) {
                if (cfg.Key.ToLowerInvariant() == id) {
                    found = cfg.Value;
                    break;
                }
            }

            if (found == null) {
                return Error("Space effect not found by this ID. Use 'list_effects' to see available ids.");
            }

            var spaceSys = The.R.Sys.Get<SpaceSys>(SpaceSys.SysId);
            spaceSys.AddEffect(found.Effect, sector);
            return OK("Added space effect {0}", found.Id);
        }

        private ConsoleCommandResult RemoveEffects(ConsoleCommandArguments args) {
            var spaceSys = The.R.Sys.Get<SpaceSys>(SpaceSys.SysId);
            int removed = spaceSys.RemoveEffects();
            if (removed == 0) {
                return OK("No ongoing space effects to remove");
            }
            return OK("Removed {0} ongoing space effects", removed);
        }

        private ConsoleCommandResult AddBeacon(ConsoleCommandArguments args) {
            SOUtils.DeployReturnBeacon(A.S, DefIdsH.ObjectsDevicesReturnBeacon);
            return OK("Deployed a return beacon");
        }

        private ConsoleCommandResult ScanRegion(ConsoleCommandArguments args) {
            var s = The.R.State;
            var parent = s.Universe.HyperspaceLocation ?? s.Universe.Player.Parent;
            while (parent != null && parent.Region == null) {
                parent = parent.Parent;
            }
            if (parent == null) {
                return Error("Could not find player parent Region");
            }
            var report = new Dictionary<MatType, int>();
            ScanSectors(s, report, parent);
            var sb = Caches.StringBuilder2;
            GenerateReport(report, sb);
            return OK("Done scanning {0}. Found:\n{1}", parent.DisplayName, sb.ToString());
        }

        private ConsoleCommandResult ScanUniverse(ConsoleCommandArguments args) {
            var s = The.R.State;
            var parent = s.Universe.Root;
            var report = new Dictionary<MatType, int>();
            foreach (var region in parent.Children) {
                ScanSectors(s, report, region);
            }
            var sb = Caches.StringBuilder2;
            GenerateReport(report, sb);
            return OK("Done scanning {0}. Found:\n{1}", parent.DisplayName, sb.ToString());
        }

        private void GenerateReport(Dictionary<MatType, int> report, StringBuilder sb) {
            sb.Clear();
            foreach (var m in report.OrderByDescending(r => r.Value)) {
                sb.AppendFormat("{0}: {1}\n", m.Key.NameT, m.Value);
            }
        }
        private SpaceObject FindParent(GameState s, int idH) {
            var parent = s.Universe.Player.Parent;
            while (parent != null && parent.Type.IdH != idH) {
                parent = parent.Parent;
            }
            if (parent == null || parent.Type.IdH != idH) {
                return null;
            }
            return parent;
        }

        private void ScanSectors(GameState s, Dictionary<MatType, int> report,
                SpaceObject region) {
            foreach (var sector in region.Children) {
                s.Sys.Universe.GenerateSector(sector, false, reveal: true);
                if (!sector.Type.IsScannable) { continue; }
                SOActionScan.ScanObject(sector);
                if (sector.Mats == null) { continue; }
                D.Warn("{0}: {1}\n{2}", sector.DisplayName,
                    string.Join(", ", sector.Mats.Select(m => m.NameT)),
                    GlyphMap.BRHR);
                foreach (var mat in sector.Mats) {
                    if (report.TryGetValue(mat, out var cnt)) {
                        cnt++;
                    } else {
                        cnt = 1;
                    }
                    report[mat] = cnt;
                }
            }
        }

        private ConsoleCommandResult TelescopeFind(ConsoleCommandArguments args) {
            var s = The.R.State;
            s.TelescopeProgress = 0.99999f;
            return OK("Telescope progress set to 99.999%");
        }
        private ConsoleCommandResult ResetTelescope(ConsoleCommandArguments args) {
            var s = The.R.State;
            s.TelescopeProgress = 0f;
            s.TelescopeScanCount = 0;
            return OK("Telescope was reset");
        }
        private ConsoleCommandResult ResetTerraforming(ConsoleCommandArguments args) {
            var s = The.R.State;
            bool reset = false;
            foreach (var reg in s.Universe.Root.Children) {
                if (!reg.HasChildren) { continue; }
                foreach (var sec in reg.Children) {
                    if (sec.Flags.HasFlag(SOFlags.Habitable)) {
                        D.Warn("Removing habitable flag from planet: {0}", sec);
                        sec.Flags &= ~SOFlags.Habitable;
                        reset = true;
                    }
                }
            }
            if (reset) {
                return OK("Terraforming reset success");
            } else {
                return OK("Nothing to reset");
            }
        }

        private ConsoleCommandResult RegenerateUniverse(ConsoleCommandArguments args) {
            var s = The.R.State;
            s.Sys.Universe.Regenerate(Rng.URange(int.MinValue, int.MaxValue));
            return OK("Regenerated Universe");
        }

        private ConsoleCommandResult TeleportTo(ConsoleCommandArguments args) {
            var arglist = args.ArgumentsWithoutCommand;
            arglist.RemoveAt(0);
            var target = string.Join(" ", arglist);
            var cmd1 = args.GetString(1);
            The.R.State.IsAchievementsBlocked = true;
            var targetSO = The.R.State.Sys.Ship.Player.Parent.Children.FirstOrDefault(
                o => o.DisplayName.StartsWith(target, System.StringComparison.InvariantCultureIgnoreCase));
            if (targetSO == null) {
                return Error("Target not found: {0}", target);
            }
            The.R.State.Sys.Ship.TeleportTo(
                targetSO.Location + Rng.UInsideUnitCircle() * 0.1f);
            return OK("Teleported to {0}", target);
        }
        public override string Autocomplete(ConsoleCommandArguments args) {
            var key = args.GetString(1);
            return Strings.Autocomplete(key, subCommands.Keys);
        }

        public override ConsoleCommandResult Execute(ConsoleCommandArguments args) {
            if (!A.HasRuntime) {
                return Error("This command only works while playing a game");
            }
            if (!args.HasArgument(1)) {
                return Error("Subcommands: {0}", string.Join(",", subCommands.Keys));
            }
            var subcmd = args.GetString(1);
            var cmdFunc = subCommands.Get(subcmd, null);
            if (cmdFunc == null) {
                return Error("Uknown subcommand: {0}. Subcommands: {1}", subcmd,
                    string.Join(",", subCommands.Keys));
            }
            var meth = GetType().GetMethod(cmdFunc, BindingFlags.Instance | BindingFlags.NonPublic);
            if (meth == null) {
                return Error("Method not found: {0}", cmdFunc);
            }
            return (ConsoleCommandResult) meth.Invoke(this, new object[] { args });
        }

        public override void Initialize() {
            Name = "space";
            HelpLine = "Tools for navigating in space";
        }
    }
}
