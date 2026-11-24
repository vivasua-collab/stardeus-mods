using System.Collections.Generic;
using Game.Components;
using KL.Grid;
using KL.Utils;
using UnityEngine;
using MessagePack;
using Game.Constants;
using Game.Utils;
using Game.CodeGen;
using System;
using Game.Data.Economy;
using Game.Tools;
using Game.UI;
using KL.QuadTree;
using Game.Mods;
using Game.Systems.AI;
using Game.Data.Space;

namespace Game.Data {
    [MessagePackObject]
    public abstract class Entity {
        [Key(0)] public int Id;
        [Key(1)] public string DefinitionId;
        [IgnoreMember] public Map Map;
        [IgnoreMember] public bool IsActive;
        [IgnoreMember] public int PosIdx;
        [IgnoreMember] public int CenterIdx;
        [IgnoreMember] public Vector2 Position;
        [IgnoreMember] public Vector2 SelectPosition;
        [IgnoreMember] public Vector2 SelectOffset;
        [IgnoreMember] public Vector2 SelectableSize;
        [IgnoreMember] public virtual Vector2 Size => new(1f, 1f);
        /// <summary>
        /// Never set this directly! Use TrySetGoal.
        /// Only one goal can be set on an entity at a time.
        /// TrySetGoal enforces that.
        /// <see cref="TrySetGoal(AIGoal, AIAgentComp)">
        /// </summary>
        [NonSerialized] [IgnoreMember] public AIGoal Goal;
        [IgnoreMember] public AudioSourceComp AudioSource;
        [IgnoreMember] public LightSourceComp Light;
        [IgnoreMember] [NonSerialized] public GameState S;
        [IgnoreMember] public FlammableComp Flammable;
        [IgnoreMember] public IComponent[] Components;
        // SHOULD BE A STAT
        [IgnoreMember] public Being Carrier;
        [IgnoreMember] public int NanobotCarrierHiveId;
        [IgnoreMember] public bool IsCarriedByAny => Carrier != null || NanobotCarrierHiveId != 0;
        [IgnoreMember] public bool IsImmutable => immutable?.Count > 0;
        [IgnoreMember] public bool IsVisibleNow => IsActive;
        protected HashSet<IComponent> immutable;
        [IgnoreMember] public bool IsInfoDisabled;
        [IgnoreMember] public bool IsRemoved;
        [IgnoreMember] public long DeactivationTime;
        [IgnoreMember] public bool HasDefinition;


        [IgnoreMember] public abstract string CategoryTitle { get; }
        [IgnoreMember] public int OriginalId;
        private Def definition;
        /// <summary>
        /// Only happens after the object gets created for the first time, not after load!
        /// </summary>
        public event Action AfterInitialize;
        /// <summary>
        /// Mostly for tests, possibly for mods
        /// </summary>
        [IgnoreMember] public Action<Entity, AIGoal> OnSetGoal;

        public bool LoadGoal(AIGoal goal) {
            if (Goal != null)  {
                D.Err("Trying to load second goal on entity {0}: Existing: {1}, New: {2}", this,
                    Goal, goal);
                return false;
            }
            Goal = goal;
            goal.UpdateIcon();
            return true;
        }

        public bool UnsetGoal(AIGoal goal) {
            if (Goal == goal) {
                Goal = null;
                return true;
            }
            return false;
        }

        public bool TrySetGoal(AIGoal goal,
                AIAgentComp forceAgent = null,
                bool addToAISystem = true) {
            if (goal == null) {
                D.Err("Trying to set a null goal for: {0}", this);
                return false;
            }
            if (Goal != null) { // old goal!
                if (goal.OverwritePriority > Goal.OverwritePriority) {
                    // A failed goal is still retried later (AISys.ProcessGoalFailed), so it must
                    // go too: left behind, it found this entity holding another goal and tripped
                    // AISys.CheckGoalTarget's sanity check
                    if (Goal.State != AIGoalState.Cancelled
                            && Goal.State != AIGoalState.Completed) {
                        AIGoal.Cancel(Goal, G.Ticks, $"{T.AdRejInterrupted}: {goal.Name}");
                    }
                } else {
                    D.Err("Trying to set a new goal for entity {0} when one exists: {1}, old: {2}",
                        this, goal, Goal);
                    return false;
                }
            }
            if (goal.TrySetAgent(forceAgent)) {
                goal.ClearCounters();
                goal.ChangeStateTo(AIGoalState.PreAssigned, G.Ticks, true);
            }
            Goal = goal;
            if (addToAISystem) {
                S.Sig.AIAddGoal.Send(goal);
            }
            OnSetGoal?.Invoke(this, goal);
            return true;
        }
        public void UnsetGoal() {
            Goal = null;
        }
        public void ClearGoal(string reason = null) {
            if (Goal == null) { return; }
            // if (reason != null) {
            //     D.Warn("Removing goal {0} because {1}", Goal, reason);
            // }
            AIGoal.Cancel(Goal, G.Ticks, reason);
            Goal = null;
        }

