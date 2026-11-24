using System.Collections.Generic;
using Game.Achievements;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Data.Economy;
using Game.Systems.AI;
using Game.Systems.AI.Combat;
using Game.Systems.Conditions;
using Game.Systems.Creatures;
using Game.Systems.Stats;
using Game.Utils;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Items {
    public sealed class InventoryItemSlot : IAttackMethodProvider, IStatSource, IAbilitySourceSlot {
        public InventoryItemDef Item;
        public readonly InventorySlotType Type;
        public ObjData ObjData;
        public string NameT;
        public bool IsBodyPart => false;
        public float Quality => ObjData?.Quality ?? 0f;
        public string StatSourceNameT => NameT;
        public string ItemName => Item == null ? T.Empty : Item.Def.NameT;
        public string SlotName => NameT;
        public string SlotType => T.Equipment;
        public string SlotIcon => Item == null ? IconId.CCross : Item.Def.Preview;
        private readonly GameState S;
        public IAbilitySourceItem AbilitySourceItem => Item;
        public Entity Entity => null;
        public bool IsUsableWithParent => Item != null;

        public string Tooltip {
            get {
                if (Item == null) {
                    return $"{NameT}: {Texts.Grey(T.Empty)}";
                } else {
                    return $"{NameT}: {Item.Def.CodexLink} ({Units.PercentageColored(Quality, clamp: false)})";
                }
            }
        }

        public readonly BeingInventory Inventory;
        public readonly LookPart LookPart;
        private readonly List<Stat> AffectedStats = new();
        private InventoryItemSlot(GameState s, BeingInventory inv, InventorySlotType type) {
            S = s;
            NameT = type.Name;
            Type = type;
            Inventory = inv;
            LookPart = type.CreateLookPart();
        }

        /// <summary>
        /// Asimov guard: life support may not be taken off where the being would die
        /// without it. Kept separate from TryUnequipItem so automated callers (the
        /// equipment policy sweep) can check it without spamming an error message.
        /// </summary>
        public bool CanUnequipSafely(BeingData o) {
            if (Type.IdH != InventorySlotIdH.Helmet && Type.IdH != InventorySlotIdH.Suit) {
                return true;
            }
            if (A.HasAsimovOverride) {
                return true;
            }
            if (o?.Being == null) {
                // No spawned body to check the surroundings of.
                return false;
            }
            return o.Safety.IsActuallySafeAtWithoutEquipment(o.Being.PosIdx);
        }

        public bool TryUnequipItem(GameState s, BeingData o) {
            if (!CanUnequipSafely(o)) {
                s.Sig.ShowErrorMessage.Send(T.AsimovViolation1);
                return false;
            }
            o.Being?.AIAgent.BB.SetCooldown(NeedIdH.Equipment, G.Ticks,
                Consts.TicksPerHour, Consts.TicksPer2Hours);
            RemoveItem();
            return true;
        }


        private string WeaponSlotTip() {
            string shootingTip;
            if (Inventory.Owner.Mind.Jobs.TryJob(JobTypeIdH.Shooting, out var shooting)
                    && !shooting.Incapable) {
                shooting.GetNameAndLevelText(out var jobName, out var jobLevel, out string tip);
                shootingTip = $"{Texts.Accented(jobName)}: {jobLevel}";
                if (shooting.Priority == 0) {
                    shootingTip = $"{shootingTip}:\n{Texts.Red(T.NoTaskReasonJobPriority0One_V1.F(jobName))}";
                }
            } else {
                shootingTip = $"{Texts.Accented(T.JobTypeShooting)}: {Texts.Red(T.AdjIncapable)}";
            }
            string meleeTip;
            if (Inventory.Owner.Mind.Jobs.TryJob(JobTypeIdH.Melee, out var melee)
                    && !melee.Incapable) {
                melee.GetNameAndLevelText(out var jobName, out var jobLevel, out string tip);
                meleeTip = $"{Texts.Accented(jobName)}: {jobLevel}";
                if (melee.Priority == 0) {
                    meleeTip = $"{meleeTip}:\n{Texts.Red(T.NoTaskReasonJobPriority0One_V1.F(jobName))}";
                }
            } else {
                meleeTip = $"{Texts.Accented(T.JobTypeMelee)}: {Texts.Red(T.AdjIncapable)}";
            }
            var illegal = IllegalWeaponsTip();
            if (illegal != null) {
                return $"{shootingTip}\n{meleeTip}\n{illegal}";
            }
            return $"{shootingTip}\n{meleeTip}";
        }

        // Weapons this being may not hold that the colony knows about, so a robot's empty
        // weapon slot explains itself
        private string IllegalWeaponsTip() {
            var owner = Inventory.Owner;
            var body = owner.Species;
            var faction = owner.EffectiveFaction;
            List<string> names = null;
            foreach (var item in InventoryItemDef.All.Values) {
                if (item.SlotType != Type || item.Def == null) { continue; }
                if (item.IsAllowedFor(body, faction)) { continue; }
                if (!S.Research.IsDefUnlocked(item.Def)
                        && S.Query.ObjCountByDef.Ask(item.Def) == 0) {
                    continue;
                }
                names ??= new List<string>();
                names.Add(item.ItemName);
            }
            if (names == null) { return null; }
            names.Sort();
            return $"{Texts.Accented(T.AdjIllegal)}: {Texts.Red(string.Join(", ", names))}";
        }

        public static InventoryItemSlot NewFor(GameState s,
                InventorySlotType type, BeingInventory inv) {
            return new InventoryItemSlot(s, inv, type);
        }

        /// <summary>
        /// Sets the inventory item without applying the effects. Only use
        /// when being is not spawned yet, i.e. when generating being inventory
        /// for someone new. Otherwise use Equip.
        /// </summary>
        /// <see cref="Equip"/>
        /// <param name="objData"></param>
        public void Set(ObjData objData) {
            if (Inventory.Owner.Being != null) {
                D.Err("Trying to Set() inventory item on a spawned being. use Equip() instead! {0} -> {1}",
                    objData, Inventory.Owner);
                return;
            }
            if (objData.State == ObjState.Equipped || objData.State == ObjState.Destroyed) {
                D.Err("Refusing to set an equipped or destroyed item: {0} -> {1}", objData,
                    Inventory.Owner);
                return;
            }
            if (Item != null) {
                D.Err("Refusing to set an item on a slot that already has something: {0} -> {1}",
                    objData, Inventory.Owner);
                return;
            }

            Item = objData.Item;
            objData.State = ObjState.Equipped;
            if (Inventory.Owner.Id != 0) {
                objData.BeingDataId = Inventory.Owner.Id;
                ItemOwnership.OnEquipped(objData, Inventory.Owner);
            }
            ObjData = objData;
            UpdateConditions(Item, true);
        }

        /// <summary>
        /// For generation only
        /// </summary>
        public void Unset() {
            if (Inventory.Owner.Being != null) {
                D.Err("Trying to Unset() inventory item on a spawned being. use Equip() instead! {0}",
                    Inventory.Owner);
                return;
            }
            var itemBefore = Item;
            Item = null;
            ObjData = null;
            UpdateConditions(itemBefore, false);
        }

        /// <summary>
        /// THIS IS NOT CALLED WHEN GAME IS LOADED! Only when part is installed.
        /// To add stuff after game was loaded see
        ///
        /// </summary>
        /// <see cref="AfterLoad"/>
        /// <see cref="Set"/>
        /// <param name="item"></param>
        /// <param name="state"></param>
        /// <returns>Old item</returns>
        public ObjData Equip(ObjData objData) {
            // D.Warn("Equipping objData: {0}", objData);
            if (Inventory.Owner.Being == null) {
                D.Err("Trying to Equip() inventory item on an inactive being. use Set() instead! {0} -> {1}",
                    objData, Inventory.Owner);
                return null;
            }
            if (objData.State == ObjState.Equipped || objData.State == ObjState.Destroyed) {
                D.Err("Refusing to equip an equipped or destroyed item: {0} -> {1}", objData,
                    Inventory.Owner);
                return null;
            }
            ObjData oldItem = ObjData;
            if (Item != null) {
                Unequip(notify: false);
            }

            Item = objData.Item;
            objData.State = ObjState.Equipped;
            objData.BeingDataId = Inventory.Owner.Id;
            ItemOwnership.OnEquipped(objData, Inventory.Owner);
            ObjData = objData;

            if (itemStatModifiers != null) {
                D.Err("Item stat modifiers not fresh for slot! {0} | {1}",
                    this, D.Dump(itemStatModifiers));
            }

            ApplyItemEffects();

            CheckCleaningBotWeaponizeAchievement();

            UpdateConditions(Item, true);
            Inventory.UpdateShownStatModifiers();
            Inventory.NotifyEquipmentChanged();
            return oldItem;
        }

        private void CheckCleaningBotWeaponizeAchievement() {
            if (Arrays.IsEmpty(Item.AttackMethods)) {
                return;
            }
            if (Inventory.Owner.Species.IdH != SpeciesIdH.CleaningBot
                    || !Inventory.Owner.IsPlayer) {
                return;
            }
            bool hasRanged = false;
            foreach (var am in Item.AttackMethods) {
                if (am.RangeMax > 3f) {
                    hasRanged = true;
                    break;
                }
            }
            if (hasRanged) {
                The.AchievementTracker.Unlock(
                    AchievementWeaponizedCleaningBot.AchievementId);
            }
        }

        private bool areEffectsApplied;

        private (int, StatModifier)[] itemStatModifiers;
        // AddsStatModifiersWhenShown, applied only while the item shows (SetShown)
        private (int, StatModifier)[] shownStatModifiers;
        private bool areShownModifiersApplied;

        public void ApplyItemEffects() {
            UpdateLooks();
            Inventory.NeedsStatsUpdate = true;
            if (Item == null) { return; }
            // when we despawn / respawn a being, this gets executed twice
            // let's just ignore the second time
            if (areEffectsApplied) {
                return;
            } else {
                areEffectsApplied = true;
            }
            // D.Warn("Applying item effects for {0}->{1}", Item, this);
            if (AddAbilitySources()) {
                Inventory.Owner.AfterAbilitiesChanged();
            }
            AddStatSources();
            itemStatModifiers ??= Item.QualityAdjustedStatModifiers(ObjData.Quality);
            AddStatModifiers();
            shownStatModifiers ??= Item.QualityAdjustedStatModifiersWhenShown(ObjData.Quality);
            AddAttackMethods();
        }

        /// <summary>
        /// Applies or removes the item's when-shown stat modifiers. Gameplay state, so the
        /// owner's inventory decides it from the same flags the looks use
        /// (BeingInventory.UpdateShownStatModifiers), never from rendering.
        /// </summary>
        public void SetShown(bool shown) {
            if (shownStatModifiers == null || shown == areShownModifiersApplied) { return; }
            areShownModifiersApplied = shown;
            var stats = Inventory.Owner.StatsBeing;
            foreach (var ss in shownStatModifiers) {
                if (!stats.Find(ss.Item1, out var stat)) { continue; }
                if (shown) {
                    stat.AddModifier(ss.Item2);
                } else {
                    stat.RemoveModifier(ss.Item2);
                }
            }
            Inventory.NeedsStatsUpdate = true;
        }

        private void UpdateLooks() {
            if (LookPart.Type == LookPartTypeDef.None) {
                // this item has no look
                return;
            }
            var data = Inventory.Owner;
            if (Item != null) {
                LookPart.SpriteId = BeingLooks.SpriteForBodyType(data.Looks.BodyType, Item.SpriteId);
                if (Item.ColorGroupId != null) {
                    LookPart.Color = ObjData.Color;
                } else {
                    LookPart.Color = Consts.ColorWhite;
                }
                LookPart.SetHides(Item.HidesLookParts, Item.HidesMask);
            } else {
                LookPart.SpriteId = null;
                LookPart.ClearHides();
            }
            Inventory.LooksChanged();
        }

        private void AddStatSources() {
            if (Item.StatSources == null) { return; }
            var stats = Inventory.Owner.StatsBeing;
            foreach (var ss in Item.StatSources) {
                var stat = stats.AddStatSource(ss.StatId, this);
                if (stat != null) {
                    AffectedStats.Add(stat);
                    stat.SourcesChanged();
                }
            }
        }
        private void AddStatModifiers() {
            if (itemStatModifiers == null) { return; }
            var stats = Inventory.Owner.StatsBeing;
            foreach (var ss in itemStatModifiers) {
                var statId = ss.Item1;
                if (stats.Find(statId, out var stat)) {
                    stat.AddModifier(ss.Item2);
                }
            }
        }
        private void RemoveStatModifiers() {
            if (itemStatModifiers == null) { return; }
            var stats = Inventory.Owner.StatsBeing;
            foreach (var ss in itemStatModifiers) {
                var statId = ss.Item1;
                if (stats.Find(statId, out var stat)) {
                    stat.RemoveModifier(ss.Item2);
                }
            }
        }
        private void AddAttackMethods() {
            if (Item.AttackMethods == null) { return; }
            foreach (var ats in Item.AttackMethods) {
                Inventory.Owner.Body.AddAttackMethod(ats, this);
            }
        }

        private bool AddAbilitySources() {
            if (Item.AddsAbilities == null) { return false; }
            bool added = false;
            foreach (var ab in Item.AddsAbilities) {
                var src = AbilitySource.CreateFor(ab, this);
                if (src.Ability != null) {
                    Inventory.Owner.AbilitySources.Add(src);
                    added = true;
                }
            }
            return added;
        }

        public ObjData Unequip() {
            return Unequip(notify: true);
        }

        private ObjData Unequip(bool notify) {
            var itemBefore = Item;
            areEffectsApplied = false;
            RemoveStatModifiers();
            itemStatModifiers = null;
            SetShown(false);
            shownStatModifiers = null;
            Item = null;
            bool removedAb = false;
            var srcs = Inventory.Owner.AbilitySources;
            for (int i = srcs.Count - 1; i >= 0; i--) {
                AbilitySource src = Inventory.Owner.AbilitySources[i];
                if (src.Slot == this) {
                    srcs.RemoveAt(i);
                    removedAb = true;
                }
            }
            if (removedAb) {
                Inventory.Owner.AfterAbilitiesChanged();
            }
            for (int i = AffectedStats.Count - 1; i >= 0; i--) {
                Stat st = AffectedStats[i];
                st.Sources.Remove(this);
                AffectedStats.RemoveAt(i);
                st.SourcesChanged();
            }
            Inventory.Owner.Body.RemoveAttackMethodsFor(this);
            var objD = ObjData;
            ItemOwnership.OnUnequipped(objD, Inventory.Owner);
            // can't do this here, because crawler data is linked to this
            // objD.BeingDataId = 0;
            ObjData = null;
            UpdateLooks();
            UpdateConditions(itemBefore, false);
            Inventory.NeedsStatsUpdate = true;
            if (notify) {
                Inventory.NotifyEquipmentChanged();
            }
            return objD;
        }

        // FIXME if more than one item will add same condition, add extra checks
        private void UpdateConditions(InventoryItemDef item, bool equipped) {
            if (item == null) { return; }
            if (Arrays.IsEmpty(item.AddsConditions)) { return; }

            var data = Inventory.Owner;
            if (equipped) {
                foreach (var c in item.AddsConditions) {
                    var condIdH = Hashes.S(c);
                    if (data.HasCondition(condIdH)) {
                        continue;
                    }
                    var condState = ConditionState.CreateFor(S, condIdH, data,
                        Severity.Irrelevant, true);
                    if (!condState) {
                        D.Err("InventoryItemDef ({0}) has invalid condition Id: {1}", item.Id, c);
                    }
                }
            } else {
                foreach (var c in item.AddsConditions) {
                    var condIdH = Hashes.S(c);
                    if (!data.HasCondition(condIdH)) { continue; }
                    data.RemoveCondition(S, condIdH);
                }
            }
        }

        public void AfterLoad(bool isLoading) {
            // validate
            if (Item != null) {
                if (ObjData == null) {
                    D.Err("Item is equipped but there is no ObjData! {0} on {1}",
                        Item, Inventory.Owner);
                    Item = null;
                } else {
                    if (isLoading && ObjData.BeingDataId == 0) {
                        D.Err("Item is equipped but ObjData.BeingDataId == 0! {0} on {1}",
                            Item, Inventory.Owner);
                        ObjData.BeingDataId = Inventory.Owner.Id;
                    }
                    if (isLoading) {
                        ItemOwnership.BackfillOwner(ObjData, Inventory.Owner);
                        if (!Item.FitsBody(Inventory.Owner.Species)) {
                            // Saved before the item got a body requirement (a robot holding
                            // an Energy Rifle), or the law changed since. Checked once the
                            // world is loaded, since dropping creates an object and the
                            // faction and the Asimov Override are settled by then.
                            Ready.When(WhenH.AllSystemsLoaded, DropIfNotAllowed);
                        }
                    }
                }
            }
            ApplyItemEffects();
        }

        private void DropIfNotAllowed() {
            var owner = Inventory.Owner;
            if (Item == null || Item.IsAllowedFor(owner.Species, owner.EffectiveFaction)) {
                return;
            }
            // Hidden or away beings have nowhere to drop it here
            if (Inventory.Owner.Being == null) { return; }
            D.Warn("Dropping {0}: {1} cannot use it", Item, Inventory.Owner);
            RemoveItem();
        }

        public string SlotTooltip() {
            string extraTip = null;
            if (Type.IdH == InventorySlotIdH.Weapon) {
                extraTip = WeaponSlotTip();
            }
            if (Item == null) {
                return extraTip;
            } else {
                if (extraTip != null) {
                    extraTip = $"{extraTip}\n";
                }
                return ObjData.Tooltip(S, skipCount: true, prefix: extraTip);
            }
        }

        public Obj RemoveItem() {
            var data = Unequip();
            Inventory.RebuildLooksNow();
            Vector2 dropPos;
            if (Inventory.Owner.Being == null) {
                dropPos = Inventory.Owner.LastPosition.Exact;
            } else {
                dropPos = Inventory.Owner.Being.Position;
            }
            var dropPosIdx = Pos.FromVector(dropPos);
            dropPosIdx = EntityUtils.AdjustObjSpawnablePos(dropPosIdx);
            dropPos = Pos.ToVector(dropPosIdx);
            var obj = S.Objs.Create(dropPos + Rng.UInsideUnitCircle() * 0.25f, data);
            return obj;
        }

        public string GetName() {
            return "InventoryItemSlot";
        }

        public bool HasRequiredAbilities(CombatUnit unit) {
            return unit.Data.HasAbility(AbilityIdH.UseItem);
        }

        public bool IsVisibleNow(InventoryLooksFlags flags) {
            var fs = Item.ShowFlags;
            var fh = Item.HideFlags;
            bool hasShowFlags = fs != InventoryLooksFlags.None;
            bool hasHideFlags = fh != InventoryLooksFlags.None;
            bool hideMatchesAny = (fh & flags) != InventoryLooksFlags.None;

            if (hasShowFlags) {
                bool showMatchesAny = (fs & flags) != InventoryLooksFlags.None;
                if (showMatchesAny) {
                    return true;
                } else if (!hasHideFlags) {
                    // only show flags and no hide flags and no match == do not show
                    return false;
                }
            }
            return !hideMatchesAny;
        }

        public bool GetEffectFor(Stat stat, out float toAdd, out float limit) {
            return Item.GetStatBaseFor(stat, ObjData.Quality, out toAdd, out limit);
        }

        public void UpdateStat(Stat stat) {
            if (Item == null) {
                D.Err("Trying to update stat when item is null for slot: {0}. Stat: {1}",
                    SlotName, stat);
                return;
            }
            if (Item.StatSources == null) {
                D.Err("Trying to update stat when item has no stat sources: {0}. Installed: {1}. Stat: {2}",
                    SlotName, Item.Id, stat);
                return;
            }
            if (Item.GetStatBaseFor(stat, ObjData.Quality, out float toAdd, out float limit)) {
                stat.CompoundBase(toAdd, limit);
            }
        }

        public override string ToString() {
            return $"InvSlot[{SlotName}|{Item}]";
        }

        // Two items whose value and quality land this close are interchangeable in
        // every way the wearer can measure, so let where the gear came from settle
        // it instead of a rounding difference.
        private const float OriginTieBreakDrift = 0.05f;
        private const float WeaponSwapMargin = 0.01f;

        public ObjData BetterItem(ObjData a, ObjData b) {
            // var aItem = a.Item;
            // var bItem = b.Item;
            if (Type.IdH == InventorySlotIdH.Weapon && a.Item != null && b.Item != null) {
                // the weapon that fights best at its quality, not the pricier one
                var scoreA = a.Item.WeaponScore(a.Quality);
                var scoreB = b.Item.WeaponScore(b.Quality);
                // a near tie keeps the incumbent, so beings don't swap back and forth
                return scoreB > scoreA * (1f + WeaponSwapMargin) ? b : a;
            }
            var marketA = MarketItem.FindFor(a.Def);
            var marketB = MarketItem.FindFor(b.Def);
            // choose more expensive part
            if (marketA != null && marketB != null) {
                var valA = marketA.Value * a.Quality;
                var valB = marketB.Value * b.Quality;
                if (Mathf.Abs(valA - valB)
                        <= Mathf.Max(valA, valB) * OriginTieBreakDrift) {
                    var owner = Inventory.Owner;
                    // MoodValueFor is the same verdict the mood effect uses, so a
                    // Thrifty being prefers the second-hand one without restating why.
                    if (ItemOwnership.MoodValueFor(b, owner)
                            > ItemOwnership.MoodValueFor(a, owner)) {
                        return b;
                    }
                    return a;
                }
                if (valB > valA) {
                    return b;
                }
            }
            return a;
        }
    }
}
