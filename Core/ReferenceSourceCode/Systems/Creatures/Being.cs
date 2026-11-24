using Game.Components;
using Game.Visuals;
using Game.Rendering;
using System;
using UnityEngine;
using MessagePack;
using KL.Algos;
using KL.Grid;
using Game.Constants;
using Game.Systems.Stats;
using Game.Systems.Conditions;
using System.Collections.Generic;
using Game.Systems.Creatures;
using Game.Systems.AI;
using Game.Systems.AI.Combat;
using Game.Systems.BodyParts;
using Game.Systems.Motion;
using Game.CodeGen;
using Game.Systems.Turrets;
using Game.Systems.Drama;
using KL.QuadTree;

namespace Game.Data {
    [MessagePackObject]
    public class Being : Entity, INamedEntity, IQuadable {
        [IgnoreMember] public BeingData Data { get; private set; }
        [IgnoreMember] public MotionControl Motion;
        [IgnoreMember] public Species Species => Data.Species;
        [IgnoreMember] public ConditionsComp Conditions;
        // [IgnoreMember] public TransformComp Transform;
        [IgnoreMember] public BeingGraphicsComp Graphics;
        //[IgnoreMember] public NeedsComp Needs;
        [IgnoreMember] public AIAgentComp AIAgent;
        [IgnoreMember] public MovableComp Movable;
        // belongs in Data
        [IgnoreMember] public override Vector2 Size => Graphics.SelectableSize;
        // belongs in data
        [IgnoreMember] public PersonaComp Persona;
        // belongs in data
        [IgnoreMember] public BeingLooksComp Looks;
        [IgnoreMember] public BeingNameTextComp Name;
        [IgnoreMember] public BondsComp Bonds;
        // Rethink with new AI
        [IgnoreMember] public bool IsDead => Data.HasStateFlag(BeingState.Dead);
        [IgnoreMember] public bool IsAlive => Data.HasStateFlag(BeingState.Alive);
        [IgnoreMember] public bool IsHidden => Data.HasStateFlag(BeingState.Hidden);
        [IgnoreMember] public bool CanFight => (Data.StateFlags & BeingState.Alive)
            == BeingState.Alive && !Data.Body.IsIncapacitated;
        [IgnoreMember] public bool HasData => Data != null;
        [IgnoreMember] public int SpriteSetId => Id;
        [IgnoreMember] public long LastHiveMindTick;
        [IgnoreMember] public int QueueIndex { get; set; }
        [IgnoreMember] public float QueuePriority { get; set; }
        [IgnoreMember] public override string CategoryTitle => Data.CategoryTitle;
        [IgnoreMember] public string EntityName => Data.Mind.LinkToShortName;
        [IgnoreMember] public readonly List<ILooksFilter> LooksFilters = new();

        public override bool SetComponent(IComponent comp, int i, int totalComps) {
            if (comp is ILooksFilter f) {
                LooksFilters.Add(f);
            }
            if (!base.SetComponent(comp, i, totalComps)) {
                return false;
            }
            else if (comp is BeingGraphicsComp gr) { Graphics = gr; }
            else if (comp is ConditionsComp cn) { Conditions = cn; }
            else if (comp is MovableComp mv) { Movable = mv; }
            else if (comp is PersonaComp pe) { Persona = pe; }
            else if (comp is BondsComp bon) { Bonds = bon; }
            else if (comp is BeingNameTextComp nam) { Name = nam; }
            else if (comp is AIAgentComp ai) { AIAgent = ai; }
            else if (comp is BeingLooksComp lo) { Looks = lo; }
            return true;
        }

        public override string ToString() {
            if (Data?.Mind?.Name != null) {
                var pos = Pos.String(PosIdx);
                return string.Format("[{0}|{5}@{4}] {1} ({2},A:{3})",
                    Id, Data.Mind.Name.DisplayName, Species.Id,
                        IsActive, pos, Data.Id);
            } else {
                return $"Being[{Id}] No Persona!";
            }
        }

        public void SetData(BeingData beingData) {
            Data = beingData;
            Transmit(null, MsgIdH.BeingDataWasSet);
        }

        public Vector2 GetPosition() {
            return SelectPosition;
        }
    }
}