        // For tests
        public virtual void ClearImmutable() {
            immutable?.Clear();
        }

        // Immutable will also make the entity invincible
        public virtual void SetImmutable(IComponent c, bool isImmutable) {
            if (isImmutable) {
                (immutable ??= new()).Add(c);
            } else {
                immutable?.Remove(c);
            }
        }

        /*
        public bool TryGetCurrentAd(out Advert ad) {
            for (int i = 0; i < Components.Length; i++) {
                IComponent c = Components[i];
                if (c is IAdvertProvider ap && ap.HasCancellableAd) {
                    ad = ap.CurrentAdvert;
                    return true;
                }
            }
            ad = null;
            return false;
        }

        [IgnoreMember] public bool HasCancellableAd {
            get {
                D.Err("Reimplement entity.HasCancellableAd");
                return false;
            }
        }
        */

        /*
        [IgnoreMember] public bool HasCancellableAd {
            get {
                for (int i = 0; i < Components.Length; i++) {
                    IComponent c = Components[i];
                    if (c is IAdvertProvider ap && ap.HasCancellableAd) {
                        return true;
                    }
                }
                return false;
            }
        }
        [IgnoreMember] public Advert CurrentAdvert {
            get {
                for (int i = 0; i < Components.Length; i++) {
                    IComponent c = Components[i];
                    if (c is IAdvertProvider ap && ap.HasCancellableAd) {
                        return ap.CurrentAdvert;
                    }
                }
                return null;
            }
        }
        */

        public bool SetCarrier(Being c) {
            if (c == this) {
                D.Err("Trying to carry itself: {0}", c);
                return false;
            }
            if (c != null) {
                if (Carrier != null && Carrier != c) {
                    D.Err("Carrier already set on {0}: {1}. Want to set: {2}", this, Carrier, c);
                    return false;
                }
                if (NanobotCarrierHiveId != 0) {
                    D.Err("Entity is already carried by nanobots: {0}. Hive: {1}",
                        this, NanobotCarrierHiveId);
                    return false;
                }
                if (Pos.SqrDistance(c.PosIdx, PosIdx) > 4) {
                    D.Err("Cannot pick up entity, too far away! carrier pos: {0} Entity pos: {1} {2}",
                        Pos.String(c.PosIdx), Pos.String(PosIdx), c.AIAgent.CurrentAction);
                    return false;
                }
            }
            bool wasCarrying = Carrier != null;
            Carrier = c;
            if (wasCarrying && c == null) {
                Transmit(null, MsgIdH.CarryEnded);
                S?.Sys?.Info?.OnCarryChanged(this);
            } else if (!wasCarrying && c != null) {
                Transmit(null, MsgIdH.CarryStarted);
                S?.Sys?.Info?.OnCarryChanged(this);
            }
            return true;
        }
        public bool SetNanobotCarrier(int hiveEntityId) {
            if (hiveEntityId != 0) {
                if (Carrier != null || NanobotCarrierHiveId != 0) {
                    D.Err("Entity is already carried: {0}. Hive: {1}", this,
                        hiveEntityId);
                    return false;
                }
            }
            bool wasCarrying = IsCarriedByAny;
            NanobotCarrierHiveId = hiveEntityId;
            bool isCarrying = IsCarriedByAny;
            if (wasCarrying && !isCarrying) {
                Transmit(null, MsgIdH.CarryEnded);
                S?.Sys?.Info?.OnCarryChanged(this);
            } else if (!wasCarrying && isCarrying) {
                Transmit(null, MsgIdH.CarryStarted);
                S?.Sys?.Info?.OnCarryChanged(this);
            }
            return true;
        }
        public void InvokeAfterInitialize() {
            if (AfterInitialize != null) {
                try {
                    AfterInitialize();
                } catch (System.Exception ex) {
                    D.LogEx(ex, "Failure in entity.AfterInitialize for {0}",
                        this);
                }
                AfterInitialize = null;
            }
        }
        // Don't return parent definition here!
        [IgnoreMember]
        public Def Definition {
            set {
                if (value == null) {
                    D.Err("Trying to set null definition! {0}", this);
                    HasDefinition = false;
                } else {
                    DefinitionId = value.Id;
                    definition = value;
                    HasDefinition = true;
                }
            }
            get { return definition; }
        }

        public void UpdatePosIdx(int pos, int dx, int dy) {
            for (int i = Components.Length - 1; i >= 0; i--) {
                var comp = Components[i];
                comp.UpdatePosIdx(pos, dx, dy);
            }
        }

