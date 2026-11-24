using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems.Creatures;
using Game.Utils;

namespace Game.Systems.Items {
    /// <summary>
    /// The single place that decides what "this item changed hands" means, how
    /// much a being minds wearing someone else's gear, and how much less a
    /// merchant will pay for it. Worn equipment only - body parts carry their own
    /// history in BodyPartState, and a weapon that changed hands is just a weapon.
    /// </summary>
    public static class ItemOwnership {
        /// <summary>
        /// Does this item carry ownership history at all? Only gear worn on the body
        /// does: nobody is unsettled by the previous owner of a knife, and a slot has
        /// to opt in via InventorySlotType.Worn, so a modded holster or toolbelt never
        /// picks this up by accident.
        /// </summary>
        public static bool TracksOwnership(ObjData o) {
            return o?.Item?.SlotType?.Worn == true;
        }

        // Def-level twin of the gate above, for questions asked before any item
        // exists - e.g. whether a storage can ever hold gear with a history.
        public static bool TracksOwnership(Def def) {
            return InventoryItemDef.TryForDef(def.IdH, out var item)
                && item.SlotType?.Worn == true;
        }

        /// <summary>
        /// Does this being have a personality that can be bothered by where its gear
        /// came from? Keyed off the mind rather than the body on purpose: a human
        /// mind in a robot chassis still cares, a robot mind never does.
        /// </summary>
        /// <see cref="Game.Systems.Mental.MentalHealth.TryNewFor"/>
        public static bool CaresAboutOrigin(BeingData data) {
            return data?.Mind?.Health != null;
        }

        public static bool HasOwner(ObjData o) {
            return o.LastOwnerId != 0 || o.LastOwnerMindId != 0;
        }

        /// <summary>
        /// Is the recorded owner the same person as the given being? Matching on
        /// either the body or the mind is what makes resurrection (same body, new
        /// mind) and mind transfer (same mind, new body) count as continuity rather
        /// than a change of hands. Zero is "unknown" and never matches - a blank
        /// mind keeps Id 0.
        /// </summary>
        public static bool MatchesOwner(ObjData o, uint beingId, uint mindId) {
            if (o.LastOwnerId != 0 && o.LastOwnerId == beingId) { return true; }
            if (o.LastOwnerMindId != 0 && o.LastOwnerMindId == mindId) { return true; }
            return false;
        }

        /// <summary>
        /// Would wearing this feel like someone else's gear to this being? Derived
        /// rather than read straight off the flag, because an item taken off its
        /// owner and left in storage has not changed hands yet - but it is still
        /// used goods to everyone except that owner, and the equipment policy has to
        /// be able to reject it before anyone puts it on.
        /// </summary>
        public static bool IsSecondHandFor(ObjData o, uint beingId, uint mindId) {
            if (!TracksOwnership(o)) { return false; }
            if (o.IsSecondHand) { return true; }
            return HasOwner(o) && !MatchesOwner(o, beingId, mindId);
        }

        public static bool IsSecondHandFor(ObjData o, BeingData wearer) {
            if (wearer == null) {
                return HasOwner(o);
            }
            return IsSecondHandFor(o, wearer.Id, wearer.Mind?.Id ?? 0);
        }

        public static bool IsDeadMansGear(ObjData o) {
            return TracksOwnership(o) && o.BelongedToDead;
        }

        /// <summary>
        /// Has this been worn by somebody other than the being asking? With no being
        /// to judge against - an item sitting in storage, its own Stats tab - the only
        /// unused case left is gear still on its first owner, since LastOwner is
        /// refreshed on every equip and a worn item's owner is therefore its wearer.
        /// </summary>
        public static bool IsUsed(ObjData o, BeingData forWearer) {
            if (!TracksOwnership(o)) { return false; }
            if (forWearer != null) { return IsSecondHandFor(o, forWearer); }
            return o.IsSecondHand || (HasOwner(o) && o.State != ObjState.Equipped);
        }

        /// <summary>
        /// Short marker naming where this gear came from, or null when there is
        /// nothing to say. Pass the being that would wear it to get the answer
        /// relative to them - their own clothes are never second-hand to them.
        /// </summary>
        public static string OriginLabel(ObjData o, BeingData forWearer) {
            // Nothing to say about gear that carries no history: weapons, body parts,
            // and any modded slot that did not opt into Worn.
            if (!TracksOwnership(o)) { return null; }
            // Where it came from and what state it is in are independent axes - they
            // are two separate mood effects - so a second-hand rag says both rather
            // than one hiding the other.
            string origin = null;
            if (IsDeadMansGear(o)) {
                origin = Texts.Red(T.ItemBelongedToDead);
            } else if (IsUsed(o, forWearer)) {
                // The state is the same for everyone, but the verdict on it is not:
                // a Thrifty being is pleased by exactly what the rest of them mind.
                // Asking MoodValueFor keeps that one decision in one place, and it
                // answers 0 with no wearer to judge against, so storage rows and the
                // Stats tab keep the neutral reading.
                origin = MoodValueFor(o, forWearer) > 0
                    ? Texts.Green(T.ItemSecondHand) : Texts.Yellow(T.ItemSecondHand);
            } else if (!o.IsTattered) {
                // Only gear that is both unworn and intact earns "New".
                origin = Texts.Green(T.AdjectiveNew);
            }
            if (!o.IsTattered) { return origin; }
            var tattered = Texts.Red(T.ItemTattered);
            return origin == null ? tattered : $"{origin}, {tattered}";
        }

