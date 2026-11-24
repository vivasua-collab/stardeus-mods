using System;
using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Reg;
using KL.Grid;
using UnityEngine;

namespace Game.Systems.Drama {
    // Where a party is held, chosen once when it starts (PartyStoryEvent). The room that fits
    // the most guests wins, then one with music, then the one closest to the host. The room
    // the host happened to stand in used to host it, so a party could cram a dozen guests into
    // a corridor. Docs/Architecture/Systems/Parties.md has the reasons.
    public sealed class PartyVenue {
        // Guests stand within this many tiles of the center (PartyGroups)
        public const int Radius = 5;
        // Every guest stands on a tile of its own, and the small groups they form keep a free
        // tile between them: about this many tiles per guest
        public const int TilesPerGuest = 3;
        public const int MinGuests = 3;
        // A room centers on its most open spot, judged this far around, unless its music does
        private const int OpennessRadius = 2;

        public int Center;
        public int AreaId;
        // How many guests the room takes, the host included
        public int Capacity;
        // Plays for the party (FunProviderComp.KeepOnUntilTick); null in a room without one
        public JukeboxComp Music;
        // The party gathers right at the music
        private bool atMusic;

        private static readonly HashSet<int> excludedAreas = new();
        private static readonly List<JukeboxComp> playing = new();

        public static bool TryFind(GameState s, BeingData host, int wanted,
                out PartyVenue venue) {
            venue = null;
            var hostBeing = host?.Being;
            if (hostBeing == null) { return false; }
            var areas = s.Sys.Areas;
            var reach = s.Sys.Path.Reg.Reach.For(hostBeing.Motion);
            var hostPos = hostBeing.PosIdx;
            CollectPersonalQuarters(s);
            CollectPlaying(s);
            PartyVenue best = null;
            foreach (var kv in areas.Areas) {
                var areaId = kv.Key;
                var room = kv.Value;
                if (room.Count < MinGuests * TilesPerGuest) { continue; }
                if (excludedAreas.Contains(areaId)) { continue; }
                // A party gathers at the music when that fits it, but a jukebox in the corner
                // of a hall must not keep a big party out of the hall
                JukeboxComp roomMusic = null;
                for (int i = 0; i < playing.Count; i++) {
                    var music = playing[i];
                    if (areas.At(music.WorkSpot) != areaId) { continue; }
                    roomMusic ??= music;
                    Consider(s, host, reach, hostPos, areaId, music.WorkSpot, music, true,
                        wanted, ref best);
                }
                var open = MostOpenSpot(s, areaId, room);
                if (open != Pos.Invalid) {
                    Consider(s, host, reach, hostPos, areaId, open, roomMusic, false,
                        wanted, ref best);
                }
            }
            excludedAreas.Clear();
            playing.Clear();
            venue = best;
            return best != null;
        }

        // Where a guest may stand: no door, teleporter, device work spot or slow tile
        // (PathSys.IsRoamable), and empty floor. Guests never block a device or a doorway.
        public static bool IsSpot(GameState s, int pos) {
            if (Pos.IsOOBNonZero(pos)) { return false; }
            return s.Sys.Path.IsRoamable(pos, true) && EntityUtils.IsEmptyFloorWithIntegrityAt(pos);
        }

        // In the party's room and close enough to its center. A party saved before venues
        // existed may have its center outside a room: then any indoor tile counts.
        public static bool IsInVenue(GameState s, int center, int areaId, int pos) {
            if (Pos.IsOOBNonZero(pos)) { return false; }
            if (Pos.SqrDistance(center, pos) > Radius * Radius) { return false; }
            var areas = s.Sys.Areas;
            if (areaId > 0) {
                return areas.At(pos) == areaId;
            }
            return !areas.IsOutdoors(pos);
        }

        // The music of the party at this center: the device the party gathers at, else the one
        // in its room closest to it
        public static JukeboxComp MusicFor(GameState s, int center) {
            if (center <= 0) { return null; }
            var all = s.Components.AllByType<JukeboxComp>();
            if (all == null) { return null; }
            var areas = s.Sys.Areas;
            var areaId = areas.At(center);
            JukeboxComp best = null;
            int bestSqr = int.MaxValue;
            foreach (var c in all) {
                var music = (JukeboxComp) c;
                if (!music.Entity.IsActive) { continue; }
                var spot = music.WorkSpot;
                if (spot == center) { return music; }
                if (areaId < 1 || areas.At(spot) != areaId) { continue; }
                var sqr = Pos.SqrDistance(spot, center);
                if (sqr < bestSqr) {
                    best = music;
                    bestSqr = sqr;
                }
            }
            return best;
        }

        public static bool IsAirComfortable(GameState s, int pos) {
            if (s.GetO2(pos) < Consts.MinComfyOxygen) {
                return false;
            }
            var temp = s.GetTemp(pos);
            return temp >= Consts.MinBiologicalComfyTemp && temp <= Consts.MaxBiologicalComfyTemp;
        }

