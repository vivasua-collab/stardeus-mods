using System.Collections.Generic;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Notify;
using Game.Utils;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Space {
    public sealed class SpaceEffectArtilleryFungus : SpaceEffect, ISaveable {
        [SelfInit]
        private static void Init() {
            Register(new SpaceEffectArtilleryFungus());
        }

        public override string Id => "ArtilleryFungus";
        private NotificationHandle notification;
        public override string Name => T.ObjPlantsArtilleryfungus;
        private long nextFungusAt;

        // The timer was not saved, and loading went through OnEnterSector, which resets it:
        // every load in a fungus sector spawned a fungus on the next tick
        public ComponentData Save() {
            var data = new ComponentData();
            data.SetLong("NextFungusAt", nextFungusAt);
            return data;
        }

        public void Load(ComponentData data) {
            nextFungusAt = data.GetLong("NextFungusAt", nextFungusAt);
        }

        private void ShowNotification() {
            notification = Notification.Build(IconId.CWarning,
                    AddSectorPrefix(T.SpaceEffectAlienLifeform), 0, NotificationType.Warning)
                .Sticky()
                .WithTooltip(T.SpaceEffectAlienLifeformDesc)
                .Show();
        }

        private void Notify(GameState s) {
            ShowNotification();

            var title = AddSectorPrefix(Texts.Yellow(T.SpaceEffectAlienLifeform));
            s.Sig.ShowFanfare.Send(title);
            s.Sys.Log.AddLine(title);
        }

        protected override void Cleanup() {
            nextFungusAt = 0;
            notification = default;
        }

        public override void OnEnterSector(GameState s, SpaceSector sector) {
            Notify(s);
            nextFungusAt = 0L;
        }

        public override void OnExitSector(GameState s, SpaceSector sector) {
            if (notification.IsValid) {
                Notification.Remove(ref notification);
            }
        }

        public override void OnLoad(GameState s, SpaceSector sector) {
            // the sticky notification only: the fanfare and the log line came again on every
            // load. A save from before the timer was saved gets a fresh delay, not a spawn.
            ShowNotification();
            nextFungusAt = s.Ticks + Rng.URange(Consts.TicksPer12Hours, Consts.TicksPer24Hours);
        }

        public override void Tick(GameState s, long ticks, SpaceSector currentSector) {
            if (!Ready.AreasInitial) { return; }
            if (nextFungusAt > s.Ticks) {
                return;
            }
            nextFungusAt = s.Ticks + Rng.URange(Consts.TicksPer12Hours, Consts.TicksPer24Hours);

            Caches.WeightedInts.Clear();
            foreach (var a in s.Sys.Areas.Areas) {
                if (a.Value.Count < 10) { continue; }
                AppendArea(s, a.Value);
            }
            foreach (var a in s.Sys.Areas.OuterAreas) {
                if (a.Value.Count < 10) { continue; }
                AppendArea(s, a.Value);
            }

            if (Caches.WeightedInts.Count == 0) {
                D.Err("Could not find corner for spawning fungus");
                return;
            }
            // S.Situation.AddTension(0.1f, "Spawn Fungus");
            var targetPos = Rng.UWeightedFrom(Caches.WeightedInts);
            var egg = s.Objs.Create(
                EntityUtils.CenterOf(targetPos),
                    The.Defs.FastGet(DefIdsH.ObjPlantsArtilleryFungus));
            var light = s.Query.LightAtPos.Ask(targetPos);
            D.Warn("Fungus added at {0}: {1}: {2}. Light: {3}", Pos.String(targetPos),
                Units.TicksToTime(G.Ticks), egg, light);
        }

        private static void AppendArea(GameState s, HashSet<int> area) {
            foreach (var pos in area) {
                var hasWallNS = EntityUtils.IsWallAt(Pos.N(pos)) ||
                    EntityUtils.IsWallAt(Pos.S(pos));
                var hasWallEW = EntityUtils.IsWallAt(Pos.E(pos)) ||
                    EntityUtils.IsWallAt(Pos.W(pos));
                if (!hasWallEW || !hasWallNS) {
                    continue;
                }
                // it's a corner
                int targetPos = Pos.Invalid;
                if (EntityUtils.IsEmptyFloorAt(pos)) {
                    targetPos = pos;
                } else {
                    if (EntityUtils.IsEmptyFloorAt(Pos.N(pos))) {
                        targetPos = Pos.N(pos);
                    } else if (EntityUtils.IsEmptyFloorAt(Pos.S(pos))) {
                        targetPos = Pos.S(pos);
                    } else if (EntityUtils.IsEmptyFloorAt(Pos.E(pos))) {
                        targetPos = Pos.E(pos);
                    } else if (EntityUtils.IsEmptyFloorAt(Pos.W(pos))) {
                        targetPos = Pos.W(pos);
                    }
                }
                var areaAtPos = s.Sys.Areas.AreaAt(pos);
                // skip small areas
                if (areaAtPos.Count < 10) { continue; }
                if (Caches.WeightedInts.ContainsKey(targetPos)) { continue; }
                if (targetPos != Pos.Invalid) {
                    var light = s.Query.LightAtPos.Ask(pos);
                    int crowd = s.Objs.FindInRadiusMatching(Pos.ToVector(pos),
                        0.5f, Caches.Obj16, (o => o.Definition.IdH == DefIdsH.ObjPlantsArtilleryFungus), 3);
                    if (crowd > 2) { continue; }
                    Caches.WeightedInts.Add(targetPos, (1.05f - light) * (1f / (crowd + 1)));
                }
            }
        }
    }
}
