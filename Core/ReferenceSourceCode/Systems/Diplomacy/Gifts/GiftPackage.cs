using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Systems.Ships;
using Game.Systems.Trade;
using Game.Utils;
using KL.Randomness;
using KL.Utils;

namespace Game.Systems.Diplomacy {
    public sealed class GiftPackage {
        public struct Consequence {
            public readonly string Icon;
            public readonly string Text;
            public Consequence(string icon, string text) {
                Icon = icon;
                Text = text;
            }
        }
        public readonly GiftMode Mode;
        public readonly GiftDirection Direction;
        public bool IsCancel;
        private readonly NPCShip ship;
        // Null for a package with no ship on the other side (see ForFaction)
        public NPCShip Ship => ship;
        public readonly FactionDef Faction;
        public int Number;
        public string Name { get; private set; }
        public string Icon;
        public float AcceptChance;
        public Consequence[] AcceptConsequences;
        public Consequence[] RejectConsequences;
        public Action<GiftPackage> OnAccept;
        public Action<GiftPackage> OnReject;
        // Right before an accepted offer removes the items: OnAccept runs after they are
        // gone, too late for what must happen before (a quest package that fails its quest
        // when destroyed must be disarmed before it is handed over)
        public Action<GiftPackage> OnBeforeGive;
        public double Value;
        public double WantedValue;
        /// <summary>
        /// Max value of the package in the current set
        /// </summary>
        public double TopValue;
        /// <summary>
        /// Value of the package relative to TopValue;
        /// </summary>
        public float ValuePercent => (float) (Value / TopValue);
        public double RemainingValue => WantedValue - Value;
        public readonly List<GiftItem> Items = new();
        public static GiftPackage ForQuest(GiftDirection dir, NPCShip ship, string icon, string title) {
            var p = new GiftPackage(dir, GiftMode.Quest, ship, 0);
            p.Icon = icon;
            p.Name = title;
            p.AcceptChance = 1f;
            return p;
        }
        public GiftPackage(GiftDirection dir, GiftMode mode, NPCShip ship, int num)
            : this(dir, mode, ship, ship?.Faction, num) { }

        // A package with no ship on the other side: a planet space port takes its
        // guests home without anyone flying over
        public static GiftPackage ForFaction(GiftDirection dir, GiftMode mode,
                FactionDef faction, int num) {
            return new GiftPackage(dir, mode, null, faction, num);
        }

        private GiftPackage(GiftDirection dir, GiftMode mode, NPCShip ship,
                FactionDef faction, int num) {
            this.ship = ship;
            Faction = faction;
            Direction = dir;
            Mode = mode;
            Number = num;
            if (num == -1) {
                Name = T.Empty;
                Icon = IconId.CForbid;
                return;
            }
            if (mode == GiftMode.SendBeings) {
                Icon = faction.FlagIcon;
            }
            Name = NameFor(mode, num);
        }

        private static string NameFor(GiftMode mode, int num) {
            switch (mode) {
                case GiftMode.Quest: {
                    return T.NounQuestPackage;
                }
                case GiftMode.SendBeings: {
                    return T.NounSurvivorsPackage;
                }
                case GiftMode.Gift: {
                    return $"{T.NounGiftPackage} #{num + 1}";
                }
                case GiftMode.Fuel: {
                    return $"{T.NounFuelPackage} #{num + 1}";
                }
                case GiftMode.SurrenderRansom:
                case GiftMode.Extortion: {
                    return $"{T.NounRansom} #{num + 1}";
                }
                default: {
                    D.Err("Unhandled mode: {0}", mode);
                    return $"{T.NounGiftPackage} #{num + 1}";
                }
            }
        }

        public void Renumber(int num) {
            // Quest and survivor packages have unnumbered (and possibly custom) names
            if (IsCancel || Mode == GiftMode.Quest || Mode == GiftMode.SendBeings) {
                return;
            }
            Number = num;
            Name = NameFor(Mode, num);
        }

        public bool HasSameContents(GiftPackage other) {
            if (Items.Count != other.Items.Count) { return false; }
            // A package never holds the same item twice, so matching each item is enough
            foreach (var item in Items) {
                bool found = false;
                foreach (var otherItem in other.Items) {
                    if (item.IsSameAs(otherItem)) {
                        found = true;
                        break;
                    }
                }
                if (!found) { return false; }
            }
            return true;
        }

