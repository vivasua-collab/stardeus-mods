using System;
using Game.Constants;
using Game.Data;
using KL.Grid;
using Game.Rendering;
using KL.Utils;
using UnityEngine;

namespace Game.Visuals {
    public class TileGraphicResolver {
        [Flags] public enum Connection : byte {
            None = 0,
            North = 1,
            East = 2,
            South = 4,
            West = 8,
        }

        public static Connection ConnectionMask(bool n, bool e, bool s, bool w) {
            Connection res = Connection.None;
            if (n) { res |= Connection.North; }
            if (e) { res |= Connection.East; }
            if (s) { res |= Connection.South; }
            if (w) { res |= Connection.West; }
            return res;
        }

        /*
        * Connected tile is 4x4 tileset:
        *  ╥ ╔ ╦ ╗
        *  ║ ╠ ╬ ╣
        *  ╨ ╚ ╩ ╝
        *  ▓ ╞ ═ ╡
        */
        public readonly Connection[] Connections = new[] {
            // First row bottom to top
            ConnectionMask(false, false, false, false),
            ConnectionMask(true, false, false, false),
            ConnectionMask(true, false, true, false),
            ConnectionMask(false, false, true, false),
            // 2nd row bottom to top
            ConnectionMask(false, true, false, false),
            ConnectionMask(true, true, false, false),
            ConnectionMask(true, true, true, false),
            ConnectionMask(false, true, true, false),
            // 3rd row bottom to top
            ConnectionMask(false, true, false, true),
            ConnectionMask(true, true, false, true),
            ConnectionMask(true, true, true, true),
            ConnectionMask(false, true, true, true),
            // 4th row bottom to top
            ConnectionMask(false, false, false, true),
            ConnectionMask(true, false, false, true),
            ConnectionMask(true, false, true, true),
            ConnectionMask(false, false, true, true),
        };

        public readonly int[] ConnectionPositionsX = new[] {
            0,
            0,
            0,
            0,
            1,
            1,
            1,
            1,
            2,
            2,
            2,
            2,
            3,
            3,
            3,
            3,
        };
        public readonly int[] ConnectionPositionsY = new[] {
            0,
            1,
            2,
            3,
            0,
            1,
            2,
            3,
            0,
            1,
            2,
            3,
            0,
            1,
            2,
            3,
        };


        private readonly Shader objTileShader;
        private readonly Shader unbuiltTileShader;
        private readonly Shader unbuiltGatedTileShader;
        private readonly Shader unplaceableTileShader;
        private readonly Shader placeableTileShader;
        private readonly Shader[] defaultShaders;
        private readonly Sprites sprites;
        public static Shader UnbuiltShader;

        public TileGraphicResolver() {
            objTileShader = RenderingService.Shaders.Get("ObjTile");
            unbuiltTileShader = RenderingService.Shaders.Get("UnconstructedTile");
            unbuiltGatedTileShader = RenderingService.Shaders.Get("UnconstructedTileGated");
            unplaceableTileShader = RenderingService.Shaders.Get("UnplaceableTile");
            placeableTileShader = RenderingService.Shaders.Get("PlaceableTile");
            defaultShaders = new Shader[RenderLayers.MaxIdExclusive];
            foreach (var layer in RenderLayers.TileRendererLayerIds) {
                defaultShaders[layer] =
                    RenderingService.Shaders.Get(RenderLayers.DefaultShader(layer));
            }
            sprites = RenderingService.Sprites;
            UnbuiltShader = unbuiltTileShader;
        }

        public bool ResolveTile(Grid<Tile> grid, int pos, out SpriteData sd,
                out Shader sh, out bool isConstructed, out bool? placementCheckResult) {
            sd = null;
            bool isConnected = false;
            var tile = grid.Get(pos);
            isConstructed = true;
            sh = ResolveShader(pos, grid.Layer, true, null, null);
            if (tile == null) {
                placementCheckResult = null;
                return true;
            }
            var tr = tile.Transform;
            placementCheckResult = tile.PlacementCheckResult;
            if (placementCheckResult == null && tr.IsChild) {
                placementCheckResult = tr.Parent.PlacementCheckResult;
            }
            if (placementCheckResult != null) {
                if (placementCheckResult.Value) {
                    sh = placeableTileShader;
                } else {
                    sh = unplaceableTileShader;
                }
            }
            // We need this in order to unset properly
            var gfx = tile.Graphics;
            if (gfx == null) { return false; }
            gfx.CurrentShader = sh;
            if (gfx.GraphicId == null) { return false; }
            if (tile.Constructable != null) {
                isConstructed = tile.Constructable.IsFinished == true;
            } else {
                // decals
                isConstructed = true;
            }

            if (placementCheckResult == null) {
                if (!isConstructed) {
                    sh = (tile.Constructable != null && !tile.Constructable.IsTaskAutoSeeking)
                        ? unbuiltGatedTileShader : unbuiltTileShader;
                    gfx.CurrentShader = sh;
                } else if (gfx.HasShader && grid.Layer != WorldLayer.Tools) {
                    sh = gfx.Shader;
                    gfx.CurrentShader = sh;
                }
            }

            if (gfx.Type == GraphicType.Connected) {
                sd = ResolveConnectedTile(grid, pos, tile, gfx.GraphicId);
                isConnected = true;
            } else {
                bool found = false;

                if (gfx.IsRotatable) {
                    sd = ResolveRotatableTile(tile, tr.Rotation);
                    found = true;
                }
                if (!found) {
                    sd = sprites.Find(tile.Graphics.Graphic());
                }
            }
            if (sd != null) {
                sd.WidthTiles = gfx.GraphicWidth;
                sd.HeightTiles = gfx.GraphicHeight;
                sd.HasHeight = gfx.HasHeight;
                sd.Scale = gfx.Scale;
            }
            return isConnected;
        }

