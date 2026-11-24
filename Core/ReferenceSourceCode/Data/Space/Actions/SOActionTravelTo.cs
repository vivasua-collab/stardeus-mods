using System.Collections;
using System.Collections.Generic;
using Game.Constants;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;
using Game.Systems.UIUX;
using Game.Input;

namespace Game.Data.Space {
    public sealed class SOActionTravelTo : SOAction {
        public const string ActionId = "TravelTo";
        [SelfInit]
        private static void Register() {
            Register(ActionId, new SOActionTravelTo());
        }

        public override bool IsAvailable(SOActionContext ctx) {
            return IsAvailable(ctx.S, ctx.Player, ctx.SO);
        }

        public static bool IsAvailable(GameState s, SpaceObject player, SpaceObject so) {
            if (so == s.Sys.Ship.DestinationSO) {
                return false;
            }
            if (so == player) {
                return false;
            }
            if (so.Ship != null) {
                so.Ship.NearbyPosFrom(player.Location, out var isNear);
                return !isNear;
            }
            if (s.Sys.Ship.IsNearby(so)) {
                return false;
            }
            return so.Parent == player.Parent
                && so.Parent.Layer == UniverseLayer.Sector;
        }

        public override bool AddOptions(QuickChoice qc) {
            var glyph = The.Bindings.GetControlName(ActionType.ClickCancel);
            var name = $"{T.TravelHere} {Texts.WithSize(glyph, 75)}";
            qc.AddButton(name, BeginTravel);
            return true;
        }

        // public override UDB GetUIBlock() {
        //     return dataBlock ??= UDB.Create(this, UDBT.DBtn, IconId.CPropulsion,
        //             T.TravelHere)
        //                 .WithClickFunction(BeginTravel);
        // }

        private void BeginTravel() {
            BeginTravel(ctx.S, ctx.Player, ctx.SO);
        }

        public static void BeginTravel(GameState s, SpaceObject player, SpaceObject so) {
            if (!IsAvailable(s, player, so)) {
                return;
            }
            BeginTravel(s, so);
        }

        public static void BeginTravel(GameState s, SpaceObject so) {
            // if parent is null, so was removed
            if (so != null && so.Parent != null) {
                SOUtils.CheckAbandonOnTravel(s, () => {
                    if (!s.Sys.Ship.SetDestination(so)) {
                        // showing tooltip
                        return;
                    }
                });
            } else {
                UISounds.PlayActionDenied();
            }

        }

        public override string Id => ActionId;

        public override int UIPriority => 50;
    }
}
