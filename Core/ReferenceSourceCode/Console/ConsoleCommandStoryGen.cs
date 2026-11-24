using KL.Console;
using KL.Utils;
using Game.Utils;
using System.Linq;
using Game.Systems.Story;

namespace Game.Console {
    public class ConsoleCommandStoryGen : ConsoleCommand {
        [SelfInit]

        private static void Load() {
            Register(new ConsoleCommandStoryGen());
        }
        public override string Autocomplete(ConsoleCommandArguments args) {
            var arg1 = args.GetString(1);
            return Strings.Autocomplete(arg1, StoryGenDef.Ordered.Select(d => d.Id));
        }

        public override ConsoleCommandResult Execute(ConsoleCommandArguments args) {
            var S = The.R.State;
            if (!args.HasArgument(1)) {
                var o = args.Context.Output;
                o.WriteLine("Available Story Generators:");
                var currentGen = The.R.Query.GetStoryGen.Ask();
                foreach (var s in StoryGenDef.Ordered) {
                    if (s == currentGen) {
                        o.WriteLine($"{s.Id} [CURRENT]");
                    } else {
                        o.WriteLine(s.Id);
                    }
                }
                return OK();
            }
            var genKey = args.GetString(1);
            var gen = StoryGenDef.Get(genKey);
            if (gen == null) {
                return Error("Story generator not available: {0}", genKey);
            }

            The.R.Sig.SetStoryGen.Send(gen);

            return OK($"Setting story generator: {gen.NameT}");
        }

        public override void Initialize() {
            Name = "storygen";
            HelpLine = "Changes the story generator";
        }
    }
}
