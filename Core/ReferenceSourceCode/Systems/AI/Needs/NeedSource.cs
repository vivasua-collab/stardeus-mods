using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.Systems.BodyParts;
using Game.Systems.Creatures;
using Game.Utils;
using Game.CodeGen;
using KL.Collections;
using KL.Utils;
using System.Text;

namespace Game.Systems.AI {
    public struct NeedSource {
        public AINeed Need;
        public INeedSourceItem Item;
        public INeedSourceSlot Slot;

        public static NeedSource CreateFor(string needId, INeedSourceSlot slot) {
            var need = Registries.AINeeds.Get(needId);
            var def = slot.NeedSourceItem;
            if (need == null) {
                D.Err("Need ID {0} not found when trying to create ability source for {1}",
                    needId, def);
                return default;
            }
            return new NeedSource {
                Need = need,
                Item = def,
                Slot = slot,
            };
        }

        public bool IsEqualTo(NeedSource other) {
            return other.Need == Need && other.Slot == Slot;
        }

        public static bool ExistsFor(BeingData data, int needId) {
            for (int i = data.NeedSources.Count - 1; i >= 0; i--) {
                var src = data.NeedSources[i];
                if (src.Need.IdH == needId) {
                    return true;
                }
            }
            return false;
        }

        public void AppendDescription(StringBuilder sb) {
            var name = Item.ItemName;
            sb.Append(Texts.Green(name));
            sb.AppendFormat(Texts.Grey(" ({0}: {1})"), Slot.SlotType, Slot.SlotName);
        }

        public override string ToString() {
            return $"NeedSource[{Need.Id}->{Slot.SlotName}]";
        }

        public bool ExistsIn(List<NeedSource> needSources, bool expectMissing) {
            for (int i = needSources.Count - 1; i >= 0; i--) {
                NeedSource ns = needSources[i];
                if (IsEqualTo(ns)) {
                    if (expectMissing) {
                        D.Err("Duplicate need source! {0} ({2}) in {1}", this, D.Dump(needSources), Slot);
                    }
                    return true;
                }
            }
            return false;
        }
    }
}
