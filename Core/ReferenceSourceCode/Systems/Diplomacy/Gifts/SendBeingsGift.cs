using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Data.Economy;
using Game.Data.Factions;
using Game.Systems.Creatures;
using Game.Systems.Ships;
using Game.Systems.Trade;
using Game.Utils;
using UnityEngine;

namespace Game.Systems.Diplomacy {
    // The "Rescued Survivors" package that hands guests back to their own faction. Shared by
    // the Send Beings hail (a ship of the faction flies over) and a planet space port of the
    // faction (the player's ship is next to it), so both pay and reward the same.
    public static class SendBeingsGift {
        // Holds every sendable guest of the faction as a Being item, always accepted, and
        // paid out on accept. Null when there is nobody to send. The ship is null at a port.
        public static GiftPackage CreatePackage(GameState s, FactionDef faction, NPCShip ship,
                Trader payer, out int count) {
            var sendable = new List<BeingData>();
            count = PassengerUtils.CollectSendableGuests(s, faction, sendable);
            if (count == 0) { return null; }

            var pack = ship != null
                ? new GiftPackage(GiftDirection.PlayerToNPC, GiftMode.SendBeings, ship, 0)
                : GiftPackage.ForFaction(GiftDirection.PlayerToNPC, GiftMode.SendBeings, faction, 0);
            for (int i = 0; i < sendable.Count; i++) {
                var item = new GiftItem();
                item.Type = GiftItemType.Being;
                item.Being = sendable[i];
                pack.AddItem(s, item);
            }
            pack.AcceptChance = 1f;

            // The faction pays for its people back - the summed value of the sent beings,
            // mirroring the money branch of GiftItem.Spawn. Done in OnAccept: payment cannot
            // ride as a GiftItemType.Money item, since in a P2N package GiftItem.Remove makes
            // the player pay.
            double total = pack.Value;
            // Gratitude grows with every person returned, but a single handover must not
            // outweigh a long history of dealings - cap it well above the best gift
            // package (0.1 for the top one) and let repeat rescues do the rest.
            float attChange = Mathf.Min(0.05f * count, 0.15f);

            var facText = T.RelationshipWith_V1.F(Texts.Accented(faction.NameWithGlyph));
            var acceptText = $"{facText}: {Units.PercentageSigned(attChange, 2, true)}";
            // Consequence.Icon is not rendered by the gift widgets - only Text is - so the
            // line has to carry its own label. ToValueString already prefixes the currency.
            var payText = $"{T.CurrencyStarcreds}: {Texts.Green(Wallet.ToValueString(total))}";
            pack.WithAccept((p) => {
                payer?.Wallet.TryPay(total, null);
                s.Wallet.Add(total);
                s.Query.ChangeFactionAttitude.Ask(faction, FactionDef.Player, attChange);
            }, GiftPackage.NewConsequence(IconId.CStarcreds, payText),
                GiftPackage.NewConsequence(IconId.CCheck, acceptText));
            return pack;
        }
    }
}
