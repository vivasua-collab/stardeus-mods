using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Data.Economy;
using Game.Systems.BodyParts;
using Game.Systems.Creatures;
using Game.Systems.Drama;
using Game.Systems.Items;
using Game.Systems.Mental;
using Game.Systems.Naming;
using Game.Systems.Notify;
using Game.Utils;
using KL.Randomness;
using KL.Utils;

namespace Game.Systems.Trade {
    // Rules, stock, pricing and hand-over for trading beings (pets, people, bots).
    //
    // Beings ride the regular TradeableItem pipeline, but their MarketItems and MarketGroups are
    // built here and never registered in MarketItem.All/CFDs, MarketGroup.All or the Market, so
    // beings never show up on the stock market or in anything that samples market items (quests,
    // loot, signals). Their price is not a market price: a being is worth exactly what its
    // sellable parts and gear would fetch loose at the same trader, plus a frame price and the
    // premiums. That makes selling a whole being always pay more than taking it apart.
    public static class BeingTrade {
        private static readonly Dictionary<int, MarketItem> itemsByDef = new();
        private static readonly Dictionary<int, MarketGroup> groups = new();
        private static readonly List<Species> tradeable = new();

        [SelfInit]
        private static void Register() {
            itemsByDef.Clear();
            groups.Clear();
            tradeable.Clear();
        }

        public struct Price {
            public double Parts;
            public double Gear;
            public double Frame;
            public double Skills;
            public double Living;
            // Trader group price for the being group, includes markup
            public float GroupFactor;
            public double Total => Parts + Gear + (Frame + Skills + Living) * GroupFactor;
        }

        private static void EnsureRegistry() {
            if (itemsByDef.Count > 0) { return; }
            foreach (var sp in Species.Known.Values) {
                if (!sp.IsTradeable || sp.Def == null) { continue; }
                var groupIdH = Hashes.S(sp.Trade.Group);
                if (!groups.TryGetValue(groupIdH, out var group)) {
                    group = new MarketGroup {
                        Id = sp.Trade.Group,
                        IdH = groupIdH,
                        NameT = $"market.group.name.{sp.Trade.Group}".T(),
                        Rarity = 1f,
                        RecessionEffect = 1f,
                    };
                    groups.Add(groupIdH, group);
                }
                var def = sp.Def;
                var mi = new MarketItem {
                    Type = MarketItemType.CFD.ToString(),
                    AssetType = MarketItemType.CFD,
                    Def = def,
                    Id = def.Id,
                    IdH = def.IdH,
                    FirstGroup = group,
                    Groups = new[] { group.Id },
                    GroupsH = new[] { group.IdH },
                    Value = sp.BeingValueBase,
                    Rarity = 1f,
                    Icon = def.Preview,
                    Name = def.NameT,
                    SearchName = def.SearchNameT,
                };
                itemsByDef[def.IdH] = mi;
                tradeable.Add(sp);
            }
        }

        public static bool TryGetMarketItem(int defIdH, out MarketItem mi) {
            EnsureRegistry();
            return itemsByDef.TryGetValue(defIdH, out mi);
        }

        public static bool CanTrade(GameState s, Trader trader, Species species, bool selling) {
            if (trader == null || trader.IsOnline) { return false; }
            if (species == null || !species.IsTradeable) { return false; }
            if (species.IsPet) { return true; }
            if (species.IsBiological) {
                if (!species.IsHumanoid) { return false; }
                if (!trader.Faction.TradesPeople) { return false; }
                if (!s.Research.IsUnlocked(DefIdsH.ResearchEconomyBlackMarket)) { return false; }
                // The ship AI may free captives, but handing its own people over to
                // slavers breaks the First Law unless the Asimov Override is installed
                return !selling || A.HasAsimovOverride;
            }
            return true;
        }

        public static bool CanStock(GameState s, Trader trader, Species species) {
            if (!CanTrade(s, trader, species, false)) { return false; }
            if (!species.IsBiological && s.Situation.ColonySizeDiff <= 0) {
                return false;
            }
            return true;
        }

        public static void GenStockFor(GameState s, Trader trader, Rng rng) {
            EnsureRegistry();
            foreach (var sp in tradeable) {
                if (!CanStock(s, trader, sp)) { continue; }
                if (!rng.Chance(sp.Trade.ChanceInStock)) { continue; }
                var mi = itemsByDef[sp.Def.IdH];
                var max = sp.Trade.MaxInStock < 1 ? 1 : sp.Trade.MaxInStock;
                var count = rng.Range(1, max + 1);
                for (int i = 0; i < count; i++) {
                    var item = TradeableItem.Create(mi, 1, TradeableItem.ItemOwner.Them);
                    item.BeingSeed = rng.Range(1, int.MaxValue);
                    trader.Inventory.Add(item);
                }
                EnsureGroup(trader, mi.FirstGroup);
            }
        }

