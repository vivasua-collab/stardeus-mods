using System.Collections.Generic;
using Game.CodeGen;
using Game.Data;
using Game.Data.Factions;
using Game.Systems.Creatures;
using Game.Systems.Items;
using Game.Systems.Naming;
using KL.Randomness;
using UnityEngine;

namespace Game.Systems.AI.Combat {
    /// <summary>
    /// Generates the raiders of one raid, one at a time, each under a cost cap the raid sets
    /// (CombatRaid.RaiderCostCap). A raider is drawn whole - species, body and kit together, so
    /// a weapon and the suit it came with stay a pair - priced with StrengthOf before it
    /// spawns, and drawn again while it costs more than the cap. That is how the popular colony
    /// sim picks raiders: only options under the cap are drawn, so a small raid can't field a
    /// sniper whatever the colony's wealth.
    ///
    /// A human's price is mostly its kit, so a rejected human keeps its species and only its
    /// kit is drawn again, from lower gear tiers as the draws go on. Without that, every
    /// rejected kit would hand the slot to a cheap bot, and a human faction would turn into a
    /// bot raid at low points. A bot's price is its body, so a bot rejected a few times in a
    /// row leaves this raid's pool and the tier cascade in RaiderSpecies falls to the bots of
    /// the tier below.
    /// </summary>
    public sealed class RaiderPicker {
        private const int MaxDraws = 8;
        // the gear tier flags drop one tier after this many draws
        private const int DrawsPerGearTier = 3;
        private const int BotRejectionsToLeavePool = 3;

        private readonly GameState S;
        private readonly Rng rng;
        private readonly FactionDef faction;
        private readonly int gearTier;
        private readonly GenBeingBodyVars bodyVars;
        private readonly bool forceHumans;
        private readonly bool forceFlyingOnly;
        private readonly HashSet<int> exceptNames = new();
        private readonly Dictionary<Species, float> speciesPool;
        private readonly Dictionary<Species, float> flyingBotPool;
        private readonly HashSet<Species> leftPool = new();
        private readonly Dictionary<Species, int> rejectionsInARow = new();

        /// <summary>Raiders generated, accepted or not.</summary>
        public int Draws;
        public int Rejections;
        /// <summary>Raiders taken over the cap because none of their draws fit.</summary>
        public int OverCap;
        /// <summary>Draws rejected for armor too good for their weapon (IsCoherentKit).</summary>
        public int Incoherent;

        public RaiderPicker(GameState S, Rng rng, FactionDef faction, int gearTier,
                GenBeingBodyVars bodyVars, bool forceHumans, bool forceFlyingOnly) {
            this.S = S;
            this.rng = rng;
            this.faction = faction;
            this.gearTier = gearTier;
            this.bodyVars = bodyVars;
            this.forceHumans = forceHumans;
            this.forceFlyingOnly = forceFlyingOnly;
            // The species pool: bots by this raid's gear tier, humans by the faction's AI
            // share (see RaiderSpecies). A flying raid also needs a bot-only pool to fall back
            // on for a human that could not roll a jet suit.
            if (!forceHumans) {
                speciesPool = new Dictionary<Species, float>();
                flyingBotPool = forceFlyingOnly ? new Dictionary<Species, float>() : null;
                FillPools();
            }
        }

        /// <summary>
        /// The next raider, ready to spawn, costing at most maxCost with a coherent kit unless
        /// none of its draws fit (then the cheapest of them, a coherent one if any).
        /// withinBudget gives it the raid's gear tier; a MinRaiders top-up past the budget
        /// rolls plain gear.
        /// </summary>
        public BeingData Pick(float maxCost, bool withinBudget) {
            var species = DrawSpecies();
            BeingData fallback = null;
            var fallbackCoherent = false;
            var fallbackPrice = float.MaxValue;
            for (int draw = 0; draw < MaxDraws; draw++) {
                var tier = withinBudget ? gearTier - GearTierDrop(species, draw) : -1;
                var data = Generate(species, tier);
                Draws++;
                var price = CombatRaid.StrengthOf(data, unspawned: true);
                var body = data.Species;
                var coherent = IsCoherentKit(data);
                if (price <= maxCost && coherent) {
                    rejectionsInARow.Remove(body);
                    return data;
                }
                Rejections++;
                if ((coherent && !fallbackCoherent)
                        || (coherent == fallbackCoherent && price < fallbackPrice)) {
                    fallback = data;
                    fallbackCoherent = coherent;
                    fallbackPrice = price;
                }
                if (!coherent) {
                    // the kit, not the body: the next draw rolls another one
                    Incoherent++;
                    continue;
                }
                if (!body.IsBiological && RejectBot(body)) {
                    species = DrawSpecies();
                }
            }
            OverCap++;
            return fallback;
        }

