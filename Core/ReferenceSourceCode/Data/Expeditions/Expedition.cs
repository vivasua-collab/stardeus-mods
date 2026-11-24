#if UNITY_EDITOR
// #define DEBUG_EXPEDITIONS
// #define DEBUG_CREW_CHANGE
#endif
using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data.Space;
using KL.Utils;
using Game.Expeditions;
using System;
using UnityEngine;
using KL.Randomness;
using Game.Utils;
using Game.CodeGen;
using Game.UI;
using Game.Systems.Creatures;
using Game.Systems.Quests;
using Game.Input;
using Game.Systems.UIUX;
using Game.Systems;
using Game.Systems.Mental;

namespace Game.Data {
    public class Expedition {
        public GameState S;
        public ExpeditionContext Context;
        // ExpeditionStageTypeH
        public int StageType => currentStage?.StageType ?? 0;
        public ShuttleComp Shuttle;
        public List<BeingData> Crew;
        public float[] CrewInitialSanity;
        public SpaceObject Target { get; private set;}
        public SpaceObject InitialTarget { get; private set; }
        public void SetTarget(SpaceObject so) {
            if (Target != null) {
                InitialTarget = Target;
            }
            Target = so;
            Context?.TargetSO.Set(so);
        }
        public bool IsExploration => Target.Type.IdH != SpaceObjectTypeIdH.Resource;
        public bool CanRecall => Target.Parent == A.Player.Parent;
        public bool IsUnloading;
        public long StartTick { get; private set; }
        public long StageStartTick;
        public long EndTick;
        public int Seed;
        public int EventCount;
        public string Status;
        private ExpeditionData data;
        public ExpeditionMiningOrders MiningOrders;
        private List<string> lootObj;
        public List<string> LootObj => lootObj;
        public uint QuestPackageId;
        public ComponentData StageData;
        private IExpeditionStage currentStage;
        public IExpeditionStage CurrentStage => currentStage;
        public readonly BCallback OnChange = new();
        public void SetStage(IExpeditionStage stage) {
            #if DEBUG_EXPEDITIONS
            D.Warn("Changing expedition stage: {0} -> {1} ({2})", currentStage,
                stage, this);
            #endif
            var prevStage = currentStage;
            currentStage = stage;
            StageStartTick = G.Ticks;
            QuestObserver?.OnExpeditionStageChange(this, prevStage, stage);
            OnChange.Invoke();
        }
        public bool IsAborted;
        public bool IsDone;
        public bool IsValid => Target != null && Shuttle != null && CurrentStage != null;

        public void DumpLoot() {
            if (Shuttle.IsLost) { return; }
            var sb = Caches.StringBuilder2;
            sb.Clear();
            if (Target.Type.IdH != SpaceObjectTypeIdH.Resource
                    || !Arrays.IsEmpty(lootObj) || QuestPackageId != 0) {
                var ws = Shuttle.Tile.Transform.OutPort;
                if (QuestPackageId != 0) {
                    var questPackage = S.Query.SpawnQuestPackage.Ask(QuestPackageId, Shuttle.OutPort);
                    sb.AppendFormat("{0} {1}\n",
                        GlyphMap.ArrowRight, Texts.LinkToEntity(questPackage));
                }
                if (lootObj != null) {
                    foreach (var obj in lootObj) {
                        var o = S.Objs.Create(EntityUtils.CenterOf(ws)
                            + Rng.UInsideUnitCircle() * 0.25f, The.Defs.Get(obj));
                        if (o.TryGetComponent<IExpeditionLootable>(out var loot)) {
                            loot.OnSpawnLoot(this);
                        }
                        sb.AppendFormat("{0} {1}\n", GlyphMap.ArrowRight,
                            Texts.LinkToObj(o));
                    }
                }
                lootObj = null;
                if (!Shuttle.Tile.MaterialStorage.IsEmpty) {
                    foreach (var mat in Shuttle.Tile.MaterialStorage.Contents) {
                        sb.AppendFormat("{0} x{1}\n", mat.Key.Def.CodexLink, mat.Value);
                    }
                }
                if (sb.Length > 0) {
                    WPopup.Create(false, IconId.CDestination, T.ExpeditionLoot, Shuttle.Name,
                        sb.ToString())
                            .WithAction2(T.Show, () => {
                            UIUtils.CloseFullScreenUI();
                            EntityUtils.CameraFocusOn(Shuttle.Tile.Transform.OutPort);
                        })
                        .WithAction(T.Ok, null)
                        .Spawn(true);
                }
            } else {
                if (!Shuttle.Tile.MaterialStorage.IsEmpty) {
                    sb.Append(T.ExpeditionLoot);
                    sb.Append(':');
                    foreach (var mat in Shuttle.Tile.MaterialStorage.Contents) {
                        sb.AppendFormat(" {0} x{1}", mat.Key.Def.CodexLink, mat.Value);
                    }
                    S.Sys.Log.AddLine(Shuttle.Tile, sb.ToString());
                }
            }
            sb.Clear();
            if (!Shuttle.Tile.MaterialStorage.IsEmpty) {
                foreach (var mat in Shuttle.Tile.MaterialStorage.Contents) {
                    if (mat.Value > 0) {
                        S.Sig.MatProduced.Send(mat.Key, mat.Value);
                    }
                }
            }
            Shuttle.Tile.MaterialStorage.DumpContents();
        }

