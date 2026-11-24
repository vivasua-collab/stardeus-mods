using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Scenarios;
using Game.Systems.Items;
using Game.Utils;
using Game.CodeGen;
using KL.Collections;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Suggestions {
    public class SuggestionRelocateSpaceSuit : Suggestion {
        [SelfInit]
        static void AutoRegister() {
            Register(new SuggestionRelocateSpaceSuit());
        }
        public const string SuggId = "RelocateSpaceSuit";
        public override string Id => SuggId;
        public override string Description {
            get {
                var glyph = The.Bindings.GetControlName(Input.ActionType.OpenInventory);
                return T.GoalRelocatespacesuitDesc_V1.F(glyph);
            }
        }
        public override string IconId => GraphicsId.BeingsClothingHumanSpaceSuit01SpaceSuit01;

        public override bool CheckDependencies(HashSet<string> completedGoals) {
            return completedGoals.Contains(SuggestionStartWreckedScenario.SuggId);
        }
        public override bool IsAvailable(GameState state) {
            // Optimization
            var ticks = state.Clock.Ticks;
            if (ticks < Consts.TicksPer2Days || ticks > Consts.TicksPer2Weeks) {
                return false;
            }
            // Bodies first: a short list, and when everyone is suited up already the
            // item scan below never runs.
            if (!FindNeeds(state, out var needsSuit, out var needsHelmet)) {
                return false;
            }
            return HasLoosePiece(state, needsSuit, needsHelmet);
        }

        // Nobody left to put it on is not something the player achieved, so it is
        // dismissed rather than completed. Without this the suggestion would stick
        // in a robots only colony, holding one of the three suggestion slots.
        public override bool ShouldBeDismissed(GameState state) {
            return !FindNeeds(state, out _, out _);
        }

        public override bool IsComplete(GameState state) {
            if (!FindNeeds(state, out var needsSuit, out var needsHelmet)) {
                return false;
            }
            return !HasLoosePiece(state, needsSuit, needsHelmet);
        }

        // Which pieces some colonist could still put on; returns whether there is a
        // colonist at all. Colonists as BeingsSys counts them for the Situation:
        // robots need no suit, and guests are not the player's to dress. Every item
        // that fits the Suit or Helmet slot provides suit integrity, so an empty slot
        // is exactly someone who would benefit.
        private static bool FindNeeds(GameState s, out bool needsSuit, out bool needsHelmet) {
            needsSuit = false;
            needsHelmet = false;
            bool hasColonist = false;
            var alive = s.Beings.Alive;
            for (int i = alive.Count - 1; i >= 0; i--) {
                var b = alive[i];
                if (!b.IsAlive || !b.Data.IsPlayer || b.Data.IsGuest) { continue; }
                if (!b.Species.IsBiological || !b.Species.IsHumanoid) { continue; }
                hasColonist = true;
                var equipment = b.Data.Inventory?.Equipment;
                if (equipment == null) { continue; }
                if (equipment.TryGetValue(InventorySlotIdH.Suit, out var suit) && suit.Item == null) {
                    needsSuit = true;
                }
                if (equipment.TryGetValue(InventorySlotIdH.Helmet, out var helmet) && helmet.Item == null) {
                    needsHelmet = true;
                }
                if (needsSuit && needsHelmet) { break; }
            }
            return hasColonist;
        }

        // A dead raider's suit does not count: the player stripped it, so it is not
        // wreckage to go looking for, and wearing it upsets the colonist.
        private static bool HasLoosePiece(GameState s, bool suit, bool helmet) {
            if (!suit && !helmet) { return false; }
            var comps = s.Components.AllByType<StorableObjComp>();
            if (comps == null) { return false; }
            foreach (var c in comps) {
                if (!c.Entity.IsActive) { continue; }
                var defIdH = c.Entity.Definition.IdH;
                bool wanted = (suit && defIdH == DefIdsH.ObjEquipmentClothingSpaceSuit01)
                    || (helmet && defIdH == DefIdsH.ObjEquipmentHatsSpaceHelmet01);
                if (!wanted) { continue; }
                if (ItemOwnership.IsDeadMansGear(((StorableObjComp)c).Obj.Data)) { continue; }
                return true;
            }
            return false;
        }

        public override float Score(GameState state) {
            return int.MaxValue;
        }
    }
}
