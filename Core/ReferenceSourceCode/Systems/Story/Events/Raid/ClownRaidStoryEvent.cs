using System.Collections.Generic;
using Game.Commands;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.CodeGen;
using Game.Systems.AI.Combat;
using Game.Systems.Creatures;
using Game.Systems.Items;
using Game.Systems.Naming;
using Game.Systems.Notify;
using Game.Systems.Ships;
using Game.Systems.Trade;
using Game.Systems.Voice;
using Game.Utils;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Story {
    public sealed class ClownRaidStoryEvent : StoryEvent {
        public const string EventId = "ClownRaid";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new ClownRaidStoryEvent());
        }

        private const int MinClowns = 3;
        private const int MaxClowns = 5;
        private const float HatChance = 0.7f;

        public override bool CheckConditions(StoryContext ctx, StoryEventDef def) {
            if (!A.IsCombatOn || A.IsInHyperspace) {
                return false;
            }
            if (TradeUI.FindTradePortal(ctx.S) == null) {
                return false;
            }
            return HasNonAlliedShipFaction(ctx);
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            var s = ctx.S;
            var portal = TradeUI.FindTradePortal(s);
            if (portal == null) {
                data.FailEvent(s, "No active trade portal to spawn clown raiders",
                    isHardFailure: false);
                return;
            }
            var faction = PickFaction(ctx, data);
            if (faction == null) {
                data.FailEvent(s, "No non-allied faction ships in the sector anymore",
                    isHardFailure: false);
                return;
            }
            var rng = data.SeededRng;
            var outPort = portal.Tile.Transform.OutPort;
            var clowns = new List<Being>();
            for (int i = 0; i < rng.Range(MinClowns, MaxClowns + 1); i++) {
                var vars = new GenBeingVars(Species.Get(SpeciesIdH.Human), BeingType.Enemy);
                vars.Faction = faction;
                vars.Body = faction.GenBodyVars;
                var bData = BeingUtils.GenBeingWithInventory(vars, rng,
                    GenItemFlags.Weaponized);
                BeingUtils.EnsureCapableOf(bData, JobTypeIdH.Melee);
                if (bData.Mind.Jobs.TryJob(JobTypeIdH.Shooting, out _)) {
                    BeingUtils.EnsureCapableOf(bData, JobTypeIdH.Shooting);
                }
                RaiderWeapons.EnsureArmed(s, bData, rng, GenItemFlags.Weaponized);
                EquipClownGear(s, bData, rng);
                var cmd = new CmdSpawnBeing(bData,
                    EntityUtils.CenterOf(outPort) + Rng.UInsideUnitCircle() * 0.5f);
                cmd.Execute(s);
                if (cmd.SpawnedBeing == null) {
                    D.Err("Failed to spawn a clown at trade portal: {0}", data);
                    continue;
                }
                clowns.Add(cmd.SpawnedBeing);
            }
            if (clowns.Count == 0) {
                data.FailEvent(s, "Failed to spawn any clown raiders",
                    isHardFailure: A.IsCombatOn);
                return;
            }
            s.Sys.Combat.StartRaidBy(faction, clowns);
            data.Faction = faction;
            data.CreateLogEntry(IconId.CJester, T.EvRaidClownTitle,
                T.EvRaidClownDesc_V1.F(Texts.LinkToTile(portal.Tile)));
            var pos = EntityUtils.CenterOf(outPort);
            data.AddEventNotification(ctx, data.LogEntry.Icon, data.LogEntry.Title,
                NotificationType.Alert, showPopup: false, showFanfare: true,
                addLogLine: true,
                onClick: () => EntityUtils.CameraFocusOn(pos, Consts.V2One),
                area: (pos, Consts.V2One * 5f), voice: VoiceLine.Boarding);
            foreach (var clown in clowns) {
                data.AddParticipant(clown.Data);
            }
            data.SetState(s, StoryEventState.Ending);
        }

        private FactionDef PickFaction(StoryContext ctx, StoryEventData data) {
            var ships = ctx.S.Query.NPCShipsAt.Ask(ctx.CurrentSector);
            if (ships == null) {
                return null;
            }
            var pool = Caches.FactionList;
            pool.Clear();
            foreach (var ship in ships) {
                var f = ship.Faction;
                if (f == null || f.IsBlank || pool.Contains(f)) { continue; }
                if (IsUsableFaction(ctx.S, f)) {
                    pool.Add(f);
                }
            }
            if (pool.Count == 0) {
                return null;
            }
            return data.SeededRng.From(pool);
        }

        private static bool HasNonAlliedShipFaction(StoryContext ctx) {
            var ships = ctx.S.Query.NPCShipsAt.Ask(ctx.CurrentSector);
            if (ships == null) {
                return false;
            }
            foreach (var ship in ships) {
                if (IsUsableFaction(ctx.S, ship.Faction)) {
                    return true;
                }
            }
            return false;
        }

        private static bool IsUsableFaction(GameState s, FactionDef faction) {
            if (faction == null || faction.IsBlank || faction.IsPlayer
                    || faction.IsInternal) {
                return false;
            }
            return !s.Sys.Diplomacy.IsAlliedWithPlayer(faction);
        }

        private static void EquipClownGear(GameState s, BeingData bData, Rng rng) {
            var inventory = bData.Inventory;
            if (inventory == null) {
                return;
            }
            var clownDef = The.Defs.FastGet(DefIdsH.ObjEquipmentClothingClownCostume01);
            if (inventory.Equipment.TryGetValue(InventorySlotIdH.Clothing, out var clothing)) {
                EquipInSlot(s, clothing, clownDef, rng);
            }
            if (rng.Chance(HatChance)
                    && inventory.Equipment.TryGetValue(InventorySlotIdH.Hat, out var hat)) {
                EquipInSlot(s, hat,
                    The.Defs.FastGet(DefIdsH.ObjEquipmentHatsJesterHat), rng);
            }
            inventory.UpdateLooks();
        }

        private static void EquipInSlot(GameState s, InventoryItemSlot slot, Def def, Rng rng) {
            if (def == null) {
                D.Err("Trying to equip clown gear with a null def!");
                return;
            }
            var obj = s.Query.GenObjFull.Ask(def, rng);
            if (obj == null) {
                D.Err("Failed to generate clown gear obj: {0}", def);
                return;
            }
            slot.Set(obj);
            slot.ApplyItemEffects();
        }
    }
}
