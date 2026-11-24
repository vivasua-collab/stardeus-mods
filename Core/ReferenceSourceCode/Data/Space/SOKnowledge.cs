using System;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using KL.Collections;
using KL.Utils;
using MessagePack;

namespace Game.Data.Space {
    [MessagePackObject]
    public class SOKnowledge { // Intel
        [Key("Data")] public ComponentData CustomData;
        [Key("Mat")] public List<int> Mats;
        [Key("POI")] public Dictionary<int, int> POIs;
        [Key("IsEmpty")] public bool IsEmpty;
        [Key("FactionId")] public string FactionId;
        [IgnoreMember] public SpaceObject SO;
        [IgnoreMember] public bool IsFullyScanned {
            get {
                if (SO.Type.IdH == SpaceObjectTypeIdH.ResourceUnknown) {
                    return false;
                }
                if (!Arrays.IsEmpty(SO.Mats)) {
                    if (Arrays.IsEmpty(Mats)) {
                        return false;
                    } else {
                        foreach (var mat in SO.Mats) {
                            if (!Mats.Contains(mat.IdH)) {
                                // unscanned mat!
                                return false;
                            }
                        }
                    }
                }
                if (SO.FactionId != FactionId) {
                    return false;
                }
                if (SO.POIs?.Count > POIs?.Count) {
                    return false;
                }
                return true;
            }
        }

        public SOKnowledge() {}
        public SOKnowledge(SpaceObject so) {
            SO = so;
        }

        private ComponentData Custom => CustomData ??= new(0, "SOKnowledge");

        [IgnoreMember] public bool Any => !Arrays.IsEmpty(Mats) || !Arrays.IsEmpty(POIs) || FactionId != null;

        public string GetString(string key, string def) {
            if (CustomData == null) { return def; }
            return CustomData.GetString(key, def);
        }
        public bool GetBool(string key, bool def) {
            if (CustomData == null) { return def; }
            return CustomData.GetBool(key, def);
        }
        public int GetInt(string key, int def) {
            if (CustomData == null) { return def; }
            return CustomData.GetInt(key, def);
        }

        public void SetString(string key, string val) {
            Custom.SetString(key, val);
        }
        public void SetBool(string key, bool val) {
            Custom.SetBool(key, val);
        }

        public void SetInt(string key, int val) {
            Custom.SetInt(key, val);
        }

        public void DiscoverMat(MatType oreType) {
            if (Mats == null) {
                Mats = new List<int>();
            }
            Mats.AddUniqueInefficient(oreType.IdH);
        }

        public bool IsAwareOf(MatType type) {
            return Mats != null && Mats.Contains(type.IdH);
        }

        public override string ToString() {
            return $"SOKnowledge: IsEmpty={IsEmpty}, FactionId={FactionId}, Mats=[{Mats}], POIs=[{POIs}], IsFullyScanned={IsFullyScanned}";
        }
    }
}
