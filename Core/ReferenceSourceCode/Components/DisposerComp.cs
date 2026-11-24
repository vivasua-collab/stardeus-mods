using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using UnityEngine;
using Game.Systems.AI;
using KL.Utils;
using Game.Systems.Creatures;
using Game.Commands;
using System;
using Game.Systems.BodyParts;
using KL.Randomness;
using Game.Systems.Crafting;
using Game.Systems.Mental;
using KL.Grid;
using Game.Components.Upgrades;
using Game.Systems.UIUX;

namespace Game.Components {
    public sealed class DisposerComp : BaseSlotsComp<DisposerComp>, IAIGoalProvider,
            ICopyableComp, IRelocatable,
            IEnergyEfficiencyUpgradeable, ISpeedUpgradeable, IDefPowerProvider {
        private const float DefaultEnergyCost = 15f;

        public void AddDefPower(Def def, ComponentConfig config, ref DefPower power) {
            power.AddConsumption(config.GetFloat(PropertyIdH.EnergyCost, DefaultEnergyCost));
        }

        [Serializable]
        public struct Disposable {
            public uint MindId;
            public uint BeingDataId;
            public int BodyPartIdH;
            public int DefIdH;
            [Newtonsoft.Json.JsonIgnore]
            public Def Def => The.Defs.FastGet(DefIdH);
        }

        // We'll need to display / save / load this, so list will be more
        // convenient
        public readonly List<Disposable> Queue = new();
        public override bool IsAvailable => Queue.Count < maxCapacity && base.IsAvailable;
        private int maxCapacity;
        private float progress;
        private float outMul;
        private float inputMul;
        private float stemCellsMul;
        private float partQualityThreshold;

        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new DisposerComp());
        }

        private BCallback stateChangedB;
        public BCallback DisposerStateChangedB => stateChangedB ??= new BCallback();
        public int MaxCapacity => maxCapacity;
        public float PartQualityThreshold {
            get => partQualityThreshold;
            set => partQualityThreshold = Mathf.Clamp01(value);
        }

        private void UpdateUIBlock() {
            stateChangedB?.Invoke();
        }

        public override string SubmenuTitle => Tile.Definition.NameT;

        private int slotDesignationH;
        public override int SlotDesignation => slotDesignationH;

        public bool IsCopyable => true;
        public string CopyConfigText => Entity.Definition.NameT;

        private ElectricNodeComp elNode;
        private float energyCost;
        private float progressPerGridTick;
        private float upgradeEnergyEfficiency = 1f;
        private float upgradeSpeed = 1f;

        public bool IsEnergyEfficiencyUpgradeable => true;
        public bool IsSpeedUpgradeable => true;

        public void ApplyEfficiencyUpgrade(float amt) {
            upgradeEnergyEfficiency += amt;
            if (upgradeEnergyEfficiency < 1f) {
                D.Err("Upgrade Efficiency < 1f after apply: {0} -> {1}",
                    amt, upgradeEnergyEfficiency);
                upgradeEnergyEfficiency = 1f;
            }
        }

        public void ApplySpeedUpgrade(float amt) {
            upgradeSpeed += amt;
            if (upgradeSpeed < 1f) {
                D.Err("Upgrade speed < 1f after apply: {0} -> {1}",
                    amt, upgradeSpeed);
                upgradeSpeed = 1f;
            }
        }
        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ElectricNodeAdded) {
                if (sender is ElectricNodeComp enode) {
                    elNode = enode;
                    enode.AddAfterTick(this, AfterTickGrid);
                }
            } else if (message == MsgIdH.ConstructionFinished) {
                CreateBeingSlots(null, null);
            }
        }
        public override void OnLateReady(bool wasLoaded) {
            if (wasLoaded && Tile.IsConstructed) {
                CreateBeingSlots(null, null);
            }
        }

        public override bool IsCompatibleWith(BeingData data) {
            return true;
        }

        public bool LoadDisposable(DisposableComp disposable, AIAgentComp agent) {
            if (Queue.Count >= maxCapacity) {
                return false;
            }

            var disp = new Disposable();
            var b = disposable.Being;
            if (b != null) {
                var data = b.Data;
                disp.BeingDataId = data.Id;
                if (b.Species.IsBiological || b.Data.Mind.Health != null) {
                    disp.MindId = PersistMind(b.Data.Mind);
                }

                b.Data.StateFlags |= BeingState.InDevice;
                if (b.IsAlive) {
                    BeingUtils.Eliminate(S, data, T.DeathDisposedAt_V2.F(
                        Tile.NameLink,
                        agent.BeingData.Mind.LinkToShortName),
                        G.Ticks);
                } else {
                    BeingUtils.Eliminate(S, data, null, 0);
                }
                // data.StateFlags = BeingState.Hidden;
                // S.Clock.OnNextUpdate(() => BeingUtils.Eliminate(S, data);
            } else {
                if (disposable.Obj != null) {
                    if (disposable.Obj.Data.BodyPart != null) {
                        // D.Warn("Disp body part: {0}", disposable.Obj.Data.BodyPart);
                        disp.BodyPartIdH = disposable.Obj.Data.BodyPart.IdH;
                    }
                    S.Clock.OnNextUpdate(() => S.Objs.Destroy(disposable.Obj));
                }
            }
            disp.DefIdH = disposable.Entity.Definition.IdH;
            Queue.Add(disp);
            UpdateUIBlock();

            return true;
        }

        private uint PersistMind(Mind mind) {
            // we have an empty mind that belonged to someone
            if (mind.PreviousId != 0) {
                return mind.PreviousId;
            }
            Mind.RegisterIfNecessary(S, mind);
            return mind.Id;
        }

        private void AfterTickGrid() {
            if (Queue.Count > 0) {
                var actualEnergyCost = energyCost;
                if (upgradeEnergyEfficiency > 1f) {
                    actualEnergyCost *= 1f / upgradeEnergyEfficiency;
                }
                elNode.SetConsumerWantedInput(actualEnergyCost);
                if (elNode.IsConsuming) {
                    var actualProgressPerGridTick = progressPerGridTick;
                    if (upgradeSpeed > 1f) {
                        actualProgressPerGridTick *= upgradeSpeed;
                    }
                    progress += actualProgressPerGridTick;
                    var disp = Queue[0];
                    var def = disp.Def;
                    if (def == null) {
                        D.Err("Skipping invalid disposable (no def): {0} | {1}",
                            D.DumpInstance(disp), Tile);
                        Queue.RemoveAt(0);
                        progress = 0f;
                        return;
                    }
                    if (progress >= 1f) {
                        Queue.RemoveAt(0);
                        UpdateUIBlock();
                        DoDispose(disp);
                        progress = 0f;
                    }
                    UpdateInfo(disp);
                }
            } else {
                elNode.SetConsumerWantedInput(0f);
                HideAllInfo();
            }
        }

        private int lastDisposableDefIdH;
        private int lastProgress;

        private void UpdateInfo(Disposable disp) {
            if (lastDisposableDefIdH != disp.DefIdH || lastProgress != (int) progress) {
                ShowInfoIcon(disp.Def.Preview, null);
                ShowInfoBar(Consts.ColorFuelBar, progress,
                    T.ProcessingBar_V1.F($"{disp.Def.NameT}: {Units.Percentage(progress)}"));
            }
        }

        private void DoDispose(Disposable disposable) {
            // FIXME - remove when the cause of NPE is found and fixed
            int step = 0;
            try {
                if (disposable.MindId != 0) {
                    step = 1;
                    var mind = S.Query.FindMind.Ask(disposable.MindId);
                    if (mind != null) {
                        step = 2;
                        CreateNamedAshes(mind);
                    }
                }
                step = 3;
                var outPortPos = Pos.ToVector(Tile.Transform.OutPort);
                step = 4;
                if (disposable.BeingDataId != 0) {
                    step = 5;
                    var data = S.Query.FindBeingData.Ask(disposable.BeingDataId);
                    if (data == null) { return; }
                    step = 6;
                    var body = data.Body;
                    if (data.LastPosition.Idx == 0) {
                        step = 7;
                        // set this for validation's sake
                        data.LastPosition.Idx = Tile.Transform.OutPort;
                        data.LastPosition.Exact = Pos.ToVector(Tile.Transform.OutPort);
                    }
                    step = 8;
                    var sp = data.Species;
                    if (sp == null) {
                        D.Err("Trying to dispose of a being with no species! {0} | {1}", data, Tile);
                        return;
                    }
                    step = 9;
                    for (int i = body.Slots.Length - 1; i >= 0; i--) {
                        BodyPartSlot part = body.Slots[i];
                        DisposeBodyPartSlot(outPortPos, part, sp);
                    }
                    return;
                }
                step = 10;
                if (disposable.BodyPartIdH != 0) {
                    step = 11;
                    var bp = BodyPartDef.Get(disposable.BodyPartIdH);
                    if (bp == null) {
                        D.Err("Trying to dispose of a body part with invalid id! {0} | {1}",
                            BodyPartIdH.ResolveName(disposable.BodyPartIdH), Tile);
                        return;
                    }
                    if (bp.SlotType == null) {
                        D.Err("Trying to dispose of a body part with no slot type! {0} | {1}",
                            bp, Tile);
                        return;
                    }
                    bool allowCrawlerGoo;
                    // Crawler parts are no longer TargetSpecies-locked (their legs and maw are
                    // transplantable now), so TargetSpecies is only the native-species marker.
                    if (bp.IsBiological
                            && Arrays.SafeGet(bp.TargetSpecies, 0) == SpeciesId.Crawler) {
                        allowCrawlerGoo = true;
                    } else {
                        allowCrawlerGoo = false;
                    }
                    step = 12;
                    DisposeDef(disposable.Def, (m) => BodyPartReduce(m, bp.IsBiological,
                        allowCrawlerGoo, bp.NativeSlotSize()));
                    return;
                }
                step = 13;

                DisposeDef(disposable.Def, DefaultReduce);

            } catch (Exception e) {
                D.Err("Error during dispose step {0}: {1} | {2}", step, e, Tile);
                return;
            }
        }

        private void DisposeBodyPartSlot(Vector2 outPortPos, BodyPartSlot part, Species sp) {
            if (part == null) { return; }
            if (part.IsPartRemoved || part.InstalledDef == null) { return; }
            var partType = part.Type;
            if (partType == null) {
                D.Err("Body part slot has no type? {0} | {1}", part, Tile);
                return;
            }
            // we can't produce a head separately, so it will be
            // considered as part of Body instead
            if (partType.IdH == BodyPartIdH.Head) {
                return;
            }
            bool isCrawler = sp.IdH == SpeciesIdH.Crawler;
            bool isOrganic = slotDesignationH == SlotDesignationH.Disposer_Organic;
            if (partType.IdH == BodyPartIdH.Body) {
                if (sp != null) {
                    DisposeDef(sp.Def, (m) => BodyPartReduce(m, sp.IsBiological,
                        isCrawler, part.Size));
                }
            } else {
                var isBio = part.InstalledDef.IsBiological;
                if (isOrganic) {
                    if (!isBio) {
                        // organic disposer yields 1 scrap metal per robotic part
                        var scrap = new Mat { Type = MatType.FastGet(MatIdH.ScrapMetal), StackSize = 1 };
                        S.CmdQ.Enqueue(new CmdSpawnMats(new[] { scrap }, Tile.Transform.OutPort, 0.1f));
                        S.Sig.MatProduced.Send(scrap.Type, scrap.StackSize);
                    } else {
                        DisposeDef(part.InstalledDef.Def, (m) => BodyPartReduce(m,
                            isBio, isCrawler, part.Size));
                    }
                } else {
                    if (!isCrawler && part.State != null && !part.State.IsDestroyed
                            && part.Quality >= partQualityThreshold) {
                        // spawn the body part
                        if (part.TryRemovePart(S, out var spawnedObj, null, true, out _)) {
                            var pos = outPortPos + Rng.UInsideUnitCircle() * 0.25f;
                            spawnedObj.Transform.TeleportTo(pos);
                            spawnedObj.Graphics.TeleportTo(pos);
                            return;
                        }
                    }
                    DisposeDef(part.InstalledDef.Def, (m) => BodyPartReduce(m,
                        isBio, isCrawler, part.Size));
                }
            }
        }

        private void CreateNamedAshes(Mind mind) {
            Mind.CreateAshes(S, EntityUtils.CenterOf(Tile.Transform.AnyPortOutFirst)
                + Rng.UInsideUnitCircle() * 0.33f, mind);
        }

        private Mat BodyPartReduce(Mat mat, bool isBiological,
                bool allowCrawlerGoo, float size) {
            if (!isBiological) {
                return DefaultReduce(mat);
            }
            // some body parts require CrawlerGoo to be grown. We don't want to give the
            // crawler goo back from non-crawler body parts
            if (!allowCrawlerGoo && mat.Type.IdH == MatIdH.CrawlerGoo) {
                // crawler goo -> null for non-crawlers
                mat.StackSize = 0;
                return mat;
            }
            // carbon -> null
            if (mat.Type.IdH == MatIdH.Carbon) {
                // mat.Type = MatType.FastGet(MatIdH.Leather);
                mat.StackSize = 0;
            }
            float inMul = inputMul;
            if (mat.Type.IdH == MatIdH.StemCells) {
                inMul = stemCellsMul;
            }
            mat.StackSize = Mathf.CeilToInt(mat.StackSize * Maths.Min(size, inMul));
            return mat;
        }

        private Mat DefaultReduce(Mat mat) {
            if (mat.StackSize == 0) {
                return mat;
            }
            float inMul = inputMul;
            if (mat.Type.IdH == MatIdH.StemCells) {
                inMul = stemCellsMul;
            }
            mat.StackSize = Mathf.CeilToInt(mat.StackSize * inMul);
            return mat;
        }

        private void DisposeDef(Def def, Func<Mat, Mat> reduceMat) {
            if (def == null) { return; }
            var craftables = Craftable.FindForDef(def);
            if (Arrays.IsEmpty(craftables)) {
                if (!def.IgnoreMarket) {
                    D.Err("Trying to dispose of a def that doesn't have a craftable but is in market! {0} -> {1}",
                        def, Tile);
                } else {
                    D.Log("Trying to dispose of a def that doesn't have a craftable: {0} -> {1}",
                        def, Tile);
                }
                return;
            }
            DisposeCraftable(craftables[0], reduceMat);
        }

        private void DisposeCraftable(Craftable cr, Func<Mat, Mat> reduceMat) {
            #if !PRODUCTION_BUILD
            if (cr.Type == "Converter") {
                D.Err("Rethink craftable disposal, disposing of a Converter recipe! {0} {1}", cr, Tile);
            }
            // FIXME figure out why this was here?
            // if (cr.IgnoreMarket) {
            //     D.Err("Disposing of an item that is ignored in the stock market! {0} {1}", cr, Tile);
            // }
            #endif
            Mat[] matsToSpawn;
            if (!Arrays.IsEmpty(cr.DisposableOutput)) {
                matsToSpawn = cr.DisposableOutput;
            } else if (!Arrays.IsEmpty(cr.Ingredients)) {
                var toSpawn = new List<Mat>();
                for (int i = 0; i < cr.Ingredients.Length; i++) {
                    Mat ing = cr.Ingredients[i];
                    if (reduceMat != null) {
                        ing = reduceMat(ing);
                    }
                    float outStackSize = ing.StackSize * outMul;
                    // if we get 0.1, we will have 10% chance to output 1
                    // if we get 1.2, we will have 20% chance to output 2, 80% chance to 1
                    int outStackSizeWOFrac = Mathf.FloorToInt(outStackSize);
                    float outStackSizeFrac = outStackSize - outStackSizeWOFrac;
                    if (outStackSizeFrac > 0f && Rng.UChance(outStackSizeFrac)) {
                        outStackSizeWOFrac++;
                    }
                    ing.StackSize = outStackSizeWOFrac;
                    if (ing.StackSize == 0) { continue; }
                    ConstructableComp.AddDeconstructedMat(toSpawn, ing, S.Rng);
                    // toSpawn.Add(ing);
                }
                matsToSpawn = toSpawn.ToArray();
            } else {
                D.Err("Craftable has no Ingredients or DisposableOutput! {0} {1}", cr, Tile);
                return;
            }

            // D.Warn("Spawning disposed mats: {0} -> {1}", cr.Id, D.DumpCollection(matsToSpawn));
            S.CmdQ.Enqueue(new CmdSpawnMats(matsToSpawn, Tile.Transform.OutPort, 0.1f));
            foreach (var mat in matsToSpawn) {
                if (mat.Type != null && mat.StackSize > 0) {
                    S.Sig.MatProduced.Send(mat.Type, mat.StackSize);
                }
            }
        }

        public override void OnRemove() {
            RemoveSlots();
        }

        protected override void OnConfig() {
            OnConfigSlots();
            inputMul = Tunables.DisposerInputMultiplier;
            stemCellsMul = Tunables.DisposerStemCellsMultiplier;
            outMul = Tunables.DisposerOutputMultiplier;
            if (S.Difficulty.Level < 1f) {
                if (S.Difficulty.Level < 0.15f) {
                    // peaceful
                    outMul += 0.3f;
                } else {
                    // relaxing
                    outMul += 0.2f;
                }
            }

            if (inputMul > 1f) { inputMul = 1f; }
            if (outMul > 1f) { outMul = 1f; }
            partQualityThreshold = Config.GetFloat(PropertyIdH.PartQualityThreshold, 0.5f);
            maxCapacity = Config.GetInt(PropertyIdH.MaxCapacity, 5);
            energyCost = Config.GetFloat(PropertyIdH.EnergyCost, DefaultEnergyCost);
            progressPerGridTick = Config.GetFloat(PropertyIdH.DurationHours, 3f);
            progressPerGridTick *= Consts.TicksPerHour;
            progressPerGridTick = 1f / progressPerGridTick;
            progressPerGridTick *= 10;
            if (Config.GetBool(PropertyIdH.Biological, false)) {
                slotDesignationH = SlotDesignationH.Disposer_Organic;
            } else {
                slotDesignationH = SlotDesignationH.Disposer_Robotic;
            }
        }

        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetFloat("PartQualityThreshold", partQualityThreshold);
            if (Queue.Count == 0) {
                data.RemoveStringSet("Queue");
                data.RemoveFloat("Progress");
            } else {
                data.SetFloat("Progress", progress);
                var items = new string[Queue.Count];
                for (int i = 0; i < Queue.Count; i++) {
                    Disposable item = Queue[i];
                    items[i] = Json.Dump(item);
                }
                data.SetStringSet("Queue", items);
            }
        }

        protected override void OnLoad(ComponentData data) {
            partQualityThreshold = data.GetFloat("PartQualityThreshold",
                partQualityThreshold);
            var items = data.GetStringSet("Queue", null);
            if (items != null) {
                progress = data.GetFloat("Progress", 0f);
                for (int i = 0; i < items.Length; i++) {
                    string item = items[i];
                    if (Json.TryDeserialize<Disposable>(item, out var disp,
                            out var err, strict: false)) {
                        Queue.Add(disp);
                    } else {
                        D.Err("Failed deserializing disposable json: {0} | {1}", err, item);
                    }
                }
            }
        }

        public override string ToString() {
            return " * Disposer";
        }

        public void OnGoalChange(AIGoal goal, AIGoalState from, AIGoalState to) {
        }

        public void OnGoalLoad(AIGoal goal) {
        }

        public bool CopyConfigTo(ICopyableComp target) {
            if (target is DisposerComp dc && dc != this) {
                dc.partQualityThreshold = partQualityThreshold;
                return true;
            }
            return false;
        }

        public bool CanCopyTo(ICopyableComp comp) {
            return comp is DisposerComp;
        }

        public void RelocateTo(Entity target) {
            if (target.TryGetComponent<DisposerComp>(out var disp)) {
                CopyConfigTo(disp);
                if (Queue.Count > 0) {
                    disp.Queue.Clear();
                    disp.Queue.AddRange(Queue);
                    Queue.Clear();
                    disp.progress = progress;
                    progress = 0f;
                }
            }
        }
    }
}
