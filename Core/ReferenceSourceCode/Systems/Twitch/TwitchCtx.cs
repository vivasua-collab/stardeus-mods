using Game.Data;
using Game.Systems.Creatures;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Twitch {
    public struct TwitchCtx {
        public TwitchMessage Msg;
        public TwitchViewer Viewer;
        public TwitchSys Sys;
        public int RepCost;
        public bool CheckRep() {
            if (RepCost == 0) {
                return true;
            }
            if (Viewer == null) {
                return false;
            }
            if (Viewer.Reputation < RepCost) {
                Reply("reputation too low ({2}). Need {0} for {1}",
                    RepCost, Msg.Command, Viewer.ReputationText);
                return false;
            }
            return true;
        }

        public string CommandName(string cmd) {
            return Sys.Settings.AddPrefix(cmd, false);
        }

        public bool TryConsumeRep() {
            if (RepCost == 0) {
                return true;
            }
            if (Viewer == null) {
                Reply("this command costs {0} reputation. Use {1} to start gaining it.",
                    RepCost, CommandName("join"));
                return false;
            }
            if (Viewer.Reputation < RepCost) {
                Reply("reputation too low ({2}). Need {0} for {1}",
                    RepCost, Msg.Command, Viewer.ReputationText);
                return false;
            }
            Viewer.Reputation -= RepCost;
            return true;
        }

        public bool UnconsumeRep() {
            if (RepCost == 0) {
                return true;
            }
            if (Viewer == null) {
                return false;
            }
            Viewer.Reputation += RepCost;
            return true;
        }

        public void Reply(string message, params object[] args) {
            Sys.Client.ReplyTo(Msg, message, args);
        }
        public void Reply(string message) {
            Sys.Client.ReplyTo(Msg, message);
        }
    }
}
