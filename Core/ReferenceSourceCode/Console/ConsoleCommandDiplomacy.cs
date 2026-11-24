#if !PRODUCTION_BUILD
using System.Collections.Generic;
using Game.Data;
using Game.Data.Factions;
using Game.Systems.Diplomacy;
using Game.Utils;
using KL.Console;
using KL.Utils;

namespace Game.Console {
    /// <summary>
    /// Dev-only tools for exercising the diplomacy system. Nothing else in the game can
    /// move faction standing on demand, which makes alliances impractical to test.
    /// </summary>
    public class ConsoleCommandDiplomacy : BaseConsoleCommand {
        [SelfInit]
        static void Load() {
            Register(new ConsoleCommandDiplomacy());
        }

        protected override Dictionary<string, string> subCommands => new() {
            { "attitude", "SetAttitude" },
            { "relation", "SetRelation" },
            { "list", "ListRelations" },
        };

        private ConsoleCommandResult SetAttitude(ConsoleCommandArguments args) {
            if (!args.HasArgument(4)) {
                return Error("Usage: diplomacy attitude <fromFaction> <toFaction> <value>");
            }
            var from = FactionDef.Get(args.GetString(2));
            var to = FactionDef.Get(args.GetString(3));
            if (from == null || to == null) {
                return Error("Unknown faction");
            }
            var target = args.GetFloat(4);
            var dip = The.R.State.Sys.Diplomacy;
            var current = dip.AttitudeOf(from, to);
            dip.ChangeAttitude(from, to, target - current);
            return OK("{0} -> {1}: {2}", from.Id, to.Id,
                Units.PercentageSigned(dip.AttitudeOf(from, to)));
        }

        private ConsoleCommandResult SetRelation(ConsoleCommandArguments args) {
            if (!args.HasArgument(4)) {
                return Error("Usage: diplomacy relation <a> <b> <neutral|allied|enemy>");
            }
            var a = FactionDef.Get(args.GetString(2));
            var b = FactionDef.Get(args.GetString(3));
            if (a == null || b == null) {
                return Error("Unknown faction");
            }
            var relStr = args.GetString(4);
            FactionRelation rel;
            switch (relStr) {
                case "neutral": rel = FactionRelation.Neutral; break;
                case "allied": rel = FactionRelation.Allied; break;
                case "enemy": rel = FactionRelation.Enemy; break;
                default: return Error("Unknown relation: {0}", relStr);
            }
            The.R.State.Sys.Diplomacy.SetRelationAndReport(a, b, rel);
            return OK("{0} <-> {1}: {2}", a.Id, b.Id, rel);
        }

        private ConsoleCommandResult ListRelations(ConsoleCommandArguments args) {
            var s = The.R.State;
            var dip = s.Sys.Diplomacy;
            var relations = dip.Relations;
            var sim = dip.Sim;
            var era = sim.EraIndex(s.Clock.Ticks);
            var sb = Caches.StringBuilder;
            sb.Clear();
            sb.AppendFormat("Era {0}, universe gen {1}\n", era, dip.UniverseGen);
            foreach (var (a, b) in relations.DiplomaticPairs) {
                sb.AppendFormat("- {0} <-> {1}: {2} | mutual {3}",
                    a.Id, b.Id, relations.Get(a, b),
                    Units.PercentageSigned(dip.MutualAttitude(a, b)));
                if (!a.IsPlayer && !b.IsPlayer) {
                    sb.AppendFormat(" | targets {0} / {1}",
                        Units.PercentageSigned(sim.EraTarget(a, b, era)),
                        Units.PercentageSigned(sim.EraTarget(b, a, era)));
                }
                if (relations.IsLockedEnemy(a, b)) {
                    sb.Append(" | LOCKED");
                }
                if (!relations.CanAlly(a, b)) {
                    sb.Append(" | NO-ALLY");
                }
                sb.Append('\n');
            }
            return OK(sb.ToString());
        }

        public override void Initialize() {
            Name = "diplomacy";
            HelpLine = "Inspect and modify faction attitudes and relations";
        }
    }
}
#endif
