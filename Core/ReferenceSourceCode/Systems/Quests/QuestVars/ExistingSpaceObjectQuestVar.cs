using System;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Data;
using Game.Data.Space;
using Game.Utils;
using KL.Randomness;
using KL.Utils;

namespace Game.Systems.Quests {
    public sealed class ExistingSpaceObjectQuestVar : QuestVar, IQuestVar<SpaceObject>,
            IQuestStepTargetVar {
        [SelfInit]
        private static void Register() {
            Registries.QuestVars.Add(VarId,
                (s, def, data, seed, l) => new ExistingSpaceObjectQuestVar(
                    s, def, data, seed, l));
        }

        public const string VarId = "ExistingSpaceObject";
        private SpaceObject so;
        public SpaceObject Value => so;

        public ExistingSpaceObjectQuestVar(GameState s, QuestVarDef def,
                QuestData data, int seed, bool isLoading)
                    : base(s, def, data, seed, isLoading) {

            int soId = 0;
            var loadSOId = config.GetString(PropertyIdH.LoadSOId, null);
            if (loadSOId != null) {
                soId = S.Query.GetSharedQuestData.Ask().GetInt(loadSOId, 0);
            }
            if (!isLoading) {
                if (soId == 0) {
                    FindRandomTarget(def, data, seed);
                } else if (!s.Universe.TryFind(soId, out so)) {
                    D.Throw("Failed to load so by id: {0} -> {1} | {2}",
                        loadSOId, soId, this);
                }
                return;
            } else if (soId == 0) {
                soId = data.Data?.GetInt(def.DataKey(VarId), 0) ?? 0;
            }

            if (soId == 0) {
                D.Throw("Failed to load PoiSOId for var {0} | {1}", def, data);
            }
            if (!s.Universe.TryFind(soId, out so)) {
                if (def.Config.GetBool(PropertyIdH.MustExistOnLoad, false)) {
                    D.Throw("Failed to loading existing SO by id {0} for var {1} | {2}",
                            soId, def, data);
                }
            }
        }

        private void FindRandomTarget(QuestVarDef def, QuestData data, int seed) {
            var objType = def.Config.GetString(PropertyIdH.Type, null);
            // if type is null, we're expecting to load the object from shared data
            if (objType == null) {
                D.Throw("Quest step failed to load a var! {0}", this);
            }
            var type = SpaceObjectType.Find(objType);
            if (type == null) {
                D.Throw("Couldn't find SpaceObjectType {0} for {1}", objType, def);
            }
            var tryCurrentSector = def.Config.GetBool(PropertyIdH.TryCurrentSector, true);
            SpaceObject start = null;
            if (tryCurrentSector) {
                start = A.PlayerSector;
            } else {
                D.Throw("Not implemented: tryCurrentSector == false for {0}", def);
            }
            foreach (var child in start.Children) {
                if (child.Type.IdH == type.IdH) {
                    so = child;
                    break;
                }
            }

            if (so == null) {
                D.Throw("Couldn't find SpaceObject, implement crawling neighbors!: {0} | {1}",
                    type, def);
            }

            var qData = data.GetOrCreateData();
            // save it in QuestData for loading it later
            qData.SetInt(def.DataKey(VarId), so.Id);
        }

        public QuestStepTarget GetTargetFor(QuestStep step) {
            return new QuestStepTarget() {
                Target = SOUtils.QuestProviderFor(so),
                Step = step,
            };
        }
    }
}
