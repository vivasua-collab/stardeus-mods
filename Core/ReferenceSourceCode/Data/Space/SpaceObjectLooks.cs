using System;
using System.Collections.Generic;
using Game.Constants;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using KL.Randomness;
using KL.UI;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;
using Game.Procgen.Space;
using Game.Visuals;

namespace Game.Data.Space {
    // FIXME refactor to split each type into it's own class
    public class SpaceObjectLooks {
        public const string VarStarmapSizeMul = "StarmapSizeMul";
        public const string VarStarfieldSizeMul = "StarfieldSizeMul";
        public const string VarSprite = "Sprite";
        public const string VarIcon = "Icon";
        private const float tooltipY = -150;
        private readonly Vector2 baseSize = new(256, 256);
        private void MoveTooltip(UITooltip tooltip, float amount) {
            tooltip.transform.localPosition = new Vector2(0, tooltipY * amount);
        }

        public static string SpriteFor(SpaceObject so, SOLooks looks) {
            if (so.HasData) {
                return so.Data.GetString(VarSprite, looks.Sprite);
            }
            return looks.Sprite;
        }

        private void LoadSpriteRenderer(Rng rng, SpaceObject obj, SpriteRenderer sr,
                SOLooks looks) {
            var shipRot = obj.S.Sys.ShipNav.ShipDefaultRot;
            ApplySkew(rng, looks, sr.transform, -shipRot);

            var sprite = SpriteFor(obj, looks);
            if (sprite != null) {
                sr.sprite = Images.Sprite(sprite);
            }

            var mat = LoadMaterial(obj, looks, rng, false);
            if (mat != null) {
                sr.material = mat;
            }

            sr.sortingOrder = obj.Type.StarfieldSortOrder;
        }

        private static Material LoadMaterial(SpaceObject obj, SOLooks looks, Rng rng, bool forUI) {
            if (string.IsNullOrWhiteSpace(looks.Shader)) { return null; }
            var sh = looks.Shader;
            if (forUI) {
                sh = $"{sh}_Fixed";
            }
            var shader = Shader.Find(sh);
            if (shader is null) {
                D.Err("Cannot find SOLooks shader for {0} {1}", looks.Id, sh);
            } else {
                var mat = new Material(shader);
                mat.name = $"Gen_{shader.name}_{obj.DisplayName}_{obj.LooksSeed}";
                mat.SetFloat(ShaderVars._GenSeed, (obj.LooksSeed
                    % short.MaxValue) / Mathf.PI);

                SetShaderProperties("Float", obj, looks, mat, rng);
                SetShaderProperties("Vector2", obj, looks, mat, rng);
                float terraformProg = 0f;
                bool isTerraformable = false;
                if (obj.Type.IdH == SpaceObjectTypeIdH.Planet) {
                    if (obj.HasTerraformableLooks) {
                        isTerraformable = true;
                        terraformProg = obj.TerraformingProgress;
                    }
                } else if (obj.Type.IdH == SpaceObjectTypeIdH.PlanetCloseup) {
                    if (obj.Parent.HasTerraformableLooks) {
                        terraformProg = obj.Parent.TerraformingProgress;
                        isTerraformable = true;
                    }
                }
                if (isTerraformable) {
                    // D.Warn("Setting tp: {0} -> {1}", obj, terraformProg);
                    mat.SetFloat(ShaderVars._TerraformProgress, terraformProg);
                }
                return mat;
            }
            return null;
        }

        private static bool OverrideFloatPropertyFor(SpaceObject obj, string prop,
                float value, out float newValue) {
            if (prop == "_RotationSpeed") {
                // closeup should not rotate
                if (obj.Type.InheritParentLooks) {
                    newValue = 0f;
                    return true;
                }
                if (obj.Id % 2 == 1) {
                    // flip rotation
                    newValue = value * -1f;
                    return true;
                }
            }
            newValue = value;
            return false;
        }

