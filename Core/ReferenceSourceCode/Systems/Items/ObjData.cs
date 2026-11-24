#if !PRODUCTION_BUILD
// #define DEBUG_OBJ_RESERVE
// #define DEBUG_STATE
#endif
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Economy;
using Game.Data.Factions;
using Game.Input;
using Game.Procgen;
using Game.Story.Events;
using Game.Systems.AI;
using Game.Systems.BodyParts;
using Game.Systems.Conditions;
using Game.Systems.Creatures;
using Game.Systems.Creatures.Surgeries;
using Game.Systems.Mental;
using Game.Systems.Stats;
using Game.Systems.UIUX;
using Game.Utils;
using KL.Grid;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Items {
    /// <summary>
    /// Everything related to Obj that can be saved / loaded or generated
    /// without creating a Obj instance and spawning it on the map
    /// </summary>
    public sealed class ObjData {
        public struct ObjPriceComparer : IComparer<ObjData> {
            public int Compare(ObjData a, ObjData b) {
                var marketA = MarketItem.FindFor(a.Def);
                var marketB = MarketItem.FindFor(b.Def);
                if (marketA.Value == marketB.Value) {
                    return 0;
                }
                return marketA.Value > marketB.Value ? -1 : 1;
            }
        }
        public enum SortMode {
            Name,
            QualityDesc,
            QualityAsc,
            ValueDesc,
            ValueAsc,
            // Must be last because we skip it when converting between int and enum
            Unsorted,
        }
        public static SortMode ObjSortMode {
            get {
                var mode = CachedPlayerPrefs.GetInt("obj.sort.mode", (int) SortMode.Name);
                return (SortMode) mode;
            }
            set {
                CachedPlayerPrefs.SetInt("obj.sort.mode", (int) value);
            }
        }

        private static readonly List<string> sortModeOptions = new();
        public static List<string> SortModeOptions {
            get {
                if (sortModeOptions.Count == 0) {
                    foreach (SortMode opt in Enum.GetValues(typeof(SortMode))) {
                        if (opt == SortMode.Unsorted) { continue; }
                        sortModeOptions.Add(SortTextFor(opt));
                    }
                }
                return sortModeOptions;
            }
        }
        private static string SortTextFor(SortMode sortMode) {
            switch (sortMode) {
                case SortMode.Name: return T.SortName;
                case SortMode.ValueAsc: return $"{T.SortValue} {GlyphMap.ArrowUp}";
                case SortMode.ValueDesc: return $"{T.SortValue} {GlyphMap.ArrowDown}";
                case SortMode.QualityAsc: return $"{T.NounQuality} {GlyphMap.ArrowUp}";
                case SortMode.QualityDesc: return $"{T.NounQuality} {GlyphMap.ArrowDown}";
                default: return T.Unknown + sortMode.ToString();
            }
        }

        public static ObjPriceComparer PriceComparer = default;
        public static uint LastId;
        public uint Id;
        /// <summary>
        /// Tile ID of where the item is stored
        /// </summary>
        public int StorageId;
        public float Quality;
        public Def Def;
        public Obj Obj;
        #if DEBUG_STATE
        private ObjState state;
        public ObjState State {
            get => state;
            set {
                D.Warn("Changing obj state: {0} -> {1} -> {2}", Def.Id, state, value);
                state = value;
            }
        }
        #else
        public ObjState State;
        public string InventoryStateName {
            get {
                switch (State) {
                    case ObjState.Active: return T.AdjectiveAvailable;
                    case ObjState.Stored: return T.ObjectInStorage;
                    case ObjState.Equipped: return T.ItemStateEquipped;
                    default: return T.NotAvailable;
                }
            }
        }
        #endif
        public bool IsAvailableIgnoreReservation =>
            State == ObjState.Active || State == ObjState.Stored;
        public bool IsAvailable =>
            (State == ObjState.Active || State == ObjState.Stored)
                && (Reservation == null || !Reservation.IsActive);
        public InventoryItemDef Item;
        /// <summary>
        /// For removed / stored body parts that are not attached to a body
        /// </summary>
        public BodyPartDef BodyPart;
        /// <summary>
        /// If it is a body part, it will have a state. If the organ has wounds
        /// or damage, this is where it will be.
        /// </summary>
        public BodyPartState BodyPartState;
        /// <summary>
        /// If this is a body part that holds memory, this is where the memory
        /// will be.
        /// </summary>
        public Mind Mind { get; private set; }
        private BCallback listNameChangedB;
        public BCallback ListNameChangedB =>
            listNameChangedB ??= new BCallback();
        /// <summary>
        /// Could be used for saving anything. Current usages:
        /// - Ashes save a copy of Mind.LastCauseOfDeath here to display it
        /// </summary>
        public string CustomString;
        /// <summary>
        /// Could be used for saving anything. Current usages:
        /// - Ashes save a copy of Mind.LastTimeOfDeath here to display it
        /// </summary>
        public long CustomLong;
        /// <summary>
        /// Multiple objects can point to a single mindId! Example:
        /// being brains + being ashes
        /// </summary>
        public uint MindId;
        public Color Color = Consts.ColorWhite;
        /// <summary>
        /// For attached crawlers, the hat will have a link to a hidden crawler.
        /// Also for Equipment to remember who was last wearing it.
        /// </summary>
        public uint BeingDataId;
        /// <summary>
        /// Durable history flags. See ItemOwnership for who sets them and when.
        /// </summary>
        /// <see cref="ItemOwnership"/>
        public ObjFlags Flags;
        /// <summary>
        /// The being that most recently took ownership of this equipment item.
        /// Refreshed on every equip, so while an item is worn this is its wearer.
        /// BeingDataId cannot be used for this - AttackActionFacehug repurposes it
        /// to point at the hidden crawler under a facehugger hat.
        /// </summary>
        public uint LastOwnerId;
        /// <summary>
        /// Mind of the last owner. Tracked alongside LastOwnerId because a mind can
        /// move between bodies and a body can receive a new mind - matching on
        /// either one means the same person, not a change of hands.
        /// </summary>
        public uint LastOwnerMindId;
        public bool IsSecondHand =>
            (Flags & ObjFlags.SecondHand) == ObjFlags.SecondHand;
        public bool BelongedToDead =>
            (Flags & ObjFlags.BelongedToDead) == ObjFlags.BelongedToDead;
        /// <summary>
        /// Falling apart badly enough that wearing it is depressing. Not a flag -
        /// equipment has no condition track of its own, so this is derived from
        /// Quality every time and follows the tunable if a mod changes it.
        /// </summary>
        public bool IsTattered =>
            Item != null && Quality <= Tunables.TatteredQualityThreshold;
        /// <summary>
        /// When stat is InDevice, this should be set to device id. Validation will remove all objects that are in device without device id
        /// </summary>
        public int DeviceId;
        private bool hasStorableData;
        private bool isStorable;
        private int storageGroupH;
        public bool CanSell => (State == ObjState.Active || State == ObjState.Stored)
            && !Def.IgnoreMarket && Def.MatType == null;

        public bool IsStorable(out int groupH) {
            if (hasStorableData) {
                groupH = storageGroupH;
                return isStorable;
            }
            hasStorableData = true;
            if (Obj != null) {
                if (Obj.TryGetComponent<StorableObjComp>(out var so)) {
                    storageGroupH = so.GroupH;
                    isStorable = true;
                } else {
                    storageGroupH = 0;
                    isStorable = false;
                }
            } else {
                var cfg = Def.ComponentConfigFor(ComponentIdH.StorableObj, false);
                if (cfg != null) {
                    var g = cfg.GetString(PropertyIdH.Group);
                    storageGroupH = Hashes.S(g);
                    isStorable = true;
                } else {
                    storageGroupH = 0;
                    isStorable = false;
                }
            }
            groupH = storageGroupH;
            return isStorable;
        }

        public string Tooltip(GameState S, string prefix = null, bool skipCount = false) {
            if (BodyPart != null) {
                if (BodyPartState != null) {
                    var sb = Caches.StringBuilder2;
                    sb.Clear();
                    if (prefix != null) {
                        sb.AppendLine(prefix);
                    }
                    if (Reservation?.Surgery != null) {
                        Reservation.AppendTooltip(sb);
                        sb.Append(GlyphMap.BRHR);
                    }
                    // if ctrl is pressed we'll see details in extended tooltip
                    AddBodyPartData(sb, true);
                    return sb.ToString();
                    // return Def.ExtendedTooltip(S, sb.ToString(), skipCount: skipCount);
                } else {
                    D.Err("Body part state null for {0}", this);
                }
            }
            if (Item != null) {
                var sb = Caches.StringBuilder2;
                sb.Clear();
                var qLine = $"{T.NounQuality}: {Texts.Accented(Units.Percentage(Quality, clamp: false), false)}";
                if (prefix != null) {
                    prefix = $"{prefix}\n{qLine}";
                } else {
                    prefix = qLine;
                }
                sb.Append(prefix);
                AddOwnershipData(sb);
                Item.AddInfo(sb, Quality, false);
                return sb.ToString();
                // return Def.ExtendedTooltip(S, prefix: sb.ToString(), skipCount: skipCount);
            }
            D.Err("Wants tooltip for mat? {0}", this);
            return null;
            // return Def.ExtendedTooltip(S, prefix: prefix, skipCount: skipCount);
        }

        private void AddOwnershipData(StringBuilder sb) {
            // No wearer to judge against here, so this answers "has anyone worn it",
            // not "would it be second-hand to you". Weapons and body parts opt out.
            var origin = ItemOwnership.OriginLabel(this, null);
            if (origin == null) { return; }
            sb.AppendLine();
            sb.Append(origin);
        }

        public void AddBodyPartData(StringBuilder sb, bool addDetails) {
            if (Mind != null) {
                // var speciesName = Mind.Species?.Name ?? T.Unknown;
                Mind.AddMindInfo(sb);
                // if (Mind.Faction.IsPlayer || Mind.Faction.IsInternal) {
                //     sb.Append(Mind.GetObituary(Texts.Yellow(T.YearsAbbr_V1.F(Units.Num(Mind.Age)))));
                //     // sb.AppendFormat("{0}: {1} ({2})", T.NounMind,
                //     //     Texts.Green(Mind.Name.DisplayName), speciesName);
                // } else {
                //     sb.AppendFormat("{0}: {1} ({2}, {3})", T.NounMind,
                //         Texts.Red(Mind.Name.DisplayName),
                //             speciesName, Texts.Red(Mind.Faction.NameT));
                // }
                // sb.Append("\n\n");
            }
            sb.AppendFormat("{0}: {1}\n", T.NounQuality,
                Texts.Accented(Units.Percentage(BodyPartState.Quality, clamp: false), false));
            sb.AppendFormat("{0}: {1}", T.PartCondition,
                Texts.Accented(Units.Percentage(BodyPartState.Condition), false));
            if (BodyPartState.IsInfected) {
                sb.AppendLine();
                sb.Append(Texts.Red(T.AdjInfected));
            }
            if ((BodyPartState.Flags & BodyPartStateFlags.Crippled) == BodyPartStateFlags.Crippled) {
                sb.AppendLine();
                sb.Append(Texts.Red(T.BodyPartCrippled));
            }
            if (!Arrays.IsEmpty(BodyPartState.Wounds)) {
                foreach (var w in BodyPartState.Wounds) {
                    sb.Append("\n\n");
                    sb.Append(Wound.TooltipFor(w, BodyPart.IsBiological));
                }
            }
            if (!Arrays.IsEmpty(BodyPartState.Scars)) {
                sb.Append("\n\n");
                sb.Append(Texts.SubTitle(T.NounScars));
                var scars = BodyPartState.Scars;
                for (int i = 0; i < scars.Count; i++) {
                    if (i > 0) {
                        sb.Append('\n');
                    }
                    sb.Append(Scar.TooltipFor(scars[i]));
                }
            }
            if (addDetails) {
                BodyPart.AddInfo(sb, Mind?.Species, BodyPartState.Quality, full: false);
            }
        }

        public int PosIdx(GameState s, out ObjStorageComp storage) {
            if (State == ObjState.Active) {
                storage = null;
                if (Obj == null) {
                    D.Err("Obj is null but ObjData.State is Active! {0}", this);
                    return Pos.Invalid;
                }
                return Obj.PosIdx;
            } else if (State == ObjState.Stored) {
                storage = s.Components.Find<ObjStorageComp>(StorageId);
                if (storage == null) {
                    D.Err("ObjData is stored but storage does not exist! {0}", this);
                    return Pos.Invalid;
                }
                return storage.InPort;
            }
            D.Err("Trying to get position of an objdata with invalid state: {0} {1}", State, this);
            storage = null;
            return Pos.Invalid;
        }

        [SkipDumpInstance]
        public ObjReservation Reservation;

        public bool TryReserveForOperation(SurgicalOperation op) {
            #if DEBUG_OBJ_RESERVE
            D.Warn("Reserving {0} for op {1}", this, op);
            #endif
            if (op == null) {
                D.Err("Trying to reserve an ObjData for null surgical op! {0}", this);
                return false;
            }
            if (State != ObjState.Active && State != ObjState.Stored) {
                D.ErrSoft("Trying to reserve an inactive or unstored obj! {0}. Surgery: {1}. Reservation: {2}",
                    this, op, Reservation);
                return false;
            }

            if (Reservation != null) {
                D.Err("Trying to reserve an object for surgery that is reserved for something else! {0}. Surgery: {1}. reservation: {2}",
                    this, op, Reservation);
                return false;
            }

            // We'll allow that, surgery often isn't instant
            // if (Obj != null && Obj.Goal != null && Obj.Goal != goal) {
            //     D.Warn("Trying to reserve an objData that has Obj goal, possibly hauling {0} | {1}",
            //         Obj.Goal, this);
            //     AIGoal.Cancel(Obj.Goal, G.Ticks, goal.Name);
            // }

            Reservation = new ObjReservation(this, G.Ticks);
            Reservation.Surgery = op;
            return true;
        }

        public bool TryReserve(AIGoal goal) {
            if (goal == null) {
                D.Err("Trying to reserve an ObjData for null goal! {0}", this);
                return false;
            }
            #if DEBUG_OBJ_RESERVE
            D.Warn("Reserving {0} by {1}. Ctx: {2}", this, goal.Agent, goal);
            #endif
            if (State != ObjState.Active && State != ObjState.Stored) {
                D.ErrSoft("Trying to reserve an inactive or unstored obj! {0}. Trying goal: {1}. Reservation: {2}",
                    this, goal, Reservation);
                return false;
            }

            if (Reservation?.IsFor(goal) == false) {
                D.Err("Trying to reserve an object that is reserved for another goal! {0}. Trying goal: {1}. reservation: {2}",
                    this, goal, Reservation);
                return false;
            }

            if (Reservation?.Surgery != null) {
                // the surgery's own goal: attach it, but keep the part reserved for the
                // surgery, or a failed goal frees the part for other surgeries (STA-1584)
                Reservation.Goal = goal;
                return true;
            }


            if (Obj != null && Obj.Goal != null && Obj.Goal != goal) {
                D.Warn("Trying to reserve an objData that has Obj goal, possibly hauling {0} | {1}",
                    Obj.Goal, this);
                AIGoal.Cancel(Obj.Goal, G.Ticks, goal.Name);
            }

            Reservation = new ObjReservation(this, G.Ticks);
            Reservation.Goal = goal;
            return true;
        }

        public bool TryUnreserveForOperation(SurgicalOperation op) {
            #if DEBUG_OBJ_RESERVE
            D.Warn("Unreserving {0} for op {1}", this, op);
            #endif
            if (Reservation == null) {
                D.Err("Trying to unreserve obj data without surgical op reservation: {0}. Op {1}",
                    this, op);
                return true;
            }
            if (Reservation.Surgery != op) {
                D.Err("Trying to unreserve obj data with wrong surgical op reservation: {0}. Op {1}",
                    Reservation, op);
                return false;
            }
            Reservation.CancelGoal(T.AdRejCancelled);
            Reservation = null;
            return true;
        }

        public bool TryUnreserve(AIGoal goal) {
            if (goal == null) {
                if (Reservation?.IsForGoal() == true) {
                    Reservation = null;
                }
                return true;
            }
            #if DEBUG_OBJ_RESERVE
            D.Warn("Unreserving {0} by {1}. Ctx: {2}", this, goal?.Agent, goal);
            #endif
            if (Reservation?.IsFor(goal) == true) {
                Reservation = null;
                return true;
            }
            if (Reservation == null) {
                D.Err("Trying to unreserve obj data without goal reservation: {0}. Goal {1}",
                    this, goal);
                return true;
            }
            D.Err("Trying to unreserve something with a different reservation. {0}. Cur res: {1}. Trying to unreserve: {2}",
                this, Reservation, goal);
            return false;
        }
        public void ApplyForActiveObj() {
            if (Obj == null) { return; }
            // TODO
        }

        public override string ToString() {
            return $"ObjData[{State}|{Def}|{Obj}|StoreId:{StorageId}|Dev:{DeviceId}|Res:{Reservation}]";
        }

        public void Validate(GameState s) {
            if (Reservation?.Validate(s) == false) {
                Reservation = null;
            }
            if (State == ObjState.Active && Obj == null) {
                State = ObjState.Destroyed;
                D.Err("Active State but no Obj: {0}. Switching to Destroyed", this);
            }
            if (State == ObjState.InDevice && DeviceId == 0) {
                D.Err("ObjData is in device but device id is 0!. Setting state to destroyed: {0}", this);
                State = ObjState.Destroyed;
            }
        }

        public static IEnumerable<ObjData> SortedBy(GameState s,
                IEnumerable<ObjData> values,
                SortMode sortMode) {
            switch (sortMode) {
                case SortMode.QualityAsc: {
                    return values.OrderByDescending(v => {
                        return ObjQuality(v);
                    }).ThenBy((v) => v.SortName);
                }
                case SortMode.QualityDesc: {
                    return values.OrderBy(v => {
                        return ObjQuality(v);
                    }).ThenBy((v) => v.SortName);
                }
                case SortMode.ValueAsc: {
                    return values.OrderByDescending(v => {
                        return ObjValue(s, v);
                    })
                    .ThenByDescending((v) => {
                        var f = v.Quality;
                        if (v.BodyPartState != null) {
                            f *= v.BodyPartState.Condition;
                        }
                        return f;
                    })
                    .ThenBy((v) => v.SortName);
                }
                case SortMode.ValueDesc: {
                    return values.OrderBy(v => {
                        return ObjValue(s, v);
                    })
                    .ThenBy((v) => {
                        var f = v.Quality;
                        if (v.BodyPartState != null) {
                            f *= v.BodyPartState.Condition;
                        }
                        return f;
                    })
                    .ThenBy((v) => v.SortName);
                }
                case SortMode.Name:
                default: {
                    return values.OrderBy(v => v.SortName);
                }
            }
        }

        private static float ObjQuality(ObjData v) {
            if (v.BodyPartState != null) {
                return v.BodyPartState.Quality;
            }
            return v.Quality;
        }

        public static float ObjValue(GameState s, ObjData v) {
            var marketItem = MarketItem.FindFor(v.Def);
            if (marketItem == null) {
                return 0f;
            }
            if (v.BodyPartState != null) {
                var cond = v.BodyPartState.Condition;
                var q = v.BodyPartState.Quality;
                return marketItem.Value * cond * q;
            } else {
                return s.Market.Adjusted(marketItem).Value * v.Quality;
            }
        }
        public string SortName {
            get {
                var name = Def.SearchNameT;
                if (BodyPart?.HoldsMind != false) {
                    if (Mind != null) {
                        name = $"{name}{Strings.ToSearchString(Mind.Name.ShortName)}";
                    } else {
                        name = $"{name}!{T.Empty}";
                    }
                }
                return name;
            }
        }
        public string ListName {
            get {
                var name = Def.NameT;
                if (BodyPart != null) {
                    name = BodyUtils.DecoratedPartName(name, BodyPart, BodyPartState, Mind);
                    name = AddQualityToName(name);
                } else if (Mind != null) { // ashes?
                    // No quality suffix: ashes are born with a random one that means nothing
                    name = Mind.DecorateListName(name);
                }
                return name;
            }
        }

        private string AddQualityToName(string name) {
            return $"{name} {Texts.GreyParentheses(Units.Percentage(Quality, clamp: false))}";
        }

        public void SetMind(Mind mind) {
            bool changed = Mind != mind;
            Mind = mind;
            if (mind != null) {
                MindId = mind.Id;
                if (mind.ObjId != 0 && mind.ObjId != Id) {
                    // validate that ashes never overwrite brain / core
                    var curObj = A.S.Query.FindObjData.Ask(mind.ObjId);
                    if (curObj != null && curObj.BodyPart == null && curObj.IsAvailableIgnoreReservation) {
                        // D.Warn("Refusing to link mind to an object {0} while mind is linked to ashes: {1}. Mind: {2}",
                        //     this, curObj, mind);
                        return;
                    }
                }
                mind.ObjId = Id;
                if (BodyPart?.HoldsMind == true) {
                    mind.Jobs?.ClearLacksAbilities();
                }
            } else {
                MindId = 0;
            }
            if (changed) {
                listNameChangedB?.Invoke();
            }
        }

        public bool MatchesSearchQuery(string searchQuery) {
            if (searchQuery == null) { return true; }
            if (Def.SearchNameT.Contains(searchQuery)) { return true; }
            if (Mind != null) {
                if (Strings.ToSearchString(Mind.Name.FullName)
                        .Contains(searchQuery)) {
                    return true;
                }
            }
            return false;
        }
    }
}
