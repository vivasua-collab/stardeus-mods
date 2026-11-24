using System;
using KL.Utils;
using UnityEngine;

namespace Game.Visuals.Hull {
    // The look of one hull view tileset, read from Graphics/Structure/Hull/<Tileset>/HullStyle.json
    // (HullStyles). Every field starts at the Default look, so a style file lists only what it
    // changes. Lengths are in tiles, angles in degrees, chances from 0 to 1. Only numbers that
    // change the look live here; the ones that keep the bake correct stay in the bake's code.
    public sealed class HullStyleDef {
        public RoofLook Roof = new();
        public SlopeLook Slope = new();
        public PanelLook Panels = new();
        public MotifLook Motifs = new();
        public DetailLook Details = new();

        // Where the flat top's inset line runs, in from the roof's foot
        public float Top => Roof.Rim + Roof.Inset;

        public sealed class RoofLook {
            // The sloped rim runs this far in from the roof's foot, then the flat top starts
            public float Rim = 0.85f;
            // The inset line runs this far past the rim; motifs and panels stay inside it
            public float Inset = 1f;
            // Wall pieces standing inside one room up to this size are roofed over; bigger ones
            // are probably real structure and show as pits
            public int MaxPillarTiles = 12;
            // The RoofSurface art covers this many tiles before it repeats
            public float SurfaceTiles = 4f;
            public float TopTone = 0.92f;
            // How far the roof and its details are lifted toward full brightness from the
            // ambient light, so they read from afar
            public float Lift = 0.55f;
            // Where the light comes from (x, y), for the slopes, creases and panel bevels
            public float[] LightDir = { -0.6224f, 0.7827f };
            // The soft shadow just inside the crease at the top of the slope
            public float CreaseShadow = 0.14f;
            public float CreaseShadowWidth = 0.12f;
            // The ink line along the inset line: darkness and width (in ink line widths)
            public float InsetInk = 0.4f;
            public float InsetInkWidth = 0.6f;
            // The ink line along the slope's foot and down the valleys (in ink line widths)
            public float FootInkWidth = 0.5f;
        }

        public sealed class SlopeLook {
            // The rim's colour: the hue and saturation of the RoofSurface art's average, so the
            // rim reads as the same material as the top (Tomas), at the rim's own brightness
            public string Color = "#5B6171";
            // Brightness: Light plus FacingLight on the sides that face the light
            public float Light = 0.78f;
            public float FacingLight = 0.5f;
            // How much darker the slope gets toward its foot
            public float FootShade = 0.18f;
            // The crease at the top of the slope: brighter on the lit sides, darker on the others
            public float CreaseLit = 1.25f;
            public float CreaseDark = 0.7f;
            public float CreaseWidth = 0.75f;
            // The sky's light catching the outer edge where the roof meets the open, the same
            // glow as on hull walls (HullRim in Tiles.cginc: 2 and 0.3 there): how strong, and
            // how far in it fades (tiles)
            public float SkyLight = 2f;
            public float SkyRimWidth = 0.3f;

            private bool isParsed;
            private string parsedFrom;
            private UnityEngine.Color parsed;

            public UnityEngine.Color ParsedColor() {
                if (isParsed && parsedFrom == Color) { return parsed; }
                isParsed = true;
                parsedFrom = Color;
                if (!Colors.TryFromHex(Color, out parsed)) {
                    D.Warn("Invalid hull slope color (should be hex, like #5B6171): {0}", Color);
                    Colors.TryFromHex("#5B6171", out parsed);
                }
                return parsed;
            }
        }

        public sealed class PanelLook {
            // No panel between the seams is left bigger than this (tiles squared) or longer than
            // MaxSpan: a big empty roof reads as unfinished
            public float MaxArea = 30f;
            public float MaxSpan = 7f;
            // A split leaves at least this much panel on either side
            public float MinSplitSide = 1.5f;
            // How far a split may land from the panel's middle, so plates stagger
            public float SplitJitter = 1f;
            // How often a split's end cuts the corner it makes, how big, and at what angle to
            // the seam it meets
            public double ChamferChance = 0.35;
            public float[] ChamferSizes = { 0.5f, 0.75f };
            public float ChamferAngle = 45f;
            // Each panel is raised, recessed or flat (the rest)
            public float RaisedChance = 0.45f;
            public float RecessedChance = 0.25f;
            // Panels smaller than this (tiles squared) stay flat: bevels on slivers look noisy
            public float MinShapedArea = 0.75f;
            // The bevel along a raised or recessed panel's edges: width (at least BevelMinPx
            // screen pixels) and how strongly it catches the light
            public float BevelWidth = 0.12f;
            public float BevelMinPx = 2.5f;
            public float BevelLight = 0.4f;
            // The thin light line inside a flat panel's edges
            public float FlatLip = 0.1f;
            // How much lighter or darker panels get: at random, and by being raised or recessed
            public float PanelTone = 0.06f;
            public float TypeTone = 0.06f;
            // The ink line along the seams: darkness and width (in ink line widths)
            public float SeamInk = 0.4f;
            public float SeamInkWidth = 0.6f;
        }

