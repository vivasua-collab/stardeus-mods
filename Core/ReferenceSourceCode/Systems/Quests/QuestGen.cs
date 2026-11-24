using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Systems.Ships;
using KL.Randomness;
using KL.Utils;

namespace Game.Systems.Quests {
    public sealed class QuestGen {
        private readonly GameState S;
        private readonly QuestSys sys;
        private readonly Dictionary<QuestDef, float> weighedDefs = new();
        public QuestGen(GameState s, QuestSys sys) {
            S = s;
            this.sys = sys;
        }

        public Quest CreateFor(IQuestProvider provider, QuestFlags flags, long ticks) {
            var sit = S.Situation;
            var diff = S.Difficulty.Level;
            weighedDefs.Clear();
            foreach (var def in QuestDef.All) {
                if ((def.Flags & flags) == QuestFlags.None) {
                    continue;
                }
                if (!IsProviderCompatible(def, provider)) {
                    continue;
                }
                if (def.Factions != null) {
                    var fac = provider.Faction;
                    if (fac == null || !def.Factions.Contains(fac)) {
                        // wrong faction
                        continue;
                    }
                }
                if (!sit.Matches(def.Require, diff)) {
                    continue;
                }
                long lastSeen = 0L;
                if (sys.Seen.TryGetValue(def, out var seen)) {
                    if (def.IsUnique) {
                        if (seen.StartCount > seen.FailCount) {
                            continue;
                        }
                        if (!def.Flags.HasFlag(QuestFlags.Retryable)) {
                            continue;
                        }
                    }
                    lastSeen = seen.LastStart;
                }

                if (def.Validators != null) {
                    bool canStart = true;
                    for (int i = def.Validators.Length - 1; i >= 0; i--) {
                        IQuestValidator val = def.Validators[i];
                        if (!val.CanStartQuest(S, def)) {
                            D.Warn("Validator won't allow the quest to start: {0} -> {1}",
                                val.Type, def);
                            canStart = false;
                            break;
                        }
                    }
                    if (!canStart) {
                        continue;
                    }
                }

                // Never seen, or seen over a year ago, weighs in full, and recently seen less.
                // The comparison was inverted: quests seen within the year were clamped to
                // full weight as well, so recency did nothing. A day at least, so a quest seen
                // this tick can still be picked when it is the only option.
                var yearAgo = ticks - Consts.TicksPerYear;
                if (lastSeen == 0L || lastSeen < yearAgo) {
                    lastSeen = yearAgo;
                }
                float ticksSinceLastSeen = System.Math.Max(ticks - lastSeen, Consts.TicksPerDay);
                weighedDefs.Add(def,
                    def.Weight * ticksSinceLastSeen / Consts.TicksPerYear);
            }
            if (weighedDefs.Count == 0) {
                D.Err("Couldn't select a quest for provider with flags: {0} | {1} - no options",
                    flags, provider);
                return null;
            }
            // the tick is mixed in: seeded by the provider alone, a ship or station rolled the
            // same quest and variables every time it offered one
            var rng = new Rng(Hashes.Quick(Hashes.Quick(provider.Seed, Primes.Prime026),
                (int) ticks));
            var qDef = rng.WeightedFrom(weighedDefs);
            var data = QuestData.CreateFor(S, qDef, rng, provider);
            if (data == null) {
                D.Err("Failed creating quest data for {0}", qDef);
                return null;
            }
            provider.QuestId = data.Id;
            return Quest.InstanceFor(S, data);
        }

        private static bool IsProviderCompatible(QuestDef def, IQuestProvider provider) {
            if (def.Providers == QuestProviderType.None) {
                return true;
            }
            var providerTypes = GetProviderTypes(provider);
            return (def.Providers & providerTypes) != QuestProviderType.None;
        }

        private static QuestProviderType GetProviderTypes(IQuestProvider provider) {
            var types = provider.ProviderType;
            if (provider is NPCShip ship) {
                if (ship.IsDerelict) {
                    types |= QuestProviderType.ShipDerelict;
                }
                if (ship.Class?.IsSpaceStation == true) {
                    types |= QuestProviderType.SpaceStation;
                }
                return types;
            }

            if (provider is SpaceObject so) {
                switch (so.Type.IdH) {
                    case SpaceObjectTypeIdH.Port:
                        types |= QuestProviderType.Port;
                        break;
                    case SpaceObjectTypeIdH.Signal:
                        types |= QuestProviderType.Signal;
                        break;
                    case SpaceObjectTypeIdH.ShipDerelict:
                        types |= QuestProviderType.ShipDerelict;
                        break;
                    case SpaceObjectTypeIdH.SpaceStation:
                        types |= QuestProviderType.SpaceStation;
                        break;
                }
            }
            return types;
        }
    }
}