        private static void SetShaderProperties(string propType, SpaceObject obj,
                SOLooks looks, Material mat, Rng rng) {
            var disableAnimations = !The.Prefs.GetBool(Pref.GAnimatedBackground,
                PrefH.GAnimatedBackground, true);
            looks.ShaderPropertiesByType(propType, out var from, out var to);
            if (Arrays.IsEmpty(from)) { return; }
            var len = from.Length;
            if (to == null || to.Length != len) {
                D.Err("{0}: ShaderProperties{1}From does not have the same count of entries as ShaderProperties{1}To",
                    looks.Id, propType);
                return;
            }
            if (propType == "Float") {
                for (int i = 0; i < len; i++) {
                    var key = from[i].Key;
                    if (disableAnimations) {
                        // anti nausea
                        if (key.EndsWith("Speed")) {
                            mat.SetFloat(key, 0f);
                            continue;
                        }
                    }
                    // Skip rotation on planet closeup view
                    var vFrom = from[i].Float;
                    var vTo = to[i].Float;
                    var newV = rng.Range(vFrom, vTo);
                    if (!OverrideFloatPropertyFor(obj, key, newV, out var v)) {
                        v = newV;
                    }
                    /*
                    D.Log("Setting random float on mat for {0}: {1} -> {2}",
                        mat.name, from[i].Key, v);
                    */
                    mat.SetFloat(key, v);
                }
            } else if (propType == "Vector2") {
                for (int i = 0; i < len; i++) {
                    var vFrom = from[i].Vector2;
                    var vTo = to[i].Vector2;
                    var vx = rng.Range(vFrom.x, vTo.x);
                    var vy = rng.Range(vFrom.y, vTo.y);
                    mat.SetVector(from[i].Key, new Vector2(vx, vy));
                }
            }
        }

        private void LoadUIImage(Rng rng, SpaceObject obj, Image img, SOLooks looks) {
            var tr = img.transform;
            ApplySkew(rng, looks, tr, 0f);

            var sprite = SpriteFor(obj, looks);
            if (looks.RandomRotation) {
                sprite = RotateLooksPart(rng.From(Facing.Types), looks);
            }
            if (sprite != null) {
                img.sprite = Images.Sprite(sprite);
            }
            if (obj.Type.IdH == SpaceObjectTypeIdH.Region) {
                img.color = obj.Region.Biome.IconColor;
            } else if (obj.Type.IdH == SpaceObjectTypeIdH.Sector) {
                img.color = obj.Sector.Biome.IconColor;
            }
            // img.color = obj.Sector.Biome.IconColor;

            img.material = LoadMaterial(obj, looks, rng, true);
        }

        private static void ApplySkew(Rng rng, SOLooks looks, Transform tr, float addRotZ) {
            var skewFrom = looks.SkewFrom;
            if (skewFrom > 0) {
                var skewTo = looks.SkewTo;
                var s = new Vector3(
                    rng.Range(skewFrom, skewTo) * Mathf.Sign(rng.Value),
                    rng.Range(skewFrom, skewTo) * Mathf.Sign(rng.Value));
                tr.localRotation = Quaternion.Euler(s * 90);
            }
            if (looks.RandomRotationZ) {
                var rot = tr.localRotation;
                var rotChange = rng.Range(-180f, 180f);
                tr.localRotation = Quaternion.Euler(rot.x, rot.y, rot.z + rotChange + addRotZ);
            }
        }

        // Starfield is the thing behind the player's ship in the game
        public SOLooks ApplyInStarfield(SpriteRenderer sr, SpaceObject obj, Vector3 defScale) {
            var looks = obj.Looks(false);
            if (obj == null) {
                D.Err("Trying to apply looks in starfield for null SO! {0}");
                return null;;
            }
            if (looks == null) { return null; }

            var objSeed = obj.LooksSeed;
            var rng = new Rng(objSeed);

            float size = CalculateSize(obj, looks, rng);
            var scaleMul = looks.StarfieldSizeMul;
            if (scaleMul > 0f) {
                size *= scaleMul;
            }
            var typeScaleMul = obj.Type.StarfieldSizeMul;
            if (typeScaleMul > 0f) {
                size *= typeScaleMul;
            }
            if (obj.HasData) {
                var customScaleMul = obj.Data.GetFloat(VarStarfieldSizeMul, 0f);
                if (customScaleMul > 0f) {
                    size *= customScaleMul;
                }
            }
            if (obj.IsShip) {
                size *= Tunables.ShipStarfieldSizeMul;
            }

            sr.transform.localScale = defScale * size;

            LoadSpriteRenderer(rng, obj, sr, looks);

            obj.Visual?.LoadVFX(looks.VisualEffect,
                looks.VisualEffectSize_Starfield);

            return looks;
        }

        public void ApplyInStarmap(UISpaceObject uiobj) {
            var obj = uiobj.SpaceObject;
            var img = uiobj.Image;
            var tooltip = uiobj.NameTooltip;
            ResetImage(img);

            bool isSectorView;
            if (uiobj.SpaceObject.Parent == null) {
                D.Err("Applying space object without parent in starmap: {0}", obj);
                isSectorView = false;
            } else {
                isSectorView = uiobj.SpaceObject.Parent.Type.IdH == SpaceObjectTypeIdH.Region;
            }

            var looks = obj.Looks(isSectorView);
            if (looks == null) { return; }

            var rng = new Rng(obj.LooksSeed);

            float size = ObjSize(obj, looks, rng);
            if (obj.IsShip) {
                size *= Tunables.ShipStarmapSizeMul;
            }
            uiobj.StarmapSize = baseSize * size;

            img.transform.localScale = Consts.V3One * size;
            // Tooltip
            MoveTooltip(tooltip, size);
            LoadUIImage(rng, obj, img, looks);
            if (uiobj.IsStarmapDecoration) {
                uiobj.transform.SetAsFirstSibling();
            }

            uiobj.LoadVFX(looks.VisualEffect, looks.VisualEffectSize_Starmap);
            return;
        }


