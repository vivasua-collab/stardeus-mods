using System;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Procgen;
using Game.Systems.Story;
using Game.Utils;
using KL.Grid;
using KL.Randomness;

namespace Game.Systems {
    // Bad luck protection for chess pieces in derelict loot. A full set takes 32 pieces and some
    // types are rare in the loot table, so without this the set took hundreds of derelicts.
    // Each derelict without a piece, while some are missing, raises the chance that the next one
    // brings a missing piece anyway (Derelict/ChessPity curve). See
    // Docs/Architecture/Systems/DerelictLoot.md.
    public sealed class ChessPitySys : GameSystem, ISaveable {
        public const string SysId = "ChessPitySys";
        public override string Id => SysId;
        // Must match the Spawnable.Category of the chess piece defs (and GenSpawn)
        public const string ChessCategory = "Chess";
        private const string K_DryDerelicts = "DryDerelicts";

        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new ChessPitySys());
        }

        // Derelicts in a row that ended up without a chess piece while some were missing
        public int DryDerelicts;

        private readonly Dictionary<Def, float> missing = new();

        // Called with the random loot picked for a derelict, before it is built. Returns the
        // chess piece the derelict gets, if any: the one the loot rolled by itself (taken out of
        // the loot), or a missing one when the dry streak calls for it. The caller places it in
        // a stage of its own ahead of the rest, because the loot stage drops whatever finds no
        // good spot, and a piece promised to the player must not be lost that way.
        public Def TakeChessPiece(GameState s, Rng rng, Dictionary<Def, int> loot) {
            missing.Clear();
            foreach (var def in The.Defs.Defs.Values) {
                var sp = def.Spawnable;
                if (sp == null || sp.Category != ChessCategory) { continue; }
                if (loot.TryGetValue(def, out var q)) {
                    // The loot rolled a piece by itself (SkipOwned lets only missing ones through)
                    if (q > 1) {
                        loot[def] = q - 1;
                    } else {
                        loot.Remove(def);
                    }
                    return def;
                }
                if (sp.SkipOwned <= 0 || def.IsAbstract || def.IsTemplate || !def.IsInSeason) {
                    continue;
                }
                var naturalChance = Math.Max(sp.Chance, sp.ChanceLocked);
                if (naturalChance <= 0f) { continue; }
                // "Owned" the way SkipOwned counts it: anywhere on the map
                var lacking = sp.SkipOwned - s.CountByDef(def);
                if (lacking <= 0) { continue; }
                // Any missing piece can come, but the ones the loot rarely brings are favored,
                // so the loot keeps filling the common ones and the set completes sooner
                missing.Add(def, lacking / Math.Max(0.05f, naturalChance));
            }
            if (missing.Count == 0) { return null; }
            DryDerelicts++;
            var curve = CurveDef.FastGet(CurveIdH.Derelict_ChessPity);
            if (curve == null || !rng.Chance(curve.Evaluate(DryDerelicts))) { return null; }
            return rng.TryWeightedFrom(missing, out var pick) ? pick : null;
        }

        // The streak ends only when a piece really stands on the new derelict
        private void OnDerelictApproached(StoryEventData data, ShipFactory factory) {
            if (factory == null) { return; }
            var pieces = S.Components.AllByType<ChessPieceComp>();
            if (pieces == null) { return; }
            var area = factory.ConfinementArea;
            foreach (var comp in pieces) {
                var tile = (comp as ChessPieceComp)?.Tile;
                if (tile == null) { continue; }
                Pos.ToXY(tile.PosIdx, out var x, out var y);
                if (x >= area.MinX && x <= area.MaxX && y >= area.MinY && y <= area.MaxY) {
                    DryDerelicts = 0;
                    return;
                }
            }
        }

        public string GetName() {
            return Id;
        }

        private ComponentData data;
        private ComponentData Data => data ??= new(0, GetName());

        public ComponentData Save() {
            var d = Data;
            d.SetInt(K_DryDerelicts, DryDerelicts);
            return d;
        }

        public void Load(ComponentData data) {
            this.data = data;
            DryDerelicts = data.GetInt(K_DryDerelicts, 0);
        }

        public override void Unload() {
            data = null;
            DryDerelicts = 0;
            missing.Clear();
        }

        protected override void OnInitialize() {
            S.Sig.DerelictShipApproached.AddListener(OnDerelictApproached);
        }
    }
}
