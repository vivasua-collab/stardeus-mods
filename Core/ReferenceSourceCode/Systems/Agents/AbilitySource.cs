using Game.Utils;
using KL.Utils;
using System.Text;

namespace Game.Systems.AI {
    /// <summary>
    /// This will contain info about what adds what ability, so we can show this
    /// to the player in UI
    /// </summary>
    public struct AbilitySource {
        public Ability Ability;
        public IAbilitySourceItem Item;
        public IAbilitySourceSlot Slot;

        public static AbilitySource CreateFor(string abilityId,
                IAbilitySourceSlot slot) {
            var ab = Ability.Get(abilityId);
            var item = slot.AbilitySourceItem;
            if (ab == null) {
                D.Err("Ability ID {0} not found when trying to create ability source for {1}",
                    abilityId, item);
                return default;
            }
            return new AbilitySource {
                Ability = ab,
                Item = item,
                Slot = slot,
            };
        }

        public void AppendDescription(StringBuilder sb) {
            var name = Item.ItemName;
            if (!Slot.IsUsableWithParent) {
                name = Texts.Red(Texts.Strike(name));
            } else {
                name = Texts.Green(name);
            }
            sb.Append(name);
            sb.AppendFormat(Texts.Grey(" ({0}: {1})"), Slot.SlotType, Slot.SlotName);
        }

        public override string ToString() {
            return $"AbSrc[{Ability.NameT}|{Slot}|{Item}]";
        }
    }
}