        // Adds the being group to the trader with a price roll seeded by the trader, so the
        // same trader always values the group the same way. Without it GroupPriceFor has
        // nothing to find for traders that were never meant to roll these groups.
        private static void EnsureGroup(Trader trader, MarketGroup group) {
            if (trader.TradeableGroups.ContainsKey(group.IdH)) { return; }
            var rng = new Rng(Hashes.Quick((int) trader.Id, group.IdH));
            trader.TradeableGroups[group.IdH] =
                rng.Curve(Tunables.TraderMarketGroupCostAdjustmentCurve);
        }

        // Offers are stored as a seed plus a name, not as BeingData: serializing a being that was
        // never spawned registers its mind, which would leave orphan minds in the save for every
        // offer that is never bought. The seed regenerates the same body, skills, traits, looks
        // and gear; the name is saved separately (see below).
        public static BeingData EnsureBeing(GameState s, Trader trader, TradeableItem item) {
            if (item.Being != null) { return item.Being; }
            if (item.Owner != TradeableItem.ItemOwner.Them) { return null; }
            var species = Species.ForDef(item.Def.IdH);
            if (species == null) {
                D.Err("Trying to generate a being offer for a non-being def: {0}", item);
                return null;
            }
            var rng = new Rng(item.BeingSeed);
            var vars = new GenBeingVars(species, BeingType.Neutral);
            vars.Body = trader.Faction.GenBodyVars;
            var flags = GenItemFlags.None;
            if (species.IsHumanoid) {
                flags |= GenItemFlags.Casual;
            }
            var being = BeingUtils.GenBeingWithInventory(vars, rng, flags);
            // Everything else regenerates identically from the seed, but names come from the
            // legacy generator's shared pool of unused parts, so the name is saved separately
            var name = being.Mind?.Name;
            if (name != null) {
                if (item.BeingName == null) {
                    item.BeingName = BeingName.Serialize(name);
                } else {
                    // Written into the generated name in place, which keeps its link back to
                    // the name source so the name is still reserved once the being spawns
                    var saved = BeingName.Deserialize(item.BeingName);
                    name.FirstName = saved.FirstName;
                    name.LastName = saved.LastName;
                    name.Nickname = saved.Nickname;
                    name.UpdateName();
                }
            }
            item.Being = being;
            return being;
        }

        public static bool IsWreck(BeingData data) {
            return data.HasStateFlag(BeingState.Dead);
        }

        public static Price PriceOf(GameState s, Trader trader, TradeableItem item) {
            var price = new Price();
            var being = item.Being ?? EnsureBeing(s, trader, item);
            if (being == null) { return price; }
            var owner = item.Owner;
            price.Parts = PartsValue(s, trader, being, owner);
            price.Gear = GearValue(s, trader, being, owner);
            price.Frame = FrameValue(being);
            if (!IsWreck(being)) {
                price.Skills = SkillPremium(being);
                price.Living = being.Species.Trade?.LivingPremium ?? 0f;
            }
            EnsureGroup(trader, item.MarketItem.FirstGroup);
            price.GroupFactor = trader.GroupPriceFor(item);
            return price;
        }

        // Each part is priced by the same code that prices it loose at this trader, which is
        // what guarantees that a whole being never sells for less than its pieces.
        private static double PartsValue(GameState s, Trader trader, BeingData being,
                TradeableItem.ItemOwner owner) {
            var total = 0d;
            foreach (var slot in being.Body.Slots) {
                if (slot.IsPartRemoved || slot.InstalledDef == null) { continue; }
                var mi = MarketItem.FindFor(slot.InstalledDef.Def);
                if (mi == null) { continue; }
                var quality = slot.State?.Quality ?? 1f;
                var condition = slot.State?.Condition ?? 1f;
                // a part in this state is never listed loose either
                if (quality <= 0f || condition <= 0f) { continue; }
                var part = TradeableItem.Create(mi, 1, owner);
                part.Quality = quality;
                part.Condition = condition;
                total += LoosePriceOf(s, trader, part);
            }
            return total;
        }

        private static double GearValue(GameState s, Trader trader, BeingData being,
                TradeableItem.ItemOwner owner) {
            if (being.Inventory == null) { return 0d; }
            var total = 0d;
            foreach (var slot in being.Inventory.Equipment.Values) {
                var obj = slot.ObjData;
                if (obj == null) { continue; }
                var mi = MarketItem.FindFor(obj.Def);
                if (mi == null) { continue; }
                var gear = TradeableItem.Create(mi, 1, owner);
                gear.Quality = obj.Quality;
                if (owner == TradeableItem.ItemOwner.Us) {
                    gear.OurObjData = obj;
                }
                total += LoosePriceOf(s, trader, gear);
            }
            return total;
        }