        private Vector2 targetOffset;
        public IQuestExpeditionObserver QuestObserver;

        public Vector2 TargetOffset {
            get {
                if (targetOffset == Consts.V2Zero) {
                    var rng = new Rng(Seed);
                    targetOffset = rng.InsideUnitCircle().normalized * 0.5f * Target.Size;
                }
                return targetOffset;
            }
        }

        public static float MiningMaxDepthFor(ShuttleComp shuttle) {
            return shuttle?.MiningDepth ?? 0f;
        }
        public static float MiningFactorFor(List<BeingData> crew, ShuttleComp shuttle) {
            float factor = 0f;
            if (shuttle?.MiningPower > 0.0001f) {
                factor += shuttle.MiningPower;
            }
            for (int i = crew.Count - 1; i >= 0; i--) {
                BeingData member = crew[i];
                if (member.Mind.Jobs.TryJob(JobTypeIdH.Mining, out var slot)) {
                    factor += slot.SkillEfficiency();
                }
            }
            return factor;
        }
        public static float BestPossibleMiningFactor(GameState s, out float maxDepth) {
            maxDepth = 0f;
            float shuttleFactor = 0f;
            var shuttles = s.Components.AllByType<ShuttleComp>();
            if (shuttles != null) {
                foreach (ShuttleComp sh in shuttles) {
                    if (sh.IsConstructed && !sh.IsLost) {
                        var fac = sh.MiningPower;
                        if (fac > shuttleFactor) {
                            shuttleFactor += fac;
                        }
                        var depth = sh.MiningDepth;
                        if (depth > maxDepth) {
                            maxDepth = depth;
                        }
                    }
                }
            }
            List<float> topFactors = new List<float>(4) {
                0f, 0f, 0f, 0f
            };
            foreach (BeingData bd in s.Query.AllBeingData.Ask().Values) {
                if (!bd.IsPlayer) { continue; }
                if (!bd.Body.IsAlive) { continue; }
                if (bd.HasStateFlag(BeingState.Stored)) { continue; }
                if (!bd.HasAbility(AbilityIdH.Mine)) { continue; }
                if (bd.Mind.Jobs.TryJob(JobTypeIdH.Mining, out var job)) {
                    var fac = job.SkillEfficiency();
                    AddToTopFactors(topFactors, fac);
                }
            }

            var finalFac = shuttleFactor;
            for (int i = 0; i < 4; i++) {
                finalFac += topFactors[i];
            }
            return finalFac;
        }

        private static void AddToTopFactors(List<float> topFactors, float fac) {
            // Find the lowest value and its index
            int lowestIndex = 0;
            float lowestValue = topFactors[0];
            for (int i = 1; i < topFactors.Count; i++) {
                if (topFactors[i] < lowestValue) {
                    lowestValue = topFactors[i];
                    lowestIndex = i;
                }
            }

            // Only insert if fac is better than the lowest
            if (fac > lowestValue) {
                topFactors[lowestIndex] = fac;
            }
        }

        public static float ExpeditionFactorFor(List<BeingData> crew) {
            float factor = 0f;
            for (int i = crew.Count - 1; i >= 0; i--) {
                BeingData member = crew[i];
                if (member.Mind.Jobs.TryJob(JobTypeIdH.Expedition, out var slot)) {
                    factor += slot.SkillEfficiency();
                }
            }
            return factor;
        }

