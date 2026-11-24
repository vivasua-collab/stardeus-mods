using System;
using System.Collections.Generic;
using System.Linq;
using Game.CodeGen;
using Game.Data;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Story {
    [Serializable]
    public sealed class StoryGenDef {
        public static Dictionary<int, StoryGenDef> All = new();
        [ExecuteOnReload]
        private static void CleanupStatics() {
            All.Clear();
            Default = null;
            LastSelected = null;
        }
        [SelfInit(SelfInit.Stage.Finalize)]
        private static void Init() {
            LoadKnown();
        }

        public static StoryGenDef LastSelected;

        private static void LinkAspects() {
            var outcomes = Enum.GetValues(typeof(StoryEventOutcome));
            foreach (var def in All.Values) {
                def.aspects = new List<StoryAspect>();
                def.handlers = new IStoryEventHandler[outcomes.Length];
                foreach (var aspect in def.Aspects) {
                    var asDef = StoryAspectDef.Get(aspect);
                    if (asDef == null) {
                        D.Err("StoryGenDef with id {0} has a non-existent aspect with Id: {1}",
                            def.Id, aspect);
                        continue;
                    }
                    def.aspects.Add(asDef.Aspect);
                    if (asDef.Aspect is IStoryEventHandler handler) {
                        var outcome = handler.HandlesOutcome;
                        var existing = def.handlers[(int) outcome];
                        if (existing != null) {
                            D.Err("Trying to overwrite existing handler for outcome {0}: {1} -> {2}",
                                outcome, existing, asDef);
                        } else {
                            def.handlers[(int) handler.HandlesOutcome] = handler;
                        }
                    }
                }
                foreach (StoryEventOutcome outcome in outcomes) {
                    var h = def.handlers[(int) outcome];
                    if (h == null) {
                        D.Err("IStoryEventHandler not available for outcome {0} in story generator: {1}",
                            outcome, def.Id);
                    }
                }
            }
        }

        public static List<StoryGenDef> Ordered => All.Values
            .Where(g => !g.Internal)
            .OrderBy(g => g.Order)
            .ToList();
        public string Id;
        public int IdH;
        public int Order;
        public bool Internal;
        private string nameT;
        public string NameT => nameT ??= $"storygen.{Id.ToLowerInvariant()}".T();
        private string descT;
        public string DescT => descT ??= $"storygen.{Id.ToLowerInvariant()}.desc".T();
        public static StoryGenDef Default;
        public List<string> Aspects;
        private List<StoryAspect> aspects;
        public List<StoryAspect> StoryAspects => aspects;
        private IStoryEventHandler[] handlers;
        /// <summary>
        /// Use Vars instead of this in C#
        /// </summary>
        public SerializableProperty[] Properties;
        private CachedProperties vars;
        public CachedProperties Vars => vars ??= new CachedProperties(Properties);
        public static StoryGenDef Get(string id, bool logError = true) {
            if (All.TryGetValue(Hashes.S(id), out var tag)) {
                return tag;
            }
            if (logError) {
                D.Err("StoryGenDef not found by id: {0}", id);
            }
            return Default;
        }
        private static void LoadKnown() {
            if (All.Count > 0) { return; }
            var types = The.ModLoader.LoadObjects<StoryGenDef>("Config/Story/Generators");
            foreach (var type in types) {
                type.IdH = Hashes.S(type.Id);
                if (type.Id == "default") {
                    Default = type;
                }
                if (!All.TryAdd(type.IdH, type)) {
                    D.Err("StoryGenDef with such ID already exists: {0}", type.Id);
                    continue;
                }
            }
            Ready.When(WhenH.CoreLoaded1, LinkAspects);
        }

        public override string ToString() {
            return $"StoryGenDef[{Id}]";
        }

        public void OnTick(long ticks, StoryContext context) {
            context.OnTick(ticks);
            var ac = aspects.Count;
            for (int i = 0; i < ac; i++) {
                var aspect = aspects[i];
                aspect.OnTick(context);
            }
        }

        public void Initialize(StoryContext context, bool isLoading) {
            #if PRODUCTION_BUILD
            D.Warn("Initializing story generator: {0}", Id);
            #endif
            context.Initialize(this, isLoading);
            var ac = aspects.Count;
            for (int i = 0; i < ac; i++) {
                var aspect = aspects[i];
                aspect.Initialize(context, isLoading);
            }
        }

        public void Unload(StoryContext context) {
            // D.Warn("Unloading story generator: {0}", Id);
            context.Unload();
            var ac = aspects.Count;
            for (int i = 0; i < ac; i++) {
                var aspect = aspects[i];
                aspect.Unload(context);
            }
        }

        public IStoryEventHandler HandlerFor(StoryEventOutcome outcome) {
            return handlers[(int) outcome];
        }
    }
}