        public string ChoiceText {
            get {
                if (Mode == GiftMode.Quest) {
                    return Texts.Grey(T.NounQuestPackage);
                }
                switch (Direction) {
                    case GiftDirection.PlayerToNPC: {
                        return Units.PercentageColored(AcceptChance);
                    }
                    case GiftDirection.NPCToPlayer: {
                        return null;
                    }
                    default: {
                        D.Err("Unhandled direction: {0}", Direction);
                        return null;
                    }
                }
            }
        }

        public bool Take(GameState s, NPCShip ship, Rng rng) {
            var tradePortal = TradeUI.FindTradePortal(s);
            if (tradePortal == null) {
                OnReject?.Invoke(this);
                return false;
            }
            SpawnItems(s, rng, tradePortal.Tile.Transform.AnyPortOutFirst);
            OnAccept?.Invoke(this);
            return true;
        }

        // Offer accepts a package whatever RemoveItems manages to take, so the player must
        // still have all of it (a short quest delivery would complete for free)
        public bool CanPay(GameState s) {
            if (Direction != GiftDirection.PlayerToNPC) { return true; }
            foreach (var item in Items) {
                if (!item.CanRemove(s)) { return false; }
            }
            return true;
        }

        public bool Offer(GameState s, Rng rng) {
            if (rng.Chance(AcceptChance)) {
                D.Warn("Accepting offer: {0}", this);
                OnBeforeGive?.Invoke(this);
                RemoveItems(s);
                OnAccept?.Invoke(this);
                return true;
            }
            D.Warn("Rejecting offer: {0}", this);
            OnReject?.Invoke(this);
            return false;
        }

        public void AddItem(GameState s, GiftItem item) {
            item.Direction = Direction;
            if (item.Type == GiftItemType.Money) {
                foreach (var i in Items) {
                    if (i.Type == GiftItemType.Money) {
                        D.Warn("Trying to add money more than once to package: {0}", this);
                        return;
                    }
                }
            }
            if (item.Type == GiftItemType.Fuel) {
                foreach (var i in Items) {
                    if (i.Type == GiftItemType.Fuel) {
                        // D.Warn("Trying to add fuel more than once to package: {0}", this);
                        return;
                    }
                }
            }
            if (item.Mat.Type != null) {
                if (item.Mat.StackSize == 0) {
                    D.Err("Refusing to add mat with zero stack size: {0} | {1}",
                        item.Mat, item);
                    return;
                }
                foreach (var i in Items) {
                    if (i.Mat.Type == item.Mat.Type) {
                        D.Warn("Trying to add same material more than once to package: {0} -> {1}",
                            this, item.Mat);
                        return;
                    }
                }
            }
            if (item.Tradeable != null) {
                if (item.TradeableQuantity < 1) {
                    D.Err("Refusing to add tradable item with zero selected quantity: {0} | {1}",
                        item.Tradeable, item);
                    return;
                }
            }
            Value += item.Value(s);
            Items.Add(item);
        }

        private void SpawnItems(GameState s, Rng rng, int spawnPos) {
            foreach (var item in Items) {
                item.Spawn(s, ship, rng, spawnPos);
            }
        }
        private void RemoveItems(GameState s) {
            foreach (var item in Items) {
                item.Remove(s, ship);
            }
        }

        public GiftPackage WithAccept(Action<GiftPackage> act,
                params Consequence[] consequences) {
            AcceptConsequences = consequences;
            OnAccept = act;
            return this;
        }
        public GiftPackage WithReject(Action<GiftPackage> act,
                params Consequence[] consequences) {
            if (Direction == GiftDirection.NPCToPlayer) {
                D.Err("Direction NPC2Player doesn't support gift package rejection!");
                return this;
            }
            RejectConsequences = consequences;
            OnReject = act;
            return this;
        }

        public static Consequence NewConsequence(string icon, string text) {
            return new Consequence(icon, text);
        }

        public override string ToString() {
            return $"GiftPackage[#{Number}|{Mode}|{Direction}]";
        }
    }
}
