using System;
using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    // Takes the player from a selected device to the view that explains it
    // (O2 pump -> oxygen overlay, battery -> Electricity panel). The buttons
    // only differ in data, so each registered instance is one row.
    public sealed class RelatedViewAction : BaseEntityAction {
        public const string IdPrefix = "RelatedView_";

        [SelfInit]
        private static void Register() {
            int order = 1;
            RegisterAction(new RelatedViewAction("Oxygen", order++,
                IconId.COxygen, () => T.OverlayTipOxygen,
                ToggleOverlay(OverlayId.AtmoO2),
                t => t.HasComponent<OxygenProducerComp>()));
            RegisterAction(new RelatedViewAction("Temperature", order++,
                IconId.CTemperature, () => T.OverlayTipHeat,
                ToggleOverlay(OverlayId.AtmoTemp),
                t => t.HasComponent<HeaterComp>() || t.HasComponent<HeatSinkComp>()));
            RegisterAction(new RelatedViewAction("Airtightness", order++,
                IconId.CAirtightness, () => T.OverlayTipAirtight,
                ToggleOverlay(OverlayId.AtmoO2Res),
                t => t.HasComponent<VentComp>() || t.HasComponent<FloorVentComp>()));
            // Connectors are about links, which the map shows
            RegisterAction(new RelatedViewAction("Electricity", order++,
                IconId.CConnected, () => T.OverlayTipElectricity,
                ToggleOverlay(OverlayId.Electricity),
                t => t.ENode != null && t.ENode.IsConnector));
            // Producers and batteries are about output and charge, which the panel shows
            RegisterAction(new RelatedViewAction("ElectricGrid", order++,
                IconId.CEnergy, () => T.ElectricityGrid,
                () => A.S.UIQuery.ToggleElectricity.Ask(!UIShowing.Electricity),
                IsGridSource));
            RegisterAction(new RelatedViewAction("Paint", order++,
                IconId.CPaint, () => T.OverlayTipTint,
                ToggleOverlay(OverlayId.Tint),
                t => t.HasComponent<TinterComp>()));
        }

        private static Action ToggleOverlay(string overlayId) {
            return () => A.S.Sig.ToggleOverlayById.Send(overlayId);
        }

        private static bool IsGridSource(Tile tile) {
            var enode = tile.ENode;
            if (enode == null) { return false; }
            return enode.Node.IsProducer || enode.Node.IsProducerSmart || enode.Node.IsBattery;
        }

        private readonly string id;
        private readonly int order;
        private readonly string icon;
        // Read when the button spawns: T is reloaded with the language
        private readonly Func<string> label;
        private readonly Action open;
        private readonly Func<Tile, bool> appliesTo;

        private RelatedViewAction(string name, int order, string icon, Func<string> label,
                Action open, Func<Tile, bool> appliesTo) {
            id = $"{IdPrefix}{name}";
            this.order = order;
            this.icon = icon;
            this.label = label;
            this.open = open;
            this.appliesTo = appliesTo;
        }

        public override string Id => id;

        private bool AppliesTo(Tile tile) {
            return tile.IsConstructed && appliesTo(tile);
        }

        protected override bool ApplyFor(GameState s, Tile tile, out EntityActionData data) {
            if (!AppliesTo(EntityUtils.ParentObj(tile))) {
                data = EntityActionData.Unavailable;
                return false;
            }
            data = new EntityActionData(this, Create, Priority.First, order);
            return true;
        }

        protected override bool ApplyForMany(GameState s, List<Tile> tiles, out EntityActionData data) {
            foreach (var t in tiles) {
                if (AppliesTo(EntityUtils.ParentObj(t))) {
                    data = new EntityActionData(this, Create, Priority.First, order);
                    return true;
                }
            }
            data = EntityActionData.Unavailable;
            return false;
        }

        private WBase Create(Lifetime l, RectTransform p, int i) {
            var widget = SpawnButton(l, true, p, i);
            widget.Load(icon, label(), open);
            return widget;
        }
    }
}
