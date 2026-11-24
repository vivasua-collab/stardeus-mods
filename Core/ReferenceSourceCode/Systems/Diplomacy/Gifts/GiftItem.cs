using System;
using System.Collections.Generic;
using Game.Commands;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Inventory;
using Game.Systems.Items;
using Game.Systems.Ships;
using Game.Systems.Trade;
using Game.Utils;
using KL.Randomness;
using KL.Utils;

namespace Game.Systems.Diplomacy {
    public sealed class GiftItem {
        public GiftDirection Direction;
        public GiftItemType Type;
        public Tile Tile;
        public BeingData Being;
        public ObjData Obj;
        public TradeableItem Tradeable;
        // Kept here and not in Tradeable.QuantitySelected, because several packages
        // of one offer can share the same TradeableItem from the trader's inventory
        public int TradeableQuantity;
        public Mat Mat;
        public double Money;
        private double value;

        public double Value(GameState s) {
            if (value > 0d) { return value; }
            if (Tradeable != null) {
                value = s.Market.ValueOf(Tradeable.Def) * TradeableQuantity;
                return value;
            }
            switch (Type) {
                case GiftItemType.Tile: {
                    value = GiftGen.ValueOf(s, Tile, s.CountByDef(Tile.Definition));
                    break;
                }
                case GiftItemType.Being: {
                    value = BeingUtils.ValueOf(s, Being, true);
                    break;
                }
                case GiftItemType.Obj: {
                    value = s.Market.ValueOf(Obj.Def);
                    break;
                }
                case GiftItemType.Fuel:
                case GiftItemType.Mat: {
                    value = s.Market.ValueOf(Mat);
                    break;
                }
                case GiftItemType.Money: {
                    value = Money;
                    break;
                }
                default: {
                    D.Err("Unhandled gift item type: {0}", Type);
                    return 0d;
                }
            }
            return value;
        }
        public void Spawn(GameState s, NPCShip ship, Rng rng, int spawnPos) {
            // D.Warn("Spawning gift item: {0}", this);

            if (Tradeable != null) {
                Tradeable.QuantitySelected = TradeableQuantity;
                if (Tradeable.Quantity < Tradeable.QuantitySelected) {
                    D.Err("Gave too much as a gift! {0}: Has: {1}. Gifting: {2}",
                        Tradeable, Tradeable.Quantity, Tradeable.QuantitySelected);
                    Tradeable.Quantity = 0;
                } else {
                    Tradeable.Quantity -= Tradeable.QuantitySelected;
                }
                Tradeable.SpawnAt(s, spawnPos);
                return;
            }

            switch (Type) {
                case GiftItemType.Being: {
                    var b = s.Beings.Create(spawnPos, Being);
                    BeingUtils.ShowColonyGreetingFor(s, b, null);
                    break;
                }
                case GiftItemType.Obj: {
                    s.Objs.Create(EntityUtils.CenterOf(spawnPos), Obj);
                    break;
                }
                case GiftItemType.Fuel:
                case GiftItemType.Mat: {
                    EntityUtils.SpawnRawMaterial(Mat, spawnPos, 0.25f);
                    break;
                }
                case GiftItemType.Money: {
                    if (ship?.WillTrade == true) {
                        ship.SO.Trader.Wallet.TryPay(Money, null);
                    }
                    s.Wallet.Add(Money);
                    break;
                }
                default: {
                    D.Err("Unhandled gift item type: {0}", Type);
                    break;
                }
            }
        }

        public void Remove(GameState s, NPCShip ship) {
            // D.Warn("Removing gift item: {0}", this);
            switch (Type) {
                case GiftItemType.Tile: {
                    new CmdRemoveTile(Tile).Execute(s);
                    break;
                }
                case GiftItemType.Being: {
                    if (Being.IsGuest) {
                        // A departing guest must not keep Mind.Role == Passenger after
                        // removal. EndGuestRole is idempotent and falls through to
                        // RemoveFromShip (which also unassigns the bed).
                        PassengerUtils.EndGuestRole(s, Being, despawn: true);
                    } else {
                        BeingUtils.RemoveFromShip(s, Being);
                    }
                    break;
                }
                case GiftItemType.Obj: {
                    TradeUI.RemoveObjData(s, Obj, this);
                    break;
                }
                case GiftItemType.Fuel:
                case GiftItemType.Mat: {
                    s.Sys.Inventory.TrySell(Mat.Type, Mat.StackSize, true);
                    break;
                }
                case GiftItemType.Money: {
                    if (ship?.WillTrade == true) {
                        ship.SO.Trader.Wallet.Add(Money);
                    }
                    s.Wallet.TryPay(Money, null);
                    break;
                }
                default: {
                    D.Err("Unhandled gift item type: {0}", Type);
                    break;
                }
            }
        }

        // Whether Remove can take the whole item now. Selling is all or nothing, so a short
        // Mat or Fuel item would be given away without anything leaving the ship
        public bool CanRemove(GameState s) {
            switch (Type) {
                case GiftItemType.Fuel:
                case GiftItemType.Mat: {
                    return s.Sys.Inventory.TrySell(Mat.Type, Mat.StackSize, false);
                }
                default: {
                    return true;
                }
            }
        }

        public bool IsSameAs(GiftItem other) {
            if (other == null || Type != other.Type) { return false; }
            if (Tradeable != null || other.Tradeable != null) {
                return Tradeable == other.Tradeable
                    && TradeableQuantity == other.TradeableQuantity;
            }
            switch (Type) {
                case GiftItemType.Tile: {
                    return Tile == other.Tile;
                }
                case GiftItemType.Being: {
                    return Being == other.Being;
                }
                case GiftItemType.Obj: {
                    return Obj == other.Obj;
                }
                case GiftItemType.Fuel:
                case GiftItemType.Mat: {
                    return Mat.Type == other.Mat.Type && Mat.StackSize == other.Mat.StackSize;
                }
                case GiftItemType.Money: {
                    return Money == other.Money;
                }
                default: {
                    return false;
                }
            }
        }

        public override string ToString() {
            return $"GiftItem[{Type},{Tile},{Being},{Obj},{Mat},{Money}]";
        }
    }
}