        /// <summary>
        /// Icon matching whatever OriginLabel leads with, for the row that shows it.
        /// </summary>
        public static string OriginIcon(ObjData o, BeingData forWearer) {
            if (IsDeadMansGear(o)) { return IconId.CSkull; }
            if (IsUsed(o, forWearer)) { return IconId.CRecycling; }
            return o.IsTattered ? IconId.CTattered : IconId.CSparkles;
        }

        /// <summary>
        /// Which provenance pile this item falls into, for grouping and pricing
        /// rather than for display - OriginLabel/OriginIcon stay the formatters.
        /// Tests in the same order as OriginLabel so the two can never disagree
        /// about which pile an item is in.
        /// </summary>
        public static ItemOriginBucket BucketFor(ObjData o, BeingData forWearer = null) {
            if (o == null || !TracksOwnership(o)) { return ItemOriginBucket.Untracked; }
            if (IsDeadMansGear(o)) { return ItemOriginBucket.DeadMans; }
            if (IsUsed(o, forWearer)) { return ItemOriginBucket.SecondHand; }
            return ItemOriginBucket.New;
        }

        /// <summary>
        /// How much less a merchant will pay for gear with this provenance.
        /// Reselling used equipment means reselling the mood cost WornItemMood
        /// charges whoever wears it next, so the merchant prices that in. 1 for
        /// New/Untracked - nothing to discount.
        /// </summary>
        public static double PriceMulFor(ItemOriginBucket bucket) {
            switch (bucket) {
                case ItemOriginBucket.SecondHand: return Tunables.TradeSecondHandPriceMul;
                case ItemOriginBucket.DeadMans: return Tunables.TradeDeadMansPriceMul;
                default: return 1d;
            }
        }

        public static string DecoratedItemName(ObjData item, BeingData wearer, string label = null, bool markNew = false) {
            label ??= item.Def.NameT;
            // No marker on gear that carries no history: weapons, body parts, and any
            // modded slot that did not opt into Worn.
            if (!TracksOwnership(item)) {
                return label;
            }
            // Condition sits closest to the name; where it came from goes outside it,
            // so a dead raider's rag reads skull-then-tattered.
            if (item.IsTattered) {
                label = $"{GlyphMap.SpriteTattered} {label}";
            }
            if (IsDeadMansGear(item)) {
                return $"{GlyphMap.SpriteSkull} {label}";
            }
            if (IsSecondHandFor(item, wearer)) {
                return $"{GlyphMap.SpriteRecycle} {label}";
            }
            // Sparkles mean new *and* intact, so a fresh rag keeps only its own mark.
            if (markNew && !item.IsTattered) {
                return $"{GlyphMap.SpriteSparkles} {label}";
            }
            return label;
        }

        /// <summary>
        /// Markers for a group of items shown on one row. Every state present in the
        /// group gets its own glyph rather than one winning, so a row holding both a
        /// hand-me-down and a fresh copy never overstates itself as only one of them.
        /// markNew mirrors DecoratedItemName's flag: the equip picker wants the
        /// sparkle to help the player spot fresh gear, but a passive display like
        /// storage should stay quiet about anything that is merely unremarkable.
        /// </summary>
        public static string OriginLabelFor(List<ObjData> items, BeingData forWearer, string label,
                bool markNew = true) {
            int dead = 0;
            int used = 0;
            int good = 0;
            int tattered = 0;
            foreach (var o in items) {
                // Gear that carries no history must not land in the good bucket and
                // come away marked New - a knife is neither new nor used to anyone.
                if (!TracksOwnership(o)) { continue; }
                if (IsDeadMansGear(o)) {
                    dead++;
                } else if (IsUsed(o, forWearer)) {
                    used++;
                } else if (!o.IsTattered) {
                    // A rag nobody has worn is not New either - it gets the tattered
                    // mark below instead of the sparkles.
                    good++;
                }
                // Independent of history, so counted outside that chain.
                if (o.IsTattered) {
                    tattered++;
                }
            }
            // if (dead == 0 && used == 0) { return null; }
            // Each state present prefixes the label, so a mixed group carries several
            // glyphs. Condition goes on first so it ends up nearest the name, matching
            // how DecoratedItemName orders a single item.
            if (tattered > 0) {
                label = $"{GlyphMap.SpriteTattered} {label}";
            }
            if (used > 0) {
                label = $"{GlyphMap.SpriteRecycle} {label}";
            }
            if (dead > 0) {
                label = $"{GlyphMap.SpriteSkull} {label}";
            }
            if (good > 0 && markNew) {
                label = $"{GlyphMap.SpriteSparkles} {label}";
            }
            return label;
            // // var label = dead > 0 ? Texts.Red(T.ItemBelongedToDead) : Texts.Grey(T.ItemSecondHand);
            // int affected = dead > 0 ? dead : used;
            // return affected == items.Count ? label : $"{label} {Units.XNum(affected)}";
        }

