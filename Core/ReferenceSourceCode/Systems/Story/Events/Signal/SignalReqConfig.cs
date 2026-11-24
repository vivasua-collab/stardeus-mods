using System;
using System.Collections.Generic;
using Game.Data;
using Game.Data.Economy;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Story {
    [Serializable]
    public class SignalReqConfig {
        [SelfInit]
        private static void Init() {
            // We want them to be fresh in unity
            byWeight.Clear();
            byGroup.Clear();
            All.Clear();
            isLoaded = false;
        }

        private static readonly Dictionary<SignalReqConfig, float> byWeight = new();
        private static readonly Dictionary<string, List<SignalReqConfig>> byGroup = new();
        public static readonly List<SignalReqConfig> All = new();

        private static bool isLoaded;

        public string Id;
        public float Chance;
        // when this is set to true, we cannot do anything until we choose an option
        public bool IsImmediateChoice;
        public bool IsRetryAvailable;
        public string RequestMsgKey;
        // this would be, if root is "foo", translation system would load all "foo.1, foo.2, etc"
        // and pick one at random
        public string RequestMsgKeyRoot;
        public string FulfillMsgKey;
        public string FulfillMsgKeyRoot;
        public string RefuseMsgKey;
        public string RefuseMsgKeyRoot;
        public float MoneyPercentFrom;
        public float MoneyPercentTo;
        public string[] RequestDefIds;
        public string[] RequestDefMarketGroups;
        public int RequestQuantity;
        public int RequestQuantityFrom;
        public int RequestQuantityTo;
        public int RewardQuantity;
        public int RewardQuantityFrom;
        public int RewardQuantityTo;
        public string[] RewardDefIds;
        public string[] RewardDefMarketGroups;
        public bool IsRewardKnown;
        // If it's set to 0.1-0.2 and you have 1000 starcreds, you will get ~150 starcreds

        public static void Load() {
            if (isLoaded) { return; }
            isLoaded = true;
            var items = The.ModLoader.LoadObjects<SignalReqConfig>(
                "Config/SignalRequests");
            foreach (var item in items) {
                var idSplit = item.Id.Split('/');
                if (idSplit.Length < 2) {
                    D.Err("SignalReqConfig does not belong to any group! Skipping: {0}", item.Id);
                    continue;
                }
                All.Add(item);

                if (!byGroup.TryGetValue(idSplit[0], out var list)) {
                    list = new List<SignalReqConfig>();
                    byGroup.Add(idSplit[0], list);
                }
                list.Add(item);
                if (item.Chance < 0.00001f) {
                    D.Err("Chance is not set for SignalReqConfig! Defaulting to 0.5: {0}",
                        item.Id);
                    item.Chance = 0.5f;
                }
                ValidateDefIds(item, item.RewardDefIds);
                ValidateDefIds(item, item.RequestDefIds);
                ValidateMarketGroups(item, item.RequestDefMarketGroups);
                ValidateMarketGroups(item, item.RewardDefMarketGroups);
            }
        }

        private static void ValidateDefIds(SignalReqConfig item, string[] ids) {
            if (Arrays.IsEmpty(ids)) { return; }
            foreach (var id in ids) {
                if (The.Defs.TryGet(id) == null) {
                    D.Err("Invalid def id in SignalReqConfig: {0} -> {1}", item.Id, id);
                }
            }
        }
        private static void ValidateMarketGroups(SignalReqConfig item, string[] groups) {
            if (Arrays.IsEmpty(groups)) { return; }
            foreach (var g in groups) {
                if (MarketGroup.Find(g) == null) {
                    D.Err("Invalid marker group in SignalReqConfig: {0} -> {1}", item.Id, g);
                }
            }
        }

        public static SignalReqConfig RandomFor(string id, Rng rng) {
            Load();
            if (!byGroup.TryGetValue(id, out var list)) {
                D.Err("There are no items in Config/SignalRequests/{0}", id);
                return null;
            }
            byWeight.Clear();
            foreach (var i in list) {
                byWeight.Add(i, i.Chance);
            }

            if (rng.TryWeightedFrom(byWeight, out var item)) {
                return item;
            }
            D.Err("Failed to get SignalRequestItem. Check Config/SignalRequests/{0} folder", id);
            return null;
        }

        public Def RandomRequestDef(Rng rng, Def except = null) {
            Caches.DefSet.Clear();
            LoadMarketGroupDefs(RequestDefMarketGroups);
            LoadDefIds(RequestDefIds);
            if (except != null) {
                return rng.FromExcept(Caches.DefSet, except);
            }
            if (Caches.DefSet.Count == 0) {
                D.Err("Could not pick a random request def for {0}. Except: {1}", Id, except);
                return null;
            }
            return rng.From(Caches.DefSet);
        }
        public Def RandomRewardDef(Rng rng, Def except = null) {
            Caches.DefSet.Clear();
            LoadMarketGroupDefs(RewardDefMarketGroups);
            LoadDefIds(RewardDefIds);
            if (except != null) {
                return rng.FromExcept(Caches.DefSet, except);
            }
            if (Caches.DefSet.Count == 0) {
                D.Err("Could not pick a random reward def for {0}. Except: {1}", Id, except);
                return null;
            }
            return rng.From(Caches.DefSet);
        }

        private void LoadDefIds(string[] ids) {
            if (Arrays.IsEmpty(ids)) { return; }
            foreach (var id in ids) {
                var def = The.Defs.Get(id);
                if (def == null) {
                    D.Err("{0} signal config contains missing RequestDefId: {1}",
                        Id, id);
                } else {
                    Caches.DefSet.Add(def);
                }
            }
        }

        private void LoadMarketGroupDefs(string[] mgs) {
            if (Arrays.IsEmpty(mgs)) { return; }
            foreach (var mg in mgs) {
                var group = MarketGroup.Find(mg);
                if (group == null) {
                    D.Err("RequestDefMarketGroups in {0} signal config contains missing group id: {1}",
                        Id, mg);
                    continue;
                }
                foreach (var mi in MarketItem.CFDs) {
                    if (Arrays.ContainsSafe(mi.Value.GroupsH, group.IdH)) {
                        var miDef = mi.Value.Def;
                        if (miDef == null) {
                            D.Err("MarketItem def is null: {0}", mi.Value.Id);
                        } else {
                            Caches.DefSet.Add(miDef);
                        }
                    }
                }
            }
        }

    }
}
