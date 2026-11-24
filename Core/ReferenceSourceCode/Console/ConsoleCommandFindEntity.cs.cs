using KL.Console;
using KL.Utils;
using Game.Constants;
using Game.Commands;
using KL.Grid;
using UnityEngine;
using Game.Data;
using Game.Components;

using Game.Utils;
namespace Game.Console {
    public class ConsoleCommandFindEntity : ConsoleCommand {
        [SelfInit]

        static void Load() {
            Register(new ConsoleCommandFindEntity());
        }

        public override ConsoleCommandResult Execute(ConsoleCommandArguments args) {
            if (!args.HasArgument(1)) {
                return Error("Please provide entity id");
            }
            var id = args.GetInt(1);
            var S = The.R.State;
            var being = The.R.State.Beings.Find(id);
            if (being != null) {
                The.R.State.Sig.SelectEntity.Send(being);
                return Close();
            }
            var obj = The.R.State.Objs.Find(id);
            if (obj != null) {
                The.R.State.Sig.SelectEntity.Send(obj);
                return Close();
            }
            var tile = The.R.State.FindEntity<Tile>(id);
            if (tile != null) {
                The.R.State.Sig.SelectEntity.Send(tile);
                return Close();
            }
            return Error("Entity not found by id: {0}", id);
        }

        public override void Initialize() {
            Name = "find_entity";
            HelpLine = "Finds entity by id";
        }
    }
}