        // Built, switched on and connected (whether the grid has the power is its business)
        private static void CollectPlaying(GameState s) {
            playing.Clear();
            var all = s.Components.AllByType<JukeboxComp>();
            if (all == null) { return; }
            foreach (var c in all) {
                var music = (JukeboxComp) c;
                var tile = music.Tile;
                if (tile != null && tile.IsActive && tile.IsConstructed && tile.ENode != null
                        && music.CanKeepUsing) {
                    playing.Add(music);
                }
            }
        }

        // Nobody throws a party in someone's bedroom
        private static void CollectPersonalQuarters(GameState s) {
            excludedAreas.Clear();
            var areas = s.Sys.Areas;
            var beings = s.Beings.Alive;
            for (int i = 0; i < beings.Count; i++) {
                var data = beings[i].Data;
                if (data == null || !data.IsPlayer) { continue; }
                var quarters = areas.PersonalQuartersAreaOf(data);
                if (quarters > 0) {
                    excludedAreas.Add(quarters);
                }
            }
        }

        private static void Consider(GameState s, BeingData host, ReachQuery reach, int hostPos,
                int areaId, int center, JukeboxComp music, bool atMusic, int wanted,
                ref PartyVenue best) {
            if (!reach.CanReach(center)) { return; }
            if (!IsAirComfortable(s, center)) { return; }
            if (!host.Safety.IsActuallySafeAtWithoutEquipment(center)) { return; }
            var capacity = CountSpots(s, center, areaId) / TilesPerGuest;
            if (capacity < MinGuests) { return; }
            var dist = Pos.Distance(hostPos, center);
            if (best != null && !IsBetter(Math.Min(wanted, capacity), music != null, atMusic,
                    dist, best, wanted, hostPos)) {
                return;
            }
            best ??= new PartyVenue();
            best.Center = center;
            best.AreaId = areaId;
            best.Capacity = capacity;
            best.Music = music;
            best.atMusic = atMusic;
        }

        // Fitting the guests comes first: a bigger room is tried before anyone is left out
        // (Tomas). Music only decides between rooms that fit as many, and then the party
        // gathers right at it when that fits as many too.
        private static bool IsBetter(int fit, bool hasMusic, bool atMusic, float dist,
                PartyVenue best, int wanted, int hostPos) {
            var bestFit = Math.Min(wanted, best.Capacity);
            if (fit != bestFit) { return fit > bestFit; }
            var bestHasMusic = best.Music != null;
            if (hasMusic != bestHasMusic) { return hasMusic; }
            if (atMusic != best.atMusic) { return atMusic; }
            return dist < Pos.Distance(hostPos, best.Center);
        }

        private static int CountSpots(GameState s, int center, int areaId) {
            Pos.ToXY(center, out var cx, out var cy);
            int count = 0;
            for (int x = cx - Radius; x <= cx + Radius; x++) {
                for (int y = cy - Radius; y <= cy + Radius; y++) {
                    if (x < 0 || y < 0 || x >= Pos.GridW || y >= Pos.GridH) { continue; }
                    var p = Pos.FromXY(x, y);
                    if (IsInVenue(s, center, areaId, p) && IsSpot(s, p)) {
                        count++;
                    }
                }
            }
            return count;
        }

        // The spot with the most room around it; among equals the one nearest the middle of
        // the room, so a big hall does not hold its party against a wall
        private static int MostOpenSpot(GameState s, int areaId, HashSet<int> room) {
            float sumX = 0f;
            float sumY = 0f;
            int n = 0;
            foreach (var p in room) {
                if (!IsSpot(s, p)) { continue; }
                Pos.ToXY(p, out var x, out var y);
                sumX += x;
                sumY += y;
                n++;
            }
            if (n == 0) { return Pos.Invalid; }
            var middle = new Vector2(sumX / n, sumY / n);
            var areas = s.Sys.Areas;
            int best = Pos.Invalid;
            int bestOpen = -1;
            float bestMiddleSqr = float.MaxValue;
            foreach (var p in room) {
                if (!IsSpot(s, p)) { continue; }
                Pos.ToXY(p, out var px, out var py);
                int open = 0;
                for (int x = px - OpennessRadius; x <= px + OpennessRadius; x++) {
                    for (int y = py - OpennessRadius; y <= py + OpennessRadius; y++) {
                        if (x < 0 || y < 0 || x >= Pos.GridW || y >= Pos.GridH) { continue; }
                        var np = Pos.FromXY(x, y);
                        if (areas.At(np) == areaId && IsSpot(s, np)) {
                            open++;
                        }
                    }
                }
                if (open < bestOpen) { continue; }
                var middleSqr = (new Vector2(px, py) - middle).sqrMagnitude;
                if (open > bestOpen || middleSqr < bestMiddleSqr) {
                    best = p;
                    bestOpen = open;
                    bestMiddleSqr = middleSqr;
                }
            }
            return best;
        }
    }
}
