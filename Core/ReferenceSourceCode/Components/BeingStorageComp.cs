using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using KL.Utils;
using UnityEngine;
using Game.CodeGen;
using Game.Systems.AI;
using System;
using KL.Grid;
using System.Buffers;
using System.Linq;
using Game.Utils;
using Game.Systems.Creatures;
using Game.Systems.BodyParts;
using Game.Systems.UIUX;
using Game.Input;

namespace Game.Components {
    public sealed class BeingStorageComp : BaseComponent<BeingStorageComp>,
            IStorageDevice, IRelocatable,
            IUICustomTooltipProvider {

        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new BeingStorageComp());
        }
        public BeingHaulTargetComp HaulTarget;

        public float MinQuality;
        private BCallback qualityChanged;
        public BCallback QualityChanged => qualityChanged ??= new();
        public int MaxCapacity;
        private int usedCapacity;
        public int UsedCapacity {
            get => usedCapacity;
            set {
                usedCapacity = value;
                UpdateStats();
            }
        }
        public bool IsFull;
        private int storageId;
        public int StorageId => storageId;
        public BeingStorageFlags Flags;
        private InfoBar bar;

        // public int WorkSpot => Tile.Transform.WorkSpot;
        public int InPort => Tile.Transform.InPort;
        public int OutPort => Tile.Transform.OutPort;

        public bool Accepts(BeingData data) {
            return (data.StorageFlags & Flags) == data.StorageFlags;
        }

        public void UpdateQualityRange(float min) {
            MinQuality = min;
            QualityChanged.Invoke();
        }

        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetInt("Used", usedCapacity);
            data.SetFloat("MinQuality", MinQuality);
        }

        protected override void OnLoad(ComponentData data) {
            usedCapacity = data.GetInt("Used", 0);
            MinQuality = data.GetFloat("MinQuality", 0f);
            UpdateCapacity();
        }

        private void UpdateCapacity() {
            IsFull = usedCapacity >= MaxCapacity;
        }

        protected override void OnConfig() {
            MaxCapacity = Config.GetInt(PropertyIdH.MaxCapacity, 100);
            var groupStrings = Config.GetStringSet(PropertyIdH.Groups, null);
            var acceptsDead = Arrays.ContainsSafe(groupStrings, "Dead");
            var acceptsAlive = Arrays.ContainsSafe(groupStrings, "Alive");
            var acceptsBiological = Arrays.ContainsSafe(groupStrings, "Biological");
            var acceptsRobotic = Arrays.ContainsSafe(groupStrings, "Robotic");
            if (!acceptsDead && !acceptsAlive) {
                D.Err("At least one group form Dead or Alive is required in BeingStorage config! {0}",
                    Entity.DefinitionId);
                acceptsDead = true;
            }
            if (!acceptsRobotic && !acceptsBiological) {
                D.Err("At least one group form Robotic or Biological is required in BeingStorage config! {0}",
                    Entity.DefinitionId);
                acceptsRobotic = true;
            }
            Flags = BeingStorageFlags.None;
            if (acceptsDead) {
                Flags |= BeingStorageFlags.Dead;
            }
            if (acceptsAlive) {
                Flags |= BeingStorageFlags.Alive;
            }
            if (acceptsRobotic) {
                Flags |= BeingStorageFlags.Robotic;
            }
            if (acceptsBiological) {
                Flags |= BeingStorageFlags.Biological;
            }
        }

        public override void OnLateReady(bool wasLoaded) {
            storageId = EntityId;
            entity.TryGetComponent(out HaulTarget);
            Tile.Transform.EnsurePortPresence(false, true, true, this);
            if (wasLoaded) {
                if (!Tile.IsConstructed) { return; }
                Ready.When(WhenH.BeingsLoaded,
                    () => RegisterStorage(EntityChange.Loaded));
            }
        }
        private void RegisterStorage(EntityChange state) {
            S.Sig.BeingStorageChanged.Send(this, state);
            UpdateStats();
        }
        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ConstructionFinished) {
                RegisterStorage(EntityChange.Constructed);
            }
        }

        public override void OnRemove() {
            S.Sig.BeingStorageChanged.Send(this, EntityChange.Destroyed);
        }

        private void UpdateUIBlock(bool wasUpdated) {
            UpdateProgressBar();
            if (wasUpdated) {
                OnChange.Invoke();
            }
        }

        private void UpdateProgressBar() {
            if (!IsConstructed) { return; }
            bar = ShowInfoBar(Consts.ColorFuelBar, (float)UsedCapacity / MaxCapacity,
                UsedCapacity, MaxCapacity, "storage.capacity.bar");
            bar.CanHaveCustomTooltip = true;
        }

        public void EjectAll() {
            ForEachStored((b) => Eject(b));
        }

        public void Eject(BeingData data) {
            if (!S.Query.UnstoreBeing.Ask(data, this)) {
                D.Err("Failed to eject being from storage: {0} | {1}", data, this);
            }
        }

        public void DisposeByQuality(float v) {
            D.Warn("Disposing by quality: {0} or lower in {1}", Units.Percentage(v), this);
            ForEachStored((bd) => {
                if (bd.Body.BodySlot.Quality <= v) {
                    Eject(bd);
                    if (bd.Being != null && bd.Being.TryGetComponent<DisposableComp>(out var disp)) {
                        disp.TryOrderDispose(false, out _);
                    }
                }
            });
        }

        public void RelocateTo(Entity target) {
            // TODO read all stored items and update their position and storage id
            if (target.TryGetComponent<BeingStorageComp>(out var other)) {
                if (UsedCapacity > 0) {
                    ForEachStored((obj) => {
                        obj.StorageId = other.EntityId;
                    });
                }
                other.UsedCapacity = UsedCapacity;
                other.UpdateStats();
            }
        }

        public int ForEachStored(Action<BeingData> action,
                BeingData.SortMode? sortMode = null,
                string searchStr = null) {
            var pool = ArrayPool<BeingData>.Shared;
            var objs = pool.Rent(UsedCapacity);
            if (!sortMode.HasValue) {
                sortMode = BeingData.BeingSortMode;
            }
            var found = S.Query.FindStoredBeingData.Ask(
                storageId, objs, searchStr, sortMode.Value);
            if (searchStr == null && found != UsedCapacity) {
                D.Err("Expected {0} beings in storage, actual: {1}! {2}",
                    UsedCapacity, found, Tile);
            }

            for (int i = 0; i < found; i++) {
                action(objs[i]);
            }
            pool.Return(objs, true);
            return found;
        }

        public bool Store(AIAgentComp agent, BeingData beingData, AIGoal goal) {
            if (agent.PosIdx != InPort) {
                D.Err("Trying to store a being from too far away! {0}, {1}, {2}, {3}",
                    Pos.StringFromTo(agent.PosIdx, InPort), agent, Tile, goal);
                return false;
            }
            return S.Query.StoreBeing.Ask(beingData, this);
        }

        public bool TryTake(AIAgentComp agent, BeingData beingData, AIGoal goal) {
            if (agent.PosIdx != OutPort) {
                D.Err("Trying to take a being from storage too far away! {0}, {1}, {2}, {3}",
                    Pos.StringFromTo(agent.PosIdx, OutPort), agent, Tile, goal);
                return false;
            }
            return S.Query.UnstoreBeing.Ask(beingData, this);
        }

        private void UpdateStats() {
            UpdateCapacity();
            UpdateUIBlock(true);
        }

        public string ShowCustomTooltip(string currentTooltip, InfoItem item) {
            var sb = Caches.StringBuilder;
            sb.Clear();
            if (bar != null) {
                sb.Append(bar.Info);
            }
            const int max = 10;
            var map = Caches.MapStringInt;
            map.Clear();
            int found = ForEachStored((bd) => {
                var name = string.Format("{0} {1}",
                    Texts.LinkToBeing(bd, Texts.LinkColor(bd.Mind?.Name.FullNameOrNameless ?? T.Nameless)),
                        Texts.GreyParentheses(bd.Species.Name));
                if (!map.TryGetValue(name, out var cnt)) {
                    cnt = 0;
                }
                map[name] = cnt + 1;
            }, BeingData.SortMode.Unsorted);
            if (found == 0) {
                return sb.ToString();
            }
            var ordered = map.OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key).Take(max);
            foreach (var kv in ordered) {
                if (kv.Value > 1) {
                    sb.AppendFormat("\n   {0} {1}: x{2}", GlyphMap.ArrowRight,
                        kv.Key, kv.Value);
                } else {
                    sb.AppendFormat("\n   {0} {1}", GlyphMap.ArrowRight,
                        kv.Key);
                }
            }
            if (map.Count > max) {
                sb.AppendLine();
                sb.Append(Texts.Grey($"   {GlyphMap.ArrowRight} +{map.Count - max}"));
            }
            return sb.ToString();
        }

        public override string ToString() {
            return $"BeingStorage[{Tile}]";
        }
    }
}
