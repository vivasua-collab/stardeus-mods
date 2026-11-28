using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Game.CodeGen;
using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.Systems.Crafting;
using Game.Systems.Items;
using Game.Systems.UIUX;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.DisasemblerEquipment {
    internal static class Ids {
        internal const string Harmony = "DisasemblerEquipment";
        internal const string RulesKey = "DEQ.Rules";
        internal const string QueueKey = "DEQ.Queue";
        internal const int Weapon = 0;
        internal const int Clothing = 1;
        internal const int New = 1;
        internal const int SecondHand = 2;
        internal const int DeadMans = 4;
        internal const int AllOrigins = New | SecondHand | DeadMans;
        internal const long ScanInterval = Consts.TicksPerHour;
    }

    [Serializable]
    public sealed class EquipmentRecycleRule {
        public int Category;
        public int DefIdH;
        public bool Enabled = true;
        public bool AnyQuality = true;
        public float MaxQuality = 1f;
        public int OriginMask = Ids.AllOrigins;

        public EquipmentRecycleRule Clone() => new EquipmentRecycleRule {
            Category = Category, DefIdH = DefIdH, Enabled = Enabled,
            AnyQuality = AnyQuality, MaxQuality = MaxQuality, OriginMask = OriginMask
        };
    }

    [Serializable]
    public sealed class EquipmentQueueEntry {
        public int DefIdH;
        public int Category;
        public float Quality;
    }

    internal static class EquipmentDefs {
        internal static int Category(Def def) {
            var cfg = def?.ComponentConfigFor(ComponentIdH.StorableObj, false);
            if (cfg == null) { return -1; }
            var group = cfg.GetString(PropertyIdH.Group);
            if (group == "Weapon") { return Ids.Weapon; }
            if (group == "Clothing") { return Ids.Clothing; }
            return -1;
        }

        internal static List<Def> List(int category) {
            var result = new List<Def>();
            foreach (var def in The.Defs.Defs.Values) {
                if (def == null || def.IsAbstract || Category(def) != category
                        || !InventoryItemDef.TryForDef(def.IdH, out _)
                        || !def.HasComponent(ComponentIdH.Disposable)) { continue; }
                result.Add(def);
            }
            result.Sort((a,b) => string.Compare(a.NameT,b.NameT,StringComparison.CurrentCulture));
            return result;
        }

        internal static bool Matches(EquipmentRecycleRule rule, ObjData data) {
            if (rule == null || !rule.Enabled || data?.Def == null || data.Item == null
                    || Category(data.Def) != rule.Category) { return false; }
            if (rule.DefIdH != 0 && data.Def.IdH != rule.DefIdH) { return false; }
            if (!rule.AnyQuality && data.Quality > rule.MaxQuality) { return false; }
            if (rule.Category == Ids.Clothing) {
                int flag;
                switch (ItemOwnership.BucketFor(data)) {
                    case ItemOriginBucket.New: flag = Ids.New; break;
                    case ItemOriginBucket.SecondHand: flag = Ids.SecondHand; break;
                    case ItemOriginBucket.DeadMans: flag = Ids.DeadMans; break;
                    default: return false;
                }
                if ((rule.OriginMask & flag) == 0) { return false; }
            }
            return true;
        }
    }

    internal static class Rules {
        private static readonly Dictionary<int,List<EquipmentRecycleRule>> ByEntity = new();

        internal static List<EquipmentRecycleRule> Get(DisposerComp d) {
            if (!ByEntity.TryGetValue(d.EntityId,out var rules)) {
                rules = new List<EquipmentRecycleRule>();
                ByEntity[d.EntityId] = rules;
            }
            return rules;
        }

        internal static bool HasEnabled(DisposerComp d) => Get(d).Any(r => r != null && r.Enabled);
        internal static bool Matches(DisposerComp d, ObjData data) =>
            Get(d).Any(r => EquipmentDefs.Matches(r,data));

        internal static void Add(DisposerComp d, EquipmentRecycleRule rule, int index) {
            var rules = Get(d);
            if (index >= 0 && index < rules.Count) { rules[index] = rule.Clone(); }
            else { rules.Add(rule.Clone()); }
        }

        internal static void Remove(DisposerComp d, int index) {
            var rules = Get(d);
            if (index >= 0 && index < rules.Count) { rules.RemoveAt(index); }
        }

        internal static void Save(DisposerComp d) {
            if (d.Data == null) { return; }
            var rules = Get(d);
            if (rules.Count == 0) { d.Data.RemoveStringSet(Ids.RulesKey); return; }
            d.Data.SetStringSet(Ids.RulesKey, rules.Select(Json.Dump).ToArray());
        }

        internal static void Load(DisposerComp d, ComponentData data) {
            var rules = new List<EquipmentRecycleRule>();
            foreach (var json in data?.GetStringSet(Ids.RulesKey,null) ?? Array.Empty<string>()) {
                if (string.IsNullOrEmpty(json)) { continue; }
                if (Json.TryDeserialize<EquipmentRecycleRule>(json,out var rule,out var error,strict:false)) {
                    if (rule != null && (rule.Category == Ids.Weapon || rule.Category == Ids.Clothing)) {
                        rules.Add(rule);
                    }
                } else {
                    D.Warn("[DisasemblerEquipment] Could not load rule: {0}",error);
                }
            }
            ByEntity[d.EntityId] = rules;
        }

        internal static void Copy(DisposerComp from, DisposerComp to) {
            if (from == null || to == null || from == to) { return; }
            ByEntity[to.EntityId] = Get(from).Select(r => r.Clone()).ToList();
        }

        internal static void Clear(DisposerComp d) {
            if (d != null) { ByEntity.Remove(d.EntityId); }
        }

        internal static string Summary(EquipmentRecycleRule r) {
            var type = r.DefIdH == 0 ? "All types" : The.Defs.FastGet(r.DefIdH)?.NameT ?? "Unknown type";
            var q = r.AnyQuality ? "Any quality" : $"≤ {Units.Percentage(r.MaxQuality)}";
            if (r.Category == Ids.Weapon) { return $"{type} — {q}"; }
            var origin = new List<string>();
            if ((r.OriginMask & Ids.New) != 0) { origin.Add("New"); }
            if ((r.OriginMask & Ids.SecondHand) != 0) { origin.Add("Second Hand"); }
            if ((r.OriginMask & Ids.DeadMans) != 0) { origin.Add("From dead"); }
            return $"{type} — {q} — {string.Join(", ",origin)}";
        }
    }

    internal static class QueueData {
        private static readonly Dictionary<int,List<EquipmentQueueEntry>> Queues = new();
        private static readonly Dictionary<uint,int> PendingSource = new();
        private static readonly Dictionary<int,int> PendingCounts = new();
        [ThreadStatic] private static EquipmentQueueEntry active;
        [ThreadStatic] private static int activeDisposer;

        private static List<EquipmentQueueEntry> Get(DisposerComp d) {
            if (!Queues.TryGetValue(d.EntityId,out var list)) {
                list = new List<EquipmentQueueEntry>();
                Queues[d.EntityId] = list;
            }
            return list;
        }

        private static EquipmentQueueEntry Make(DisposableComp disposable) {
            var data = disposable?.Obj?.Data;
            var cat = EquipmentDefs.Category(data?.Def);
            if (data?.Item == null || cat < 0) { return null; }
            return new EquipmentQueueEntry { DefIdH=data.Def.IdH, Category=cat, Quality=data.Quality };
        }

        private static void Align(List<EquipmentQueueEntry> list, int count) {
            while (list.Count > count) { list.RemoveAt(0); }
            while (list.Count < count) { list.Insert(0,null); }
        }

        internal static void Loaded(DisposerComp d, DisposableComp item, int before) {
            var list = Get(d);
            Align(list,before);
            list.Add(Make(item));
            if (item?.Obj?.Data != null) { ClearPending(item.Obj.Data.Id); }
        }

        internal static void AfterTick(DisposerComp d) => Align(Get(d),d.Queue.Count);

        internal static void Begin(DisposerComp d, DisposerComp.Disposable item) {
            active = null;
            activeDisposer = 0;
            var list = Get(d);
            if (list.Count > d.Queue.Count) {
                var entry = list[0];
                list.RemoveAt(0);
                if (entry != null && entry.DefIdH == item.DefIdH) {
                    active = entry;
                    activeDisposer = d.EntityId;
                }
            }
        }

        internal static bool IsActiveEquipment(DisposerComp d, Craftable cr) =>
            active != null && activeDisposer == d.EntityId && cr?.ProductDef != null
            && active.DefIdH == cr.ProductDef.IdH
            && (active.Category == Ids.Weapon || active.Category == Ids.Clothing);

        internal static float ActiveQuality => active?.Quality ?? 0f;
        internal static void End() { active = null; activeDisposer = 0; }

        internal static void Save(DisposerComp d) {
            if (d.Data == null) { return; }
            var list = Get(d);
            Align(list,d.Queue.Count);
            var values = list.Select(e => e == null ? "-" : Json.Dump(e)).ToArray();
            if (values.Length == 0) { d.Data.RemoveStringSet(Ids.QueueKey); }
            else { d.Data.SetStringSet(Ids.QueueKey,values); }
        }

        internal static void Load(DisposerComp d, ComponentData data) {
            var list = new List<EquipmentQueueEntry>();
            foreach (var json in data?.GetStringSet(Ids.QueueKey,null) ?? Array.Empty<string>()) {
                if (string.IsNullOrEmpty(json) || json == "-") { list.Add(null); continue; }
                if (Json.TryDeserialize<EquipmentQueueEntry>(json,out var entry,out var error,strict:false)) {
                    list.Add(entry);
                } else {
                    D.Warn("[DisasemblerEquipment] Could not load queue metadata: {0}",error);
                    list.Add(null);
                }
            }
            Align(list,d.Queue.Count);
            Queues[d.EntityId] = list;
        }

        internal static bool IsPending(uint id) => PendingSource.ContainsKey(id);
        internal static int PendingFor(DisposerComp d) =>
            PendingCounts.TryGetValue(d.EntityId,out var n) ? n : 0;

        internal static void MarkPending(uint id, DisposerComp d) {
            ClearPending(id);
            PendingSource[id] = d.EntityId;
            PendingCounts[d.EntityId] = PendingFor(d) + 1;
        }

        internal static void ClearPending(uint id) {
            if (!PendingSource.TryGetValue(id,out var source)) { return; }
            PendingSource.Remove(id);
            if (PendingCounts.TryGetValue(source,out var n)) {
                if (n <= 1) { PendingCounts.Remove(source); }
                else { PendingCounts[source] = n - 1; }
            }
        }

        internal static void CleanPending(GameState state) {
            foreach (var id in PendingSource.Keys.ToArray()) {
                var data = state.Query.TryFindObjData.Ask(id);
                if (data == null || data.State != ObjState.Active || data.Obj == null
                        || !data.Obj.TryGetComponent<DisposableComp>(out var d) || !d.HasAnyGoal) {
                    ClearPending(id);
                }
            }
        }

        internal static void MoveQueue(DisposerComp from, DisposerComp to) {
            Queues[to.EntityId] = new List<EquipmentQueueEntry>(Get(from));
            Queues.Remove(from.EntityId);
        }

        internal static void Remove(DisposerComp d) {
            if (d == null) { return; }
            Queues.Remove(d.EntityId);
            PendingCounts.Remove(d.EntityId);
            foreach (var id in PendingSource.Where(p => p.Value == d.EntityId).Select(p => p.Key).ToArray()) {
                PendingSource.Remove(id);
            }
        }
    }

    internal static class Output {
        internal static void Produce(DisposerComp d, Craftable cr) {
            var ingredients = cr?.Ingredients;
            if (ingredients == null || ingredients.Length == 0) {
                D.Warn("[DisasemblerEquipment] No ingredients in Craftable for {0}; no materials returned.",
                    cr?.ProductDef);
                return;
            }

            // 100% quality = 1.0; base return is 50% of recipe cost.
            var factor = 0.5f * Mathf.Max(0f,QueueData.ActiveQuality);
            var result = new List<Mat>();
            for (int i=0;i<ingredients.Length;i++) {
                var mat = ingredients[i];
                mat.StackSize = Mathf.FloorToInt(mat.StackSize * factor);
                if (mat.StackSize <= 0 || mat.Type == null) { continue; }
                ConstructableComp.AddDeconstructedMat(result,mat,d.S.Rng);
            }
            if (result.Count == 0) { return; }
            var output = result.ToArray();
            d.S.CmdQ.Enqueue(new CmdSpawnMats(output,d.Tile.Transform.OutPort,0.1f));
            foreach (var mat in output) {
                if (mat.Type != null && mat.StackSize > 0) { d.S.Sig.MatProduced.Send(mat.Type,mat.StackSize); }
            }
        }
    }

    internal static class AutoScanner {
        private static readonly Dictionary<int,long> LastScan = new();

        internal static void Tick(DisposerComp d) {
            if (d == null || d.SlotDesignation != SlotDesignationH.Disposer_Robotic
                    || !d.Tile.IsConstructed || !Rules.HasEnabled(d)) { return; }
            var now = G.Ticks;
            if (LastScan.TryGetValue(d.EntityId,out var last) && now-last < Ids.ScanInterval) { return; }
            LastScan[d.EntityId] = now;

            var state = d.S;
            QueueData.CleanPending(state);
            if (!state.Sys.Slots.HasFreeFor(d.SlotDesignation)) { return; }
            var free = d.MaxCapacity - d.Queue.Count - QueueData.PendingFor(d);
            if (free <= 0) { return; }

            var all = state.Query.GetAllObjs.Ask();
            if (all == null) { return; }
            foreach (var data in all.Values) {
                if (free <= 0) { break; }
                if (data == null || !data.IsAvailable
                        || (data.State != ObjState.Active && data.State != ObjState.Stored)
                        || QueueData.IsPending(data.Id) || !Rules.Matches(d,data)) { continue; }

                ObjStorageComp storage = null;
                if (data.State == ObjState.Active) {
                    var obj = data.Obj;
                    if (obj == null || (obj.TryGetComponent<StorableObjComp>(out var storable) && storable.IsForbidden)
                            || (obj.TryGetComponent<DisposableComp>(out var dc) && dc.HasAnyGoal)) {
                        continue;
                    }
                } else {
                    storage = state.Components.Find<ObjStorageComp>(data.StorageId);
                    if (storage == null) { continue; }
                    storage.Eject(data,true);
                    if (data.State != ObjState.Active || data.Obj == null) { continue; }
                }

                var candidate = data.Obj;
                if (candidate == null || !candidate.TryGetComponent<DisposableComp>(out var disposable)
                        || disposable.HasAnyGoal) {
                    if (storage != null) { storage.Store(null,data,null); }
                    continue;
                }

                if (disposable.TryOrderDispose(false,out _)) {
                    QueueData.MarkPending(data.Id,d);
                    free--;
                } else if (storage != null) {
                    storage.Store(null,data,null);
                }
            }
        }

        internal static void Remove(DisposerComp d) {
            if (d != null) { LastScan.Remove(d.EntityId); }
        }
    }

    public static class DisasemblerEquipmentModInit {
        [RuntimeInitializeOnLoadMethod]
        private static void Init() {
            if (Harmony.HasAnyPatches(Ids.Harmony)) { return; }
            new Harmony(Ids.Harmony).PatchAll(typeof(DisasemblerEquipmentModInit).Assembly);
            D.Warn("[DisasemblerEquipment] Harmony patches registered.");
        }
    }

    [HarmonyPatch(typeof(DisposerComp),"OnSave")]
    internal static class SavePatch {
        private static void Postfix(DisposerComp __instance) {
            Rules.Save(__instance);
            QueueData.Save(__instance);
        }
    }

    [HarmonyPatch(typeof(DisposerComp),"OnLoad")]
    internal static class LoadPatch {
        private static void Postfix(DisposerComp __instance, ComponentData data) {
            Rules.Load(__instance,data);
            QueueData.Load(__instance,data);
        }
    }

    [HarmonyPatch(typeof(DisposerComp),nameof(DisposerComp.CopyConfigTo))]
    internal static class CopyPatch {
        private static void Postfix(DisposerComp __instance, ICopyableComp target) {
            if (target is DisposerComp other) { Rules.Copy(__instance,other); }
        }
    }

    [HarmonyPatch(typeof(DisposerComp),nameof(DisposerComp.RelocateTo))]
    internal static class RelocatePatch {
        private static void Postfix(DisposerComp __instance, Entity target) {
            if (target != null && target.TryGetComponent<DisposerComp>(out var other)) {
                QueueData.MoveQueue(__instance,other);
            }
        }
    }

    [HarmonyPatch(typeof(DisposerComp),nameof(DisposerComp.OnRemove))]
    internal static class RemovePatch {
        private static void Postfix(DisposerComp __instance) {
            Rules.Clear(__instance);
            QueueData.Remove(__instance);
            AutoScanner.Remove(__instance);
        }
    }

    [HarmonyPatch(typeof(DisposerComp),nameof(DisposerComp.LoadDisposable))]
    internal static class LoadItemPatch {
        private static void Prefix(DisposerComp __instance, out int __state) {
            __state = __instance.Queue.Count;
        }
        private static void Postfix(DisposerComp __instance, DisposableComp disposable,
                bool __result, int __state) {
            if (__result) { QueueData.Loaded(__instance,disposable,__state); }
        }
    }

    [HarmonyPatch]
    internal static class AfterTickPatch {
        private static MethodBase TargetMethod() => AccessTools.Method(typeof(DisposerComp),"AfterTickGrid");
        private static void Prefix(DisposerComp __instance, out int __state) {
            __state = __instance.Queue.Count;
        }
        private static void Postfix(DisposerComp __instance, int __state) {
            if (__instance.Queue.Count < __state) { QueueData.AfterTick(__instance); }
            AutoScanner.Tick(__instance);
        }
    }

    [HarmonyPatch]
    internal static class BeginDisposePatch {
        private static MethodBase TargetMethod() => AccessTools.Method(typeof(DisposerComp),"DoDispose");
        private static void Prefix(DisposerComp __instance, DisposerComp.Disposable disposable) {
            QueueData.Begin(__instance,disposable);
        }
        private static void Postfix() { QueueData.End(); }
    }

    [HarmonyPatch]
    internal static class OutputPatch {
        private static MethodBase TargetMethod() => AccessTools.Method(typeof(DisposerComp),"DisposeCraftable");
        private static bool Prefix(DisposerComp __instance, Craftable cr) {
            if (!QueueData.IsActiveEquipment(__instance,cr)) { return true; }
            Output.Produce(__instance,cr);
            return false;
        }
    }

    [HarmonyPatch(typeof(DisposerTab),nameof(DisposerTab.LoadContent))]
    internal static class DisposerTabPatch {
        private static void Postfix(DisposerTab __instance, WGroup group) {
            var d = AccessTools.Field(typeof(DisposerTab),"disposer")?.GetValue(__instance) as DisposerComp;
            var lifetime = AccessTools.Field(typeof(BaseEntityTab),"lifetime")?.GetValue(__instance) as Lifetime;
            if (d == null || lifetime == null || d.SlotDesignation != SlotDesignationH.Disposer_Robotic) { return; }
            RuleUI.Append(__instance,d,lifetime,group);
        }
    }

    internal static class RuleUI {
        internal static void Append(DisposerTab tab, DisposerComp d, Lifetime life, WGroup group) {
            var block = UISpawn.Block(life,group,group.Container.childCount);
            int i = 0;
            UISpawn.Widget<WText>(life,UIPrefabId.WContent2Header,block.Container,i++)
                .Load("Automatic equipment recycling");
            SpawnList(tab,d,life,block.Container,ref i,Ids.Weapon,"Weapons",IconId.CCog);
            SpawnList(tab,d,life,block.Container,ref i,Ids.Clothing,"Clothing and armor",IconId.CRecycling);
        }

        private static void SpawnList(DisposerTab tab,DisposerComp d,Lifetime life,
                RectTransform container,ref int i,int category,string title,string icon) {
            UISpawn.Widget<WText>(life,UIPrefabId.WContent2Header,container,i++).Load(title);
            var rules = Rules.Get(d);
            for (int n=0;n<rules.Count;n++) {
                var rule = rules[n];
                if (rule == null || rule.Category != category) { continue; }
                UISpawn.Widget<WIconText>(life,UIPrefabId.WContent2Text,container,i++)
                    .Load(icon,Rules.Summary(rule));
                var edit = UISpawn.Widget<WButtonText>(life,UIPrefabId.WFooterItem_DeleteButton,container,i++);
                int index=n;
                edit.Load("Edit rule",()=>OpenEditor(tab,d,life,edit,category,index));
                var remove = UISpawn.Widget<WButtonText>(life,UIPrefabId.WFooterItem_DeleteButton,container,i++);
                remove.Load("Remove rule",()=>{ Rules.Remove(d,index); A.S.UIQuery.ReloadEntityTab.Ask(tab); });
            }
            var add = UISpawn.Widget<WButtonText>(life,UIPrefabId.WFooterItem_DeleteButton,container,i++);
            add.Load("Add rule",()=>OpenEditor(tab,d,life,add,category,-1));
        }

        private static void OpenEditor(DisposerTab tab,DisposerComp d,Lifetime parent,
                WBase anchor,int category,int editingIndex) {
            var rules = Rules.Get(d);
            var rule = editingIndex >= 0 && editingIndex < rules.Count ? rules[editingIndex].Clone()
                : new EquipmentRecycleRule { Category=category, Enabled=true, AnyQuality=true,
                    MaxQuality=1f, OriginMask=Ids.AllOrigins };
            var panel = A.S.UIQuery.GetEntitySubPanel.Ask();
            if (panel.IsShowing) { panel.Close(); }
            var life = panel.CreateLifetime(parent);
            panel.SetTitle(editingIndex < 0 ? "Add recycling rule" : "Edit recycling rule");
            var block = UISpawn.Block(life,panel.Container,0);
            int i=0;
            var defs = EquipmentDefs.List(category);
            var options = new List<string> { "All types" };
            options.AddRange(defs.Select(x=>x.NameT));
            var type = UISpawn.Widget<WDropdownWithIcon>(life,UIPrefabId.WContent2Dropdown,block.Container,i++);
            type.Icon.Load(IconId.CFilter);
            int selected=0;
            if (rule.DefIdH != 0) {
                var at=defs.FindIndex(x=>x.IdH==rule.DefIdH);
                if (at>=0) { selected=at+1; } else { rule.DefIdH=0; }
            }
            type.Dropdown.Load(options,selected,v=>rule.DefIdH=v<=0?0:defs[v-1].IdH);

            var enabled = UISpawn.Widget<WToggleWithIconText2>(life,UIPrefabId.WContent2Text2Toggle,block.Container,i++);
            enabled.Item.Load(IconId.CCog,"Rule active",rule.Enabled?T.Yes:T.No);
            enabled.Toggle.Load(rule.Enabled,v=>{rule.Enabled=v;enabled.Item.SetText2(v?T.Yes:T.No);});

            var any = UISpawn.Widget<WToggleWithIconText2>(life,UIPrefabId.WContent2Text2Toggle,block.Container,i++);
            any.Item.Load(IconId.CPercentGreen,"Any quality",rule.AnyQuality?T.Yes:T.No);
            var slider = UISpawn.Widget<WSliderWithIconText2>(life,UIPrefabId.WContent2Text2Slider,block.Container,i++);
            rule.MaxQuality=Mathf.Clamp(rule.MaxQuality,0f,3f);
            slider.Item.Load(IconId.CBookmark,"Maximum quality",rule.AnyQuality?"∞":Units.Percentage(rule.MaxQuality));
            slider.Slider.Load(rule.MaxQuality,v=>{rule.MaxQuality=Mathf.Clamp(v,0f,3f);slider.Item.SetText2(Units.Percentage(rule.MaxQuality));},0f,3f);
            slider.Slider.SetInteractable(!rule.AnyQuality);
            any.Toggle.Load(rule.AnyQuality,v=>{
                rule.AnyQuality=v; any.Item.SetText2(v?T.Yes:T.No);
                slider.Slider.SetInteractable(!v);
                slider.Item.SetText2(v?"∞":Units.Percentage(rule.MaxQuality));
            });

            if (category == Ids.Clothing) {
                OriginToggle(life,block.Container,ref i,rule,Ids.New,IconId.CSparkles,"New");
                OriginToggle(life,block.Container,ref i,rule,Ids.SecondHand,IconId.CRecycling,"Second Hand");
                OriginToggle(life,block.Container,ref i,rule,Ids.DeadMans,IconId.CSkull,"Belonged to dead");
            }

            var save=UISpawn.Widget<WButtonText>(life,UIPrefabId.WFooterItem_DeleteButton,block.Container,i++);
            save.Load("Save rule",()=>{Rules.Add(d,rule,editingIndex);panel.Close();A.S.UIQuery.ReloadEntityTab.Ask(tab);});
            var cancel=UISpawn.Widget<WButtonText>(life,UIPrefabId.WFooterItem_DeleteButton,block.Container,i++);
            cancel.Load("Cancel",()=>panel.Close());
            panel.Show(anchor);
        }

        private static void OriginToggle(Lifetime life,RectTransform container,ref int i,
                EquipmentRecycleRule rule,int mask,string icon,string label) {
            var row=UISpawn.Widget<WToggleWithIconText2>(life,UIPrefabId.WContent2Text2Toggle,container,i++);
            bool on=(rule.OriginMask&mask)!=0;
            row.Item.Load(icon,label,on?T.Yes:T.No);
            row.Toggle.Load(on,v=>{
                if(v) rule.OriginMask|=mask; else rule.OriginMask&=~mask;
                row.Item.SetText2(v?T.Yes:T.No);
            });
        }
    }
}
