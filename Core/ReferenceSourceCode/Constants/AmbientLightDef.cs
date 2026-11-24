using System;
using Game.Utils;
using Game.CodeGen;
using UnityEngine;
using System.Collections.Generic;
using KL.Utils;

namespace Game.Constants {
    public class AmbientLightDef {
        public static readonly Dictionary<string, AmbientLightDef> All = new();
        public static AmbientLightDef Default;
        public static void LoadKnown() {
            if (All.Count == 0) {
                var all = The.ModLoader.LoadObjects<AmbientLightDef>("Config/Visuals/AmbientLight");
                foreach (var amb in all) {
                    if (!All.TryAdd(amb.Id, amb)) {
                        D.Err("Trying to add AmbientLight more than once! {0} ({1})", amb.Id, amb.NameKey);
                    } else {
                        if (amb.Id == Consts.AmbientLightColorDefault) {
                            Default = amb;
                        }
                    }
                }
            }
        }

        public static AmbientLightDef Get(string id) {
            if (All.TryGetValue(id, out var amb)) {
                return amb;
            }
            return Default;
        }

        public string Id;
        public int SortOrder;
        public string NameKey;
        private string nameT;
        public string NameT => nameT ??= NameKey.T();
        public string Color;
        public Color Light {
            get {
                if (Colors.TryFromHex(Color, out var color)) {
                    return color;
                }
                D.Err("Invalid ambient light color (should be standard hex format): {0}", Color);
                return UnityEngine.Color.magenta;
            }
        }
    }
}