        public static void OnEquipped(ObjData o, BeingData wearer) {
            if (!TracksOwnership(o) || wearer == null) { return; }
            var mindId = wearer.Mind?.Id ?? 0;
            if (HasOwner(o) && !MatchesOwner(o, wearer.Id, mindId)) {
                // Never cleared afterwards - otherwise taking the item off and
                // putting it back on would launder it and drop the debuff for free.
                o.Flags |= ObjFlags.SecondHand;
            } else {
                // The same person, possibly resurrected with a new mind or moved
                // into a new body. Their own clothes again, so the corpse taint goes.
                o.Flags &= ~ObjFlags.BelongedToDead;
            }
            o.LastOwnerId = wearer.Id;
            o.LastOwnerMindId = mindId;
        }

        /// <summary>
        /// Hooked on unequip rather than on the strip order, so every route gear can
        /// leave a corpse by is covered: the strip goal, a dropped weapon, the
        /// policy sweep. Incapacitation drops a weapon too, hence the IsAlive check.
        /// </summary>
        public static void OnUnequipped(ObjData o, BeingData wearer) {
            if (!TracksOwnership(o) || wearer == null) { return; }
            if (wearer.Body?.IsAlive == false) {
                o.Flags |= ObjFlags.BelongedToDead;
            }
        }

        /// <summary>
        /// Saves written before ownership tracking existed have no owner recorded on
        /// gear that is currently worn. The wearer counts as the first owner, so old
        /// clothes do not turn second-hand the first time they come off.
        /// </summary>
        public static void BackfillOwner(ObjData o, BeingData wearer) {
            if (!TracksOwnership(o) || wearer == null) { return; }
            if (HasOwner(o)) { return; }
            o.LastOwnerId = wearer.Id;
            o.LastOwnerMindId = wearer.Mind?.Id ?? 0;
        }

        /// <summary>
        /// How much this one item would shift the wearer's mood, ignoring anything
        /// else they have on. Shared by the mood effect and the equipment preference
        /// tie-break so the two can never disagree about what a being likes.
        /// </summary>
        public static int MoodValueFor(ObjData o, BeingData wearer) {
            if (!CaresAboutOrigin(wearer)) { return 0; }
            if (IsDeadMansGear(o)) { return DeadMansMoodFor(wearer); }
            if (!IsSecondHandFor(o, wearer)) { return 0; }
            return SecondHandMoodFor(wearer);
        }

        /// <summary>
        /// What one second-hand garment is worth to this being, whatever else it has
        /// on. Separate from MoodValueFor so the aggregate effect in WornItemMood can
        /// ask for a category it has already established without re-testing an item.
        /// Every value is a tunable, so a mod can retune the whole feature - or zero a
        /// category to switch that complaint off, which WornItemMood.AddEffect honours
        /// by emitting nothing at all.
        /// </summary>
        public static int SecondHandMoodFor(BeingData wearer) {
            if (wearer.HasTrait(TraitIdH.Thrifty)) {
                return Tunables.WornGearMoodSecondHandThrifty;
            }
            return wearer.HasTrait(TraitIdH.Fastidious)
                ? Tunables.WornGearMoodSecondHandFastidious
                : Tunables.WornGearMoodSecondHand;
        }

        // Thrifty minds the corpse, not the reuse, so it sits between the default and
        // nothing rather than turning positive the way the second-hand value does.
        public static int DeadMansMoodFor(BeingData wearer) {
            if (wearer.HasTrait(TraitIdH.Fastidious)) {
                return Tunables.WornGearMoodDeadMansFastidious;
            }
            return wearer.HasTrait(TraitIdH.Thrifty)
                ? Tunables.WornGearMoodDeadMansThrifty
                : Tunables.WornGearMoodDeadMans;
        }

        /// <summary>
        /// Does this being refuse to put on used gear of its own accord? Fastidious
        /// does: the mood hit is self-inflicted, and anyone would avoid it. Only ever
        /// asked on the way in - EquipmentPolicyFilter never lets it strip what a
        /// being already wears, since standing there naked is the worse outcome.
        /// </summary>
        public static bool RefusesUsedGear(BeingData wearer) {
            return CaresAboutOrigin(wearer) && wearer.HasTrait(TraitIdH.Fastidious);
        }
    }
}
