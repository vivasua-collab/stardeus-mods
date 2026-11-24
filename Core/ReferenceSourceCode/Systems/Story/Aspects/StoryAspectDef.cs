using System.Collections.Generic;
using Game.Data;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Story {
    public sealed class StoryAspectDef {
        public static Dictionary<int, StoryAspectDef> All = new();
        [ExecuteOnReload]
        private static void CleanupStatics() {
            All.Clear();
        }
        [SelfInit(SelfInit.Stage.Finalize)]
        private static void Init() {
            LoadKnown();
        }

        public string Id;
        public string AspectId;
        public int IdH;
        public StoryAspect Aspect;
        /// <summary>
        /// Use Vars instead of this in C#
        /// </summary>
        public SerializableProperty[] Properties;
        private CachedProperties vars;
        public CachedProperties Vars => vars ??= new CachedProperties(Properties);
        public static StoryAspectDef Get(string id) {
            if (All.TryGetValue(Hashes.S(id), out var tag)) {
                return tag;
            }
            D.Err("StoryAspectDef not found by id: {0}", id);
            return null;
        }
        public static StoryAspectDef FastGet(int idH) {
            if (All.TryGetValue(idH, out var def)) {
                return def;
            }
            D.Err("StoryAspectDef not found by id hash: {0}", idH);
            return null;
        }
        private static void LoadKnown() {
            if (All.Count > 0) { return; }
            var types = The.ModLoader.LoadObjects<StoryAspectDef>("Config/Story/Aspects");
            foreach (var type in types) {
                type.IdH = Hashes.S(type.Id);
                var factory = Registries.StoryAspects.Get(type.AspectId);
                if (factory == null) {
                    D.Err("Couldn't find StoryAspectFactory for StoryAspectDef with id: {0} -> {1}",
                        type.Id, type.AspectId);
                    continue;
                }
                type.Aspect = factory();
                type.Aspect.Def = type;
                if (!All.TryAdd(type.IdH, type)) {
                    D.Err("StoryAspectDef with such ID already exists: {0}", type.Id);
                    continue;
                }
            }
        }

        public override string ToString() {
            return $"StoryAspectDef[{Id}]";
        }
    }
}
