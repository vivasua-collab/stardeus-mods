using System;
using Game.Constants;
using Game.Data;
using Game.Rendering;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;
using KL.Grid;

namespace Game.Components {
    public sealed class TileGraphicsComp : BaseComponent<TileGraphicsComp> {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new TileGraphicsComp());
        }
        public TileGraphicsComp() {
            SkipTypeLookupCache = true;
        }
        public int AnimationFrame;
        private string initialGraphicId;
        public string GraphicId;
        public bool IsRotatable;
        private string[] graphicRot;
        private string[][] graphicRotAnim;
        private string[] graphicAnim;
        public GraphicType Type;
        public bool IsAnimated;
        public int GraphicHeight = 1;
        public int GraphicWidth = 1;
        public Vector2 OriginalGraphicSize => HasHeight ?
                new Vector2(GraphicWidth, GraphicHeight + 1) :
                new Vector2(GraphicWidth, GraphicHeight);
        public bool HasHeight;
        public bool HasShader;
        public Shader Shader;
        public Shader CurrentShader;
        public int AnimationFrames;
        public Vector2 Scale = new(1, 1);
        // Floors only: the strip drawn as the side of the slab under a south edge
        // (Foundation.shader); the fallback strip when null (FoundationStyles)
        public string FoundationId;
        // Floors only: whether the floor is an angled corner whose transparent half is where
        // the ship ends (ink, foundation: FloorKindBuffer). Null when the def does not say,
        // and then guessed from passability and integrity, which mod frames that are not
        // walkable without being corners need to override.
        public bool? IsAngled;

        public static TileGraphicsComp Decal(string gfxId) {
            var tg = new TileGraphicsComp();
            tg.Type = GraphicType.Decal;
            tg.GraphicId = gfxId;
            return tg;
        }

        public string[] GraphicRot() {
            if (AnimationFrame == 0) {
                return graphicRot;
            }
            return graphicRotAnim[AnimationFrame - 1];
        }

        public SpriteData IconSD => IconSDFor(Entity.Definition.Id, Config);

        public static SpriteData IconSDFor(string defId, ComponentConfig c) {
            var g = c.GetString(PropertyIdH.Graphic);
            SpriteData sd;
            if (RenderingService.Sprites.TryFind(g, out sd)) {
                // Found regular sprite
            } else if (RenderingService.Sprites.TryFind($"{g}_D", out sd)) {
                // Found rotatable sprite with down rotation
            } else if (RenderingService.Sprites.TryFind($"{g}_0_D", out sd)) {
                // Found animated rotatable sprite with down rotation
            } else if (RenderingService.Sprites.TryFind($"{g}_0", out sd)) {
                // Found animated sprite
            }
            if (sd == null) {
                D.Err("Could not figure out preview for tile: {0}", defId);
                return RenderingService.Sprites.Find(GraphicsIdH.IconsColorMissing).Copy();
            }
            var type = c.GetString(PropertyIdH.Type, null);
            if (type != null && type.StartsWith("Connected")) {
                var copy = sd.Copy();
                // Show only the single tile for connected pieces
                copy.SetUV(new Rect(copy.UV.xMin, copy.UV.yMin,
                    copy.UV.width * 0.25f, copy.UV.height * 0.25f));
                sd = copy;
            }
            if (sd == null) {
                D.Err("Could not figure out preview for tile: {0}", defId);
                return RenderingService.Sprites.Find(GraphicsIdH.IconsColorMissing).Copy();
            }
            return sd;
        }

        public string Graphic() {
            if (AnimationFrame == 0) {
                return GraphicId;
            }
            return graphicAnim[AnimationFrame - 1];
        }

        public void SetAnimationFrame(int frame) {
            if (AnimationFrame == frame) { return; }
            AnimationFrame = frame;
            EntityUtils.TriggerChange(Tile, false);
        }

        public override void OnReady(bool wasLoaded) {
            GraphicHeight = Tile.Transform.Height;
            GraphicWidth = Tile.Transform.Width;
        }

        private bool ResolveRotatable() {
            if (!Config.GetBool(PropertyIdH.RotateGraphic, true)) {
                return false;
            }

            var transform = Tile.Transform;
            if (transform == null) {
                D.Err("Transform is null for {0}", entity.Definition?.Id);
            } else {
                return transform.IsRotatable;
            }
            var trConfig = Tile.Definition.ComponentConfigFor(ComponentIdH.TileTransform);
            if (trConfig == null) {
                D.Err("TileTransform config is null on tile {0}", Tile);
                return TileTransformComp.DefaultIsRotatable;
            }
            if (trConfig.TryFindProperty(PropertyIdH.IsRotatable, out var prop)) {
                return prop.Bool;
            }
            // IsRotatable is not defined, but it defaults to false
            return TileTransformComp.DefaultIsRotatable;
        }

        private GraphicType ParseGraphicType(string type) {
            if (type == null) { return GraphicType.Basic; }
            // this is hella slow when deserialization go brr
            // Type = Enums.FromString<GraphicType>(Config.GetString(PropertyIdH.Type", "Basic));
            switch (type) {
                case "Basic" : return GraphicType.Basic;
                case "Connected" : return GraphicType.Connected;
                case "Object" : return GraphicType.Object;
                case "Decal" : return GraphicType.Decal;
            }
            D.Err("Unknown graphic type: {0} in TileGraphicsComp of {1}",
                type, Entity.Definition.Id);
            return GraphicType.Basic;
        }

        private bool aliasLR;
        private bool aliasUD;

        protected override void OnConfig() {
            IsRotatable = ResolveRotatable();

            var shaderName = Config.GetString(PropertyIdH.Shader, null);
            if (!string.IsNullOrWhiteSpace(shaderName)) {
                Shader = RenderingService.Shaders.Get(shaderName);
                HasShader = true;
            }
            initialGraphicId = GraphicId = Config.GetString(PropertyIdH.Graphic);
            aliasLR = Config.GetBool(PropertyIdH.AliasLR, false);
            aliasUD = Config.GetBool(PropertyIdH.AliasUD, false);
            AnimationFrames = Config.GetInt(PropertyIdH.AnimationFrames, 0);
            IsAnimated = AnimationFrames > 0;
            var scaleX = Config.GetFloat(PropertyIdH.ScaleX, 1);
            var scaleY = Config.GetFloat(PropertyIdH.ScaleY, 1);
            Scale = new Vector2(scaleX, scaleY);
            Type = ParseGraphicType(Config.GetString(PropertyIdH.Type, null));
            HasHeight = Config.GetBool(PropertyIdH.HasHeight, false);
            FoundationId = Config.GetString(PropertyIdH.Foundation, null);
            IsAngled = Config.TryFindProperty(PropertyIdH.IsAngled, out var angled)
                ? angled.Bool : null;
            ValidateLayerAboveForHeight();
            BuildGraphics();
        }

        private void ValidateLayerAboveForHeight() {
            if (!HasHeight) { return; }
            var def = Entity?.Definition;
            if (def == null) { return; }
            var layer = RenderLayers.ById(def.LayerId);
            D.Ass(layer != null && layer.LayerAbove != -1,
                "Tile def {0} has height but layer {1} has no LayerAbove",
                def.Id, WorldLayer.Name(def.LayerId));
        }

        private string AliasedRot(string input) {
            if (aliasLR && input.EndsWith("_L")) {
                return $"{input.Remove(input.Length - 2, 2)}_R";
            }
            if (aliasUD && input.EndsWith("_U")) {
                return $"{input.Remove(input.Length - 2, 2)}_D";
            }
            return input;
        }

        public void ResetGraphic() {
            if (GraphicId != initialGraphicId) {
                // D.Warn("Resetting graphic id for tile: {0} -> {1} | {2}",
                //     GraphicId, initialGraphicId, Tile);
                SetGraphicId(initialGraphicId);
            }
        }

        public void SetGraphicId(string id, bool delayIfNotReady = true) {
            if (delayIfNotReady && !Ready.Rendering) {
                S.Clock.OnNextUpdate(() => SetGraphicId(id, false));
                return;
            }
            var isIdChanged = id != GraphicId;
            if (!isIdChanged) { return; }
            GraphicId = id;
            if (!hasBuiltGraphics) {
                BuildGraphics();
            } else {
                try {
                    if (IsRotatable) {
                        graphicRot[0] = string.Format(AliasedRot("{0}_D"), id);
                        graphicRot[1] = string.Format(AliasedRot("{0}_L"), id);
                        graphicRot[2] = string.Format(AliasedRot("{0}_U"), id);
                        graphicRot[3] = string.Format(AliasedRot("{0}_R"), id);
                    }
                    if (IsAnimated) {
                        if (IsRotatable) {
                            //graphicRotAnim = new string[AnimationFrames - 1][];
                            for (int i = 1; i < AnimationFrames; i++) {
                                graphicRotAnim[i - 1][0] = string.Format(
                                    AliasedRot("{0}_{1}_D"), id, i);
                                graphicRotAnim[i - 1][1] = string.Format(
                                    AliasedRot("{0}_{1}_L"), id, i);
                                graphicRotAnim[i - 1][2] = string.Format(
                                    AliasedRot("{0}_{1}_U"), id, i);
                                graphicRotAnim[i - 1][3] = string.Format(
                                    AliasedRot("{0}_{1}_R"), id, i);
                            }
                        }
                        //graphicAnim = new string[AnimationFrames - 1];
                        for (int i = 1; i < AnimationFrames; i++) {
                            graphicAnim[i - 1] = string.Format("{0}_{1}", id, i);
                        }
                    }
                } catch (Exception ex) {
                    D.LogEx(ex, "Failed setting graphic id: {0} on {1}", id, Tile);
                }
            }
            EntityUtils.TriggerChange(Tile);
        }


        private bool hasBuiltGraphics;
        private void BuildGraphics() {
            if (hasBuiltGraphics) { return; }
            hasBuiltGraphics = true;

            if (IsRotatable) {
                CreateGraphicRot();
            }
            if (IsAnimated) {
                if (IsRotatable) {
                    graphicRotAnim = new string[AnimationFrames - 1][];
                    for (int i = 1; i < AnimationFrames; i++) {
                        graphicRotAnim[i - 1] = new string[4] {
                            string.Format(AliasedRot("{0}_{1}_D"), GraphicId, i),
                            string.Format(AliasedRot("{0}_{1}_L"), GraphicId, i),
                            string.Format(AliasedRot("{0}_{1}_U"), GraphicId, i),
                            string.Format(AliasedRot("{0}_{1}_R"), GraphicId, i)
                        };
                    }
                }
                graphicAnim = new string[AnimationFrames - 1];
                for (int i = 1; i < AnimationFrames; i++) {
                    graphicAnim[i - 1] = string.Format(AliasedRot("{0}_{1}"),
                        GraphicId, i);
                }
            }
        }

        private void CreateGraphicRot() {
            graphicRot = new string[4] {
                string.Format(AliasedRot("{0}_D"), GraphicId),
                string.Format(AliasedRot("{0}_L"), GraphicId),
                string.Format(AliasedRot("{0}_U"), GraphicId),
                string.Format(AliasedRot("{0}_R"), GraphicId)
            };
        }

        public override string ToString() {
            return string.Format(" * TileGraphics: [{0}x{1}] {2}",
                GraphicWidth, GraphicHeight, GraphicId);
        }

        #if !PRODUCTION_BUILD
        public string GetTileIconsDebugInfo() {
            var sb = Caches.StringBuilder2;
            sb.Clear();
            sb.AppendFormat("=== GRAPHICS DUMP FOR TILE ICONS {0} ===\n", Tile.Id);
            sb.AppendFormat("Tile: {0}\n", Tile);
            sb.AppendFormat("Task: {0}\n", Tile.Goal);
            foreach (var comp in Tile.Components) {
                var type = comp.GetType();
                var field = type.GetField("infoIcon",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (field == null && type.BaseType != null) {
                    field = type.BaseType.GetField("infoIcon",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                }
                if (field != null) {
                    var value = field.GetValue(comp);
                    sb.AppendFormat(" -> Component: {0}, infoIcon: {1}\n", type.Name, value ?? "null");
                } else {
                    sb.AppendFormat(" -> Component: {0}, No field!\n", type.Name);
                }
            }
            var infos = new EntityInfoItems[16];
            var cnt = S.Sys.Info.InfoFor(Tile.PosIdx, infos);
            sb.AppendFormat("Actual icons for pos: {0}: {1}\n", Pos.String(Tile.PosIdx), cnt);
            for (int i = 0; i < cnt; i++) {
                sb.AppendFormat(" -> {0}\n", infos[i].DebugInfo);
            }
            sb.AppendFormat("=== END GRAPHICS DUMP ===\n");
            return sb.ToString();
        }
        #endif
    }
}