        public void Initialize(int id, GameState s) {
            D.Ass(id > 0, "Trying to initialize an entity with zero id! {0}",
                this);
            S = s;
            IsActive = true;
            Id = id;
            OriginalId = id;
        }

        public void Deactivate(long ticks) {
            IsActive = false;
            DeactivationTime = ticks;
            //// D.Log("Deactivating entity: {0}", Id);
        }

        public void HideAllInfo() {
            for (int i = 0; i < Components.Length; i++) {
                IComponent comp = Components[i];
                comp.HideAllInfo();
            }
        }

        ///<summary>DO NOT CALL THIS! Use S.Components.RemoveRuntime!</summary>
        public virtual bool RemoveComponent(IComponent comp) {
            var tempC = new List<IComponent>(Components);
            var countBefore = tempC.Count;
            tempC.Remove(comp);
            if (tempC.Count == countBefore) {
                D.Err("Tried to remove component {0} but not found! {1}", comp,
                    D.Dump(tempC));
                return false;
            }
            Components = tempC.ToArray();
            return true;
        }

        public virtual bool SetComponent(IComponent comp, int i, int totalComps) {
            if (Components == null) {
                Components = new IComponent[totalComps];
            } else {
                // Check for duplicates
                for (int i1 = 0; i1 < Components.Length; i1++) {
                    IComponent c = Components[i1];
                    if (c == null) { continue; }
                    var n = comp.NameHash;
                    if (c.NameHash == n) {
                        D.Err("Trying to add duplicate component on entity! {0} -> {1} ({2})",
                                definition.Id, comp.GetName(), comp.GetType());
                        if (ModRegistry.IsModded(this, out var mod)) {
                            ModRegistry.ShowModdedDefErrorPopup(definition, mod,
                                $"Component is declared more than once in <b>{definition.Id}.json</b>: {Texts.Accented(comp.GetName())} ({comp.GetType()})");
                        }
                        c.EntityId = 0;
                        return false;
                    }
                }
            }

            if (Components.Length < totalComps) {
                Array.Resize(ref Components, totalComps);
            }

            comp.EntityId = Id;
            var def = Definition;
            if (def != null) {
                comp.LayerId = def.LayerId;
            }

            if (comp is FlammableComp fl) { Flammable = fl; }
            if (comp is AudioSourceComp aud) { AudioSource = aud; }
            if (comp is LightSourceComp li) { Light = li; }
            Components[i] = comp;
            return true;
        }

        public bool AddComponent(IComponent comp) {
            if (Components == null) {
                return SetComponent(comp, 0, 1);
            } else {
                var cl = Components.Length;
                return SetComponent(comp, cl, cl + 1);
            }
        }
        public T GetComponent<T>(int nameHash) {
            if (Components == null) { return default; }
            for (int i = 0; i < Components.Length; i++) {
                IComponent c = Components[i];
                if (nameHash == c.NameHash && c is T tc) {
                    return tc;
                }
            }
            return default;
        }

        public T GetRequiredComponent<T>(IComponent requiredBy) {
            if (!TryGetComponent<T>(out var res)) {
                D.Err("{0} needs to have a {1} in the same Def! Please add {1} component to {2}",
                    requiredBy?.GetName(), typeof(T), DefinitionId);
            }
            return res;
        }

        public T GetComponent<T>() {
            if (Components == null) { return default; }
            for (int i = 0; i < Components.Length; i++) {
                IComponent c = Components[i];
                if (c is T tc) {
                    return tc;
                }
            }
            #if UNITY_EDITOR
            var type = typeof(T);
            if (!type.IsInterface && !typeof(IComponent).IsAssignableFrom(type)) {
                D.Err("Trying to get component of an unsupported type, neither an interface, nor IComponent: {0} (def: {1})",
                    type, definition);
            }
            #endif
            return default;
        }
        public bool TryGetComponent<T>(out T comp, Func<T, bool> check) {
            if (Components == null) {
                comp = default;
                return false;
            }
            for (int i = 0; i < Components.Length; i++) {
                IComponent c = Components[i];
                if (c is T tc && check(tc)) {
                    comp = tc;
                    return true;
                }
            }
            comp = default;
            return false;
        }
        public bool TryGetComponent<T>(out T comp) {
            if (Components == null) {
                comp = default;
                return false;
            }
            for (int i = 0; i < Components.Length; i++) {
                IComponent c = Components[i];
                if (c is T tc) {
                    comp = tc;
                    return true;
                }
            }
            comp = default;
            return false;
        }
        public bool TryGetComponent<T>(int nameHash, out T comp) {
            if (Components == null) {
                comp = default;
                return false;
            }
            for (int i = 0; i < Components.Length; i++) {
                IComponent c = Components[i];
                if (c.NameHash == nameHash && c is T tc) {
                    comp = tc;
                    return true;
                }
            }
            comp = default;
            return false;
        }
        public bool HasComponent<T>() {
            if (Components == null) { return false; }
            for (int i = Components.Length - 1; i >= 0; i--) {
                IComponent c = Components[i];
                if (c == null) { continue; }
                if (c is T) { return true; }
            }
            return false;
        }

