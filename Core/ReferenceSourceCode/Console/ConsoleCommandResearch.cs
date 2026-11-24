using System.Collections.Generic;
using Game.Research;
using Game.Utils;
using KL.Console;

namespace Game.Console {
    public class ConsoleCommandResearch : BaseConsoleCommand {
        [SelfInit]
        private static void Load() {
            Register(new ConsoleCommandResearch());
        }

        protected override Dictionary<string, string> subCommands => new() {
            { "unlock", "Unlock" },
        };

        public override string Autocomplete(ConsoleCommandArguments args) {
            var cmd = args.GetString(1);
            if (cmd != "unlock") {
                return base.Autocomplete(args);
            }
            var value = args.GetString(2);
            if (value == null) {
                return "unlock ";
            }
            value = value.ToLowerInvariant();
            if ("all".StartsWith(value)) {
                return "unlock all";
            }
            if ("bookmarked".StartsWith(value)) {
                return "unlock bookmarked";
            }
            foreach (var def in ResearchTree.All) {
                if (def.Id.ToLowerInvariant().StartsWith(value)) {
                    return $"unlock {def.Id}";
                }
            }
            foreach (var def in ResearchTree.All) {
                if (def.Id.ToLowerInvariant().Contains(value)) {
                    return $"unlock {def.Id}";
                }
            }
            return base.Autocomplete(args);
        }

        private ConsoleCommandResult Unlock(ConsoleCommandArguments args) {
            if (!args.HasArgument(2)) {
                return Error("Usage: research unlock <all|bookmarked|research_id>");
            }
            var target = args.GetString(2);
            if (target == "all") {
                return UnlockAll();
            }
            if (target == "bookmarked") {
                return UnlockBookmarked();
            }
            foreach (var def in ResearchTree.All) {
                if (def.Id != target) { continue; }
                if (!The.R.State.Research.TryUnlockBonus(def.IdH)) {
                    return Error("Research is already unlocked: {0}", target);
                }
                return OK("Unlocked research: {0}", target);
            }
            return Error("Unknown research id: {0}", target);
        }

        private ConsoleCommandResult UnlockAll() {
            var research = The.R.State.Research;
            var ids = new List<int>();
            foreach (var def in ResearchTree.All) {
                if (!research.IsUnlocked(def.IdH)) {
                    ids.Add(def.IdH);
                }
            }
            for (int i = 0; i < ids.Count; i++) {
                research.TryUnlockBonus(ids[i]);
            }
            return OK("Unlocked {0} research nodes", ids.Count);
        }

        private ConsoleCommandResult UnlockBookmarked() {
            var research = The.R.State.Research;
            var bookmarks = The.R.State.Sys.Research.Bookmarks;
            var ids = new List<int>(bookmarks.Count);
            for (int i = 0; i < bookmarks.Count; i++) {
                if (!bookmarks[i].IsUnlocked) {
                    ids.Add(bookmarks[i].IdH);
                }
            }
            for (int i = 0; i < ids.Count; i++) {
                research.TryUnlockBonus(ids[i]);
            }
            return OK("Unlocked {0} bookmarked research nodes", ids.Count);
        }

        public override void Initialize() {
            Name = "research";
            HelpLine = "Unlocks research";
        }
    }
}
