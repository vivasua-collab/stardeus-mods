using System;
using System.Collections.Generic;
using System.Linq;
using Game.CodeGen;
using Game.Constants;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Ships {
    [Serializable]
    public sealed class ShipWeaponTargetTypeDef {
        public string Id;
        public string GroupId;
        public int IdH;
        public string Icon;
        public string NameKey;
        public string GroupNameKey;
        public string NameT;
        public string GroupNameT;
        public bool NonShipTarget;
        public bool SkipDuringShipCombat;
        public string[] DefaultTargetByWeaponTypes;
        public ShipWeaponType[] DefaultTargetBy;
        public static Dictionary<int, ShipWeaponTargetTypeDef> All = new();
        public static ShipWeaponTargetTypeDef ShipHull;

        [ExecuteOnReload]
        private static void CleanupStatics() {
            All.Clear();
        }
        [SelfInit(SelfInit.Stage.Finalize)]
        private static void Init() {
            LoadKnown();
        }

        public static ShipWeaponTargetTypeDef Get(string id) {
            if (!All.TryGetValue(Hashes.S(id), out var def)) {
                D.Err("ShipWeaponTargetTypeDef not found by id: {0}", id);
                D.Err("Available: {0}", string.Join(", ",
                    All.Values.Select(v => v.Id)));
                def = All.Values.FirstOrDefault();
            }
            return def;
        }
        public static ShipWeaponTargetTypeDef FastGet(int idH) {
            if (!All.TryGetValue(idH, out var def)) {
                D.Err("ShipWeaponTargetTypeDef not found by id hash: {0}", idH);
                def = All.Values.FirstOrDefault();
            }
            return def;
        }

        private static void LoadKnown() {
            if (All.Count > 0) { return; }
            var types = The.ModLoader.LoadObjects<ShipWeaponTargetTypeDef>(
                "Config/Ships/Devices/WeaponTargetTypes");
            foreach (var type in types) {
                type.IdH = Hashes.S(type.Id);

                if (type.IdH == ShipWeaponTargetTypeIdH.ShipHull) {
                    ShipHull = type;
                }

                var nameKey = type.NameKey ?? $"weapon.target.type.{type.Id.ToLowerInvariant()}";
                type.NameT = nameKey.T();
                var gNameKey = type.GroupNameKey ?? $"weapon.target.group.{type.GroupId.ToLowerInvariant()}";
                type.GroupNameT = gNameKey.T();

                if (type.Icon == null) {
                    D.Err("ShipWeaponTargetTypeDef has no Icon!: {0}", type.Id);
                    type.Icon = IconId.CWarning;
                }

                if (type.DefaultTargetByWeaponTypes == null) {
                    D.Err("ShipWeaponTargetTypeDef has no DefaultTargetByWeaponTypes!: {0}", type.Id);
                    type.DefaultTargetBy = new ShipWeaponType[0];
                } else {
                    type.DefaultTargetBy = Enums.FromStringArray<ShipWeaponType>(
                        type.DefaultTargetByWeaponTypes, type);
                }

                if (!All.TryAdd(type.IdH, type)) {
                    D.Err("ShipWeaponTargetTypeDef with such ID already exists: {0}", type.Id);
                    continue;
                }
            }
        }

        public static ShipWeaponTargetTypeDef For(IShipWeaponTarget target) {
            if (target == null) { return ShipHull; }
            return target.TargetType;
        }

        public override string ToString() {
            return $"ShipWeaponTargetType[{Id}]";
        }
    }
}
