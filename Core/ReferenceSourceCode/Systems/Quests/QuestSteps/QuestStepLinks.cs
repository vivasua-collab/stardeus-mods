using Game.Data;
using Game.Utils;
using KL.Grid;

namespace Game.Systems.Quests {
    // Tutorial steps about a device that is already aboard (the Stasis Array) point at that
    // device instead of its codex entry: the player already knows what it is, and where it sits
    // is the useful part - the codex entry of a Stasis Array only says it cannot be built. With
    // several instances there is no single one to point at, so the codex link stays.
    public static class QuestStepLinks {
        public static string DeviceLink(GameState s, Def def) {
            Entity single = null;
            if (s.Map.TryAllByDef(def, out var found)) {
                foreach (var e in found.Values) {
                    if (!e.IsActive) { continue; }
                    if (single != null) { return def.CodexLink; }
                    single = e;
                }
            }
            return single != null ? Texts.LinkToEntity(single) : def.CodexLink;
        }

        // The target closest to an anchor is the one the player is meant to wall in (the pod
        // wardrobe, the pod rations), so link that instance rather than the codex entry.
        // Carried targets are skipped, a meal on its way to be eaten is not the one to keep.
        public static string ClosestLink(GameState s, Def anchorDef, Def[] targetDefs) {
            Entity closest = null;
            var bestDist = int.MaxValue;
            if (s.Map.TryAllByDef(anchorDef, out var anchors)) {
                foreach (var anchor in anchors.Values) {
                    if (!anchor.IsActive) { continue; }
                    foreach (var targetDef in targetDefs) {
                        if (!s.Map.TryAllByDef(targetDef, out var targets)) { continue; }
                        foreach (var target in targets.Values) {
                            if (!target.IsActive || target.IsCarriedByAny) { continue; }
                            var dist = Pos.SqrDistance(anchor.PosIdx, target.PosIdx);
                            if (dist < bestDist) {
                                bestDist = dist;
                                closest = target;
                            }
                        }
                    }
                }
            }
            return closest != null ? Texts.LinkToEntity(closest) : targetDefs[0].CodexLink;
        }
    }
}
