using System;
using System.Collections;
using System.Collections.Generic;
using Game.Constants;
using Game.Systems.UIUX;
using Game.UI;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Data.Space {
    public sealed class SOActionDevDebug : SOAction {
        public const string ActionId = "DevDebug";
        [SelfInit]
        private static void Register() {
            Register(ActionId, new SOActionDevDebug());
        }

        public override bool IsAvailable(SOActionContext ctx) {
           // #if UNITY_EDITOR
            //return true;
            //#else
            return A.InstantBuild;
            //#endif
        }

        public override bool AddOptions(QuickChoice qc) {
            qc.AddIconButton("Sandbox: Teleport Here", new QuickChoice.IconButton() {
                AutoClose = true,
                Icon = Images.Sprite(IconId.WEye),
                OnClick = TeleportToTarget,
                TipFunc = ShowPlanetInfo,
            });
            if (CanOpenShipDebugMenu()) {
                qc.AddIconButton("Debug NPC Ship", new QuickChoice.IconButton() {
                    AutoClose = true,
                    Icon = Images.Sprite(IconId.WDev),
                    OnClick = ClickDebug,
                });
            }
            return true;
        }

        private void TeleportToTarget() {
            var s = ctx.S;
            var targetSO = ctx.SO;
            if (targetSO.Region != null) {
                targetSO = targetSO.Region.InboundSector.SO;
            }
            var loc = targetSO.LocationApprox(ctx.Player.Location);
            if (!UIShowing.Starmap || A.StarmapCtx.Layer == UniverseLayer.Sector) {
                s.Sys.Ship.TeleportTo(loc);
            } else {
                if (!SOUtils.WarpTo(s, A.Player.Parent, targetSO)) {
                    UISounds.PlayActionDenied();
                }
            }
        }

        private void ClickDebug() {
            if (!CanOpenShipDebugMenu()) {
                return;
            }
            var qc = QuickChoice.Create("Debug NPC Ship");
            foreach (var reaction in Registries.NPCShipReactions.All.Values) {
                var shipReaction = reaction;
                qc.AddButton(shipReaction.Id, () => {
                    shipReaction.ReactTo(ctx.SO.Ship, A.Player.Ship);
                });
            }
            ctx.S.UIQuery.ShowQuickChoice.Ask(qc);
        }

        private bool CanOpenShipDebugMenu() {
            var ship = ctx.SO.Ship;
            return ship != null && !ship.IsPlayer && !ship.IsDerelict;
        }

        private string ShowPlanetInfo() {
            Caches.StringBuilder.Clear();
            Caches.StringBuilder.Append("<size=80%>");
            Caches.StringBuilder.AppendFormat("Debug: {0}\n", ctx.SO.Type.InfoT);
            Caches.StringBuilder.AppendFormat("Knowledge: {0}\n", ctx.SO.Knowledge);//D.DumpInstance(ctx.SO.Knowledge, true, forLog: true));
            if (ctx.SO.HasAnyMats) {
                Caches.StringBuilder.AppendFormat("Mats: {0}\n", D.Dump(ctx.SO.Mats));
            }
            Caches.StringBuilder.AppendFormat("Children: {0}\n", Arrays.Count(ctx.SO.Children));
            Caches.StringBuilder.AppendFormat("Is Reachable: {0}\n",
                ctx.SO.IsReachableFrom(ctx.S.Universe.Player.Location));
            Caches.StringBuilder.AppendFormat("Type: {0}\n", ctx.SO.Type);
            //D.DumpInstance(ctx.SO.Type, true, forLog: true));
            /*
            Caches.StringBuilder.AppendFormat("AlienLifeChance: {0}\n",
                SpaceObjectLootType.Get(ctx.SO.LootTypeId)?.AlienLifeChance);
                */
            //Caches.StringBuilder.AppendFormat("Looks: {0}\n", ctx.SO.Looks());
            if (ctx.SO.HasData) {
                Caches.StringBuilder.AppendFormat("SO Data Floats: {0}\n", D.Dump(ctx.SO.Data.Floats));
                Caches.StringBuilder.AppendFormat("SO Data Ints: {0}\n", D.Dump(ctx.SO.Data.Ints));
                Caches.StringBuilder.AppendFormat("SO Data Bools: {0}\n", D.Dump(ctx.SO.Data.Bools));
            }
            if (ctx.S.Sys.Planets.TryGetResourceFor(ctx.SO, out var res, out var _)) {
                Caches.StringBuilder.AppendFormat("Resource: {0}\n", res);
            }
            Caches.StringBuilder.AppendFormat("SO: {0}\n", ctx.SO);//D.DumpInstance(ctx.SO, true, forLog: true));
            Caches.StringBuilder.Append("</size>");
            return Caches.StringBuilder.ToString();
        }

        public override string Id => ActionId;

        public override int UIPriority => -1000;
    }
}
