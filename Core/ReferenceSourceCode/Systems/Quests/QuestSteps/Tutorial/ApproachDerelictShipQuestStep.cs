using System;
using System.Collections.Generic;
using System.Linq;
using Game.CodeGen;
using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Input;
using Game.Procgen;
using Game.Systems.Ships;
using Game.Systems.Space;
using Game.Systems.Story;
using Game.Utils;
using KL.Grid;
using KL.Utils;

namespace Game.Systems.Quests {
    public sealed class ApproachDerelictShipQuestStep : BaseQuestStep {
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(CheckId,
                (s, c, l) => new ApproachDerelictShipQuestStep(s, c, l));
        }
        public const string CheckId = "ApproachDerelictShip";
        private const string Key_PackId = "PackId";
        private QuestStepTarget target;
        private StoryEventDef evDef;
        private QuestVar derelictVar;
        private QuestDef nextQuestDef;
        private HashSet<int> existingUnlinkedPackageIds;
        private SpaceObject derelictSO {
            get {
                if (target == null) {
                    D.Err("No target for derelict SO! {0}", this);
                    return null;
                }
                if (target.Target is NPCShip ship) {
                    return ship.SO;
                } else if (target.Target is SpaceObject tso) {
                    return tso;
                } else {
                    D.Err("Quest target isn't a space object! {0} | {1}",
                        target.Target, this);
                    return null;
                }
            }
        }
        public ApproachDerelictShipQuestStep(GameState s, QuestStep step, bool isLoading)
                : base(CheckId, s, step, isLoading) {

            derelictVar = GetQuestVarByKey(PropertyIdH.Var_Derelict);
            var evDefId = config.GetString(PropertyIdH.StoryEventId);
            evDef = StoryEventDef.Get(evDefId);
            if (evDef == null) {
                D.Throw("StoryEventId not found: {0} | {1}", evDefId, step);
            }

            var nextQuestDefId = config.GetString(PropertyIdH.PackageQuestId, null);
            if (nextQuestDefId != null) {
                nextQuestDef = QuestDef.Get(nextQuestDefId);
                if (nextQuestDef == null) {
                    D.Throw("PackageQuestId {0} - quest not found: {1}",
                        nextQuestDef, step);
                }
            }

            if (derelictVar is IQuestStepTargetVar targetVar) {
                target = targetVar.GetTargetFor(step);
                if (target != null) {
                    target.OnInteract = ApproachDerelict;
                }
            } else {
                D.Throw("Derelict var not IQuestStepTargetVar! {0} | {1}",
                    derelictVar, step);
            }

            UpdateTexts();

            if (isLoading && step.IsActive) {
                RegisterTarget();
            }
        }

        private void UpdateTexts() {
            step.Title = T.QuestStepApproachDerelictShip_V1.F(
                Texts.Bold(T.SpaceObjectShipderelict));
            step.Desc = T.QuestStepApproachDerelictShipTip;
            if (!step.IsEnded) {
                var so = derelictSO;
                if (so != null) {
                    step.Title = T.QuestStepApproachDerelictShip_V1.F(so.StarmapLink);
                    step.Desc = $"{step.Desc}{GlyphMap.BRHR}{T.NounTarget}: {so.ParentPath(false)}";
                }
            }
        }

        private void ApproachDerelict() {
            var so = derelictSO;
            if (so == null) {
                D.Err("Derelict is null! {0}", this);
                FailStep();
                return;
            }
            SnapshotUnlinkedQuestPackages();
            var cmd = new CmdSpawnDerelictShip(so, evDef);
            cmd.OnComplete = AfterGenDerelict;
            S.CmdQ.Enqueue(cmd);
        }

        private void SnapshotUnlinkedQuestPackages() {
            existingUnlinkedPackageIds = new HashSet<int>();
            var allPacks = S.Components.AllByType<QuestPackageComp>();
            if (allPacks != null) {
                foreach (QuestPackageComp package in allPacks) {
                    if (package.QuestPackageId == 0 && !package.IsOpen) {
                        existingUnlinkedPackageIds.Add(package.EntityId);
                    }
                }
            }
        }

        private bool IsNewUnlinkedPackage(QuestPackageComp package) {
            return package.QuestPackageId == 0 && !package.IsOpen
                && existingUnlinkedPackageIds?.Contains(package.EntityId) != true;
        }

        private void AfterGenDerelict() {
            var package = S.Components.FindFirstMatching<QuestPackageComp>(IsNewUnlinkedPackage);
            if (package == null) {
                D.Err("Failed to find quest package on the derelict! {0}", this);
                foreach (var isle in S.Sys.Areas.Islands.Values) {
                    if (!EntityUtils.IsWithinMargin(isle.First(), false, false)) {
                        // found the derelict
                        foreach (var pos in isle) {
                            if (EntityUtils.IsEmptyFloorAt(pos)) {
                                var packObj = S.Objs.Create(Pos.ToVector(pos),
                                    The.Defs.FastGet(DefIdsH.ObjContainersQuestPackage));
                                D.Warn("Spawned fallback package: {0}", packObj);
                                package = packObj.GetComponent<QuestPackageComp>();
                                break;
                            }
                        }
                    }
                    if (package != null) { break; }
                }
            }
            existingUnlinkedPackageIds = null;
            if (package == null) {
                D.Err("Failed to find or spawn quest package on the derelict! {0}", this);
                FailStep();
                return;
            }

            var pack = QuestPackage.CreateFor(step, QuestPackageFlags.FailQuestOnDestroy);
            pack.Seed = target.Target.Seed;
            PopulatePackageRewards(pack, nextQuestDef);

            if (!S.Query.AddQuestPackage.Ask(pack)) {
                return;
            }
            var stateBefore = pack.State;
            package.AssignQuestPackage(pack);
            // Spawned while derelict generation had Ready.Game off, so the box did not
            // track itself
            UnseenObjectsSys.For(S)?.Track(package.Obj);
            pack.State = QuestPackageState.Spawned;
            S.Sig.QuestPackageStateChanged.Send(pack, stateBefore, pack.State);
            CompleteStep();
        }

        public override void OnStateChange(QuestStepState before, QuestStepState after) {
            if (after == QuestStepState.Active) {
                S.Query.SetQuestBool.Ask(QuestBoolIds.DisallowApproachDerelict, false);
                RegisterTarget();
                UpdateTexts();
            }
        }

        private void RegisterTarget() {
            if (target?.IsRegistered == false) {
                S.Query.RegisterQuestStepTarget.Ask(target, true);
            }
        }

        public override void Cleanup() {
            UnregisterTarget();
        }

        private void UnregisterTarget() {
            if (target?.IsRegistered == true) {
                S.Query.RegisterQuestStepTarget.Ask(target, false);
                target = null;
            }
        }
    }
}
