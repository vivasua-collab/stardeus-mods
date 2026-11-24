using System.Collections.Generic;
using Game.CodeGen;
using Game.Data;
using Game.Data.Factions;
using Game.Systems.Creatures;
using Game.Systems.Items;
using KL.Randomness;
using KL.Utils;

namespace Game.Scenarios {
    // The starting crew holds the scenario's starting weapons (Scenario.StartingWeapons) from
    // the first tick, each weapon in its own hands. They used to lie somewhere on the floor,
    // and since the Weapon slot policy defaults to Manual nobody picked them up: a new player
    // met the first raid with an unarmed bot crew and lost it.
    //
    // CanHold and Assign are shared with the crew setup (StartingCrewJobs), which rerolls a
    // whole-crew roll until each weapon has a holder, so setup and generation cannot disagree.
    public static class StartingCrewWeapons {
        public static List<InventoryItemDef> ItemsFor(string[] weaponIds) {
            var items = new List<InventoryItemDef>();
            if (weaponIds == null) { return items; }
            foreach (var id in weaponIds) {
                var def = The.Defs.TryGet(id);
                if (def == null || !InventoryItemDef.TryForDef(def.IdH, out var item)
                        || item.SlotType == null) {
                    D.Err("Starting weapon is not an inventory item: {0}", id);
                    continue;
                }
                items.Add(item);
            }
            return items;
        }

        // The body and the law, leaving out the mind: a slot for the item, hands to use it
        // (UseItem), and a frame the item is allowed for. hasHands comes from the caller,
        // because an unspawned setup crew only knows it through GenBeingAbilities. The
        // starting crew is always the player's, and an unspawned one has no effective faction
        // to read yet.
        public static bool BodyCanHold(BeingData data, InventoryItemDef item, bool hasHands) {
            if (!hasHands || data.Inventory == null) { return false; }
            if (!data.Inventory.Equipment.ContainsKey(item.SlotType.IdH)) { return false; }
            return item.IsAllowedFor(data.Species, FactionDef.Player);
        }

        public static bool CanHold(BeingData data, InventoryItemDef item, bool hasHands) {
            if (!BodyCanHold(data, item, hasHands)) { return false; }
            return item.RequiredJobId == 0 || data.Mind?.Jobs?.CanDo(item.RequiredJobId) == true;
        }

        // One holder per weapon, null where nobody is left for it. The weapon the fewest can
        // hold picks first, so someone who could hold either is not taken by the weapon others
        // could hold too. Each weapon goes to the best at its job (Shooting, Melee); ties keep
        // the crew order.
        public static BeingData[] Assign(List<(BeingData Data, bool HasHands)> crew,
                List<InventoryItemDef> weapons) {
            var holders = new BeingData[weapons.Count];
            var candidates = new int[weapons.Count];
            var order = new List<int>(weapons.Count);
            for (int w = 0; w < weapons.Count; w++) {
                foreach (var c in crew) {
                    if (CanHold(c.Data, weapons[w], c.HasHands)) {
                        candidates[w]++;
                    }
                }
                order.Add(w);
            }
            // stable: equal counts keep the scenario's weapon order
            order.Sort((a, b) => candidates[a] != candidates[b]
                ? candidates[a].CompareTo(candidates[b]) : a.CompareTo(b));
            var taken = new HashSet<BeingData>();
            foreach (var w in order) {
                var weapon = weapons[w];
                BeingData best = null;
                int bestLevel = int.MinValue;
                foreach (var c in crew) {
                    if (taken.Contains(c.Data) || !CanHold(c.Data, weapon, c.HasHands)) {
                        continue;
                    }
                    var level = weapon.RequiredJobId == 0
                        ? 0 : c.Data.Mind.Jobs.LevelOf(weapon.RequiredJobId);
                    if (level > bestLevel) {
                        best = c.Data;
                        bestLevel = level;
                    }
                }
                holders[w] = best;
                if (best != null) {
                    taken.Add(best);
                }
            }
            return holders;
        }

        // After generation, once, on the ship that was kept (like StartingJetSuit). Beings that
        // already hold something in the slot are left alone, and stasis sleepers (hidden) are
        // not crew yet. A weapon nobody can hold is left next to the crew, so it is not lost.
        public static void EquipCrew(GameState s, string[] weaponIds, Rng rng) {
            if (Arrays.IsEmpty(weaponIds)) { return; }
            var weapons = ItemsFor(weaponIds);
            var crew = new List<(BeingData Data, bool HasHands)>();
            foreach (var b in s.Beings.All) {
                if (!b.IsAlive || b.IsHidden || !b.Data.IsPlayer) { continue; }
                if (b.Data.Inventory?.Equipment.TryGetValue(InventorySlotIdH.Weapon,
                        out var held) == true && held.Item != null) {
                    continue;
                }
                crew.Add((b.Data, b.Data.HasAbility(AbilityIdH.UseItem)));
            }
            var holders = Assign(crew, weapons);
            for (int w = 0; w < weapons.Count; w++) {
                var weapon = weapons[w];
                var obj = s.Query.GenObjFull.Ask(weapon.Def, rng);
                if (obj == null) { continue; }
                var holder = holders[w];
                if (holder != null) {
                    // Equipped items are saved by their object id, which generation does
                    // not give
                    s.Query.PersistObjData.Ask(obj);
                    holder.Inventory.Equipment[weapon.SlotType.IdH].Equip(obj);
                    continue;
                }
                if (crew.Count == 0) {
                    D.Warn("No starting crew to hold or keep {0}", weapon.Def);
                    continue;
                }
                D.Warn("Nobody in the starting crew can hold {0}, it is left next to {1}",
                    weapon.Def, crew[0].Data);
                s.Objs.Create(crew[0].Data.Being.Position, obj);
            }
        }
    }
}