        public float MiningFactor => MiningFactorFor(Crew, Shuttle);
        public float ExpeditionFactor => ExpeditionFactorFor(Crew);

        public float MiningMaxDepth => MiningMaxDepthFor(Shuttle);

        public void Tick(long ticks) {
            if (currentStage == null) { return; }
            if (StartTick == 0) {
                D.Err("Expedition has StarTick of 0! Fixing: {0}", this);
                StartTick = ticks;
            }
            var newStage = currentStage.Tick(this, ticks);
            if (newStage != currentStage) {
                SetStage(newStage);
            }
            if (ticks % Consts.TicksPer5Minutes == 4) {
                TickCrewSanity(ticks);
            }
        }

        private void TickCrewSanity(long ticks) {
            var daysPassed = (ticks - StartTick) / (float) Consts.TicksPerDay;
            for (int i = Crew.Count - 1; i >= 0; i--) {
                var mem = Crew[i];
                if (mem.Mind.Health == null) { continue; }
                if (mem.StateFlags.HasFlag(BeingState.Dead)) { continue; }
                var sanityChange = ExpeditionSys.SanityChangeFor(this, mem, daysPassed);
                var effect = Mathf.RoundToInt(sanityChange * 100f);
                if (effect == 0) { continue; }
                // Refresh as an ongoing mood effect so it flows through the SanityTarget/
                // gravitation model. Effects with the same Reason replace each other
                // (start tick preserved), so magnitude updates as days accumulate.
                // Duration extends ~1 day beyond now so it lingers briefly after return.
                var eff = MentalEffect.CreateWithIcon(
                    mem.Being?.PosIdx ?? Shuttle?.Tile.PosIdx ?? -1,
                    icon: IconId.CPickaxe, start: StartTick,
                    duration: (ticks - StartTick) + Consts.TicksPerDay,
                    T.Expedition, effect);
                A.S.Sig.AddMentalEffect.Send(mem, eff);
            }
        }

        public void AddObjLoot(string id) {
            (lootObj ??= new List<string>()).Add(id);
        }

        public static ExpeditionData Serialize(Expedition exp) {
            var data = new ExpeditionData();
            data.StageType = exp.StageType;
            data.ShuttleId = exp.Shuttle.EntityId;
            data.CrewIds = new uint[exp.Crew.Count];
            for (int i = 0; i < exp.Crew.Count; i++) {
                BeingData member = exp.Crew[i];
                if (member == null || member.Id == 0) {
                    D.Err("Expedition member is null or with id 0 on save: {0}", member);
                }  else {
                    data.CrewIds[i] = member.Id;
                }
            }
            data.TargetId = exp.Target.Id;
            data.StartTick = exp.StartTick;
            data.StageStartTick = exp.StageStartTick;
            data.IsUnloading = exp.IsUnloading;
            data.Status = exp.Status;
            data.EventCount = exp.EventCount;
            data.MiningOrders = exp.MiningOrders;
            data.CrewInitialSanity = exp.CrewInitialSanity;
            data.QuestPackageId = exp.QuestPackageId;
            #if !PRODUCTION_BUILD
            if (exp.MiningOrders == null) {
                D.Err("Saving Expedition without mining orders!");
            }
            #endif
            data.EndTick = exp.EndTick;
            data.Seed = exp.Seed;
            data.StageData = exp.StageData;
            data.LootObj = exp.lootObj;
            return data;
        }

        public void Launch() {
            if (Context == null) {
                D.Warn("Expedition context is null on launch, creating new context: {0}", this);
                Context = new ExpeditionContext() {
                    Shuttle = Shuttle,
                    Expedition = this,
                    MiningOrders = MiningOrders,
                };

                Context.TargetSO.SetSilent(Target);
            }
            StageData = new ComponentData(0, "StageData");
            var stage = ExpeditionStages.Find(ExpeditionStageTypeH.Prep);
            SetStage(stage);
            StartTick = G.Ticks;
            // Tick immediately to mark beings
            currentStage.Tick(this, G.Ticks);
            CreateSanitySnapshot();
        }

