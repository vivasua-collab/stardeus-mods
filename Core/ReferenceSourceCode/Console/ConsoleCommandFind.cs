using KL.Console;
using KL.Utils;
using Game.Constants;
using Game.Commands;
using KL.Grid;
using UnityEngine;
using Game.Data;
using Game.Components;
using System.Linq;
using System;

using Game.Utils;
namespace Game.Console {
    public class ConsoleCommandFind : ConsoleCommand {
        [SelfInit]

        static void Load() {
            Register(new ConsoleCommandFind());
        }

        public override ConsoleCommandResult Execute(ConsoleCommandArguments args) {
            if (!args.HasArgument(1)) {
                return Error("Please provide name");
            }
            var name = args.GetString(1);
            var S = The.R.State;
            var being = The.R.State.Beings.All.FirstOrDefault(
                    b => b.Data.Mind.Name.FullName.Contains(name,
                        StringComparison.InvariantCultureIgnoreCase));
            if (being != null) {
                The.R.State.Sig.SelectEntity.Send(being);
                return Close();
            }
            var obj = The.R.State.Objs.All.FirstOrDefault(
                o => o.Definition.NameT.Contains(name,
                    StringComparison.InvariantCultureIgnoreCase));
            if (obj != null) {
                The.R.State.Sig.SelectEntity.Send(obj);
                return Close();
            }
            var e = S.Map.AllEntities.Values.FirstOrDefault(
                e => e.Definition.NameT.Contains(name,
                    StringComparison.InvariantCultureIgnoreCase));
            if (e != null) {
                The.R.State.Sig.SelectEntity.Send(e);
                return Close();
            }
            return Error("Entity not found by name: {0}", name);
        }

        public override void Initialize() {
            Name = "find";
            HelpLine = "Finds entity by name";
        }
    }
}
