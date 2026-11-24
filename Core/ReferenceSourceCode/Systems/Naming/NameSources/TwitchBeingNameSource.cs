using System.Collections.Generic;
using System.IO;
using System.Text;
using Game.CodeGen;
using Game.Data;
using Game.Modifiers;
using Game.Systems.Creatures;
using Game.Systems.Mental;
using Game.Systems.Twitch;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Naming {
    public sealed class TwitchBeingNameSource : NameSource {
        public const string SourceId = "TwitchBeing";
        public const BeingType supportedBeingTypes =
            BeingType.StasisWakeup |
            BeingType.Friendly |
            BeingType.Clone |
            BeingType.Newborn;

        [SelfInit]
        private static void Register() {
            Register(SourceId, new TwitchBeingNameSource());
        }

        public override int PriorityFor(NameType type) {
            // super high priority
            return 100000;
        }

        public override bool Supports(NameRequest req) {
            return req.Type == NameType.Being
                && (req.BeingType == BeingType.StasisWakeup || !req.Species.IsBiological || req.Species.IsHumanoid)
                    && (req.BeingType & supportedBeingTypes) != BeingType.None;
        }

        public override float ChanceFor(NameRequest request) {
            return 1f;
        }

        private void ConsumeName(NameResult res) {
            if (res.InternalResult is TwitchViewer viewer) {
                if (twitch.Queue.TryRemove(viewer)) {
                    if (res.NameOwner is BeingData bd) {
                        twitch.AssignBeing(viewer, bd, false);
                    }
                }
            }
        }

        private TwitchSys twitch;

        public override void Setup(GameState S) {
            twitch = S.Sys.Twitch;
        }

        public override bool TryNameFor(NameRequest req, out NameResult res) {
            if (twitch == null || twitch.IsDeactivated) {
                IsDisabled = true;
                res = null;
                return false;
            }

            if (twitch.Queue == null || Arrays.IsEmpty(twitch.Queue.Queue)) {
                res = null;
                return false;
            }

            var viewers = TwitchSys.GlobalData.Viewers;
            if (Arrays.IsEmpty(viewers)) {
                res = null;
                return false;
            }

            TwitchViewer chosen = twitch.Queue.Next(viewers, twitch.Settings);

            if (chosen == null) {
                D.Warn("Could not select next viewer with queue: {0}",
                    twitch.Queue.Size);
                res = null;
                return false;
            }

            res = new NameResult(ConsumeName);
            var bn = new BeingName();
            bn.ParseName(chosen.Name);
            D.Warn("Got twitch name: {0} -> {1}", chosen.Name, bn);
            res.SetBeingName(bn, chosen);
            return true;
        }

    }
}
