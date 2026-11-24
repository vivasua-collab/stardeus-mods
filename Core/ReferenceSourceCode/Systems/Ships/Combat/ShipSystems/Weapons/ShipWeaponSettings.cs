using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Utils;
using KL.Objects;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Ships {
    /// <summary>
    /// If anything containing GameState would ever get added here, it needs to
    /// be cleaned up in Reset(), or it will memory leak!
    /// </summary>
    public sealed class ShipWeaponSettings {
        public bool IsReadOnly;
        public ShipWeaponFireMode FireMode;
        /// <summary>
        /// How much flux (percent) are we allowed to have AFTER shooting this weapon?
        /// </summary>
        public float ShootFluxThreshold;
        public Dictionary<ShipWeaponTargetTypeDef, int> AutoTargetPriorities;
        public const int TargetPriorityDefault = 3;

        public int GetPriorityFor(ShipWeaponTargetTypeDef type) {
            if (AutoTargetPriorities == null) {
                return TargetPriorityDefault;
            }
            if (AutoTargetPriorities.TryGetValue(type, out var prio)) {
                return prio;
            }
            return TargetPriorityDefault;
        }
        public void SetPriorityFor(ShipWeaponTargetTypeDef type, int prio) {
            if (prio == TargetPriorityDefault) {
                AutoTargetPriorities?.Remove(type);
                if (AutoTargetPriorities != null && AutoTargetPriorities.Count == 0) {
                    AutoTargetPriorities = null;
                }
                return;
            }
            AutoTargetPriorities ??= new();
            AutoTargetPriorities[type] = prio;
        }

        public static ShipWeaponSettings Copy(ShipWeaponSettings from) {
            if (from == null) {
                return null;
            }
            var s = new ShipWeaponSettings();
            s.CopyFrom(from);
            return s;
        }

        public bool CopyFrom(ShipWeaponSettings from) {
            if (Equals(from)) { return false; }
            FireMode = from.FireMode;
            ShootFluxThreshold = from.ShootFluxThreshold;
            AutoTargetPriorities = Arrays.CopyCollection(from.AutoTargetPriorities);
            return true;
        }

        public static ShipWeaponSettings Default {
            get {
                return new ShipWeaponSettings() {
                    FireMode = ShipWeaponFireMode.Automatic,
                    ShootFluxThreshold = 0.95f,
                };
            }
        }
        public static ShipWeaponSettings DefaultReadOnly {
            get {
                var settings = Default;
                settings.IsReadOnly = true;
                return settings;
            }
        }

        public string FireModeName {
            get {
                switch (FireMode) {
                    case ShipWeaponFireMode.Automatic: return T.Auto;
                    case ShipWeaponFireMode.Manual: return T.Manual;
                    default: {
                        D.Err("Unknown fire mode: {0}", FireMode);
                        return FireMode.ToString();
                    }
                }
            }
        }

        public override bool Equals(object obj) {
            if (obj is not ShipWeaponSettings other) {
                return false;
            }

            if (FireMode != other.FireMode) {
                return false;
            }
            if (!Mathf.Approximately(ShootFluxThreshold, other.ShootFluxThreshold)) {
                return false;
            }
            // default version sucks balls
            // var comp = EqualityComparer<Dictionary<ShipWeaponTargetTypeDef, int>>.Default;
            var comp = new Equality.DictionaryComparer<ShipWeaponTargetTypeDef, int>();
            if (!comp.Equals(AutoTargetPriorities, other.AutoTargetPriorities)) {
                return false;
            }
            return true;
        }

        public override int GetHashCode() {
            int hash = FireMode.GetHashCode();
            hash = (hash * 397) ^ ShootFluxThreshold.GetHashCode();
            hash = (hash * 397) ^ (AutoTargetPriorities?.GetHashCode() ?? 0);
            return hash;
        }
    }
}
