using System.Collections.Generic;
using Game.Constants;
using Game.Systems.Stats;
using Game.Utils;
using Unity.Mathematics;

namespace Game.Systems.Mental {
    /// <summary>
    /// What a colonist holds for and against the AI. Trust (the AIAlignment stat) is its base
    /// value plus one modifier per cause, so it is always recomputed from saved effects and
    /// can't drift. See Docs/Architecture/Systems/AIAlignment.md.
    /// </summary>
    public sealed class Trust {
        public readonly List<TrustEffect> Effects = new();
        private readonly Mind owner;
        // The stat the modifiers sit on now. The mind can move to another body or leave the
        // Player faction, and the old body must not keep them.
        private Stat appliedTo;
        // One tooltip line per cause: stacked effects of a source share a modifier, named with
        // their count and the time the soonest one has left
        private readonly Dictionary<int, StatModifier> mods = new();

        private struct Line {
            public int Points;
            public int Count;
            public long SoonestEnd;
            public string ReasonKey;
        }
        private static readonly Dictionary<int, Line> lines = new();
        private static readonly List<int> stale = new();

        // At or below it a colonist may join a mutiny (MutinyStoryEvent)
        public const float MutinyLine = 0.5f;

        public Trust(Mind owner) {
            this.owner = owner;
        }

        public void Add(int sourceIdH, int points, long ticks, long durationTicks, string reasonKey) {
            if (points == 0 || durationTicks <= 0) { return; }
            Effects.Add(Create(sourceIdH, points, ticks, ticks + durationTicks, reasonKey));
            Sync();
        }

        // A standing effect that lasts while its cause holds: at most one per source
        public void SetHeld(int sourceIdH, int points, long ticks, string reasonKey, bool held) {
            var idx = HeldIndex(sourceIdH);
            if (idx >= 0) {
                var e = Effects[idx];
                if (held && e.Points == points && e.ReasonKey == reasonKey) { return; }
                Effects.RemoveAt(idx);
            }
            if (held && points != 0) {
                Effects.Add(Create(sourceIdH, points, ticks, 0, reasonKey));
            }
            Sync();
        }

        public void Expire(long ticks) {
            bool changed = false;
            for (int i = Effects.Count - 1; i >= 0; i--) {
                var e = Effects[i];
                if (e.IsTimed && e.EndTick <= ticks) {
                    Effects.RemoveAt(i);
                    changed = true;
                }
            }
            if (changed) {
                Sync();
            }
        }

        public int CountFrom(int sourceIdH) {
            int count = 0;
            for (int i = Effects.Count - 1; i >= 0; i--) {
                if (Effects[i].SourceId == sourceIdH) {
                    count++;
                }
            }
            return count;
        }

        public void Sync() {
            var data = owner.Owner;
            Stat stat = null;
            if (data?.StatsBeing != null && owner.Faction?.IsPlayer == true) {
                stat = data.StatsBeing.AIAlignment;
            }
            if (appliedTo != null && appliedTo != stat) {
                foreach (var mod in mods.Values) {
                    appliedTo.RemoveModifier(mod, false);
                }
            }
            appliedTo = stat;
            if (stat == null) {
                mods.Clear();
                return;
            }

            lines.Clear();
            for (int i = 0; i < Effects.Count; i++) {
                var e = Effects[i];
                lines.TryGetValue(e.SourceId, out var line);
                line.Points += e.Points;
                line.Count++;
                line.ReasonKey ??= e.ReasonKey;
                if (e.IsTimed && (line.SoonestEnd == 0 || e.EndTick < line.SoonestEnd)) {
                    line.SoonestEnd = e.EndTick;
                }
                lines[e.SourceId] = line;
            }

            stale.Clear();
            foreach (var kv in mods) {
                if (!lines.ContainsKey(kv.Key)) {
                    stat.RemoveModifier(kv.Value, false);
                    stale.Add(kv.Key);
                }
            }
            foreach (var key in stale) {
                mods.Remove(key);
            }

            var ticks = G.Ticks;
            foreach (var kv in lines) {
                var line = kv.Value;
                var value = line.Points / 100f;
                var name = NameFor(line, ticks);
                if (mods.TryGetValue(kv.Key, out var mod)) {
                    if (mod.Value == value && mod.NameT == name) {
                        // puts it back if a stats rebuild threw it away (a load restores
                        // beings after the first attempt)
                        stat.TryAddModifier(mod, true);
                        continue;
                    }
                    stat.RemoveModifier(mod, false);
                }
                mod = StatModifier.Add(value, name);
                mods[kv.Key] = mod;
                stat.TryAddModifier(mod, true);
            }
        }

        private static string NameFor(Line line, long ticks) {
            var reason = (line.ReasonKey ?? string.Empty).T();
            if (line.Count > 1) {
                reason = $"{reason} (x{line.Count})";
            }
            if (line.SoonestEnd > 0) {
                reason = $"{reason} ({Units.TicksToTime(line.SoonestEnd - ticks, true)})";
            }
            return reason;
        }

        private int HeldIndex(int sourceIdH) {
            for (int i = Effects.Count - 1; i >= 0; i--) {
                var e = Effects[i];
                if (e.SourceId == sourceIdH && !e.IsTimed) {
                    return i;
                }
            }
            return -1;
        }

        private static TrustEffect Create(int sourceIdH, int points, long start, long end,
                string reasonKey) {
            return new TrustEffect {
                SourceId = sourceIdH,
                Points = (sbyte) math.clamp(points, -100, 100),
                StartTick = start,
                EndTick = end,
                ReasonKey = reasonKey,
            };
        }
    }
}