        private static double LoosePriceOf(GameState s, Trader trader, TradeableItem loose) {
            var isBuying = loose.Owner == TradeableItem.ItemOwner.Them;
            if (trader.TryGroupPriceFor(loose.MarketItem, loose.Owner, out _)) {
                trader.UpdatePriceFor(s, loose);
                return loose.FinalPriceOfOne(isBuying);
            }
            // The trader does not deal in this group, so the piece could not be traded here
            // on its own. Price it at market with markup only.
            var market = s.Market.Adjusted(loose.MarketItem)?.Value ?? 0d;
            var mul = isBuying
                ? loose.MarketItem.PlayerBuyPriceMultiplier
                : loose.MarketItem.PlayerSellPriceMultiplier;
            var q = loose.IsBodyPart ? loose.Quality * loose.Condition : loose.Quality;
            return market * q * trader.MarkupFor(loose.Owner) * mul;
        }

        private static double FrameValue(BeingData being) {
            double value = being.Species.BeingValueBase;
            if (being.Body.TryGetSlot(BodyPartSlotIdH.Body, out var body) && body.State != null) {
                value *= body.State.Quality * body.State.Condition;
            }
            return value;
        }

        // Buyers pay for a specialist, so this looks at the best few skills rather than the
        // sum over all jobs (Jobs.SkillsValue), which rewards having many unskilled jobs.
        public static float SkillScore(BeingData being) {
            var jobs = being.Mind?.Jobs;
            if (jobs == null) { return 0f; }
            int a = 0, b = 0, c = 0;
            foreach (var job in jobs.JobSlots.Values) {
                if (job.IsInternal || job.Incapable || job.Job.Unskilled) { continue; }
                int lvl = job.Level;
                if (lvl > a) {
                    c = b;
                    b = a;
                    a = lvl;
                } else if (lvl > b) {
                    c = b;
                    b = lvl;
                } else if (lvl > c) {
                    c = lvl;
                }
            }
            return (a + b + c) / 3f / JobSlot.SkillRangeTo;
        }

        private static float SkillPremium(BeingData being) {
            var sp = being.Species;
            if (!sp.IsBiological || !sp.IsHumanoid) { return 0f; }
            return CurveDef.FastGet(CurveIdH.Economy_BeingSkillPremium).Evaluate(SkillScore(being));
        }

        // Dynamic tooltips are rebuilt every frame while hovered, so the text is built once and
        // kept on the item. Nothing in it changes while the trade window is open: prices are
        // only updated when the window (re)loads, which also clears this cache, and both
        // wallets only change when the trade is executed, which closes the window.
        public static string Tooltip(GameState s, Trader trader, TradeableItem item, bool isBuying) {
            return item.BeingTooltip ??= BuildTooltip(s, trader, item, isBuying);
        }

        private static string BuildTooltip(GameState s, Trader trader, TradeableItem item, bool isBuying) {
            var being = item.Being;
            // GetBio builds in Caches.StringBuilder4, so take its result before reusing it
            var bio = being.UI.GetBio(forStarting: being.Being == null, shortVersion: false);
            var price = item.BeingPrice;
            var sb = Caches.StringBuilder4;
            sb.Clear();
            var total = item.FinalPriceOfOne(isBuying);
            var totalStr = Wallet.ToValueString(total, true);
            totalStr = TradeUI.PriceText(totalStr, total, isBuying, s, trader);
            if (isBuying) {
                sb.AppendLine(T.CanBuyFor_V2.F(Texts.Accented(1), totalStr));
            } else {
                sb.AppendLine(T.CanSellFor_V2.F(Texts.Accented(1), totalStr));
            }
            AppendPriceLine(sb, T.NounBodyParts, price.Parts);
            AppendPriceLine(sb, T.Equipment, price.Gear);
            AppendPriceLine(sb, T.NounBody, price.Frame * price.GroupFactor);
            AppendPriceLine(sb, T.Skills, price.Skills * price.GroupFactor);
            AppendPriceLine(sb, T.BeingStateAlive, price.Living * price.GroupFactor);
            sb.Append('\n');
            sb.Append(bio);
            return sb.ToString();
        }

        private static void AppendPriceLine(System.Text.StringBuilder sb, string label, double value) {
            if (value < 1d) { return; }
            sb.AppendFormat(" - {0}: {1}\n", label, Texts.Accented(Wallet.ToValueString(value, true)));
        }