        private void CreateSanitySnapshot() {
            CrewInitialSanity = new float[Crew.Count];
            for (int i = Crew.Count - 1; i >= 0; i--) {
                var mem = Crew[i];
                if (mem.Mind.Health != null) {
                    CrewInitialSanity[i] = mem.Mind.Health.Sanity.BaseValue;
                } else {
                    CrewInitialSanity[i] = -1f;
                }
            }
        }

        public static Expedition Deserialize(ExpeditionData data) {
            var exp = new Expedition();
            exp.data = data;
            return exp;
        }

        public void AfterLoad() {
            try {
                AfterLoadDo();
            } catch (Exception ex) {
                D.LogEx(ex, "Failed loading expedition: {0}", this);
            }
        }

        private void AfterLoadDo() {
            var exp = this;
            exp.Seed = data.Seed;
            exp.Shuttle = S.Components.Find<ShuttleComp>(data.ShuttleId);
            if (exp.Shuttle == null) {
                D.Err("Could not find shuttle for expedition {0}!",
                    exp.Target?.DisplayName);
                exp.IsDone = true;
            }
            exp.Crew = new List<BeingData>(data.CrewIds.Length);
            for (int i = data.CrewIds.Length - 1; i >= 0; i--) {
                uint memberId = data.CrewIds[i];
                var member = S.Query.FindBeingData.Ask(memberId);
                if (member == null) {
                    D.Err("Could not find expedition member after load with id: {0}", memberId);
                    continue;
                }
                if (member != null) {
                    exp.CrewAdd(member);
                } else {
                    D.Err("Expedition member not found by id {0} during load", memberId);
                }
            }
            exp.CrewInitialSanity = data.CrewInitialSanity;
            if (data.CrewInitialSanity == null) {
                CreateSanitySnapshot();
            }
            exp.Target = S.Universe.Find(data.TargetId);
            exp.StartTick = data.StartTick;
            exp.StageStartTick = data.StageStartTick;

            // legacy fix
            if (exp.StartTick == 0) {
                exp.StartTick = G.Ticks;
            }
            if (exp.StageStartTick == 0) {
                exp.StageStartTick = G.Ticks;
            }

            exp.EndTick = data.EndTick;
            exp.Status = data.Status;
            exp.IsUnloading = data.IsUnloading;
            exp.MiningOrders = data.MiningOrders;
            exp.QuestPackageId = data.QuestPackageId;
            if (exp.MiningOrders == null) {
                exp.MiningOrders = ExpeditionMiningOrders.Default(S);
            }
            // TODO: remove this after a while
            // double check if we are supposed to be unloading
            if (exp.IsUnloading && !exp.MiningOrders.IsAutoUnload) {
                exp.IsUnloading = false;
            }
            exp.EventCount = data.EventCount;
            exp.StageData = data.StageData;
            exp.lootObj = data.LootObj;
            if (exp.StageData == null) {
                exp.StageData = new(0, "StageData");
            }
            var expStageType = data.StageType;
            data = null;

            Context = new ExpeditionContext() {
                Shuttle = exp.Shuttle,
                Expedition = exp,
                MiningOrders = exp.MiningOrders,
            };

            Context.TargetSO.SetSilent(exp.Target);
            var stage = ExpeditionStages.Find(expStageType);
            SetStage(stage);
        }

        public IExpeditionStage ForceStage(int stage) {
            currentStage?.Abort(this);
            var newStage = ExpeditionStages.Find(stage);
            SetStage(newStage);
            return currentStage;
        }

        public override string ToString() {
            if (IsValid) {
                return $"Expedition [{Seed}][{Shuttle.Name}] -> [{Target.Name}]";
            }
            return "Expedition [Invalid]";
        }

        public Expedition WithCrew(List<BeingData> crew) {
            #if DEBUG_CREW_CHANGE
            D.Warn("Crew: set {0} for {1}", D.DumpCollection(crew), this);
            #endif
            Crew = new List<BeingData>(crew);
            return this;
        }
        public void CrewAdd(BeingData mem) {
            #if DEBUG_CREW_CHANGE
            D.Warn("Crew: add {0} to {1}", mem, this);
            #endif
            Crew.Add(mem);

        }
        public void CrewRemove(BeingData mem) {
            #if DEBUG_CREW_CHANGE
            D.Warn("Crew: remove {0} from {1}", mem, this);
            #endif
            Crew.Remove(mem);
        }

    }
}
