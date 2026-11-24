using System;
using System.Collections.Generic;
using System.Linq;
using Game.CodeGen;
using Game.Data;
using Game.Rendering;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Ships {
    [Serializable]
    public sealed class ShipDeviceDef {
        public string Id;
        public int IdH;
        public string DefId;
        [NonSerialized] public Def Def;
        public string ZoneSpriteId;
        public string ZoneSpriteId_D;
        public string ZoneSpriteId_U;
        public string ZoneSpriteId_L;
        public string ZoneSpriteId_R;
        // matches Facing.Type enum idx: D, L, U, R
        public Sprite[] ZoneSprites { get; private set; }
        public float ZoneSpriteScale;
        public float Rarity;
        public string TargetTypeId;
        public float EnergyCostPassive;
        public float EnergyCostActive;
        public ShipWeaponTargetTypeDef TargetType;
        public int HP;
        public int Tier;
        /// <summary>
        /// How many class points does this device cost to have
        /// </summary>
        public int ClassPoints;

        // Passive .. active draw for the codex. activeMulMin/Max scale the active cost
        // (shields scale it with ship size)
        public void AddDefPower(ref DefPower power, float activeMulMin = 1f,
                float activeMulMax = 1f) {
            var activeMin = EnergyCostActive * activeMulMin;
            var activeMax = EnergyCostActive * activeMulMax;
            var min = EnergyCostPassive > 0f
                ? Mathf.Min(EnergyCostPassive, activeMin)
                : activeMin;
            power.AddConsumption(min, Mathf.Max(EnergyCostPassive, activeMax));
        }

        public static Dictionary<int, ShipDeviceDef> All = new();

        [ExecuteOnReload]
        private static void CleanupStatics() {
            All.Clear();
        }
        [SelfInit(SelfInit.Stage.Finalize)]
        private static void Init() {
            LoadKnown();
        }

        public static ShipDeviceDef Get(string id) {
            if (!All.TryGetValue(Hashes.S(id), out var def)) {
                D.Err("ShipDeviceDef not found by id: {0}", id);
                D.Err("Available: {0}", string.Join(", ",
                    All.Values.Select(v => v.Id)));
                def = All.Values.FirstOrDefault();
            }
            return def;
        }
        public static ShipDeviceDef FastGet(int idH) {
            if (!All.TryGetValue(idH, out var def)) {
                D.Err("ShipDeviceDef not found by id hash: {0}", idH);
                def = All.Values.FirstOrDefault();
            }
            return def;
        }

        private static void LinkDefs() {
            List<ShipDeviceDef> broken = null;
            foreach (var w in All.Values) {
                w.Def = The.Defs.Get(w.DefId);
                if (w.Def == null) {
                    D.Err("Incorrectly linked device def: {0}, DefId not found: {1}",
                        w.Id, w.DefId);
                    broken ??= new();
                    broken.Add(w);
                }
            }
            if (broken != null) {
                foreach (var w in broken) {
                    All.Remove(w.IdH);
                }
                D.Err("Removed {0} broken device defs: {1}", broken.Count,
                    D.DumpCollection(broken));
            }
            foreach (var def in All.Values) {
                if (def.TargetTypeId == null) {
                    D.Err("ShipDeviceDef has not defined TargetTypeId! {0}",
                        def.Id);
                } else {
                    def.TargetType = ShipWeaponTargetTypeDef.Get(def.TargetTypeId);
                    if (def.TargetType == null) {
                        D.Err("ShipDeviceDef has missing TargetTypeId! {0}: {1}",
                            def.TargetTypeId, def.Id);
                    }
                }
            }
        }

        private static void LoadKnown() {
            if (All.Count > 0) { return; }
            var types = The.ModLoader.LoadObjects<ShipDeviceDef>(
                "Config/Ships/Devices/Data");
            foreach (var type in types) {
                type.IdH = Hashes.S(type.Id);

                LinkZoneSprites(type);

                if (type.HP <= 0) {
                    D.Err("ShipDeviceDef has no HP <= 0: {0}", type.Id);
                    type.HP = 100;
                }
                if (type.Rarity <= 0f) {
                    D.Err("ShipDeviceDef has Rarity <= 0: {0}", type.Id);
                    type.Rarity = 1f;
                }
                if (!All.TryAdd(type.IdH, type)) {
                    D.Err("ShipDeviceDef with such ID already exists: {0}", type.Id);
                    continue;
                }
            }
            Ready.When(WhenH.CoreLoaded1, LinkDefs);
        }

        private static void LinkZoneSprites(ShipDeviceDef type) {
            type.ZoneSprites = new Sprite[4];
            if (RenderingService.Sprites.TryFind(type.ZoneSpriteId_D ?? type.ZoneSpriteId, out var sd)) {
                type.ZoneSprites[0] = sd.Sprite;
            } else {
                D.Err("Can't find ZoneSpriteId_D ({1}) or ZoneSpriteID ({2}) for ship device: {0}",
                    type.Id, type.ZoneSpriteId_D, type.ZoneSpriteId);
            }
            if (RenderingService.Sprites.TryFind(type.ZoneSpriteId_L ?? type.ZoneSpriteId, out sd)) {
                type.ZoneSprites[1] = sd.Sprite;
            } else {
                D.Err("Can't find ZoneSpriteId_L ({1}) or ZoneSpriteID ({2}) for ship device: {0}",
                    type.Id, type.ZoneSpriteId_L, type.ZoneSpriteId);
            }
            if (RenderingService.Sprites.TryFind(type.ZoneSpriteId_U ?? type.ZoneSpriteId, out sd)) {
                type.ZoneSprites[2] = sd.Sprite;
            } else {
                D.Err("Can't find ZoneSpriteId_U ({1}) or ZoneSpriteID ({2}) for ship device: {0}",
                    type.Id, type.ZoneSpriteId_U, type.ZoneSpriteId);
            }
            if (RenderingService.Sprites.TryFind(type.ZoneSpriteId_R ?? type.ZoneSpriteId, out sd)) {
                type.ZoneSprites[3] = sd.Sprite;
            } else {
                D.Err("Can't find ZoneSpriteId_R ({1}) or ZoneSpriteID ({2}) for ship device: {0}",
                    type.Id, type.ZoneSpriteId_R, type.ZoneSpriteId);
            }
        }
    }
}