        public static void LoadOurBeings(GameState s, Trader trader, List<TradeableItem> items) {
            EnsureRegistry();
            foreach (var b in s.Beings.All) {
                var data = b.Data;
                if (!CanSellNow(s, trader, data)) { continue; }
                var mi = itemsByDef[data.Species.Def.IdH];
                var item = TradeableItem.Create(mi, 1, TradeableItem.ItemOwner.Us);
                item.Being = data;
                EnsureGroup(trader, mi.FirstGroup);
                trader.UpdatePriceFor(s, item);
                items.Add(item);
            }
        }

        public static bool CanSellNow(GameState s, Trader trader, BeingData data) {
            if (data?.Being == null || !data.Being.IsActive) { return false; }
            var sp = data.Species;
            if (sp == null || !sp.IsTradeable || !itemsByDef.ContainsKey(sp.Def.IdH)) {
                return false;
            }
            if (data.HasStateFlag(BeingState.Hidden)) { return false; }
            if (IsCarried(s, data.Being)) { return false; }
            if (IsWreck(data)) {
                // Salvage: any faction's bot wreck aboard, never an organic corpse
                return sp.Trade.SellWreck && CanTrade(s, trader, sp, true);
            }
            if (!data.HasStateFlag(BeingState.Alive)) { return false; }
            if (!data.IsPlayer || data.IsGuest) { return false; }
            if (data.Body.IsIncapacitated) { return false; }
            return CanTrade(s, trader, sp, true);
        }

        private static bool IsCarried(GameState s, Being being) {
            foreach (var b in s.Beings.All) {
                if (b.Graphics?.Carrying == being) { return true; }
            }
            return false;
        }

        public static void Sell(GameState s, Trader trader, BeingData data) {
            var name = data.Mind?.Name?.ShortName ?? data.Species.Def.NameT;
            var wasAlive = !IsWreck(data);
            var wasCrew = wasAlive && data.Species.IsBiological && data.Species.IsHumanoid;
            var wasPet = wasAlive && data.Species.IsPet;
            data.Being?.Graphics?.DropIfCarrying();
            var msg = T.TradeBeingSold_V2.F(Texts.LinkToBeing(data, name), trader.Name);
            s.Sys.Log.AddLine(msg);
            Notification.BuildFor(data, IconId.CTrade, msg, Consts.TicksPerDay,
                    NotificationType.Info)
                .Show();
            BeingUtils.RemoveFromShip(s, data);
            // Nobody mourns a bot being sold
            if (wasCrew || wasPet) {
                UpsetCrewOverSale(s, data, wasCrew);
            }
            if (wasCrew) {
                TrustSys.OnCrewSold(s, data);
            }
        }

        // Selling a person upsets the crew, but a pet only minds when it was its own master who
        // got sold. Selling a pet upsets only its master. The bond is directional: DramaSys sets
        // Pet on the master's opinion of the pet, and Master on the pet's opinion of the master.
        private static void UpsetCrewOverSale(GameState s, BeingData sold, bool wasPerson) {
            var soldMindId = sold.Mind?.Id ?? 0;
            foreach (var bd in s.Query.AllBeingData.Ask().Values) {
                if (bd == sold || !bd.IsPlayer) { continue; }
                if (bd.Mind?.Health == null) { continue; }
                if (bd.HasStateFlag(BeingState.Dead)) { continue; }
                BeingOpinionFlags bond;
                if (wasPerson) {
                    bond = bd.Species.IsPet ? BeingOpinionFlags.Master : BeingOpinionFlags.None;
                } else {
                    bond = BeingOpinionFlags.Pet;
                }
                if (bond != BeingOpinionFlags.None && !HasBond(bd, soldMindId, bond)) { continue; }
                var pos = bd.Being?.PosIdx ?? -1;
                var eff = MentalEffect.CreateWithIcon(pos, IconId.CStress, G.Ticks,
                    Consts.TicksPer3Days, T.MoodCrewMemberSold, -6);
                s.Sig.AddMentalEffect.Send(bd, eff);
            }
        }

        private static bool HasBond(BeingData who, uint aboutMindId, BeingOpinionFlags bond) {
            if (aboutMindId == 0 || who.Mind.Bonds == null) { return false; }
            return who.Mind.Bonds.Opinions.TryGetValue(aboutMindId, out var op)
                && (op.Flags & bond) == bond;
        }

        public static void Spawn(GameState s, TradeableItem item, int pos) {
            if (item.Being == null) {
                D.Err("Trying to spawn a being offer that was never generated: {0}", item);
                return;
            }
            var b = s.Beings.Create(pos, item.Being);
            if (b != null) {
                BeingUtils.ShowColonyGreetingFor(s, b, null);
            }
        }
    }
}
