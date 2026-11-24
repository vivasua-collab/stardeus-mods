using System;
using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.Systems.Mental;
using Game.Systems.Policies;
using Game.Systems.UIUX;
using Game.Utils;
using Game.CodeGen;
using KL.Collections;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Systems.BodyParts;
using Game.Systems.Creatures;
using KL.Grid;
using System.Linq;

namespace Game.Systems.Items {
    public sealed class ObjsSys : GameSystem, ISaveableSpecial {
        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new ObjsSys());
        }

        private readonly Dictionary<Def, int> knownStorables = new();

        public const string SysId = "ObjsSys";
        public override string Id => SysId;

        private bool isLoaded;
        public readonly List<ObjData> ActiveObjs = new();
        public readonly Dictionary<uint, ObjData> All = new();
        private readonly Dictionary<Def, int> countsByDef = new();
        private readonly Dictionary<Def, int> equippedCountsByDef = new();
        private readonly Dictionary<Def, BInt> objCountsB = new();
        private ObjStorage storage;
        // Every haul target asks for haulable objects in turn, and walking all objects for
        // each of them dominated haul ordering in big colonies, so the walk is shared by
        // everyone asking within a tick
        private readonly List<ObjData> haulableObjs = new();
        private long haulableObjsTick = -1;

        public override void Unload() {
            storage = null;
            haulableObjs.Clear();
            haulableObjsTick = -1;
        }

        protected override void OnInitialize() {
            ObjData.LastId = 0;
            S.Sig.AfterLoadState.AddListener(OnLoadState);
            S.Query.GenInventoryItem.SetResponder(GenerateInventoryItem);
            storage = new ObjStorage(this, S);
        }
        private ObjData BestInstallableBodyPart(BodyPartSlot slot) {
            ObjData bestPart = null;
            bool isBio = slot.Body.Owner.Species.IsBiological;
            foreach (var o in All.Values) {
                if (o.BodyPart == null) { continue; }
                if (!o.IsAvailable) { continue; }
                // an automatic pick, unlike a part the player chose for a surgery
                if (o.Obj?.StorableObj?.IsForbidden == true) { continue; }
                // broken / destroyed parts are not installable
                if (!o.BodyPartState.IsUsableSelf) { continue; }
                // let's not install irreparable parts
                if (o.BodyPartState.WoundSeverity > Severity.Critical) {
                    continue;
                }
                // only offer robotic parts to robots and biological parts to biologicals
                if (o.BodyPart.IsBiological != isBio) {
                    continue;
                }
                if (o.BodyPart.IsCompatibleWith(slot, out var isVanilla)) {
                    if (o.Mind != null && o.Mind.Faction?.IsPlayer == false) {
                        // don't install core chips of raiders
                        continue;
                    }
                    if (bestPart == null) {
                        bestPart = o;
                        continue;
                    }
                    bestPart = slot.BetterPart(bestPart, o);
                }
            }
            return bestPart;
        }

        private bool InstallableBodyParts(BodyPartSlot slot, List<ObjData> res) {
            if (res.Count > 0) {
                D.Err("res is not empty for AvailableBodyParts query");
                res.Clear();
            }
            foreach (var o in All.Values) {
                if (o.BodyPart == null) { continue; }
                if (!o.IsAvailable) { continue; }
                // broken / destroyed parts are not installable
                if (!o.BodyPartState.IsUsableSelf) { continue; }
                if (o.BodyPart.IsCompatibleWith(slot, out _)) {
                    res.Add(o);
                }
            }
            return res.Count > 0;
        }

        private ObjData TryFindObjData(uint id) {
            if (All.TryGetValue(id, out var res)) {
                return res;
            }
            return null;
        }

        private ObjData FindObjData(uint id) {
            if (!isLoaded) {
                throw new Exception("Querying obj data too early!");
            }
            if (All.TryGetValue(id, out var obj)) {
                return obj;
            }
            D.Err("Could not find ObjData by id: {0}", id);
            return null;
        }

        private int FindStoredObjData(int storageId, ObjData[] res, string searchStr,
                ObjData.SortMode sortMode) {
            int found = 0;
            var searchQuery = Strings.ToSearchString(searchStr, true);
            IEnumerable<ObjData> all;
            if (sortMode == ObjData.SortMode.Unsorted) {
                all = All.Values;
            } else {
                all = ObjData.SortedBy(S, All.Values, sortMode);
            }
            foreach (var obj in all) {
                if (obj.StorageId == storageId) {
                    if (!obj.MatchesSearchQuery(searchQuery)) {
                        continue;
                    }
                    res[found++] = obj;
                    if (found == res.Length) {
                        break;
                    }
                }
            }
            return found;
        }
        private int FindCompatibleEquipment(AIAgentComp agent, int invSlotId, ObjData[] res,
                HashSet<uint> blacklist, HashSet<int> storageBlacklist) {
            int found = 0;
            int max = res.Length;
            // This is the fallback path for AIActionFindBestEquipment, so it has to
            // apply the same policy constraints. invSlotId 0 means "any slot", which
            // has no single policy - the filter then allows everything.
            var slotType = invSlotId == 0 ? null : InventorySlotType.FastGet(invSlotId);
            var filter = EquipmentPolicyFilter.For(
                S.Query.EquipmentPolicyFor.Ask(agent.BeingData, slotType),
                agent.BeingData);
            foreach (var obj in All) {
                var objData = obj.Value;
                var item = objData.Item;
                if (item == null) { continue; }
                if (!objData.IsAvailable) { continue; }
                if (invSlotId != 0 && item.SlotType.IdH != invSlotId) { continue; }
                if (objData.Obj?.Goal?.IsByPlayer == true) {
                    continue;
                }
                if (blacklist?.Contains(objData.Id) == true) { continue; }
                if (objData.State == ObjState.Stored
                        && storageBlacklist?.Contains(objData.StorageId) == true) {
                    continue;
                }
                if (!filter.Allows(objData)) { continue; }
                if (item.IsCompatibleWith(agent.BeingData)) {
                    res[found++] = objData;
                    if (found == max) {
                        return found;
                    }
                }
            }
            return found;
        }

        // Deliberately unfiltered by policy: this backs the manual "equip this item"
        // panel, and the player must always be able to override their own policy.
        private int FindCompatibleEquipmentUI(AIAgentComp agent, int invSlotId, List<ObjData> res) {
            return FindEquipmentUI(agent, invSlotId, res, compatible: true);
        }

        // The other half of the manual picker: what this being can't use, so the player
        // sees why the sniper rifle in storage is missing from the list above.
        private int FindIncompatibleEquipmentUI(AIAgentComp agent, int invSlotId, List<ObjData> res) {
            return FindEquipmentUI(agent, invSlotId, res, compatible: false);
        }

        private int FindEquipmentUI(AIAgentComp agent, int invSlotId, List<ObjData> res,
                bool compatible) {
            int found = 0;
            foreach (var obj in All) {
                var objData = obj.Value;
                var item = objData.Item;
                if (item == null) { continue; }
                if (!objData.IsAvailable) { continue; }
                if (invSlotId != 0 && item.SlotType.IdH != invSlotId) { continue; }
                if (item.IsCompatibleWith(agent.BeingData) == compatible) {
                    res.Add(objData);
                    found++;
                }
            }
            return found;
        }

        private ObjData FindEquipmentOfColor(AIAgentComp agent, InventoryItemDef def, Color color) {
            float closestDist = float.MaxValue;
            ObjData closest = null;
            var agentPos = agent.PosIdx;
            foreach (var obj in All) {
                var objData = obj.Value;
                var item = objData.Item;
                if (item != def) { continue; }
                if (objData.Color != color) { continue; }
                if (!objData.IsAvailable) { continue; }
                var objPosIdx = objData.PosIdx(S, out _);
                var sqrDist = Pos.SqrDistance(agentPos, objPosIdx);
                if (sqrDist < 5 * 5) { return objData; }
                if (sqrDist < closestDist) {
                    closestDist = sqrDist;
                    closest = objData;
                }
            }
            return closest;
        }

        private void OnLoadState(GameState state) {
            RestoreData();
            state.Clock.OnTick.AddListener(OnTick);

            S.Query.GenObjFull.SetResponder(GenerateObj);
            S.Query.ActivateObjData.SetResponder(Activate);
            S.Query.ObjCountByDef.SetResponder(CountByDef);
            S.Query.FindObjs.SetResponder(FindObjs);
            S.Query.ObjCounts.SetResponder(CountsByDef);
            S.Query.DeactivateObjData.SetResponder(Deactivate);
            S.UIQuery.GetBindableObjCountFor.SetResponder(GetBindableObjCount);
            S.UIQuery.FindCompatibleEquipmentUI.SetResponder(FindCompatibleEquipmentUI);
            S.UIQuery.FindIncompatibleEquipmentUI.SetResponder(FindIncompatibleEquipmentUI);
            S.Query.FindCompatibleEquipment.SetResponder(FindCompatibleEquipment);
            S.Query.FindEquipmentOfColor.SetResponder(FindEquipmentOfColor);
            S.Query.FindObjData.SetResponder(FindObjData);
            S.Query.TryFindObjData.SetResponder(TryFindObjData);
            S.Query.FindStoredObjData.SetResponder(FindStoredObjData);
            S.Query.PersistObjData.SetResponder(MakePersistent);
            S.Query.InstallableBodyParts.SetResponder(InstallableBodyParts);
            S.Query.BestInstallableBodyPart.SetResponder(BestInstallableBodyPart);
            S.Query.BestAvailableEquipment.SetResponder(BestAvailableEquipment);
            S.Query.FindHaulableUnstoredObjs.SetResponder(FindHaulableUnstoredObjs);
            S.Query.GetAllObjs.SetResponder(GetAllObjs);
            S.Query.ListKnownStorableObjDefsForGroup.SetResponder(ListKnownStorables);

            isLoaded = true;
            Ready.Set(WhenH.ObjsLoaded, true);
        }

        private int FindObjs(List<ObjData> res, Func<ObjData, bool> filter) {
            int found = 0;
            var allBeings = S.Query.AllBeingData.Ask();
            foreach (var o in All.Values) {
                if (o.State == ObjState.Destroyed) { continue; }
                if (o.State == ObjState.InDevice) { continue; }
                if (o.State == ObjState.Equipped) {
                    // who equipped it?
                    if (allBeings.TryGetValue(o.BeingDataId, out var being)) {
                        if (!being.IsPlayer) { continue; }
                    }
                }
                if (filter(o)) {
                    found++;
                    res.Add(o);
                }
            }
            return found;
        }

        private Dictionary<Def, int> CountsByDef() {
            return countsByDef;
        }

        private ObjData BestAvailableEquipment(InventoryItemSlot slot,
                HashSet<uint> blacklist, HashSet<int> storageBlacklist) {
            var owner = slot.Inventory.Owner;
            // Resolved once, then reused for every candidate below.
            var filter = EquipmentPolicyFilter.For(
                S.Query.EquipmentPolicyFor.Ask(owner, slot.Type), owner);
            // If something was equipped, it is our best item right now - unless the
            // policy forbids it, in which case anything allowed has to beat it.
            ObjData bestItem = filter.Allows(slot.ObjData) ? slot.ObjData : null;
            foreach (var o in All.Values) {
                if (!o.IsAvailable) { continue; }
                if (o.Item == null) { continue; }
                if (o.Item.SlotType != slot.Type) {
                    continue;
                }
                // relocation and such
                if (o.Obj?.Goal?.IsByPlayer == true) {
                    continue;
                }
                if (!o.Item.IsCompatibleWith(owner)) { continue; }
                if (!filter.Allows(o)) { continue; }
                if (blacklist?.Contains(o.Id) == true) { continue; }
                if (o.State == ObjState.Stored
                        && storageBlacklist?.Contains(o.StorageId) == true) {
                    continue;
                }
                if (bestItem == null) {
                    bestItem = o;
                    continue;
                }
                bestItem = slot.BetterItem(bestItem, o);
            }
            if (bestItem == slot.ObjData) {
                // we didn't find a better item
                return null;
            }
            return bestItem;
        }

        private Dictionary<uint, ObjData> GetAllObjs() {
            return All;
        }

        private int ListKnownStorables(List<Def> result, int[] groups) {
            foreach (var def in knownStorables.OrderBy(v => v.Key.NameT)) {
                if (Arrays.Contains(groups, def.Value)) {
                    result.Add(def.Key);
                }
            }
            return result.Count;
        }

        private int CountByDef(Def def) {
            if (!countsByDef.TryGetValue(def, out var cnt)) {
                cnt = 0;
            }
            return cnt;
        }

        private BInt GetBindableObjCount(Def def) {
            if (!objCountsB.TryGetValue(def, out var bInt)) {
                var cnt = countsByDef.TryGetValue(def, out var c) ? c : 0;
                bInt = new BInt(cnt, capacity: 64);
                objCountsB[def] = bInt;
            }
            return bInt;
        }

        private int FindHaulableUnstoredObjs(ObjData[] res, Func<ObjData, bool> accepts) {
            var ticks = G.Ticks;
            long canHaulAfter = ticks - Consts.TicksPerHour;
            if (haulableObjsTick != ticks) {
                haulableObjsTick = ticks;
                haulableObjs.Clear();
                foreach (var o in All.Values) {
                    if (IsHaulableUnstored(o, canHaulAfter, true)) {
                        haulableObjs.Add(o);
                    }
                }
            }
            int found = 0;
            for (int i = 0; i < haulableObjs.Count; i++) {
                var o = haulableObjs[i];
                // an earlier asker this tick may have ordered it hauled already
                if (!IsHaulableUnstored(o, canHaulAfter, false)) { continue; }
                if (accepts(o)) {
                    res[found++] = o;
                    if (found == res.Length) {
                        break;
                    }
                }
            }
            return found;
        }

        private static bool IsHaulableUnstored(ObjData o, long canHaulAfter, bool logErrors) {
            if (o.State != ObjState.Active || o.Reservation?.IsActive == true) {
                return false;
            }
            if (o.Obj == null) {
                #if !PRODUCTION_BUILD
                if (logErrors) {
                    D.Err("ObjData is active, but Obj is null! {0}", o);
                }
                #endif
                return false;
            }
            if (o.Obj.StorableObj == null) { return false; }
            if (o.Obj.StorableObj.IsForbidden) { return false; }
            if (o.Obj.StorableObj.SpawnedAt > canHaulAfter) { return false; }
            return o.Obj.Goal == null;
        }

        private void ValidateObjs() {
            List<uint> toRemove = null;
            List<ObjData> toEject = null;
            foreach (var obj in All.Values) {
                if (obj.State == ObjState.Active && obj.Obj == null) {
                    bool removed = ActiveObjs.Remove(obj);
                    D.Err("Removing Active objdata without Obj instance! Removed from active: {0} | {1} | {2}",
                        removed, obj, D.DumpInstance(obj));
                    toRemove ??= new();
                    toRemove.Add(obj.Id);
                }
                if (obj.State == ObjState.Stored) {
                    if (obj.Quality == 0f || obj.BodyPartState?.IsDestroyed == true) {
                        toEject ??= new();
                        toEject.Add(obj);
                    }
                }
            }
            if (toEject != null) {
                Ready.When(WhenH.AllSystemsLoaded,  () => {
                    D.ErrSoft("Ejecting {0} irreparable objects", toEject.Count);
                    foreach (var obj in toEject) {
                        var st = S.Components.Find<ObjStorageComp>(obj.StorageId);
                        if (st == null) {
                            D.Err("No storage for obj: {0}", obj);
                            continue;
                        }
                        S.Query.UnstoreObj.Ask(obj, st);
                    }
                });
            }

            if (toRemove != null) {
                D.Err("Removing {0} broken objects", toRemove.Count);
                foreach (var id in toRemove) {
                    All.Remove(id);
                }
            }
        }

        private readonly HashSet<uint> toRemove = new();
        private int objCountChecksum = 0;

        private void OnTick(long ticks) {
            countsByDef.Clear();
            equippedCountsByDef.Clear();
            toRemove.Clear();
            var allBeings = S.Query.AllBeingData.Ask();
            foreach (var o in All.Values) {
                if (o.State == ObjState.Destroyed) {
                    D.Err("Destroyed obj found in All objs on Tick: {0}", o);
                    toRemove.Add(o.Id);
                    continue;
                }
                if (o.State == ObjState.Equipped) {
                    // who equipped it?
                    if (allBeings.TryGetValue(o.BeingDataId, out var being)) {
                        // colonized person, item should be destroyed
                        if ((being.Body.Flags & BodyFlags.Missing) == BodyFlags.Missing) {
                            // A deserter may come back, and wears its gear when it does
                            // (AIBreakdownActionAbandonShip). Away, it is not the colony's.
                            if (being.Mind?.Role == ColonyRole.Deserter) {
                                continue;
                            }
                            D.Warn("Removing item belonging to someone who is missing: {0} | {1}",
                                o, being);
                            toRemove.Add(o.Id);
                            // Load repairs (a leaked dead being, beings lost with their storage)
                            // mark Missing without destroying the inventory like Eliminate does.
                            // A slot left holding the item saved an id that no longer exists
                            being.Inventory?.ForgetItem(o);
                            continue;
                        }
                        if (!being.IsPlayer) {
                            continue;
                        }
                    }
                }
                if (o.State != ObjState.InDevice) {
                    if (!countsByDef.TryGetValue(o.Def, out var cnt)) {
                        cnt = 0;
                    }
                    countsByDef[o.Def] = cnt + 1;
                    if (o.State == ObjState.Equipped) {
                        if (!equippedCountsByDef.TryGetValue(o.Def, out var eqCnt)) {
                            eqCnt = 0;
                        }
                        equippedCountsByDef[o.Def] = eqCnt + 1;
                    }
                }
                o.Validate(S);
            }
            if (toRemove.Count > 0) {
                foreach (var id in toRemove) {
                    if (All.TryGetValue(id, out var destroyed)) {
                        if (ActiveObjs.Remove(destroyed)) {
                            D.Err("Active objects contained destroyed obj! {0}",
                                destroyed);
                        }
                        RelinkMindOf(destroyed);
                        All.Remove(id);
                    }
                }
                toRemove.Clear();
            }
            var sum = ObjCountChecksum();
            if (objCountChecksum != sum) {
                objCountChecksum = sum;
                S.Sig.ObjCountChanged.Send(countsByDef, equippedCountsByDef);
                foreach (var bind in objCountsB) {
                    bind.Value.Set(countsByDef.TryGetValue(bind.Key, out var c) ? c : 0);
                }
            }
        }
        private int ObjCountChecksum() {
            int sum = 0;
            foreach (var kv in countsByDef) {
                int keyId = kv.Key.IdH;
                var amt = kv.Value;
                sum = unchecked(sum * 31 + keyId + amt);
            }
            foreach (var kv in equippedCountsByDef) {
                sum = unchecked(sum * 31 + kv.Key.IdH * 17 + kv.Value);
            }
            return sum;
        }

        public void SaveSpecial(SystemsDataSpecial sd) {
            sd.Objs ??= new();
            sd.Objs.LastId = ObjData.LastId;
            sd.Objs.Objs = ObjDataData.Serialize(All);
            if (knownStorables.Count > 0) {
                sd.Objs.KnownStorables = new (int, int)[knownStorables.Count];
                int i = 0;
                foreach (var known in knownStorables) {
                    sd.Objs.KnownStorables[i++] = (known.Key.IdH, known.Value);
                }
            }
        }

        private ObjsSysData data;

        public void LoadSpecial(SystemsDataSpecial sd) {
            data = sd?.Objs;
        }

        private void RestoreData() {
            if (data == null) {
                MigrateLegacyObjs();
                return;
            }
            if (data.KnownStorables != null) {
                foreach (var s in data.KnownStorables) {
                    var def = The.Defs.FastGet(s.Item1);
                    if (def != null) {
                        var groupH = s.Item2;
                        knownStorables[def] = groupH;
                    }
                }
            }
            ObjData.LastId = data.LastId;
            if (data.Objs != null) {
                var l = data.Objs.Length;
                for (int i = 0; i < l; i++) {
                    var d = data.Objs[i];
                    if (d == null) {
                        D.Err("Null ObjDataData entry at index {0} while loading objs! Skipping", i);
                        continue;
                    }
                    Obj o;
                if (d.ObjId != 0) {
                    o = S.Objs.Find(d.ObjId);
                } else {
                    o = null;
                }
                if (ObjDataData.TryDeserialize(S, d, o, out var od, true)) {
                    if (od.State == ObjState.Destroyed) {
                        D.Err("Skipping load of a destroyed object! {0}", od);
                        continue;
                    }
                    All[od.Id] = od;
                    MarkAsKnownStorable(od);
                    if (od.State == ObjState.InDevice) {
                        if (od.DeviceId == 0) {
                            D.Err("ObjData is in device but no device id! Destroying: {0}", od);
                            od.State = ObjState.Destroyed;
                            continue;
                        }
                    }
                    if (od.State == ObjState.Active) {
                        ActiveObjs.Add(od);
                        od.ApplyForActiveObj();
                    }
                }
            }
            }
            MigrateLegacyObjs();
            ValidateObjs();
            storage?.ValidateAll(All);
            Ready.When(WhenH.AllSystemsLoaded, LinkReservations);
        }

        private void LinkReservations() {
            foreach (var o in All.Values) {
                o.Reservation?.Load(S);
            }
        }

        private void MigrateLegacyObjs() {
            foreach (var o in S.Objs.All) {
                if (o.Data == null) {
                    var data = GenerateObj(o.Definition, Rng.Unseeded);
                    data.State = ObjState.Active;
                    o.Data = data;
                    data.Obj = o;
                    Activate(data);
                    D.Err("Activating legacy object without data! {0}", o);
                }
            }
        }

        private static readonly Dictionary<InventoryItemDef, float> genItemOptions = new();
        private static readonly Dictionary<InventoryItemDef, float> genJetSuitOptions = new();

        private ObjData GenerateInventoryItem(Rng rng, InventoryItemSlot slot,
                GenItemFlags flags) {
            var slotType = slot.Type;
            if (slotType.IdH == InventorySlotIdH.Weapon) {
                if (!flags.HasFlag(GenItemFlags.Weaponized)) {
                    return null;
                }
            }
            if (slotType.IdH == InventorySlotIdH.Suit || slotType.IdH == InventorySlotIdH.Helmet) {
                if ((flags & (GenItemFlags.SpaceReady | GenItemFlags.JetSuit)) == GenItemFlags.None) {
                    return null;
                }
            }
            if (slotType.IdH == InventorySlotIdH.Clothing) {
                // TODO check if it is a nudist
                if (!flags.HasFlag(GenItemFlags.Casual)) {
                    return null;
                }
            }
            if (slotType.IdH == InventorySlotIdH.Hat) {
                // TODO check if its a hat person
                if (!flags.HasFlag(GenItemFlags.Casual)) {
                    return null;
                }
                if (rng.Chance(0.75f)) {
                    return null;
                }
            }
            // Higher raid gear tiers bias weighting toward higher-CombatValue items (better
            // weapons/armor) instead of picking purely by Rarity. Items with no combat value
            // (hats, clothing) are unaffected. Uses an exponent rather than a linear multiplier
            // on CombatValue: a linear "Rarity * (1 + CombatValue * mul)" term saturates almost
            // immediately once CombatValue * mul dominates the "+1" (tier 1 already lands within
            // ~3% of what an infinite multiplier would produce), so tier 2 barely differed from
            // tier 1. Raising CombatValue to a power keeps biasing the distribution further
            // toward the top of the range as the exponent grows, so tiers stay separated.
            // Exponent 0 at tier 0 reproduces today's Rarity-only weighting exactly, for every
            // caller (colonists, derelict crews, gifts) that never sets these flags.
            var combatValueExponent = 0f;
            if (flags.HasFlag(GenItemFlags.EliteArmed)) {
                combatValueExponent = 3f;
            } else if (flags.HasFlag(GenItemFlags.WellArmed)) {
                combatValueExponent = 1.5f;
            }

            genItemOptions.Clear();
            genJetSuitOptions.Clear();
            // A jet suit is wanted, not required: it used to be added to the pool before the
            // SpawnRequirements check below, so a JetSuit request always produced one even on
            // day 1, and a raid could not tell "no jet suit unlocked yet" from "has one". Now
            // jet suits are gated like every other item; when none is eligible the slot falls
            // back to a regular suit, and the caller reads the outcome off the Fly ability.
            var wantsJetSuit = slotType.IdH == InventorySlotIdH.Suit
                && (flags & GenItemFlags.JetSuit) == GenItemFlags.JetSuit;
            var owner = slot.Inventory.Owner;
            // Raiders roll against their own situation, see ItemSpawnSituation
            var situation = ItemSpawnSituation.For(S);
            foreach (var item in InventoryItemDef.All.Values) {
                if (slotType == item.SlotType && item.Rarity > 0f) {
                    if (!item.IsCompatibleWith(owner)) { continue; }

                    // We generate inventory in setup screen, where map is not there yet
                    if (item.SpawnRequirements != null && S.Map?.Situation != null) {
                        if (!situation.Matches(item.SpawnRequirements, S.Difficulty.Level)) {
                            // #if !PRODUCTION_BUILD
                            // D.Warn("Skipping inventory item gen for situation mismatch: {0} | {1}",
                            //     item, D.DumpInstance(item.SpawnRequirements));
                            // #endif
                            continue;
                        }
                    }
                    var weight = item.Rarity;
                    if (combatValueExponent > 0f && item.CombatValue > 0f) {
                        weight *= Mathf.Pow(item.CombatValue, combatValueExponent);
                    }
                    if (wantsJetSuit && Arrays.ContainsSafe(item.AddsAbilities, AbilityId.Fly)) {
                        genJetSuitOptions.TryAdd(item, weight);
                    }
                    genItemOptions.TryAdd(item, weight);
                }
            }
            var options = wantsJetSuit && genJetSuitOptions.Count > 0
                ? genJetSuitOptions : genItemOptions;
            if (rng.TryWeightedFrom(options, out var chosenItem)) {
                var obj = GenerateObj(chosenItem.Def, rng);
                if (flags.HasFlag(GenItemFlags.EliteArmed)) {
                    obj.Quality = rng.RangeSmooth(0.9f, 1f);
                } else if (flags.HasFlag(GenItemFlags.WellArmed)) {
                    obj.Quality = rng.RangeSmooth(0.8f, 1f);
                }
                return obj;
            }
            return null;
        }

        /// <summary>
        /// Creates (generates) a full being data without creating an instance
        /// </summary>
        /// <returns>new BeingData with everything, procgen</returns>
        private ObjData GenerateObj(Def def, Rng rng) {
            var d = new ObjData();
            d.Def = def;
            GenerateMissingParts(d, rng);
            return d;
        }

        private bool GenerateMissingParts(ObjData data, Rng rng) {
            data.Quality = rng.RangeSmooth(0.7f, 1f);
            if (data.Def != null) {
                if (data.Item == null && InventoryItemDef.TryForDef(data.Def.IdH, out var item)) {
                    data.Item = item;
                    // if its an inventory item, we will generate the color if necessary
                    if (!string.IsNullOrEmpty(item.ColorGroupId)) {
                        var palette = Palette.ForGroup(item.ColorGroupId);
                        if (palette == null) {
                            D.Err("Invalid palette {0} in ColorGroupId for item: {1}",
                                item.ColorGroupId, data.Def.Id);
                        } else {
                            data.Color = rng.From(palette)?.Color ?? Consts.ColorWhite;
                        }
                    }
                }
                if (data.BodyPart == null && BodyPartDef.TryForDef(data.Def.IdH, out var bp)) {
                    data.BodyPart = bp;
                    data.BodyPartState = BodyPartState.Used(rng.RangeSmooth(0.1f, 0.9f), rng);
                }
            }
            return true;
        }

        private bool Activate(ObjData data) {
            if (data.Obj == null) {
                D.Err("Trying to activate a data while data.Obj == null: {0}",
                    D.DumpInstance(data));
                return false;
            }
            if (data.State == ObjState.Destroyed) {
                D.Err("Trying to activate a destroyed obj: {0}",
                    D.DumpInstance(data));
                return false;
            }
            MakePersistent(data);
            data.State = ObjState.Active;
            ActiveObjs.Add(data);
            data.ApplyForActiveObj();
            return true;
        }
        private bool MakePersistent(ObjData data) {
            if (data.State == ObjState.Destroyed) {
                D.Err("Trying to make a destroyed obj persistent! {0}", data);
                return false;
            }
            if (data.Id == 0) {
                data.Id = ++ObjData.LastId;
                All.Add(data.Id, data);
                MarkAsKnownStorable(data);
                return true;
            }
            return false;
        }

        private void MarkAsKnownStorable(ObjData data) {
            if (!knownStorables.ContainsKey(data.Def)) {
                if (data.IsStorable(out var groupH)) {
                    knownStorables.Add(data.Def, groupH);
                } else {
                    knownStorables.Add(data.Def, 0);
                }
            }
        }

        private bool Deactivate(ObjData data, ObjState state) {
            if (state == ObjState.Active) {
                D.Err("Trying to deactivate a obj with target state: {0}", state);
                return false;
            }
            ActiveObjs.RemoveRef(data);
            data.Obj = null;
            data.State = state;
            if (state == ObjState.Destroyed) {
                RelinkMindOf(data);
                if (!All.Remove(data.Id)) {
                    D.Err("Failed removing destroyed obj! {0}", data);
                }
            }
            return true;
        }

        /// <summary>
        /// An obj holding a mind is leaving All for good. The mind outlives it, so its
        /// back-link has to go somewhere else: another obj can still hold the same mind
        /// (ashes + brain), and ashes are preferred, same as in ObjData.SetMind.
        /// </summary>
        private void RelinkMindOf(ObjData data) {
            var mind = data.Mind;
            if (mind == null) { return; }
            if (mind.ObjId != data.Id) { return; }
            mind.ObjId = FindOtherObjIdFor(mind, data);
        }

        private uint FindOtherObjIdFor(Mind mind, ObjData except) {
            uint bodyPartId = 0;
            foreach (var o in All.Values) {
                if (o == except || o.Mind != mind) { continue; }
                if (o.State == ObjState.Destroyed) { continue; }
                // ashes win over brains / cores
                if (o.BodyPart == null) { return o.Id; }
                bodyPartId = o.Id;
            }
            return bodyPartId;
        }
    }
}
