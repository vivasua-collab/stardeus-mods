using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Data.Economy;
using Game.Systems.UIUX;
using Game.Utils;
using KL.Utils;
using MessagePack;

namespace Game.Systems.Trade {
    [MessagePackObject]
    public sealed class OnlineOrder {
        [Key("Items")] public List<TradeableItem> Items;
        [Key("TotalPrice")] public double TotalPrice;
        [Key("OrderTick")] public long OrderTick;
        [Key("DeliveryTick")] public long DeliveryTick;
        [Key("SpaceObjectId")] public int SpaceObjectId;
        [Key("CapsuleId")] public int CapsuleId;

        [IgnoreMember] public readonly B<string> DeliveryText = new(null);

        [IgnoreMember] public string Title {
            get {
                string title;
                var firstItem = Items[0];
                title = firstItem.Name;
                if (firstItem.Quantity > 1) {
                    title = $"{title} x{firstItem.Quantity}";
                }
                if (Items.Count > 1) {
                    title = $"{title} <b>+{Items.Count - 1}</b>";
                }
                return title;
            }
        }

        [IgnoreMember] public bool IsDelivered => CapsuleId > 0;
        [IgnoreMember] public bool IsPendingDelivery => CapsuleId == 0 || (G.Ticks - DeliveryTick) < Consts.TicksPerHour;
        [IgnoreMember]public string Icon {
            get {
                if (IsDelivered) {
                    return GraphicsId.ObjContainersStorageCapsule_Open;
                } else if (IsPendingDelivery) {
                    if (SpaceObjectId == A.S.Universe.Player.Parent.Id) {
                        return GraphicsId.ObjContainersStorageCapsule;
                    } else {
                        return IconId.CWarning;
                    }
                } else {
                    return IconId.CCross;
                }
            }
        }

        public string Tooltip() {
            var sb = Caches.StringBuilder;
            sb.Clear();

            if (DeliveryTick > G.Ticks) {
                if (SpaceObjectId != A.S.Universe.Player.Parent.Id) {
                    sb.Append(Texts.Red(T.ShopOrderWrongLocationTip));
                    sb.AppendLine();
                }
                sb.Append(T.ShopOrderEstimatedDelivery_V1.F(
                    Texts.Accented(Units.TicksToTime(DeliveryTick - G.Ticks, true))));
            } else {
                if (CapsuleId > 0) {
                    //delivered
                    sb.AppendFormat("{0}: {1}", T.ShopOrderDelivered,
                        Texts.Accented(Units.TicksAgo((int)(G.Ticks - DeliveryTick))));
                } else if (CapsuleId < 0) {
                    // failed
                    sb.AppendFormat("{0}: {1}", T.ShopOrdersDeliveryFailed,
                        Texts.Accented(Units.TicksAgo((int)(G.Ticks - DeliveryTick))));
                } else {
                    // pending
                    sb.AppendFormat(T.GoalStateInprogress);
                }
            }
            sb.Append("\n\n");
            foreach (var item in Items) {
                sb.AppendFormat(" - {0} {1}\n", Texts.Accented(item.Name),
                        Texts.Grey(Units.XNum(item.Quantity)));
            }
            sb.AppendFormat("\n{0}: {1}", T.NounPrice, Texts.Green(Wallet.ToValueString(TotalPrice)));
            return sb.ToString();
        }

        public void UpdateDeliveryText(GameState s) {
            if (DeliveryTick > G.Ticks) {
                if (SpaceObjectId == s.Universe.Player.Parent.Id) {
                    DeliveryText.Set(T.Eta_V1.F(Units.TicksToTime(DeliveryTick - G.Ticks, true)));
                } else {
                    DeliveryText.Set(Texts.Red(T.ShopOrderWrongLocation));
                }
            } else {
                if (CapsuleId > 0) {
                    DeliveryText.Set(Texts.Green(T.ShopOrderDelivered));
                } else if (CapsuleId < 0) {
                    DeliveryText.Set(Texts.Red(T.ShopOrdersDeliveryFailed));
                } else {
                    DeliveryText.Set(T.GoalStateInprogress);
                }
            }
        }
    }
}
