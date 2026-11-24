#if !PRODUCTION_BUILD
// #define DEBUG_NAMING
#endif
using System;
using System.Collections.Generic;
using System.Linq;
using Game.Constants;
using Game.Data;
using Game.Systems.Mental;
using KL.Collections;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Naming {
    public sealed class NamingSys : GameSystem, ISaveableSpecial {
        public const string SysId = "NamingSys";
        public override string Id => SysId;

        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new NamingSys());
        }

        public void LoadSpecial(SystemsDataSpecial sd) {
            var usedNames = sd?.Naming?.UsedNames;
            foreach (var n in Registries.NameSources.All.Values) {
                n.UsedNames.Clear();
                if (usedNames != null && usedNames.TryGetValue(n.IdH, out var used)) {
                    n.UsedNames.AddRange(used);
                }
            }
        }

        public void SaveSpecial(SystemsDataSpecial sd) {
            sd.Naming ??= new();
            sd.Naming.UsedNames = new();
            foreach (var n in Registries.NameSources.All.Values) {
                sd.Naming.UsedNames[n.IdH] = n.UsedNames;
            }
        }

        public override void Unload() {
            foreach (var n in Registries.NameSources.All.Values) {
                n.UsedNames.Clear();
            }
        }

        protected override void OnInitialize() {
            S.Sig.AfterLoadState.AddListener(OnLoadSave);
            S.Query.GenerateName.SetResponder(GenerateName);
            S.Query.GenerateNameExcept.SetResponder(GenerateNameExcept);
        }

        private void OnLoadSave(GameState state) {
            foreach (var ns in Registries.NameSources.All.Values) {
                ns.Setup(state);
            }
        }

        private NameResult GenerateNameExcept(NameRequest req, HashSet<int> except) {
            if (except == null) {
                return GenerateName(req);
            }
            int attempts = 100;
            NameResult res = null;
            while (attempts-- > 0) {
                res = GenerateName(req);
                if (except.Add(res.Hash)) {
                    return res;
                }
                // D.Warn("Already used name: {0}", res);
            }
            D.Err("Ran out of attempts to generate unique name!");
            return res;
        }

        private NameResult GenerateName(NameRequest req) {
            var srcs = Registries.NameSources.All.Values
                .Where(src => !src.IsDisabled && src.Supports(req))
                .OrderByDescending(src => src.PriorityFor(req.Type))
                .ToArray();
            if (Arrays.IsEmpty(srcs)) {
                D.Err("Sources not available for name request type: {0}! Falling back to Basic", req.Type);
                srcs = new NameSource[] {
                    Registries.NameSources.Get(BasicNameSource.SourceId)
                };
            }
            NameSource lastSrc = null;
            req.Rng ??= Rng.Unseeded;
            for (int i = 0; i < srcs.Length; i++) {
                NameSource src = srcs[i];
                #if DEBUG_NAMING
                D.Warn("Trying name source #{0} {1} for type: {2}", i, src.Id, req.Type);
                #endif
                if (req.Rng.Chance(src.ChanceFor(req))) {
                    if (src.TryNameFor(req, out var name)) {
                        #if DEBUG_NAMING
                        D.Warn("Got reply from name source #{0} {1} for type: {2}", i, src.Id, req.Type);
                        #endif
                        return name;
                    } else {
                        #if DEBUG_NAMING
                        D.Warn("Failed to get reply from name source #{0} {1} for type: {2}", i, src.Id, req.Type);
                        #endif
                    }
                } else {
                    #if DEBUG_NAMING
                    D.Warn("Chance failed for name source #{0} {1} for type: {2}", i, src.Id, req.Type);
                    #endif
                }
                lastSrc = src;
            }
            // ran out of chances, let's use the last generator as fallback
            D.Err("Failed generating name, trying fallback: {0}", req);
            if (lastSrc.TryNameFor(req, out var fallback)) {
                return fallback;
            }
            D.Err("Failed generating name, all sources returned false {0}", req);
            fallback = new NameResult(null);
            fallback.SetName(T.Nameless);
            fallback.SetBeingName(BeingName.BlankFor(req.Species));
            return fallback;
        }
    }
}