        public sealed class MotifLook {
            // How often each motif is picked for a room (0: never)
            public MotifWeights Weights = new();
            // Rooms whose main panel is narrower or smaller than this get no motif
            public float MinSide = 2f;
            public float MinArea = 8f;
            public PlatesLook Plates = new();
            public HexesLook Hexes = new();
            public FishboneLook Fishbone = new();
            public FrameLook Frame = new();
        }

        public sealed class MotifWeights {
            public float Plates = 1f;
            public float Hexes = 1f;
            public float Fishbone = 1f;
            public float Frame = 1f;
        }

        // A band along the panel with a raised middle step, a peak, cross lines and corner cuts
        public sealed class PlatesLook {
            // Narrower panels always take the first band width
            public float NarrowBelow = 2.5f;
            public float[] BandHalfWidths = { 0.5f, 1f };
            // The raised middle step, when there is StepRoom beside the band
            public float[] Steps = { 0.5f, 1f };
            public float StepRoom = 1.75f;
            // How far along the panel the step may start (share of the half length)
            public float StepReach = 0.45f;
            public float PeakChance = 0.5f;
            public float PeakMax = 1f;
            public float PeakAngle = 45f;
            public float CrossChance = 0.75f;
            public float CornerCutChance = 0.6f;
            public float[] CornerCutSizes = { 1f, 1.5f, 2f };
            public float CornerCutAngle = 45f;
        }

        // Elongated hexagons along the panel, nested when there is room
        public sealed class HexesLook {
            // Half height of a hexagon, as a share of the panel's half width
            public float HeightShare = 0.75f;
            public float Gap = 0.5f;
            public int MaxCount = 3;
            public float NestChance = 0.6f;
            public float NestInset = 0.5f;
            public float VerticalsChance = 0.5f;
            // The slanted ends, from the long axis
            public float Angle = 45f;
        }

        // A spine with ribs running out from it
        public sealed class FishboneLook {
            // Narrower panels always take the first spine width
            public float NarrowBelow = 2f;
            public float[] SpineHalfWidths = { 0f, 0.5f };
            public float[] RibGaps = { 2f, 2.5f, 3f };
            public float HalfOffsetChance = 0.5f;
            public float CenterLineChance = 0.5f;
            // The ribs, from the spine
            public float RibAngle = 45f;
        }

        // A second inset line following the room's outline
        public sealed class FrameLook {
            // Narrower panels always take NarrowGap
            public float NarrowBelow = 2.5f;
            public float[] Gaps = { 0.75f, 1f };
            public float NarrowGap = 0.5f;
            public float CrossChance = 0.6f;
            public float SecondCrossChance = 0.5f;
        }

        public sealed class DetailLook {
            // Details keep this far past the rim, clear of the slope
            public float Clearance = 0.55f;
            public PieceLook Hatch = new() { MinRoomTiles = 40, Size = new[] { 2, 2 } };
            public PieceLook Hub = new() { MinRoomTiles = 80, Size = new[] { 2, 2 }, Spacing = 4 };
            public GrilleLook Grilles = new();
            public PieceLook Vent = new() { MinRoomTiles = 120, Size = new[] { 2, 1 }, Spacing = 5 };
            public LightLook Lights = new();
        }

        public sealed class PieceLook {
            // Rooms of at least this many tiles get one
            public int MinRoomTiles;
            // Tiles across and up
            public int[] Size;
            // Tiles (along the axes) kept from the room's other details
            public int Spacing;
        }

        // Grilles toward the room's corners
        public sealed class GrilleLook {
            public int Count = 2;
            public int BigRoomCount = 4;
            public int BigRoomTiles = 60;
            public int[] Size = { 1, 1 };
            public int Spacing = 2;
        }

        // Running lights on the rim over straight wall runs
        public sealed class LightLook {
            // One every this many tiles
            public int Every = 7;
            // How far up the rim from its foot
            public float Height = 0.25f;
            public float Size = 1f;
            // How many times brighter than drawn the light's bright core shines, so it passes
            // white and glows with the Glow setting. 1 keeps the art as drawn.
            public float Glow = 2.5f;
        }

        // How far a line runs across for each tile along, at this angle. Worked out in double so
        // 45 degrees gives exactly 1 and the Core styles keep their seams to the texel.
        public static float Rise(float degrees) {
            return (float) Math.Tan(degrees * Math.PI / 180.0);
        }

        // How far along a line at this angle two parallels one tile apart are (sqrt 2 at 45)
        public static float Slant(float degrees) {
            return (float) (1.0 / Math.Sin(degrees * Math.PI / 180.0));
        }

