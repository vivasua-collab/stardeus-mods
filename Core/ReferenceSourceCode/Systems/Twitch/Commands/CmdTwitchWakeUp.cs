using Game.Data;
using Game.Systems;
using Game.Commands;
using UnityEngine;
using Game.CodeGen;
using Game.Constants;
using Game.Components;
using Game.Systems.Creatures;
using KL.Grid;
using KL.Randomness;
using System.Collections.Generic;
using Game.Systems.Naming;
using Game.Input;
using Game.Data.Factions;

using Game.Utils;
namespace Game.Systems.Twitch.Commands {
    public class CmdTwitchWakeUp : TwitchCommand {
        private const string cmd = "wakeup";

        [SelfInit]
        protected static void Register() {
            TwitchSys.RegisterCommand(cmd, 3000, (ctx) => new CmdTwitchWakeUp(ctx),
                "Wakes the viewer up from stasis. Takes species name as an optional parameter.");
        }

        public CmdTwitchWakeUp(TwitchCtx ctx) : base(cmd, ctx) {}

        protected override void Execute() {
            TwitchViewer viewer = ctx.Viewer;
            if (viewer == null) {
                viewer = ctx.Sys.FindOrCreateViewer(ctx.Msg);
            }
            if (!CheckRole(TwitchRoleIdH.Play, viewer)) {
                return;
            }
            var existingBeing = Being;
            if (existingBeing != null) {
                ctx.Reply("@{0} already in game: {1} named {2}",
                    viewer.Name, existingBeing.Species.Name,
                    existingBeing.Mind.Name.FullNameOrNameless);
                return;
            }
            if (!ctx.CheckRep()) {
                return;
            }

            var args = Args;
            var species = Species.FindByName(args);
            if (species == null && !string.IsNullOrWhiteSpace(args)) {
                ctx.Reply("no species found by name: {0}", args);
                return;
            }

            if (species != null && !species.IsBiological) {
                ctx.Reply("cannot wake up {0} from stasis - not a biological organism", species.Name);
                return;
            }

            // The viewer takes over someone who is actually asleep aboard, instead of a new
            // person appearing out of a chamber that belongs to somebody else
            var manifest = ManifestSys.For(S);
            StasisArrayComp array = null;
            BeingData data = null;
            var arrays = S.Components.AllByType<StasisArrayComp>();
            var candidates = new List<BeingData>();
            if (arrays != null) {
                foreach (StasisArrayComp arr in arrays) {
                    if (arr.BeingCount < 1) { continue; }
                    manifest.EnsurePopulated(arr);
                    manifest.FindSleepers(arr, candidates);
                    for (int i = candidates.Count - 1; i >= 0; i--) {
                        var c = candidates[i];
                        // No species asked for means a person, not somebody's dog
                        if (!StasisUtils.IsUnrevealed(c)
                                || (species == null ? c.Species.IsPet : c.Species != species)) {
                            candidates.RemoveAt(i);
                        }
                    }
                    if (candidates.Count > 0) {
                        array = arr;
                        data = Rng.UFrom(candidates);
                        break;
                    }
                }
            }
            if (data == null) {
                if (species == null) {
                    ctx.Reply("Stasis Array is empty or unavailable");
                } else {
                    ctx.Reply("nobody of species {0} is asleep in stasis", species.Name);
                }
                return;
            }

            ctx.TryConsumeRep();
            data.Mind.Name.ParseName(viewer.Name);
            var being = StasisUtils.Wake(S, data, array, true, G.Ticks);
            if (being == null) {
                ctx.Reply("failed to wake up, something is wrong with Stasis Array at {0}",
                    Pos.String(array.Tile.PosIdx));
                return;
            }
            ctx.Sys.AssignBeing(viewer, being.Data, true);
        }
    }
}
