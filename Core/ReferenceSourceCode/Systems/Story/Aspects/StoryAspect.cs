using System;
using Game.CodeGen;
using Game.Data;
using KL.Utils;

namespace Game.Systems.Story {
    public abstract class StoryAspect {
        public delegate StoryAspect StoryAspectFactory();
        protected static void Register(string id, StoryAspectFactory a) {
            Registries.StoryAspects.Add(id, a);
        }
        public abstract string Id { get; }
        public int IdH;
        protected StoryAspect() {
            IdH = Hashes.S(Id);
        }
        public StoryAspectDef Def;
        public abstract void Initialize(StoryContext ctx, bool isLoading);
        public abstract void Unload(StoryContext ctx);
        public abstract void OnTick(StoryContext ctx);
        public StoryEventOutcome ParseOutcome() {
            var outcomeType = Def.Vars.Get(PropertyIdH.Outcome, null);
            if (outcomeType == null) {
                throw new Exception(
                    $"Outcome not set for {this}! Set it in JSON: {Def}");
            }
            if (!Enum.TryParse(outcomeType, out StoryEventOutcome outcome)) {
                throw new Exception(
                    $"Could not parse outcome {outcomeType} for {this}: {Def}");
            }
            return outcome;
        }
        /// <summary>
        /// Same as LoadCurve, but a missing property leaves the curve null without an error.
        /// </summary>
        protected void LoadOptionalCurve(int propIdH, ref Curve curve) {
            if (Def.Vars.TryCurve(propIdH, out curve)) {
                return;
            }
            var curveName = Def.Vars.Get(propIdH, string.Empty);
            curve = string.IsNullOrEmpty(curveName) ? null : CurveDef.Get(curveName);
        }
        protected void LoadCurve(int propIdH, ref Curve curve) {
            // try getting a FloatSet
            if (Def.Vars.TryCurve(propIdH, out curve)) {
                return;
            }
            // try CurveDef
            var curveName = Def.Vars.Get(propIdH, string.Empty);
            if (!string.IsNullOrEmpty(curveName)) {
                curve = CurveDef.Get(curveName);
            }
            if (curve == null) {
                D.Err("Failed resolving curve: {0}. Property does not contain FloatSet or String pointing to an existing CurveDef ({1})",
                    PropertyIdH.ResolveName(propIdH), Def);
            }
        }
    }
}