        public Shader ResolveShader(int pos, int layer, bool isConstructed, bool? placementCheckResult,
                Shader mainShader) {
            if (!isConstructed) {
                if (placementCheckResult != null) {
                    if (placementCheckResult.Value) {
                        return placeableTileShader;
                    } else {
                        return unplaceableTileShader;
                    }
                }
                if (ReferenceEquals(mainShader, unbuiltTileShader)
                        || ReferenceEquals(mainShader, unbuiltGatedTileShader)) {
                    return mainShader;
                }
                return unbuiltTileShader;
            }
            if (ReferenceEquals(mainShader, unbuiltTileShader)) {
                return mainShader;
            }
            if (layer >= 0 && layer < defaultShaders.Length
                    && defaultShaders[layer] != null) {
                return defaultShaders[layer];
            }
            D.Err("Trying to resolve shader for an unexpected tile layer: {0}. Pos: {1}",
                layer, Pos.String(pos));
            return objTileShader;
        }

        private SpriteData FlippedTile(string id, Tile tile, Facing.Type rotation) {
            // Try flipping the opposite
            var opFacing = Facing.Opposite(rotation);
            var oppositeId = tile.Graphics.GraphicRot()[(int) opFacing];
            if (sprites.TryFind(oppositeId, out var sdOp)) {
                var sdFlipped = sdOp.Copy();
                sdFlipped.DoFlip(Facing.ToFlip(opFacing));
                // Rememeber for next time
                sdFlipped.Id = id;
                sdFlipped.IdH = Hashes.S(id);
                sprites.Store(sdFlipped);
                return sdFlipped;
            }
            return null;
        }

        private SpriteData RotatedTile(string id, Tile tile, Facing.Type rotation) {
            // There was no opposite, try just rotating base tile
            var sdRot = sprites.Find(tile.Graphics.Graphic());
            sdRot = sdRot.Copy();
            // but if it's a multitile object, let's return the base tile
            // we will then only rotate the work spot
            if (rotation != Facing.Type.Down && !tile.Transform.IsMulti) {
                sdRot.Rotation = Facing.ToRot(rotation);
            }
            sdRot.Id = id;
            sdRot.IdH = Hashes.S(id);
            sprites.Store(sdRot);
            return sdRot;
        }

        public SpriteData ResolveRotatableTile(Tile tile, Facing.Type rotation) {
            var rots = tile.Graphics.GraphicRot();
            string id;
            if (rots == null) {
                D.Err("Rotations was null for tile {0} at {1}",
                    tile.Definition.Id, tile.PosIdx);
                return sprites.Find(tile.Graphics.Graphic());
            } else {
                id = rots[(int) rotation];
            }

            if (sprites.TryFind(id, out var sd)) {
                // yay, cached
                return sd;
            }

            // Do the heavy lifting
            bool isFlipped = !tile.Transform.IsMulti
                && (sprites.Contains(rots[0])
                || sprites.Contains(rots[1])
                || sprites.Contains(rots[2])
                || sprites.Contains(rots[3]));

            if (isFlipped) {
                sd = FlippedTile(id, tile, rotation);
                // Cache all variants
                for (int i = 0; i < 4; i++) {
                    var id2 = rots[i];
                    if (id != id2 && !sprites.Contains(id2)) {
                        FlippedTile(id2, tile, rotation);
                    }
                }
            } else {
                sd = RotatedTile(id, tile, rotation);
                // Cache all variants
                for (int i = 0; i < 4; i++) {
                    var id2 = rots[i];
                    if (id != id2) { RotatedTile(id2, tile, (Facing.Type) i); }
                }
            }

            return sd;
        }

        private bool IsConnectedWith(Tile neighbor, Tile tile) {
            return neighbor != null;
        }

        private SpriteData ResolveConnectedTile(Grid<Tile> grid, int pos,
                Tile tile, string graphicId) {
            var baseSd = sprites.Find(graphicId);
            var sdCopy = baseSd.Copy();
            var UV = sdCopy.UV;

            // get neighbor connectedness
            var n = grid.GetSafe(Pos.N(pos));
            var e = grid.GetSafe(Pos.E(pos));
            var s = grid.GetSafe(Pos.S(pos));
            var w = grid.GetSafe(Pos.W(pos));

            // get connection mask
            var mask = ConnectionMask(
                IsConnectedWith(n, tile),
                IsConnectedWith(e, tile),
                IsConnectedWith(s, tile),
                IsConnectedWith(w, tile));

            // get the connection tile position
            var gfxId = Array.IndexOf(Connections, mask);
            var x = ConnectionPositionsX[gfxId];
            var y = ConnectionPositionsY[gfxId];

            // Calculate tile spot
            var fracX = (UV.xMax - UV.xMin) / 4;
            var fracY = (UV.yMax - UV.yMin) / 4;
            sdCopy.SetUV(new Rect(
                UV.xMin + x * fracX, UV.yMin + y * fracY, fracX, fracY));

            return sdCopy;
        }
    }
}