        /// <summary>
        /// Armor no more than one tier above the weapon it comes with. The cost cap alone
        /// rejects a rifle in a Combat Suit but lets a knife through in one, since a cheap
        /// weapon leaves room for an expensive suit: early raids then wore Combat Suits and
        /// Assault Armor with Riot Shields, and dropped them as loot. The popular colony sim
        /// keeps raider kits coherent by giving each kind of raider a weapon and an apparel
        /// budget that grow together.
        /// </summary>
        public static bool IsCoherentKit(BeingData data) {
            var inventory = data.Inventory;
            if (inventory == null) { return true; }
            var weaponTier = 0;
            if (inventory.Equipment.TryGetValue(InventorySlotIdH.Weapon, out var weapon)
                    && weapon.Item != null) {
                weaponTier = WeaponTier(weapon.Item);
            }
            foreach (var slot in inventory.Equipment.Values) {
                var item = slot.Item;
                if (item == null) { continue; }
                var slotId = slot.Type.IdH;
                if (slotId != InventorySlotIdH.Suit && slotId != InventorySlotIdH.Helmet) {
                    continue;
                }
                if (ArmorTier(item) > weaponTier + 1) { return false; }
            }
            return true;
        }

        // The research tier the weapon is unlocked at, from 0: Knife, Nail Gun and Riot Shield
        // 0, Machete, SMG, Handgun and Sniper Rifle 1, Energy Rifle 2, Vibroblade 3
        private static int WeaponTier(InventoryItemDef weapon) {
            return Mathf.Max(0, (weapon.Def?.Tier ?? 0) - 1);
        }

        // Suits and helmets are not on the research tree, so their tier is their rank by armor
        // among the gear raiders can roll for that slot: Space Suit 0, Space Suit 02 1, Combat
        // Suit 2, Assault Armor 3
        private static int ArmorTier(InventoryItemDef item) {
            var armor = ArmorOf(item);
            if (armor <= 0f) { return 0; }
            var tier = 0;
            foreach (var other in InventoryItemDef.All.Values) {
                if (other.SlotType != item.SlotType || other.Rarity <= 0f) { continue; }
                if (ArmorOf(other) < armor) { tier++; }
            }
            return tier;
        }

        private static float ArmorOf(InventoryItemDef item) {
            var mods = item.StatModifiers;
            if (mods == null) { return 0f; }
            float armor = 0f;
            for (int i = 0; i < mods.Length; i++) {
                var id = mods[i].Item1;
                if (id == StatIdH.ArmorSharp || id == StatIdH.ArmorPiercing
                        || id == StatIdH.ArmorBlunt || id == StatIdH.ArmorEnergy) {
                    armor += mods[i].Item2.Value;
                }
            }
            return armor;
        }

        // A human redraws its kit a few times per gear tier. A bot steps down a tier with each
        // rejection, so a late bot body gets to try plainer weapons before it leaves the pool:
        // all three tries at the top tier priced a Virus Sentry out of raids it can afford.
        private int GearTierDrop(Species drawn, int draw) {
            if (!drawn.IsBiological) {
                rejectionsInARow.TryGetValue(drawn, out var rejected);
                return rejected;
            }
            return draw / DrawsPerGearTier;
        }

        // True when the bot left the pool, and the slot needs another species
        private bool RejectBot(Species bot) {
            rejectionsInARow.TryGetValue(bot, out var n);
            n++;
            rejectionsInARow[bot] = n;
            if (n < BotRejectionsToLeavePool || forceHumans) { return false; }
            leftPool.Add(bot);
            FillPools();
            return true;
        }

        private void FillPools() {
            RaiderSpecies.WeightedFor(faction, gearTier, forceFlyingOnly, speciesPool, leftPool);
            if (flyingBotPool != null) {
                RaiderSpecies.BotsOnlyFor(gearTier, true, flyingBotPool, leftPool);
            }
        }

        private Species DrawSpecies() {
            if (forceHumans) {
                return Species.Get(SpeciesIdH.Human);
            }
            if (rng.TryWeightedFrom(speciesPool, out var species)) {
                return species;
            }
            return faction.AI < 1f ? Species.Get(SpeciesIdH.Human) : Species.Get(SpeciesIdH.Drone);
        }

