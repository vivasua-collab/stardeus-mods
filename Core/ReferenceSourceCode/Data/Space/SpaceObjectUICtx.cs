using System;
using System.Collections.Generic;
using System.Linq;
using Game.CodeGen;
using Game.Constants;
using Game.Systems.UIUX;
using Game.UI;
using Game.Utils;
using KL.Utils;

namespace Game.Data.Space {
    public sealed class SpaceObjectUICtx {
        public static void ShowQuickChoiceFor(SpaceObject so, UISpaceObject uiso) {
            // D.Warn("Adding ctx SO: {0}", res.Count);
            if (so == null || so.Parent == null) {
                return;
            }
            var player = so?.S?.Universe?.Player;
            if (player == null || player.Parent == null) {
                return;
            }

            ShowSOActions(so, uiso, player);
        }

        public static void ShowSOActions(SpaceObject so,
                UISpaceObject uiso, SpaceObject player) {
            // List UI Actions
            Caches.SOActions.Clear();
            SOAction.LoadFor(new SOActionContext() {
                S = so.S,
                Player = player,
                SO = so,
                UISO = uiso,
                UIType = SOActionContext.SOActionUIType.Starmap,
            }, Caches.SOActions);

            var qc = QuickChoice.Create(TitleFor(so, player));
            qc.OnClose = () => {
                Caches.SOActions.Clear();
            };

            bool loaded = false;
            for (int i = 0; i < Caches.SOActions.Count; i++) {
                SOAction act = Caches.SOActions[i];
                loaded |= act.AddOptions(qc);
            }

            if (loaded) {
                if (uiso != null) {
                    qc.OnClose = () => {
                        so.S.UISig.OnHoverUISpaceObject.Send(uiso, false);
                    };
                }
                so.S.UIQuery.ShowQuickChoice.Ask(qc);
            }
        }

        // Our own wreck is flagged derelict until it can fly, and "Derelict Ship" on our
        // own ship reads like we clicked the starting derelict instead
        private static string TitleFor(SpaceObject so, SpaceObject player) {
            if (so == player) {
                return T.OurShip;
            }
            if (so.Ship?.IsDerelict == true) {
                return T.SpaceObjectShipderelict;
            }
            return so.IsShip ? T.SpaceObjectShip : so.ListName;
        }
    }
}
