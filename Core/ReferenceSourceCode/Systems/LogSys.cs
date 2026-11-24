using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using Game.Systems.Creatures;
using System.Collections.Generic;
using KL.Utils;

namespace Game.Systems {
    public sealed class LogSys : GameSystem, ISaveableSpecial {
        public const string SysId = "LogSys";
        public override string Id => SysId;

        private readonly Dictionary<uint, List<LogEntry>> beingEntries = new();
        private readonly Dictionary<int, List<LogEntry>> tileEntries = new();
        private readonly Dictionary<uint, List<LogEntry>> objEntries = new();
        private readonly List<LogEntry> contextFreeEntries = new();
        private readonly List<uint> emptyIdBuffer = new();
        private readonly List<int> emptyTileIdBuffer = new();
        private string lastLineWithoutTime;
        // Off the top of the hour, where many other systems do their hourly work
        private const int PruneMinuteOfHour = 53;

        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new LogSys());
        }

        public override void Unload() {
            beingEntries.Clear();
            tileEntries.Clear();
            objEntries.Clear();
            contextFreeEntries.Clear();
            lastLineWithoutTime = null;
        }

        protected override void OnInitialize() {
            S.Sig.AfterLoadState.AddListener(OnLoadSave);
            S.Sig.TileRelocated.AddListener(OnTileRelocated);
            S.Query.GetBeingLogEntries.SetResponder(GetBeingLogEntries);
            S.Query.GetTileLogEntries.SetResponder(GetTileLogEntries);
            S.Query.GetObjLogEntries.SetResponder(GetObjLogEntries);
            S.Query.GetAllLogEntries.SetResponder(GetAllLogEntries);
        }

        private void OnLoadSave(GameState s) {
            s.Clock.OnTick.AddListener(OnTick);
            // Saves made before age pruning existed shrink right away
            PruneExpired(G.Ticks);
            if (G.Ticks > 0) {
                AddLine(T.BootingNeuralNetwork, Texts.TMPColorGreen);
            }
        }

        private string time =>
            $"<color=#808080>{GameTimeText.Day}|{GameTimeText.HoursText}</color>";

        // --- Query responders ---

        private List<LogEntry> GetBeingLogEntries(uint id) {
            return beingEntries.TryGetValue(id, out var list) ? list : null;
        }

        private List<LogEntry> GetTileLogEntries(int id) {
            return tileEntries.TryGetValue(id, out var list) ? list : null;
        }

        private List<LogEntry> GetObjLogEntries(uint id) {
            return objEntries.TryGetValue(id, out var list) ? list : null;
        }

        private int GetAllLogEntries(List<LogEntry> result) {
            result.Clear();
            foreach (var kvp in beingEntries) {
                AddCombined(result, kvp.Value);
            }
            foreach (var kvp in tileEntries) {
                AddCombined(result, kvp.Value);
            }
            foreach (var kvp in objEntries) {
                AddCombined(result, kvp.Value);
            }
            result.AddRange(contextFreeEntries);
            return result.Count;
        }

        // Entity-only entries are copies of entries logged elsewhere, or too noisy for it
        private static void AddCombined(List<LogEntry> result, List<LogEntry> entries) {
            for (int i = 0; i < entries.Count; i++) {
                if (!entries[i].EntityOnly) {
                    result.Add(entries[i]);
                }
            }
        }

        // --- Tile relocation ---

        private void OnTileRelocated(int oldId, int newId) {
            if (!tileEntries.TryGetValue(oldId, out var list)) {
                return;
            }
            tileEntries.Remove(oldId);
            var newEntityId = (uint)newId;
            for (int i = 0; i < list.Count; i++) {
                var e = list[i];
                e.EntityId = newEntityId;
                list[i] = e;
            }
            if (tileEntries.TryGetValue(newId, out var existing)) {
                existing.AddRange(list);
                CullList(existing, Tunables.LogMaxEntriesPerTile);
            } else {
                tileEntries[newId] = list;
            }
        }

        // --- AddLine overloads ---

        public void AddLine(BeingData beingData, string what) {
            if (beingData.Being != null) {
                AddLine(beingData.Being, what);
            } else {
                var linkText = Texts.LinkToBeing(beingData);
                var line = $"{linkText}: {what}";
                AddEntry(line, LogEntry.TypeBeing, beingData.Id);
            }
        }

        public void AddLine(Entity entity, string what) {
            if (entity is Tile t) {
                AddLine(t, what);
            } else if (entity is Being b) {
                AddLine(b, what);
            } else if (entity is Obj o) {
                AddLine(o, what);
            } else {
                AddLine(what);
            }
        }

        public void AddLine(Obj who, string what, string color, bool canRepeat = false) {
            var name = Texts.Accented3(who.Definition.NameT);
            var line = $"{Texts.LinkTo(name, who.Data.Id, 'o')}: <color={color}>{what}</color>";
            AddEntry(line, LogEntry.TypeObj, who.Data.Id, canRepeat);
        }

        public void AddLine(Obj who, string what, bool canRepeat = false) {
            var name = Texts.Accented3(who.Definition.NameT);
            var line = $"{Texts.LinkTo(name, who.Data.Id, 'o')}: {what}";
            AddEntry(line, LogEntry.TypeObj, who.Data.Id, canRepeat);
        }

        public void AddLine(Tile who, string what, string color, bool canRepeat = false) {
            var name = Texts.Accented2(who.Definition.NameT);
            var line = $"{Texts.LinkTo(name, who.Id, 't')}: <color={color}>{what}</color>";
            AddEntry(line, LogEntry.TypeTile, (uint)who.Id, canRepeat);
        }

        public void AddLine(Tile who, string what, bool canRepeat = false) {
            var name = Texts.Accented2(who.Definition.NameT);
            var line = $"{Texts.LinkTo(name, who.Id, 't')}: {what}";
            AddEntry(line, LogEntry.TypeTile, (uint)who.Id, canRepeat);
        }

        public void AddLine(Being who, string what, string color, bool canRepeat = false) {
            var name = Texts.Accented(who.Data.Mind.Name.DisplayName);
            var line = $"{Texts.LinkTo(name, who.Data.Id, 'b')}: <color={color}>{what}</color>";
            AddEntry(line, LogEntry.TypeBeing, who.Data.Id, canRepeat);
        }

        public void AddLine(Being who, string what, bool canRepeat = false) {
            AddEntry(BeingLine(who, what), LogEntry.TypeBeing, who.Data.Id, canRepeat);
        }

        private string BeingLine(Being who, string what) {
            if (S.Sys.Combat.IsHostileCombatInProgress) {
                var name = who.Data.Mind.CombatLogName;
                return $"{name}: {what}";
            }
            var linkText = Texts.LinkToBeing(who.Data);
            return $"{linkText}: {what}";
        }

        // Logs a failed task on the worker, and copies the same line onto the task's target
        // so a player inspecting a device or object can see why its tasks are not getting done
        public void AddGoalFailure(Being worker, Entity target, string what) {
            var line = BeingLine(worker, what);
            AddEntry(line, LogEntry.TypeBeing, worker.Data.Id);
            if (target != worker) {
                AddEntityOnlyLine(target, line);
            }
        }

        // A line for the entity's own log tab only. Bypasses the repeat check (a copied line
        // always equals the line it was copied from) and the LogEntryAdded signal, which only
        // feeds the combined log.
        public void AddEntityOnlyLine(Entity entity, string line) {
            if (!TryGetLogKey(entity, out var type, out var id)) { return; }
            StoreEntry(new LogEntry {
                Tick = G.Ticks,
                Text = $"{time} {line}",
                EntityType = type,
                EntityId = id,
                EntityOnly = true,
            });
        }

        private static bool TryGetLogKey(Entity entity, out byte type, out uint id) {
            switch (entity) {
                case Tile t:
                    type = LogEntry.TypeTile;
                    id = (uint)t.Id;
                    return true;
                case Obj o:
                    type = LogEntry.TypeObj;
                    id = o.Data.Id;
                    return true;
                case Being b:
                    type = LogEntry.TypeBeing;
                    id = b.Data.Id;
                    return true;
                default:
                    type = LogEntry.TypeContextFree;
                    id = 0;
                    return false;
            }
        }

        public void AddLine(string line, bool canRepeat = false) {
            if (string.IsNullOrEmpty(line)) {
                D.Err("Trying to add blank log line!");
                return;
            }
            AddEntry(line, LogEntry.TypeContextFree, 0, canRepeat);
        }

        public void AddLine(string line, string color) {
            AddLine(Texts.WithColor(line, color));
        }

        // --- Core entry creation ---

        private void AddEntry(string line, byte entityType, uint entityId,
                bool canRepeat = false) {
            if (!canRepeat && lastLineWithoutTime == line) {
                return;
            }
            lastLineWithoutTime = line;

            var entry = new LogEntry {
                Tick = G.Ticks,
                Text = $"{time} {line}",
                EntityType = entityType,
                EntityId = entityId,
            };

            StoreEntry(entry);
            S.Sig.LogEntryAdded.Send(entry);
        }

        private void StoreEntry(LogEntry entry) {
            switch (entry.EntityType) {
                case LogEntry.TypeBeing: {
                    if (!beingEntries.TryGetValue(entry.EntityId, out var list)) {
                        list = new List<LogEntry>();
                        beingEntries[entry.EntityId] = list;
                    }
                    list.Add(entry);
                    CullList(list, Tunables.LogMaxEntriesPerBeing);
                    break;
                }
                case LogEntry.TypeTile: {
                    var tileId = (int)entry.EntityId;
                    if (!tileEntries.TryGetValue(tileId, out var list)) {
                        list = new List<LogEntry>();
                        tileEntries[tileId] = list;
                    }
                    list.Add(entry);
                    CullList(list, Tunables.LogMaxEntriesPerTile);
                    break;
                }
                case LogEntry.TypeObj: {
                    if (!objEntries.TryGetValue(entry.EntityId, out var list)) {
                        list = new List<LogEntry>();
                        objEntries[entry.EntityId] = list;
                    }
                    list.Add(entry);
                    CullList(list, Tunables.LogMaxEntriesPerObj);
                    break;
                }
                default: {
                    contextFreeEntries.Add(entry);
                    CullList(contextFreeEntries, Tunables.LogMaxContextFreeEntries);
                    break;
                }
            }
        }

        // --- Clear methods ---

        public void ClearAll() {
            beingEntries.Clear();
            tileEntries.Clear();
            objEntries.Clear();
            contextFreeEntries.Clear();
        }

        public void ClearBeingEntries() {
            beingEntries.Clear();
        }

        public void ClearTileEntries() {
            tileEntries.Clear();
        }

        public void ClearObjEntries() {
            objEntries.Clear();
        }

        public void ClearContextFreeEntries() {
            contextFreeEntries.Clear();
        }

        public void ClearEntityEntries(byte entityType, uint entityId) {
            switch (entityType) {
                case LogEntry.TypeBeing:
                    beingEntries.Remove(entityId);
                    break;
                case LogEntry.TypeTile:
                    tileEntries.Remove((int)entityId);
                    break;
                case LogEntry.TypeObj:
                    objEntries.Remove(entityId);
                    break;
                default:
                    contextFreeEntries.Clear();
                    break;
            }
        }

        private static void CullList(List<LogEntry> list, int max) {
            if (max <= 0) { return; }
            while (list.Count > max) {
                list.RemoveAt(0);
            }
        }

        // --- Age pruning ---
        // Count caps alone bound each list, but never remove one: entity ids are not reused,
        // so every destroyed device, hauled item and dead raider would keep its log in the
        // save forever. Dropping old entries (and the lists they empty) bounds the log by
        // recent activity instead of by the lifetime of the save.

        private void OnTick(long ticks) {
            if (ticks % Consts.TicksPerHour != PruneMinuteOfHour) { return; }
            PruneExpired(ticks);
        }

        private void PruneExpired(long now) {
            var maxAgeDays = Tunables.LogMaxAgeDays;
            if (maxAgeDays <= 0) { return; }
            PruneOlderThan(now - (long)maxAgeDays * Consts.TicksPerDay);
        }

        public void PruneOlderThan(long cutoffTick) {
            PruneDict(beingEntries, cutoffTick, emptyIdBuffer);
            PruneDict(tileEntries, cutoffTick, emptyTileIdBuffer);
            PruneDict(objEntries, cutoffTick, emptyIdBuffer);
            PruneList(contextFreeEntries, cutoffTick);
        }

        private static void PruneDict<TKey>(Dictionary<TKey, List<LogEntry>> dict,
                long cutoffTick, List<TKey> emptyKeys) {
            emptyKeys.Clear();
            foreach (var kvp in dict) {
                PruneList(kvp.Value, cutoffTick);
                if (kvp.Value.Count == 0) {
                    emptyKeys.Add(kvp.Key);
                }
            }
            for (int i = 0; i < emptyKeys.Count; i++) {
                dict.Remove(emptyKeys[i]);
            }
            emptyKeys.Clear();
        }

        // Entries are appended in tick order, so the expired ones are all at the front
        private static void PruneList(List<LogEntry> list, long cutoffTick) {
            int expired = 0;
            while (expired < list.Count && list[expired].Tick < cutoffTick) {
                expired++;
            }
            if (expired > 0) {
                list.RemoveRange(0, expired);
            }
        }

        // --- Save / Load ---

        public void SaveSpecial(SystemsDataSpecial sd) {
            var all = new List<LogEntry>();
            foreach (var kvp in beingEntries) {
                all.AddRange(kvp.Value);
            }
            foreach (var kvp in tileEntries) {
                all.AddRange(kvp.Value);
            }
            foreach (var kvp in objEntries) {
                all.AddRange(kvp.Value);
            }
            all.AddRange(contextFreeEntries);

            sd.Log ??= new LogSysData();
            sd.Log.Entries = all.ToArray();
            sd.Log.LastLine = lastLineWithoutTime;
        }

        public void LoadSpecial(SystemsDataSpecial sd) {
            beingEntries.Clear();
            tileEntries.Clear();
            objEntries.Clear();
            contextFreeEntries.Clear();

            if (sd?.Log == null) {
                return;
            }

            lastLineWithoutTime = sd.Log.LastLine;

            if (sd.Log.Entries != null) {
                for (int i = 0; i < sd.Log.Entries.Length; i++) {
                    StoreEntry(sd.Log.Entries[i]);
                }
            } else if (!string.IsNullOrEmpty(sd.Log.GameLog)) {
                // Legacy migration: old saves stored log as a single string
                var lines = sd.Log.GameLog.Split('\n');
                foreach (var line in lines) {
                    if (!string.IsNullOrWhiteSpace(line)) {
                        StoreEntry(new LogEntry {
                            Tick = 0,
                            Text = line.Trim(),
                            EntityType = LogEntry.TypeContextFree,
                            EntityId = 0,
                        });
                    }
                }
            }
        }
    }
}