        ///<summary>[Obsolete("Use HasComponent<T> if possible")]</summary>
        public bool HasComponent(int nameHash) {
            if (Components == null) { return false; }
            for (int i = Components.Length - 1; i >= 0; i--) {
                IComponent c = Components[i];
                if (c == null) { continue; }
                if (c.NameHash == nameHash) {
                    return true;
                }
            }
            return false;
        }
        public IComponent GetComponent(int nameHash) {
            if (Components == null) { return null; }
            for (int i = 0; i < Components.Length; i++) {
                IComponent c = Components[i];
                if (c == null) { continue; }
                if (c.NameHash == nameHash) {
                    return c;
                }
            }
            return null;
        }
        public virtual void AfterRemove() {
            if (Id == 0) {
                D.Err("Calling AfterRemove for entity with id 0! {0}", this);
                return;
            }
            //D.Warn("Removing entity: Id: {0}. Former Id: {1}", Id, FormerId);
            OriginalId = Id;
            Id = 0;
        }

        public override bool Equals(object obj) {
            return obj is Entity entity && OriginalId == entity.OriginalId;
        }

        public bool GenReplaceWith(GameState s, Entity other) {
            if (other.Definition.LayerId != Definition.LayerId) {
                D.Err("GenReplaceWith different layer id! {0} -> {1}",
                    DefinitionId, other.DefinitionId);
                return false;
            }
            if (Components == null || other.Components == null) {
                D.Err("Refusing to replace entity, components are null! {0}", this);
                return false;
            }
            if (Components.Length != other.Components.Length) {
                D.Err("Cannot gen replace with, component lengths don't match! {0} | {1}",
                    DefinitionId, other.DefinitionId);
                return false;
            }
            Caches.IntSet.Clear();
            foreach (var comp in other.Components) {
                Caches.IntSet.Add(comp.TypeHash);
            }
            foreach (var comp in Components) {
                if (!Caches.IntSet.Contains(comp.TypeHash)) {
                    D.Err("GenReplace: Our Component not found in other: {0}", comp.GetName());
                    return false;
                }
            }
            Caches.IntSet.Clear();
            foreach (var comp in Components) {
                Caches.IntSet.Add(comp.TypeHash);
            }
            foreach (var comp in other.Components) {
                if (!Caches.IntSet.Contains(comp.TypeHash)) {
                    D.Err("GenReplace: Other Component not found in our: {0}", comp.GetName());
                    return false;
                }
            }

            s.Map.UnregisterEntity(this);
            Definition = other.definition;
            s.Map.RegisterEntity(this);
            Transmit(null, MsgIdH.GenDefReplaced);
            return true;
        }

        public override int GetHashCode() {
            unchecked {
                if (OriginalId == 0) {
                    if (HasDefinition) {
                        D.Err("Trying to get hashcode for entity with OriginalId 0: {0}", this);
                    }
                    return base.GetHashCode();
                }
                return Hashes.Quick(Primes.Prime019, OriginalId);
            }
        }

        public override string ToString() {
            if (Id == OriginalId) {
                return string.Format("[{0}] {1} (Active: {2}). Components: {3}",
                    Id, DefinitionId, IsActive, Components?.Length);
            } else {
                return string.Format("[{0}(Orig: {1})] {2} (Active: {3}). Components: {4}",
                    Id, OriginalId, DefinitionId, IsActive, Components?.Length);
            }
        }

        public string InfoString() {
            var sb = new System.Text.StringBuilder();
            sb.AppendFormat("[{0}] {1} (Active: {2}). Components: \n",
                Id, DefinitionId, IsActive);
            if (Components != null) {
                for (int i = 0; i < Components.Length; i++) {
                    IComponent comp = (IComponent)Components[i];
                    if (comp == null)  {
                        sb.AppendLine(" * NULL COMPONENT!");
                    } else {
                        sb.AppendLine(comp.ToString());
                    }
                }
            }
            return sb.ToString();
        }

        public void CompactComponents(int numMissing) {
            Array.Resize(ref Components, Components.Length - numMissing);
        }

        public void Transmit(IComponent sender, int message) {
            var comps = Components;
            if (comps == null) { return; }
            var cl = comps.Length;
            for (int i = 0; i < cl; i++) {
                var comp = comps[i];
                if (comp == null) { return; }
                // Skip the sender from receiving a copy of the message
                if (object.ReferenceEquals(comp, sender)) { continue; }
                //if (comp.TypeHash == sender.TypeHash) { continue; }
                comp.Receive(sender, message);
            }
        }
    }
}
