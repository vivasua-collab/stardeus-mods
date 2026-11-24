using System;
using System.Text;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems.AI;
using Game.Systems.Creatures.Surgeries;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Items {
    public sealed class ObjReservation {
        public readonly ObjData Obj;
        public AIGoal Goal;
        // AIGoal ReservedForGoal { get; private set; }
        public SurgicalOperation Surgery;
        public uint LoadGoalId;
        public int LoadSurgeryId;
        public long CreatedAt;
        public bool IsBlank => Goal == null && Surgery == null
            && LoadGoalId == 0 && LoadSurgeryId == 0;

        public ObjReservation(ObjData data, long ticks) {
            Obj = data;
            CreatedAt = ticks;
        }

        public bool IsActive {
            get {
                if (Surgery != null) {
                    if (Surgery.State.Value == SurgicalOpState.Completed) {
                        return false;
                    }
                    if (Surgery.State.Value == SurgicalOpState.Canceled) {
                        return false;
                    }
                    if (Surgery.State.Value == SurgicalOpState.Failed) {
                        return false;
                    }
                    return true;
                }
                return AIGoal.IsActive(Goal);
            }
        }

        public bool IsFor(AIGoal goal) {
            if (LoadGoalId != 0) {
                if (goal.Id == LoadGoalId) {
                    Goal = goal;
                    LoadGoalId = 0;
                } else {
                    D.Err("Trying to reserve obj with different reserved goal id, also too early, goals not linked! {0} | {1} | {2}",
                        Obj, goal, this);
                    return false;
                }
            }
            if (Surgery != null && goal.TryGetVar(AIGoalVars.SurgeryId, out int sId)) {
                if (sId == Surgery.Id) {
                    return true;
                }
            }
            if (Goal != null && Goal == goal) {
                return true;
            }
            return IsBlank;
        }

        public override string ToString() {
            return $"ObjReservation[ObjId:{Obj.Id}|Goal:{LoadGoalId}:{Goal}|Surgery:{LoadSurgeryId}:{Surgery}]";
        }

        public bool IsForGoal() {
            return Goal != null && Surgery == null;
        }
        public void Load(GameState s) {
            if (LoadGoalId != 0) {
                var goal = s.Query.FindAIGoal.Ask(LoadGoalId, true);
                if (goal == null) {
                    D.Err("Failed loading reserved goal for obj with goal id: {0}: {1}",
                        LoadGoalId, this);
                }
                Goal = goal;
                LoadGoalId = 0;
            }
            if (LoadSurgeryId != 0) {
                var op = s.Query.GetSurgicalOp.Ask(LoadSurgeryId);
                if (op == null) {
                    D.Err("Failed loading reserved surgery for obj with surgery id: {0}: {1}",
                        LoadSurgeryId, this);
                }
                Surgery = op;
                LoadSurgeryId = 0;
            }
        }

        public bool Validate(GameState s) {
            if (Goal != null && !AIGoal.IsActive(Goal) && Surgery == null) {
                D.Err("ObjData is reserved for an inactive goal! unreserving: {0} | {1}",
                    this, Goal);
                return false;
            }
            return true;
        }

        public string Tooltip {
            get {
                Caches.StringBuilder.Clear();
                AppendTooltip(Caches.StringBuilder);
                return Caches.StringBuilder.ToString();
            }
        }

        public void AppendTooltip(StringBuilder sb) {
            if (Goal != null) {
                if (Goal.Agent != null) {
                    sb.AppendLine(T.ReservedFor_V1.F(
                        $"{Goal.Name} ({Goal.Agent.BeingData.Mind.LinkToShortName})"));
                } else {
                    sb.AppendLine(T.ReservedFor_V1.F(Goal.Name));
                }
            } else if (Surgery != null) {
                sb.AppendLine(T.ReservedFor_V1.F($"{Surgery.TargetBeing.Mind.LinkToShortName} {GlyphMap.ArrowRight} {Surgery.Tooltip(accent: false)}"));
            }
        }

        public bool CancelGoal(string why) {
            if (Goal == null) {
                // D.Err("Trying to cancel reservation goal, but there is no goal on this reservation. Reason: {0}. Res: {1}", why, this);
                return false;
            }
            // a surgery reservation can outlive the goal attached to it
            if (!AIGoal.IsActive(Goal) && !AIGoal.IsAvailable(Goal)) {
                return false;
            }
            AIGoal.Cancel(Goal, G.Ticks, why);
            return true;
        }
    }
}