        // Clamps what would hang the bake, crash it or turn the shader's maths into NaN; any
        // other value is left alone, however odd it looks
        public void Sanitize() {
            var defaults = new HullStyleDef();
            // a group written as null in a style file falls back whole
            Roof ??= defaults.Roof;
            Slope ??= defaults.Slope;
            Panels ??= defaults.Panels;
            Motifs ??= defaults.Motifs;
            Motifs.Weights ??= defaults.Motifs.Weights;
            Motifs.Plates ??= defaults.Motifs.Plates;
            Motifs.Hexes ??= defaults.Motifs.Hexes;
            Motifs.Fishbone ??= defaults.Motifs.Fishbone;
            Motifs.Frame ??= defaults.Motifs.Frame;
            Details ??= defaults.Details;
            Details.Hatch ??= defaults.Details.Hatch;
            Details.Hub ??= defaults.Details.Hub;
            Details.Grilles ??= defaults.Details.Grilles;
            Details.Vent ??= defaults.Details.Vent;
            Details.Lights ??= defaults.Details.Lights;
            Roof.Rim = Mathf.Max(0.05f, Roof.Rim);
            Slope.SkyRimWidth = Mathf.Max(0.01f, Slope.SkyRimWidth);
            Roof.Inset = Mathf.Max(0f, Roof.Inset);
            Roof.SurfaceTiles = Mathf.Max(0.1f, Roof.SurfaceTiles);
            if (Roof.LightDir == null || Roof.LightDir.Length != 2) {
                Roof.LightDir = defaults.Roof.LightDir;
            }
            Panels.MaxArea = Mathf.Max(1f, Panels.MaxArea);
            Panels.MaxSpan = Mathf.Max(1f, Panels.MaxSpan);
            Panels.MinSplitSide = Mathf.Max(0.5f, Panels.MinSplitSide);
            Panels.SplitJitter = Mathf.Max(0f, Panels.SplitJitter);
            Panels.ChamferSizes = NonEmpty(Panels.ChamferSizes, defaults.Panels.ChamferSizes);
            Panels.ChamferAngle = Angle(Panels.ChamferAngle);
            var w = Motifs.Weights;
            w.Plates = Mathf.Max(0f, w.Plates);
            w.Hexes = Mathf.Max(0f, w.Hexes);
            w.Fishbone = Mathf.Max(0f, w.Fishbone);
            w.Frame = Mathf.Max(0f, w.Frame);
            var p = Motifs.Plates;
            p.BandHalfWidths = NonEmpty(p.BandHalfWidths, defaults.Motifs.Plates.BandHalfWidths);
            p.Steps = NonEmpty(p.Steps, defaults.Motifs.Plates.Steps);
            p.CornerCutSizes = NonEmpty(p.CornerCutSizes, defaults.Motifs.Plates.CornerCutSizes);
            p.PeakAngle = Angle(p.PeakAngle);
            p.CornerCutAngle = Angle(p.CornerCutAngle);
            var h = Motifs.Hexes;
            h.HeightShare = Mathf.Max(0.05f, h.HeightShare);
            h.MaxCount = Mathf.Max(1, h.MaxCount);
            h.Angle = Angle(h.Angle);
            var f = Motifs.Fishbone;
            f.SpineHalfWidths = NonEmpty(f.SpineHalfWidths, defaults.Motifs.Fishbone.SpineHalfWidths);
            f.RibGaps = NonEmpty(f.RibGaps, defaults.Motifs.Fishbone.RibGaps);
            // the ribs are laid out one gap after another until the panel ends
            for (int i = 0; i < f.RibGaps.Length; i++) {
                f.RibGaps[i] = Mathf.Max(0.25f, f.RibGaps[i]);
            }
            f.RibAngle = Angle(f.RibAngle);
            Motifs.Frame.Gaps = NonEmpty(Motifs.Frame.Gaps, defaults.Motifs.Frame.Gaps);
            var d = Details;
            Piece(d.Hatch, defaults.Details.Hatch);
            Piece(d.Hub, defaults.Details.Hub);
            Piece(d.Vent, defaults.Details.Vent);
            d.Grilles.Count = Mathf.Clamp(d.Grilles.Count, 0, 4);
            d.Grilles.BigRoomCount = Mathf.Clamp(d.Grilles.BigRoomCount, 0, 4);
            d.Grilles.Size = Size(d.Grilles.Size, defaults.Details.Grilles.Size);
            d.Lights.Every = Mathf.Max(1, d.Lights.Every);
            d.Lights.Size = Mathf.Max(0.1f, d.Lights.Size);
        }

        private static float Angle(float degrees) {
            return Mathf.Clamp(degrees, 5f, 85f);
        }

        private static float[] NonEmpty(float[] values, float[] defaults) {
            return values == null || values.Length == 0 ? defaults : values;
        }

        private static void Piece(PieceLook piece, PieceLook defaults) {
            piece.Size = Size(piece.Size, defaults.Size);
            piece.Spacing = Mathf.Max(0, piece.Spacing);
        }

        private static int[] Size(int[] size, int[] defaults) {
            if (size == null || size.Length != 2) { return defaults; }
            size[0] = Mathf.Max(1, size[0]);
            size[1] = Mathf.Max(1, size[1]);
            return size;
        }
    }
}