        // One whole raider of the drawn species, its kit rolled at the given gear tier (below
        // 0: plain gear). Not spawned yet.
        public BeingData Generate(Species species, int tier) {
            // Every raider is armed (see RaiderWeapons); the gear tier only decides how well.
            // It biases the draw toward better gear (see GenItemFlags.WellArmed/EliteArmed and
            // ObjsSys.GenerateInventoryItem).
            var equipment = GenItemFlags.SpaceReady | GenItemFlags.Casual
                | GenItemFlags.Weaponized;
            if (tier >= 2) {
                equipment |= GenItemFlags.EliteArmed;
            } else if (tier >= 1) {
                equipment |= GenItemFlags.WellArmed;
            }

            // The faction goes in before the gear: which weapons a robot may roll is
            // up to its faction's law (RobotWeaponLaw), Virus robots carry anything
            var vars = new GenBeingVars(species, BeingType.Enemy);
            vars.Body = bodyVars;
            vars.ExceptNames = exceptNames;
            vars.Faction = faction;
            if (IsPilotedHuman(species)) {
                vars = PilotedHumanVars(species, bodyVars, exceptNames);
                vars.Faction = faction;
                species = vars.Species;
            }
            if (forceFlyingOnly && species.IsBiological) {
                // A human can only fly in from the map edge in a jet suit.
                equipment |= GenItemFlags.JetSuit;
            }
            var data = BeingUtils.GenBeingWithInventory(vars, rng, equipment);

            if (forceFlyingOnly && !BeingUtils.EnsureFlying(data)
                    && !data.HasAbility(AbilityIdH.Fly) && !forceHumans) {
                // No jet suit could be rolled (a mod may gate them all), so this slot goes
                // to a flying bot instead.
                if (!rng.TryWeightedFrom(flyingBotPool, out species)) {
                    species = Species.Get(SpeciesIdH.Drone);
                }
                equipment &= ~GenItemFlags.JetSuit;
                vars = new GenBeingVars(species, BeingType.Enemy);
                vars.Body = bodyVars;
                vars.ExceptNames = exceptNames;
                vars.Faction = faction;
                data = BeingUtils.GenBeingWithInventory(vars, rng, equipment);
                BeingUtils.EnsureFlying(data);
            }

            BeingUtils.EnsureCapableOf(data, JobTypeIdH.Melee);
            // Before the Shooting fix below, so a ranged weapon rolled here gets it too.
            RaiderWeapons.EnsureArmed(S, data, rng, equipment);
            // A raider handed a ranged weapon must be able to shoot it: SelectAttackMethod
            // ignores ranged attacks for a being that can't do the Shooting job, so such a
            // raider fights with its fists and StrengthOf prices it at fist level - the
            // gear tiers (which weight rifles up) were then buying nothing.
            var hasRangedWeapon = data.Inventory != null
                && data.Inventory.Equipment.TryGetValue(InventorySlotIdH.Weapon, out var weapon)
                && weapon.Item?.Kind == WeaponKind.Ranged;
            if (hasRangedWeapon || data.Mind.Jobs.TryJob(JobTypeIdH.Shooting, out _)) {
                BeingUtils.EnsureCapableOf(data, JobTypeIdH.Shooting);
            }
            data.Mind.SetFaction(faction);
            data.Mind.CombatConfig = CombatUnitConfig.Fight;
            return data;
        }

        // A bot draw of an all-bot faction may come as a human body run by the bot's mind
        // (FactionDef.PilotedHumanShare). In a flying raid it needs a jet suit like any human
        // raider, and without one the slot falls back to a flying bot.
        private bool IsPilotedHuman(Species drawn) {
            if (forceHumans || gearTier < 1) { return false; }
            if (drawn.IsBiological || faction.PilotedHumanShare <= 0f) { return false; }
            return rng.Chance(faction.PilotedHumanShare);
        }

        // The bot mind keeps its own name and needs; the body comes heavily modded, with
        // robotic eyes, limbs and implants from the bot side of the faction.
        private static GenBeingVars PilotedHumanVars(Species botMind,
                GenBeingBodyVars bodyVars, HashSet<int> exceptNames) {
            var vars = new GenBeingVars(Species.Get(SpeciesIdH.Human), BeingType.Enemy);
            vars.MindSpecies = botMind;
            bodyVars.ChanceModded = 1f;
            bodyVars.ChanceBionic = Mathf.Max(bodyVars.ChanceBionic, 0.7f);
            bodyVars.ChanceImplant = Mathf.Max(bodyVars.ChanceImplant, 0.5f);
            bodyVars.ChanceCustomMind = 0f;
            vars.Body = bodyVars;
            vars.ExceptNames = exceptNames;
            return vars;
        }
    }
}
