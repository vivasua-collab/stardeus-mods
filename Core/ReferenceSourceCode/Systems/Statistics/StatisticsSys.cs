using System.Collections.Generic;
using Game;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.UIUX;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Statistics {
    public sealed class StatisticsSys : GameSystem, ISaveableSpecial {
        public const string SysId = "StatisticsSys";
        public override string Id => SysId;

        private enum EntityKind : byte { Mat = 0, Obj = 1, Being = 2 }

        private sealed class DefStats {
            public SortedDictionary<long, int> Stock = new();
            public SortedDictionary<long, int> Produced = new();
            public SortedDictionary<long, int> Consumed = new();
            public int PendingProduced;
            public int PendingConsumed;
            // stock compression state
            public int LastWrittenStockValue = -1; // -1 = never written
            public long LastStockWriteTick;        // tick of last written stock entry
            // live-today view: Stock + current live value at G.Ticks
            public SortedDictionary<long, int> StockView;
            public long StockViewTick = -1;        // tick when live entry was last updated
            public bool StockDirty;               // set when Stock is modified; triggers full StockView rebuild
            public EntityKind Kind;
            public Def Def;
        }

        // scratch list for StockView stale-entry removal — never shared with Caches.Longs
        private readonly List<long> viewScratch = new();

        // synthetic fallback for GetStockHistory when fewer than 2 data points exist;
        // reused buffer - callers must consume before the next Ask
        private readonly SortedDictionary<long, int> synthStock = new();

        private readonly Dictionary<int, DefStats> defStats = new();
        private readonly Dictionary<int, BInt> prodRateB = new(); // keyed by Def.IdH
        private readonly Dictionary<int, BInt> consRateB = new();
        private long nextFlushTick;

        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new StatisticsSys());
        }

        protected override void OnInitialize() {
            S.Sig.AfterLoadState.AddListener(OnLoadSave);
        }

        private void OnLoadSave(GameState state) {
            nextFlushTick = -1;
            state.Clock.OnTick.AddListener(OnTick);
            state.Sig.MatProduced.AddListener(OnMatProduced);
            state.Sig.MatConsumed.AddListener(OnMatConsumed);
            state.Sig.BeingAdded.AddListener(OnBeingAdded);
            state.Sig.BeingDied.AddListener(OnBeingDied);
            state.Sig.ObjProduced.AddListener(OnObjProduced);
            state.Sig.ObjConsumed.AddListener(OnObjConsumed);

            S.Query.StockHistory.SetResponder(GetStockHistory);
            S.Query.DefProductionHistory.SetResponder(GetProductionHistory);
            S.Query.DefConsumptionHistory.SetResponder(GetConsumptionHistory);
            S.Query.DefProductionRate.SetResponder(GetProductionRate);
            S.Query.DefConsumptionRate.SetResponder(GetConsumptionRate);
            S.UIQuery.GetBindableProductionRate.SetResponder(GetBindableProductionRate);
            S.UIQuery.GetBindableConsumptionRate.SetResponder(GetBindableConsumptionRate);
        }

        private void OnTick(long ticks) {
            if (!Ready.Gen) { return; }
            if (ticks < nextFlushTick) { return; }
            nextFlushTick = ticks + Consts.TicksPerHour;
            FlushHour(ticks);
        }

        private void OnMatProduced(MatType mat, int amount) {
            if (!Ready.Gen) { return; }
            if (mat?.Def == null) { return; }
            GetOrCreate(mat.Def, EntityKind.Mat).PendingProduced += amount;
        }

        private void OnMatConsumed(MatType mat, int amount) {
            if (!Ready.Gen) { return; }
            if (mat?.Def == null) { return; }
            GetOrCreate(mat.Def, EntityKind.Mat).PendingConsumed += amount;
        }

        private void OnBeingAdded(Being being) {
            if (!Ready.Gen) { return; }
            var def = being?.Definition;
            if (def == null) { return; }
            GetOrCreate(def, EntityKind.Being).PendingProduced += 1;
        }

        private void OnBeingDied(BeingData data) {
            if (!Ready.Gen) { return; }
            var def = data?.Species?.Def;
            if (def == null) { return; }
            GetOrCreate(def, EntityKind.Being).PendingConsumed += 1;
        }

        private void OnObjProduced(Def def, int amount) {
            if (!Ready.Gen) { return; }
            if (def == null) { return; }
            GetOrCreate(def, EntityKind.Obj).PendingProduced += amount;
        }

        private void OnObjConsumed(Def def, int amount) {
            if (!Ready.Gen) { return; }
            if (def == null) { return; }
            GetOrCreate(def, EntityKind.Obj).PendingConsumed += amount;
        }

        private DefStats GetOrCreate(Def def, EntityKind kind) {
            if (!defStats.TryGetValue(def.IdH, out var stats)) {
                stats = new DefStats { Kind = kind, Def = def };
                defStats[def.IdH] = stats;
            }
            return stats;
        }

        private void FlushHour(long tick) {
            foreach (var kv in defStats) {
                var stats = kv.Value;

                // --- rates: written every hour ---
                stats.Produced[tick] = stats.PendingProduced;
                stats.Consumed[tick] = stats.PendingConsumed;
                stats.PendingProduced = 0;
                stats.PendingConsumed = 0;

                // --- stock: written at most once per day, with compression ---
                var dayElapsed = tick - stats.LastStockWriteTick >= Consts.TicksPerDay;
                if (dayElapsed || stats.LastWrittenStockValue < 0) {
                    var currentStock = GetCurrentStock(stats);
                    if (stats.LastWrittenStockValue < 0) {
                        // first-ever write: just record it
                        stats.Stock[tick] = currentStock;
                        stats.LastWrittenStockValue = currentStock;
                        stats.LastStockWriteTick = tick;
                        stats.StockDirty = true;
                    } else if (currentStock != stats.LastWrittenStockValue) {
                        // value changed: if gap > 1 day, backfill yesterday at the old value
                        // so the chart shows the flat period before the change
                        var gap = tick - stats.LastStockWriteTick;
                        if (gap > Consts.TicksPerDay) {
                            stats.Stock[tick - Consts.TicksPerDay] = stats.LastWrittenStockValue;
                        }
                        stats.Stock[tick] = currentStock;
                        stats.LastWrittenStockValue = currentStock;
                        stats.LastStockWriteTick = tick;
                        stats.StockDirty = true;
                    }
                    // if unchanged: skip the entry entirely (compression)
                }

                var stockCountBefore = stats.Stock.Count;
                TrimHistory(stats.Stock, tick, Consts.TicksPerWeek);
                if (stats.Stock.Count != stockCountBefore) { stats.StockDirty = true; }
                TrimHistory(stats.Produced, tick, Consts.MatRateHistoryDuration);
                TrimHistory(stats.Consumed, tick, Consts.MatRateHistoryDuration);
            }
            // update rate bindables for any defs the UI is watching
            foreach (var kv in prodRateB) {
                var rate = defStats.TryGetValue(kv.Key, out var ps)
                    ? SumLastHours(ps.Produced, 24) : 0;
                kv.Value.Set(rate);
            }
            foreach (var kv in consRateB) {
                var rate = defStats.TryGetValue(kv.Key, out var cs)
                    ? SumLastHours(cs.Consumed, 24) : 0;
                kv.Value.Set(rate);
            }
        }

        private int GetCurrentStock(DefStats stats) {
            switch (stats.Kind) {
                case EntityKind.Mat:
                    var mt = stats.Def.MatType;
                    return mt != null ? S.Sys.Inventory.AvailableCount(mt) : 0;
                case EntityKind.Obj:
                    return S.Query.ObjCountByDef.Ask(stats.Def);
                case EntityKind.Being:
                    return S.Beings.CountByDef(stats.Def);
                default:
                    return 0;
            }
        }

        private static void TrimHistory(SortedDictionary<long, int> history, long tick, long maxAge) {
            if (history.Count == 0) { return; }
            var cutoff = tick - maxAge;
            Caches.Longs.Clear();
            foreach (var h in history) {
                if (h.Key < cutoff) { Caches.Longs.Add(h.Key); }
            }
            foreach (var k in Caches.Longs) { history.Remove(k); }
            Caches.Longs.Clear();
        }

        public SortedDictionary<long, int> GetStockHistory(Def def) {
            if (def == null) { return null; }
            var now = G.Ticks;
            if (!defStats.TryGetValue(def.IdH, out var stats)) {
                return SyntheticStockHistory(def, now);
            }
            if (stats.StockView == null || stats.StockDirty) {
                // full rebuild: Stock changed (daily flush wrote/trimmed entries)
                if (stats.StockView == null) {
                    stats.StockView = new SortedDictionary<long, int>(stats.Stock);
                } else {
                    // sync committed entries into view
                    foreach (var kv in stats.Stock) { stats.StockView[kv.Key] = kv.Value; }
                    // remove entries trimmed from Stock (view may have stale keys)
                    viewScratch.Clear();
                    foreach (var kv in stats.StockView) {
                        if (kv.Key != stats.StockViewTick && !stats.Stock.ContainsKey(kv.Key)) {
                            viewScratch.Add(kv.Key);
                        }
                    }
                    foreach (var k in viewScratch) { stats.StockView.Remove(k); }
                    viewScratch.Clear();
                }
                stats.StockDirty = false;
                // fall through to update the live entry below
            }
            // update live "today" entry whenever the tick advances (zero-alloc: just key/value replace)
            if (stats.StockViewTick != now) {
                if (stats.StockViewTick >= 0) { stats.StockView.Remove(stats.StockViewTick); }
                stats.StockView[now] = GetCurrentStock(stats);
                stats.StockViewTick = now;
            }
            if (stats.StockView.Count >= 2) { return stats.StockView; }
            return SyntheticStockHistory(def, now);
        }

        private SortedDictionary<long, int> SyntheticStockHistory(Def def, long now) {
            // Fewer than 2 data points: show a line from 0 one day ago up to the
            // current count so the chart is never blank.
            synthStock.Clear();
            var baseTick = now - Consts.TicksPerDay;
            if (baseTick < 0) { baseTick = 0; }
            var cnt = S.Sys.Inventory.CountOf(def);
            synthStock[baseTick] = cnt;
            synthStock[now] = cnt;
            return synthStock;
        }

        public SortedDictionary<long, int> GetProductionHistory(Def def) {
            if (def == null) { return null; }
            if (!defStats.TryGetValue(def.IdH, out var stats)) { return null; }
            return stats.Produced.Count > 0 ? stats.Produced : null;
        }

        public SortedDictionary<long, int> GetConsumptionHistory(Def def) {
            if (def == null) { return null; }
            if (!defStats.TryGetValue(def.IdH, out var stats)) { return null; }
            return stats.Consumed.Count > 0 ? stats.Consumed : null;
        }

        public int GetProductionRate(Def def, int hoursWindow = 24) {
            if (def == null) { return 0; }
            if (!defStats.TryGetValue(def.IdH, out var stats)) { return 0; }
            return SumLastHours(stats.Produced, hoursWindow);
        }

        public int GetConsumptionRate(Def def, int hoursWindow = 24) {
            if (def == null) { return 0; }
            if (!defStats.TryGetValue(def.IdH, out var stats)) { return 0; }
            return SumLastHours(stats.Consumed, hoursWindow);
        }

        private BInt GetBindableProductionRate(Def def) {
            if (def == null) { return null; }
            if (!prodRateB.TryGetValue(def.IdH, out var b)) {
                b = new BInt(GetProductionRate(def, 24), capacity: 64);
                prodRateB[def.IdH] = b;
            }
            return b;
        }

        private BInt GetBindableConsumptionRate(Def def) {
            if (def == null) { return null; }
            if (!consRateB.TryGetValue(def.IdH, out var b)) {
                b = new BInt(GetConsumptionRate(def, 24), capacity: 64);
                consRateB[def.IdH] = b;
            }
            return b;
        }

        private static int SumLastHours(SortedDictionary<long, int> history, int hours) {
            if (history.Count == 0) { return 0; }
            var cutoff = G.Ticks - (long)hours * Consts.TicksPerHour;
            var sum = 0;
            foreach (var kv in history) {
                if (kv.Key >= cutoff) { sum += kv.Value; }
            }
            return sum;
        }

        public override void Unload() {
            defStats.Clear();
            prodRateB.Clear();
            consRateB.Clear();
            synthStock.Clear();
            nextFlushTick = 0;
        }

        public void SaveSpecial(SystemsDataSpecial sd) {
            if (defStats.Count == 0) { return; }
            var tick = S.Ticks;
            // flush pending rates only — stock is written daily by FlushHour compression
            foreach (var kv in defStats) {
                var stats = kv.Value;
                if (stats.PendingProduced > 0 || stats.PendingConsumed > 0) {
                    stats.Produced[tick] = stats.PendingProduced;
                    stats.Consumed[tick] = stats.PendingConsumed;
                    stats.PendingProduced = 0;
                    stats.PendingConsumed = 0;
                }
            }

            var stockDict = new Dictionary<string, SortedDictionary<long, int>>();
            var prodDict  = new Dictionary<string, SortedDictionary<long, int>>();
            var consDict  = new Dictionary<string, SortedDictionary<long, int>>();
            var kindDict  = new Dictionary<string, byte>();
            var lastStockValueDict = new Dictionary<string, int>();
            var lastStockTickDict  = new Dictionary<string, long>();

            foreach (var kv in defStats) {
                var stats = kv.Value;
                var id = stats.Def.Id;
                if (stats.Stock.Count > 0)    { stockDict[id] = stats.Stock; }
                if (stats.Produced.Count > 0) { prodDict[id]  = stats.Produced; }
                if (stats.Consumed.Count > 0) { consDict[id]  = stats.Consumed; }
                kindDict[id] = (byte)stats.Kind;
                if (stats.LastWrittenStockValue >= 0) {
                    lastStockValueDict[id] = stats.LastWrittenStockValue;
                    lastStockTickDict[id]  = stats.LastStockWriteTick;
                }
            }

            sd.Statistics ??= new StatisticsSysData();
            sd.Statistics.Stock          = stockDict.Count > 0          ? stockDict          : null;
            sd.Statistics.Produced       = prodDict.Count > 0           ? prodDict           : null;
            sd.Statistics.Consumed       = consDict.Count > 0           ? consDict           : null;
            sd.Statistics.Kind           = kindDict.Count > 0           ? kindDict           : null;
            sd.Statistics.LastStockValue = lastStockValueDict.Count > 0 ? lastStockValueDict : null;
            sd.Statistics.LastStockTick  = lastStockTickDict.Count > 0  ? lastStockTickDict  : null;
        }

        public void LoadSpecial(SystemsDataSpecial sd) {
            defStats.Clear();
            if (sd?.Statistics == null) { return; }

            var data = sd.Statistics;
            var clockTicks = S.Ticks;

            // corruption guard: if first stock entry is newer than current clock, discard all
            if (data.Stock != null) {
                foreach (var entry in data.Stock) {
                    if (entry.Value.Count > 0) {
                        var last = default(KeyValuePair<long, int>);
                        foreach (var e in entry.Value) { last = e; }
                        if (last.Key > clockTicks) {
                            D.Warn("StatisticsSys: corrupt history on load (last tick {0} > clock {1}), discarding.",
                                last.Key, clockTicks);
                            return;
                        }
                        break;
                    }
                }
            }

            // collect all known def ids
            var allIds = new HashSet<string>();
            if (data.Stock != null)   { foreach (var k in data.Stock.Keys)   { allIds.Add(k); } }
            if (data.Produced != null){ foreach (var k in data.Produced.Keys){ allIds.Add(k); } }
            if (data.Consumed != null){ foreach (var k in data.Consumed.Keys){ allIds.Add(k); } }

            foreach (var id in allIds) {
                var def = The.Defs.FastGet(Hashes.S(id));
                if (def == null) {
                    D.Warn("StatisticsSys: skipping unknown def on load: {0}", id);
                    continue;
                }
                var kind = EntityKind.Mat;
                if (data.Kind != null && data.Kind.TryGetValue(id, out var kb)) {
                    kind = (EntityKind)kb;
                }
                var stats = GetOrCreate(def, kind);
                if (data.Stock != null && data.Stock.TryGetValue(id, out var stock)) {
                    stats.Stock = stock;
                }
                if (data.Produced != null && data.Produced.TryGetValue(id, out var prod)) {
                    stats.Produced = prod;
                }
                if (data.Consumed != null && data.Consumed.TryGetValue(id, out var cons)) {
                    stats.Consumed = cons;
                }
                // restore compression state so FlushHour continues seamlessly after load
                if (data.LastStockValue != null && data.LastStockValue.TryGetValue(id, out var lsv)) {
                    stats.LastWrittenStockValue = lsv;
                }
                if (data.LastStockTick != null && data.LastStockTick.TryGetValue(id, out var lst)) {
                    stats.LastStockWriteTick = lst;
                }
            }

            nextFlushTick = S.Ticks + Consts.TicksPer30Minutes;
        }
    }
}