        public static float ObjSize(SpaceObject obj, SOLooks looks, Rng rng = null) {
            if (rng == null) {
                rng = new Rng(obj.LooksSeed);
            }
            var size = CalculateSize(obj, looks, rng);
            if (looks.FixedStarmapSize == 0f) {
                var scaleMul = looks.StarmapSizeMul;
                if (scaleMul > 0) {
                    size *= scaleMul;
                }
                var typeScaleMul = obj.Type.StarmapSizeMul;
                if (typeScaleMul > 0f) {
                    size *= typeScaleMul;
                }
                var customScaleMul = obj.Data.GetFloat(VarStarmapSizeMul, 0f);
                if (customScaleMul > 0f) {
                    size *= customScaleMul;
                }
            }

            if (obj.Type.IdH == SpaceObjectTypeIdH.Player) {
                // player's ship can be hard to see if it's too small
                if (size < 0.5f) {
                    size = 0.5f;
                }
            }

            return size;
        }

        private static float CalculateSize(SpaceObject obj, SOLooks looks, Rng rng) {
            if (looks != null && looks.FixedStarmapSize != 0f) {
                return looks.FixedStarmapSize;
            }
            /*
            var size = looks.Scale;
            if (size < 0.000001f) {
                size = 1f;
            }
            */
            // only use actual obj size for mask image (i == 0)
            var size = 1f;
            var scaleRand = looks?.ScaleRand ?? 0f;
            if (scaleRand > 0.000001f) {
                size *= rng.Range(1f - scaleRand, 1f + scaleRand);
            }
            var objSize = obj.Size;
            if (objSize < 0.000001f) {
                objSize = 1f;
            }
            size *= objSize;
            return size;
        }

        public void UpdateDirection(UISpaceObject uiobj, Vector2 dir) {
            var obj = uiobj.SpaceObject;
            var looks = obj.Looks(false);
            if (!looks.CanRotate) { return; }
            if (dir.sqrMagnitude < 0.0000000000000000000000000001f) { return ; }
            var facing = Facing.FromDirection(dir, Facing.Type.Down);

            if (uiobj.SpaceObject.Ship?.IsDerelict == true) {
                // always rotate derelict by some angle
                int times = uiobj.SpaceObject.Id % 4;
                for (int i = 0; i < times; i++) {
                    facing = Facing.Rotate(facing, Facing.Type.Left);
                }
            }

            var sprite = RotateLooksPart(facing, looks);

            if (!string.IsNullOrEmpty(sprite)) {
                uiobj.Image.sprite = Images.Sprite(sprite);
            }
        }

        private Facing.Type lastFacing = Facing.Type.Down;
        private bool updatedFacing;

        public bool UpdateDirection(SpaceObject obj, SpriteRenderer sr, Vector2 dir) {
            var looks = obj.Looks(false);
            if (!looks.CanRotate) { return false; }
            if (dir.sqrMagnitude < 0.0000000000000000000000000001f) { return false; }
            var facing = Facing.FromDirection(dir, Facing.Type.Down);
            if (!updatedFacing || facing != lastFacing) {
                lastFacing = facing;
                updatedFacing = true;
            } else {
                return false;
            }
            var sprite = RotateLooksPart(facing, looks);
            if (!string.IsNullOrEmpty(sprite)) {
                sr.sprite = Images.Sprite(sprite);
            }
            return true;
        }

        private string GetSprite(string spr, string fallback) {
            return string.IsNullOrWhiteSpace(spr) ? fallback : spr;
        }

        private string RotateLooksPart(Facing.Type facing, SOLooks looks) {
            var defSprite = looks.Sprite;
            switch (facing) {
                case Facing.Type.Down: {
                        return GetSprite(looks.Sprite_D, defSprite);
                    }
                case Facing.Type.Up: {
                        return GetSprite(looks.Sprite_U, defSprite);
                    }
                case Facing.Type.Left: {
                        return GetSprite(looks.Sprite_L, defSprite);
                    }
                case Facing.Type.Right: {
                        return GetSprite(looks.Sprite_R, defSprite);
                    }
            }
            return defSprite;
        }

        private void ResetImage(Image image) {
            var rt = (RectTransform) image.transform;
            rt.sizeDelta = baseSize;
            rt.localScale = Consts.V3One;
            rt.localRotation = Consts.QId;
            image.material = null;
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            image.color = Color.white;
            image.sprite = null;
        }
    }
}
