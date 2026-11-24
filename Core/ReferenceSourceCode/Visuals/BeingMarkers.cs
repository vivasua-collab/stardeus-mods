using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Rendering;
using Game.Systems.Creatures;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Visuals {
    public enum MarkerSide { Ours, Guest, Hostile }

    // A colored dot or square on every living being when zoomed out far enough that the beings
    // themselves are a few pixels big (STA-1661). Not tied to the hull view: it covers a
    // medium ship well before the beings get that small, and markers that early cluttered
    // the view (Tomas, 2026-10-14). White is ours, green a guest, red anyone else. Humans get a dot, other
    // creatures a smaller dot, bots a square. The markers keep their size on screen, so the
    // mesh is rebuilt every frame while they show; it is one quad per being and one draw.
    // What was drawn is kept for FreeSelectBeingMarkerHandler, which hovers and clicks them.
    public sealed class BeingMarkers {
        public const bool DefaultEnabled = true;
        // A tile this big on screen (pixels) or bigger shows no markers, this small or smaller
        // shows them fully; in between they fade with the zoom
        private const float FadeStartTilePx = 12f;
        private const float FadeEndTilePx = 9f;
        // Marker radius in pixels at UI scale 1
        private const float HumanRadiusPx = 7f;
        private const float CreatureRadiusPx = 5f;
        private const float BotRadiusPx = 6f;
        private const float OutlinePx = 1.5f;
        private const float HoverGrow = 1.3f;
        private const float IncapacitatedAlpha = 0.5f;
        // How far outside a marker the mouse still picks it, in pixels
        private const float PickSlackPx = 3f;
        // The name labels' colors (BeingNameTextComp: ours white, guests green, hostile red),
        // brightened to read as a few pixels on dark space
        private static readonly Color OursColor = new Color32(240, 240, 240, 255);
        private static readonly Color GuestColor = new Color32(96, 232, 64, 255);
        private static readonly Color HostileColor = new Color32(255, 64, 56, 255);

        private readonly GameState S;
        private readonly Material material;
        private readonly int cullingLayer;
        private Mesh mesh;
        private readonly List<Vector3> verts = new();
        private readonly List<Vector2> uvs = new();
        private readonly List<Vector4> shapes = new();
        private readonly List<Vector2> centers = new();
        private readonly List<Color32> colors = new();
        private readonly List<int> tris = new();
        private readonly List<Drawn> drawn = new();
        private readonly List<float> pickDist = new();
        private float alpha;
        private float pxPerUnit = 1f;

        private struct Drawn {
            public Being Being;
            public Vector2 Center;
            public float Radius;
        }

        // Set by the hover handler every frame it hovers one; drawn a bit bigger, with a light
        // outline. Hover is not called while the mouse is over the UI, so it expires.
        private Being hovered;
        private int hoveredFrame;
        public bool IsVisible => alpha > 0f && drawn.Count > 0;

        public void SetHovered(Being b) {
            hovered = b;
            hoveredFrame = Time.frameCount;
        }

        public static bool IsEnabled => The.Prefs.GetBool(Pref.GBeingMarkers,
            PrefH.GBeingMarkers, DefaultEnabled);

        public BeingMarkers(GameState s) {
            S = s;
            var shader = Shader.Find("BeingMarker");
            if (shader == null) {
                D.Err("BeingMarker shader not found, being markers are off");
                return;
            }
            material = new Material(shader) { name = "BeingMarker" };
            The.Alloc.Register(material);
            cullingLayer = RenderLayers.ById(WorldLayer.Floor).CullingLayer;
        }

        public void Update(Camera cam) {
            drawn.Clear();
            if (material == null || cam == null) { return; }
            alpha = WantedAlpha(cam);
            if (alpha <= 0f) {
                hovered = null;
                return;
            }
            if (Time.frameCount - hoveredFrame > 1) {
                hovered = null;
            }
            pxPerUnit = cam.pixelHeight / (2f * cam.orthographicSize);
            var pxScale = Video.GetCurrentUIScale(out _) / pxPerUnit;
            BuildMesh(pxScale);
            if (drawn.Count == 0) { return; }
            Graphics.DrawMesh(mesh, Matrix4x4.identity, material, cullingLayer);
        }

        private float WantedAlpha(Camera cam) {
            if (UIShowing.MainMenu || UIShowing.FullScreenWidget || ToggleUI.IsUIHidden
                    || !IsEnabled) {
                return 0f;
            }
            if (!cam.orthographic || cam.orthographicSize <= 0f) { return 0f; }
            var tilePx = cam.pixelHeight / (2f * cam.orthographicSize);
            return Mathf.InverseLerp(FadeStartTilePx, FadeEndTilePx, tilePx);
        }

        private void BuildMesh(float pxScale) {
            verts.Clear();
            uvs.Clear();
            shapes.Clear();
            centers.Clear();
            colors.Clear();
            tris.Clear();
            var alive = S.Beings.Alive;
            for (int i = 0; i < alive.Count; i++) {
                var b = alive[i];
                if (!TryStyle(b, out var color, out var isSquare, out var radiusPx)) {
                    continue;
                }
                var isHovered = b == hovered;
                if (isHovered) {
                    radiusPx *= HoverGrow;
                }
                var r = radiusPx * pxScale;
                var outline = OutlinePx / radiusPx;
                var c = b.SelectPosition;
                AddQuad(c, r, color, isSquare, outline, isHovered);
                drawn.Add(new Drawn { Being = b, Center = c, Radius = r });
            }
            if (mesh == null) {
                mesh = new Mesh { name = "BeingMarkers" };
                mesh.MarkDynamic();
                The.Alloc.Register(mesh);
            }
            mesh.Clear();
            if (drawn.Count == 0) { return; }
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetUVs(1, shapes);
            mesh.SetUVs(2, centers);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0, false);
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(1e6f, 1e6f, 1f));
        }

        // The same sides the name labels show (BeingNameTextComp): a restrained colonist
        // (AntiPlayerTemp) is still ours, a restrained guest is still a guest
        public static MarkerSide SideOf(BeingData data) {
            var fac = data.Mind.Faction;
            if (data.IsGuest) { return MarkerSide.Guest; }
            if (fac.IsPlayer || fac.IdH == FactionIdH.AntiPlayerTemp
                    || data.EffectiveFaction.IsPlayer) {
                return MarkerSide.Ours;
            }
            return MarkerSide.Hostile;
        }

        // Whether the being gets a marker, and how it looks
        private bool TryStyle(Being b, out Color32 color, out bool isSquare,
                out float radiusPx) {
            color = default;
            isSquare = false;
            radiusPx = 0f;
            if (!b.IsActive || b.Graphics == null || b.Graphics.IsHidden) { return false; }
            // the carrier's marker covers whoever it carries
            if (b.IsCarriedByAny) { return false; }
            var data = b.Data;
            if (data.Mind == null || data.IsInStasis || data.Mind.Faction == null
                    || data.Mind.Faction.IsBlank) {
                return false;
            }
            var baseColor = SideOf(data) switch {
                MarkerSide.Ours => OursColor,
                MarkerSide.Guest => GuestColor,
                _ => HostileColor,
            };
            var a = alpha;
            if (data.Body.IsIncapacitated) {
                a *= IncapacitatedAlpha;
            }
            baseColor.a = a;
            color = baseColor;
            var species = b.Species;
            if (!species.IsBiological) {
                isSquare = true;
                radiusPx = BotRadiusPx;
            } else if (species.IsHumanoid) {
                radiusPx = HumanRadiusPx;
            } else {
                radiusPx = CreatureRadiusPx;
            }
            return true;
        }

        private void AddQuad(Vector2 c, float r, Color32 color, bool isSquare, float outline,
                bool isHovered) {
            var i0 = verts.Count;
            verts.Add(new Vector3(c.x - r, c.y - r, 0f));
            verts.Add(new Vector3(c.x - r, c.y + r, 0f));
            verts.Add(new Vector3(c.x + r, c.y + r, 0f));
            verts.Add(new Vector3(c.x + r, c.y - r, 0f));
            uvs.Add(new Vector2(-1f, -1f));
            uvs.Add(new Vector2(-1f, 1f));
            uvs.Add(new Vector2(1f, 1f));
            uvs.Add(new Vector2(1f, -1f));
            var shape = new Vector4(isSquare ? 1f : 0f, outline, isHovered ? 1f : 0f, 0f);
            for (int k = 0; k < 4; k++) {
                shapes.Add(shape);
                centers.Add(c);
                colors.Add(color);
            }
            tris.Add(i0);
            tris.Add(i0 + 1);
            tris.Add(i0 + 2);
            tris.Add(i0);
            tris.Add(i0 + 2);
            tris.Add(i0 + 3);
        }

        // Beings whose marker the world position is on (with a few pixels of slack), nearest
        // first. Only what was drawn this frame counts.
        public int Pick(Vector2 worldPos, List<Being> result) {
            result.Clear();
            if (!IsVisible) { return 0; }
            var slack = PickSlackPx / pxPerUnit;
            pickDist.Clear();
            for (int i = 0; i < drawn.Count; i++) {
                var d = drawn[i];
                var reach = d.Radius + slack;
                var dist = (d.Center - worldPos).sqrMagnitude;
                if (dist > reach * reach) { continue; }
                var at = pickDist.Count;
                while (at > 0 && pickDist[at - 1] > dist) { at--; }
                pickDist.Insert(at, dist);
                result.Insert(at, d.Being);
            }
            return result.Count;
        }

        public void Dispose() {
            drawn.Clear();
            hovered = null;
            if (mesh != null) {
                Object.Destroy(mesh);
                The.Alloc.Unregister(mesh);
                mesh = null;
            }
            if (material != null) {
                Object.Destroy(material);
                The.Alloc.Unregister(material);
            }
        }
    }
}
