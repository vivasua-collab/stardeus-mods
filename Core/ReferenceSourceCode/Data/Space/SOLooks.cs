using System;
using UnityEngine;

namespace Game.Data.Space {
    [Serializable]
    public class SOLooks {
        public string Id;
        public bool IsForTerraformable;
        public float Probability;
        public string Shader;
        public float SkewFrom;
        public float SkewTo;
        // Seeded randomized scale size for certain objects
        public float ScaleRand;
        // Negative will bring the object closer to our ship. Starfield only
        public float OrderDist;
        // For starmap
        // Additional multiplier for starmap size.
        // Can be additionally modified by setting SpaceObject.Data.SetFloat(SpaceObjectLooks.VarStarmapSizeMul);
        public float StarmapSizeMul;
        // For starfield (behind the game ship).
        // Can be additionally modified by setting SpaceObject.Data.SetFloat(SpaceObjectLooks.VarStarfieldSizeMul);
        public float StarfieldSizeMul;
        public bool IsConstantSizeInStarmap;
        public string Sprite;
        public bool CanRotate;
        public bool KeepUprightInStarfield;
        /// <summary>
        /// This rotation selects a random L/R/U/D sprite
        /// </summary>
        public bool RandomRotation;
        /// <summary>
        /// This rotation rotates an objects around z axis randomly
        /// </summary>
        public bool RandomRotationZ;
        /// <summary>
        /// If this is not 0, it will be used instead of SpaceObject.Size * multiplier
        /// </summary>
        public float FixedStarmapSize;
        public string Sprite_L;
        public string Sprite_R;
        public string Sprite_U;
        public string Sprite_D;
        public float AnimateRotationSpeed;
        /// <summary>
        /// A visual effect spawned inside the UISpaceObject and inside StarfieldSO
        /// </summary>
        public string VisualEffect;
        public float VisualEffectSize_Starmap;
        public float VisualEffectSize_Starfield;

        public SerializableProperty[] ShaderPropertiesFloatFrom;
        public SerializableProperty[] ShaderPropertiesFloatTo;
        public SerializableProperty[] ShaderPropertiesVector2From;
        public SerializableProperty[] ShaderPropertiesVector2To;

        public void ShaderPropertiesByType(string propType,
                out SerializableProperty[] from, out SerializableProperty[] to) {
            if (propType == "Float") {
                from = ShaderPropertiesFloatFrom;
                to = ShaderPropertiesFloatTo;
            } else if (propType == "Vector2") {
                from = ShaderPropertiesVector2From;
                to = ShaderPropertiesVector2To;
            } else {
                from = to = null;
            }
        }

        /// <summary>
        /// Warning - does copy shader properties by reference!
        /// </summary>
        public static SOLooks Clone(SOLooks looks) {
            var copy = new SOLooks();
            copy.Id = looks.Id;
            copy.IsForTerraformable = looks.IsForTerraformable;
            copy.Probability = looks.Probability;
            copy.Shader = looks.Shader;
            copy.SkewFrom = looks.SkewFrom;
            copy.SkewTo = looks.SkewTo;
            copy.ScaleRand = looks.ScaleRand;
            copy.OrderDist = looks.OrderDist;
            copy.StarmapSizeMul = looks.StarmapSizeMul;
            copy.StarfieldSizeMul = looks.StarfieldSizeMul;
            copy.IsConstantSizeInStarmap = looks.IsConstantSizeInStarmap;
            copy.Sprite = looks.Sprite;
            copy.CanRotate = looks.CanRotate;
            copy.KeepUprightInStarfield = looks.KeepUprightInStarfield;
            copy.RandomRotation = looks.RandomRotation;
            copy.RandomRotationZ = looks.RandomRotationZ;
            copy.FixedStarmapSize = looks.FixedStarmapSize;
            copy.Sprite_L = looks.Sprite_L;
            copy.Sprite_R = looks.Sprite_R;
            copy.Sprite_U = looks.Sprite_U;
            copy.Sprite_D = looks.Sprite_D;
            copy.AnimateRotationSpeed = looks.AnimateRotationSpeed;
            copy.VisualEffect = looks.VisualEffect;
            copy.VisualEffectSize_Starmap = looks.VisualEffectSize_Starmap;
            copy.VisualEffectSize_Starfield = looks.VisualEffectSize_Starfield;
            copy.ShaderPropertiesFloatFrom = looks.ShaderPropertiesFloatFrom;
            copy.ShaderPropertiesFloatTo = looks.ShaderPropertiesFloatTo;
            copy.ShaderPropertiesVector2From = looks.ShaderPropertiesVector2From;
            copy.ShaderPropertiesVector2To = looks.ShaderPropertiesVector2To;
            return copy;
        }

        // Template means that you can have multiple sprites with 01, 02, etc
        // one random will be chosen from that list
        public override string ToString() {
            return $"SOLooks[{Id}]. StarmapSizeMul: {StarmapSizeMul}. StarfieldSizeMul: {StarfieldSizeMul}. OrderDist: {OrderDist}";
        }
    }
}
