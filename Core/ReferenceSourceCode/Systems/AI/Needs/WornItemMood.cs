using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Items;
using Game.Systems.Mental;
using Game.Systems.Traits;
using UnityEngine;

namespace Game.Systems.AI {
    /// <summary>
    /// Mood for what a being is wearing: where the gear came from, and whether it is
    /// falling apart. Driven from AINeedEquipment.Tick alongside the Naked effect it
    /// is modelled on. Each category is a permanent effect, set on every check and
    /// removed by the same check once nothing in that category is worn.
    /// </summary>
    /// <see cref="ItemOwnership"/>
    public static class WornItemMood {
        // A being with six bad slots must not feel six times worse than one with a
        // single bad shirt, so the count only nudges the base value.
        private const int MaxExtraItems = 2;

        // Being threadbare is not an ownership fact, so this resolver lives here rather
        // than in ItemOwnership. Thrifty is about reuse, not about living in rags, so
        // it gets no say; Fastidious cares about exactly this.
        private static int TatteredMoodFor(BeingData data) {
            return data.HasTrait(TraitIdH.Fastidious)
                ? Tunables.WornGearMoodTatteredFastidious : Tunables.WornGearMoodTattered;
        }

        /// <param name="caresAboutGear">false for beings that never get gear moods
        /// (guests, non-humanoids): their counts stay 0, which removes any such effect.</param>
        public static void Apply(GameState s, BeingData data, bool caresAboutGear) {
            var health = data.Mind.Health;
            if (health == null) { return; }
            int deadCount = 0;
            int secondHandCount = 0;
            int tatteredCount = 0;
            var inv = data.Inventory;
            if (caresAboutGear && inv != null && ItemOwnership.CaresAboutOrigin(data)) {
                var mindId = data.Mind.Id;
                foreach (var slot in inv.Equipment.Values) {
                    var o = slot.ObjData;
                    // Worn gear only, which is also what keeps a rusty knife from
                    // depressing anybody.
                    if (!ItemOwnership.TracksOwnership(o)) { continue; }
                    if (o.BelongedToDead) {
                        deadCount++;
                    } else if (ItemOwnership.IsSecondHandFor(o, data.Id, mindId)) {
                        secondHandCount++;
                    }
                    // Independent of history: a pristine-provenance rag is still a rag.
                    if (o.IsTattered) {
                        tatteredCount++;
                    }
                }
            }
            SetDeadMansMood(s, health, data, deadCount);
            SetSecondHandMood(s, health, data, secondHandCount);
            SetTatteredMood(s, health, data, tatteredCount);
        }

        // Each category is its own effect with its own count, rather than one shared
        // complaint that the worst category wins. Merging them let a single corpse
        // item decide what every other slot was worth: a Thrifty being in three
        // hand-me-downs and one dead raider's coat read as a flat -8, with no sign of
        // the bonus that trait exists for. Note the classification above stays
        // exclusive - an item is one grievance or the other, never both - but a being
        // can hold both at once.
        private static void SetDeadMansMood(GameState s, MentalHealth health, BeingData data,
                int deadCount) {
            // Thrifty is bothered less here, not pleased, so the reason stays the one
            // everybody else gets - it is still a complaint at -3.
            SetMood(s, health, MoodSourceIdH.DeadMansGear, IconId.CSkull,
                WithGearTrait(T.MoodDeadMansGear, data, thriftyFirst: false),
                deadCount == 0 ? 0 : ItemOwnership.DeadMansMoodFor(data), deadCount);
        }

        private static void SetSecondHandMood(GameState s, MentalHealth health, BeingData data,
                int secondHandCount) {
            var baseValue = secondHandCount == 0 ? 0 : ItemOwnership.SecondHandMoodFor(data);
            // One source for the liked and the disliked effect: a being either likes
            // hand-me-downs (Thrifty) or minds them, never both, so a changed trait just
            // swaps the reason in place.
            SetMood(s, health, MoodSourceIdH.SecondHandGear, IconId.CRecycling,
                WithGearTrait(baseValue > 0 ? T.MoodSecondHandGearLiked : T.MoodSecondHandGear,
                    data, thriftyFirst: true),
                baseValue, secondHandCount);
        }

        // Condition rather than provenance, and counted over every slot rather than
        // the ones the two above claim: a dead raider's threadbare coat is a complaint
        // about the corpse and a complaint about the rags, and the sources differ so
        // MentalHealth keeps both.
        private static void SetTatteredMood(GameState s, MentalHealth health, BeingData data,
                int tatteredCount) {
            var reason = data.HasTrait(TraitIdH.Fastidious)
                ? TraitDef.MoodReason(T.MoodTatteredGear, TraitIdH.Fastidious)
                : T.MoodTatteredGear;
            SetMood(s, health, MoodSourceIdH.TatteredGear, IconId.CTattered, reason,
                tatteredCount == 0 ? 0 : TatteredMoodFor(data), tatteredCount);
        }

        // Names the trait that set the value, in the order ItemOwnership decides it: Thrifty
        // wins for hand-me-downs, Fastidious for a dead person's gear. The effects are keyed by
        // their source, not their reason, so the reason may change with the trait.
        private static string WithGearTrait(string reason, BeingData data, bool thriftyFirst) {
            var thrifty = data.HasTrait(TraitIdH.Thrifty);
            var fastidious = data.HasTrait(TraitIdH.Fastidious);
            if (thrifty && (thriftyFirst || !fastidious)) {
                return TraitDef.MoodReason(reason, TraitIdH.Thrifty);
            }
            if (fastidious) {
                return TraitDef.MoodReason(reason, TraitIdH.Fastidious);
            }
            return reason;
        }

        private static void SetMood(GameState s, MentalHealth health, int sourceIdH,
                string icon, string reason, int baseValue, int count) {
            // Zero switches the complaint off (and removes it) - every value here is a
            // tunable a mod can blank. Checked before the nudge below, which would
            // otherwise read a zeroed value as positive and hand out a stray buff to
            // anyone wearing two.
            int value = 0;
            if (baseValue != 0) {
                int extra = Mathf.Min(count - 1, MaxExtraItems);
                value = baseValue < 0 ? baseValue - extra : baseValue + extra;
            }
            // autoLog false: what a being wears is not news for the ship log, and the
            // Fastidious values would clear the default log threshold.
            health.SetPermanentEffect(s, sourceIdH, icon, reason, value, autoLog: false);
        }
    }
}
