using System;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Systems.Mental;
using Game.Systems.Sleep;
using Game.Systems.Stats;
using Game.Systems.Traits;

namespace Game.Systems.AI {
    public sealed class AIActionSleepInBed : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionSleepInBed());
        }
        public const string ActId = "SleepInBed";
        public static int ActIdH;
        private static StatModifier sleepPressureReduction;
        private AIActionSleepInBed() : base(ActId, () => T.ActSleep) {
            Outcomes = AIState.With(AIVarsH.TriedSleepingInBed, true);
            MarkAsQuiet();
            WithRequiredNeed(NeedIdH.Sleep);
            WithRequiredAbilities(AbilityIdH.Sleep);
            ActIdH = IdH;
        }

        private bool IsUsingBed(AIAgentComp agent, out bool makeshift) {
            makeshift = agent.BB.Bool3NotSaved;
            return agent.BB.Bool1NotSaved;
        }


        private void SetUsingBed(AIAgentComp agent, bool makeshift) {
            // bool1 saved is for knowing that the being body may have been hidden
            agent.BB.Bool1 = true;
            //Bool2NotSaved is used by CanHeal!!!!
            agent.BB.Bool3NotSaved = makeshift;
            // the bool1notsaved is for actually putting the being in to the slot
            agent.BB.Bool1NotSaved = true;
        }
        public override AIActionResult Execute(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            // resolve bed
            if (!ResolveBed(S, agent, out var bed, out var slot, false)) {
                return OnSlotUnavailable(agent, goal, ticks);
            }

            if (agent.Motion.IsBusy) {
                if (!slot.CanUse(agent.Being)) {
                    // forget current bed
                    agent.BB.Object1 = null;
                    // someone else is using it already?
                    agent.Motion.Abort(T.NavFailTargetUnavailable);

                }
                return InProgress;
            }

            if (CheckDone(S, agent, goal)) {
                if (IsUsingBed(agent, out _) || bed == null) {
                    if (slot != null) {
                        slot.RemoveUser(agent.Being);
                        // don't try to remove twice
                        agent.BB.Bool1 = false;
                    }
                    TryWalkAway(agent);
                    goal.WithWorld(AIVarsH.TriedSleepingInBed, true);
                    return Success;
                } else {
                    // we loaded? can't stop the action without getting into bed first
                    D.Warn("Sleep is done, but we apparently aren't using the bed! Bed: {0} | {1} | {2}",
                        bed, agent, goal);
                }
            }

            if (IsUsingBed(agent, out _)) {
                return OnUseSlot(agent, goal, slot, ticks);
            }

            int pos = slot.InPort;
            if (Pos.SqrDistance(agent.PosIdx, pos) < 3) {
                return OnUseSlot(agent, goal, slot, ticks);
            } else {
                if (agent.BB.Bool1) {
                    // FIXME: Reliably happens due to save/load in damageddeviceexploded story test in batch
                    D.ErrSoft("Agent is too far from the bed, removing from bed! {0} | {1}",
                        Pos.StringFromTo(agent.PosIdx, pos), agent);
                    slot.RemoveUser(agent.Being);
                    agent.BB.Bool1 = false;
                }
            }

            if (TryGoToOnce(agent, pos, false)) {
                if (sleepPressureReduction == null) {
                    sleepPressureReduction = StatModifier.AddPercent(
                        -0.9f, T.VerbGoingTo_V1.F(T.NounBed));
                }
                agent.BeingData.StatsBeing.SleepPressureRate.TryAddModifier(
                    sleepPressureReduction);
                return InProgress;
            }

            if (slot != null) {
                // Can't reach owned bed, try temporary
                if (slot.Owner == agent.BeingData) {
                    // fallback
                    agent.BB.Object1 = null;
                    agent.BB.Object2 = null;
                    if (ResolveBed(S, agent, out var bed2, out var slot2, true)) {
                        agent.BB.ClearMoveTarget();
                        return InProgress;
                    }
                }
                return FailedNavToTarget(agent, slot.Comp.Tile.PosIdx);
            } else {
                return Failed(T.AdRejTargetUnavailable);
            }
        }

        private static bool ResolveBed(GameState S, AIAgentComp agent,
                out BedComp bed, out BedSlot slot, bool isTempFallback) {
            if (agent.BB.Object1 is not BedComp bed1) {
                if (isTempFallback) {
                    bed = S.Query.FindTemporaryBed.Ask(agent.BeingData, true);
                } else {
                    bed = S.Query.FindBed.Ask(agent.BeingData, true);
                }
                if (bed == null) {
                    slot = null;
                    return false;
                } else {
                    agent.BB.Object1 = bed;
                }
            } else {
                bed = bed1;
            }
            // resolve slot
            if (agent.BB.Object2 is not BedSlot slot1) {
                slot = bed.SlotFor(agent.BeingData);
                if (slot == null) {
                    return false;
                } else {
                    agent.BB.Object2 = slot;
                    agent.BB.Bool2NotSaved = agent.BeingData.Body.CanHeal
                        || agent.BeingData.Body.IsIncapacitated;
                }
            } else {
                slot = slot1;
            }

            return true;
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            // we may set Object1 / 2 through another action before this
            // agent.BB.Object1 = null;
            // agent.BB.Object2 = null;
            agent.BB.ClearMoveTarget();
            // was in bed
            agent.BB.Bool1 = false;
            agent.BB.Bool1NotSaved = false;
            // bool2 = can heal
            agent.BB.Bool3NotSaved = false;
            if (agent.BB.Object2 != null) {
                agent.BB.Bool2NotSaved = agent.BeingData.Body.CanHeal
                    || agent.BeingData.Body.IsIncapacitated;
            } else {
                agent.BB.Bool2NotSaved = false;
            }
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            if (sleepPressureReduction != null) {
                agent.BeingData.StatsBeing.SleepPressureRate.RemoveModifier(
                    sleepPressureReduction, false);
            }
            // body was hidden
            if (agent.BB.Bool1) {
                agent.BB.Bool1 = false;
                // was in bed
                if (agent.BB.Object2 is not BedSlot slot) {
                    ResolveBed(agent.S, agent, out var bed, out slot, false);
                    if (agent.BB.Object2 is not BedSlot slot2) {
                        // fallback
                        ResolveBed(agent.S, agent, out bed, out slot, true);
                    }
                }
                slot?.RemoveUser(agent.Being);
            }
            bool sleptOutside = false;
            if (agent.S.Sys.Areas.IsOutside(agent.PosIdx) && !agent.S.Sys.Path.IsDoor(agent.PosIdx)) {
                sleptOutside = true;
                agent.AddMentalEffect(MentalEffect.CreateWithIcon(agent.PosIdx,
                    IconId.CSleep, ticks, Consts.TicksPerDay, T.SleepEffOutdoors, -12));
            }
            if (IsUsingBed(agent, out var isMakeshift)) {
                SleepMoods.EndLastNight(agent.BeingData.Mind.Health, ticks);
                if (!sleptOutside) {
                    CheckBedroom(agent, ticks, isMakeshift);
                }
                agent.BeingData.StatsBeing.BloodRegenRate.RemoveModifier(inBedRestMod_BloodRegen);
                agent.BeingData.StatsBeing.WoundHealRate.RemoveModifier(inBedRestMod_WoundHeal);
                agent.BeingData.StatsBeing.BloodClotRate.RemoveModifier(inBedRestMod_BloodClot);
                if (agent.BeingData.HasStateFlag(Creatures.BeingState.Alive)) {
                    var sleepVal = agent.BeingData.StatsBeing.Sleep.Value;
                    float needVal = sleepVal;
                    if (sleepVal > 0.9f) {
                        ApplyBedBonus(agent, ticks, isMakeshift);
                        needVal *= 0.7f;
                    } else if (sleepVal < 0.6f) {
                        ApplySleepInterrupted(agent, ticks);
                        needVal *= 0.6f;
                    } else {
                        ApplyPrematureWakeup(agent, ticks);
                        needVal *= 0.3f;
                    }
                    AINeedSleep.MakeThirsty(agent, ticks, needVal);
                }
            }

            #if !PRODUCTION_BUILD
            // validate for bugs
            var body = agent.BeingData.Looks.Get(LookPartTypeDef.Body);
            if (body != null) {
                if (body.Color.a == 0f) {
                    D.Err("Body hidden after deactivating sleep in bed! {0} | {1} | {2}", agent.BB.Object2, goal, agent);
                }
            }
            var clothing = agent.BeingData.Looks.Get(LookPartTypeDef.Clothing);
            if (clothing != null) {
                if (clothing.Color.a == 0f) {
                    D.Err("Clothing hidden after deactivating sleep in bed! {0} | {1} | {2}", agent.BB.Object2, goal, agent);
                }
            }
            #endif

            agent.BB.ClearMoveTarget();
            agent.BB.Bool1NotSaved = false;
            agent.BB.Bool2NotSaved = false;
            agent.BB.Bool3NotSaved = false;
            agent.BB.Object1 = null;
            agent.BB.Object2 = null;
            agent.BB.ClearParticles();
            agent.Being.Graphics.SetVertical(true);
            agent.Being.Graphics.SetOffset(Consts.V2Zero);
            agent.BB.Bool1 = false;
        }
        private void CheckBedroom(AIAgentComp agent, long ticks, bool isMakeshift) {
            if (!agent.BeingData.Mind.Species.IsHumanoid) {
                return;
            }
            if (agent.BeingData.Mind.Health == null) {
                return;
            }
            // humanoids want to have individual rooms and won't be happy to sleep
            // in the same room as industrial equipment
            var s = agent.S;
            var roomId = s.Sys.Areas.At(agent.PosIdx);
            var indRes = s.Query.HasIndustrialDevicesInArea.Ask(roomId);
            sbyte effect;
            string reason;
            int dur = Consts.TicksPer4Hours;
            if (indRes.hasIndustrial) {
                if (indRes.hasNoisy) {
                    // worst case = biggest debuff
                    effect = -7;
                    dur = Consts.TicksPer4Hours;
                    reason = T.SleepEffIndustrialNoisy;
                } else {
                    // bad case = big debuff
                    effect = -5;
                    reason = T.SleepEffIndustrial;
                }
            } else if (!isMakeshift) {
                var numBeds = s.Query.NumBedsInArea.Ask(roomId, agent.BeingData, 2);
                if (numBeds > 1) {
                    // dormatory
                    // not ideal case = small debuff
                    effect = -2;
                    reason = T.SleepEffDorm;
                } else if (s.Sys.Areas.PersonalQuartersAreaOf(agent.BeingData) == roomId) {
                    // own room, by the one personal quarters rule: a room alone but too
                    // small is cramped, not personal quarters (STA-1777)
                    // best case = small buff, kept until the next wake-up: a well built ship
                    // earns a mood that lasts the day (SanityBalance.md, "Lasting positives")
                    effect = +2;
                    dur = Consts.TicksPerDay;
                    reason = T.SleepEffOwnRoom;
                } else {
                    effect = 0;
                    reason = null;
                }
            } else {
                effect = 0;
                reason = null;
            }
            if (effect != 0) {
                agent.AddMentalEffect(MentalEffect.CreateWithIcon(agent.PosIdx,
                    IconId.CSleep, ticks, dur, reason, effect)
                    .WithTooltip(QuartersTip(agent.BeingData)));
            }

            var room = s.Sys.Areas.AreaById(roomId);
            var roomSize = room.Count;
            var isMinimalist = agent.BeingData.HasTrait(TraitIdH.Minimalist);
            var minSize = isMinimalist ? AreaSys.MinimalistRoomMinSize
                : Tunables.Room_PersonalQuartersMinSize;
            var cozyMaxSize = isMinimalist ? AreaSys.MinimalistCozyRoomMaxSize
                : Tunables.Room_PersonalQuartersMaxSize;
            var tooBigSize = isMinimalist ? AreaSys.MinimalistRoomTooBigSize
                : Tunables.Room_TooBigStartingSize;
            effect = 0;
            reason = null;
            dur = Consts.TicksPer4Hours;
            if (roomSize < minSize) {
                // cramped
                effect = -5;
                reason = T.SleepEffRoomTooSmall;
            } else if (roomSize > tooBigSize) {
                // too big
                effect = -5;
                reason = T.SleepEffRoomTooLarge;
            } else if (roomSize <= cozyMaxSize) {
                // cozy room
                var adm = s.Query.NumAdmirableInArea.Ask(roomId, 1);
                if (adm > 0) {
                    effect = +3;
                    dur = Consts.TicksPerDay;
                    reason = T.SleepEffCozyRoom;
                }
            }

            if (effect != 0) {
                agent.AddMentalEffect(MentalEffect.CreateWithIcon(agent.PosIdx,
                    IconId.CSleep, ticks, dur, reason, effect)
                    .WithTooltip($"{T.Size}: {roomSize}\n{QuartersTip(agent.BeingData)}"));
            }
        }

        // The sleep moods name a room kind without saying what makes one (STA-1840): the
        // personal quarters rule spells out the single bed, no industry, and the sizes
        private static string QuartersTip(BeingData being) {
            AreaSys.PersonalQuartersSizeRange(being, out var min, out var max);
            var tip = T.QuestStepHasPersonalQuartersDesc2_V2.F(min, max);
            // The sizes are the Minimalist's own. The trait is named here and not in the mood
            // reason, which SleepMoods matches by its exact text
            if (being.HasTrait(TraitIdH.Minimalist)) {
                var trait = TraitDef.Get(TraitIdH.Minimalist);
                if (trait != null) {
                    tip = $"{Texts.Accented(trait.NameT)}\n{tip}";
                }
            }
            return tip;
        }


        private bool CheckDone(GameState S, AIAgentComp agent, AIGoal goal) {
            // getting up would leave a bedridden being on the floor next to the bed
            if (agent.BeingData.Body.IsIncapacitated || agent.BeingData.IsBedridden) {
                return false;
            }
            if (S.Sys.Firex.IsAnythingOnFire) {
                var closestOnFire = S.Sys.Firex.GetClosestReachableFireFor(
                    agent.Being, out var distSqr);
                if (closestOnFire != null && distSqr < 100 &&
                        S.Sys.Areas.IsSameArea(closestOnFire.PosIdx, agent.PosIdx)) {
                    D.Warn("Waking up due to fire: {0} -> {1}",
                        agent, closestOnFire);
                    ApplySleepInterrupted(agent, G.Ticks);
                    return true;
                }
            }
            // was injured at start of rest
            if (agent.BB.Bool2NotSaved) {
                if (agent.BeingData.Body.CanHeal) {
                    if (agent.TicksInCurrentAction < Consts.TicksPer24Hours
                            || agent.Motion.IsImmobilized) {
                        return false;
                    }
                    #if !PRODUCTION_BUILD
                    // D.ErrSoft("Someone who can heal was sleeping for 24 hours! {0} | {1}",
                    //     agent, goal);
                    // EntityUtils.DebugPauseFocusOn(agent.Being);
                    // return false;
                    #endif
                }
            } else if (agent.BeingData.StatsBeing.Sleep.Value <= 0.98f) {
                if (agent.TicksInCurrentAction < Consts.TicksPer12Hours) {
                    return false;
                }
                #if !PRODUCTION_BUILD
                D.ErrSoft("Someone was sleeping for 12 hours! {0} | {1} | {2}",
                    agent, goal, agent.BeingData.StatsBeing.Sleep);
                // EntityUtils.DebugPauseFocusOn(agent.Being);
                // return false;
                #endif
            }
            goal.WithWorld(AIVarsH.HasSlept, true);
            return true;
        }

        private StatModifier inBedRestMod_BloodClot;
        private StatModifier inBedRestMod_BloodRegen;
        private StatModifier inBedRestMod_WoundHeal;

        private void EnsureModifiers() {
            if (inBedRestMod_BloodClot != null) { return; }
            inBedRestMod_BloodClot = StatModifier.AddPercent(
                Tunables.BedRest_BloodClotRate_AddPct, T.VerbResting);
            inBedRestMod_BloodRegen = StatModifier.AddPercent(
                Tunables.BedRest_BloodRegenRate_AddPct, T.VerbResting);
            inBedRestMod_WoundHeal = StatModifier.Add(
                Tunables.BedRest_WoundHealRate_AddPctPerHour / Consts.TicksPerHour, T.VerbResting);
        }

        private AIActionResult OnUseSlot(AIAgentComp agent, AIGoal goal, BedSlot slot, long ticks) {
            var data = agent.BeingData;
            var speed = data.StatsBeing.SleepRate.Value;
            data.StatsBeing.Sleep.AddToBaseWithCompoundLimit(speed);

            if (!slot.Comp.IsUsable) {
                // bed was relocated?
                D.Warn("Bed unusable! {0}->{1}", slot.Comp.Tile, agent);
                return Failed($"{T.RequiredDeviceIsNotAvailable}: {slot.Comp.Tile.Definition.CodexLink}");
            }

            // random chance is for changing position while sleeping
            var chance = 0.001f;
            var body = data.Body;

            if (!IsUsingBed(agent, out _)
                    || (!body.IsIncapacitated && Rng.UChance(chance))) {
                return PutAgentInBed(agent, slot);
            }

            if ((body.CanHeal || body.IsIncapacitated) && agent.Being.Goal == null && data.IsFriendly) {
                if (agent.S.Sys.Slots.HasCompatibleFor(SlotDesignationH.Healing,
                        agent.BeingData, freeOnly: true)) {
                    if (body.IsIncapacitated) {
                        // once in a while, let's see if an autodoc is available, and if so
                        // let's create a task to relocate us to the autodoc
                        var colonyG = AIGoal.Create(JobTypeIdH.Hauling,
                            T.MoveToPos_V1.F(T.ObjectsDevicesAutodoc), agent.Being.Conditions, ticks)
                            .WithIcon(IconId.CHealth)
                            .WithoutRetry()
                            .WithWorldToDesired(AIVarsH.IsCarriedToHealingDevice, false, true)
                            .WithRequiredAbilities(AbilityIdH.Carry)
                            .WithPriority(4)
                            .WriteInitialState();
                        agent.Being.TrySetGoal(colonyG);
                    }
                }
            }

            return InProgress;
        }

        private AIActionResult PutAgentInBed(AIAgentComp agent, BedSlot slot) {
            if (Pos.SqrDistance(agent.PosIdx, slot.InPort) > 4) {
                D.Err("Trying to put agent in bed from too far away! {0}",
                    Pos.StringFromTo(agent.PosIdx, slot.InPort));
                return FailedNavToTarget(agent, slot.InPort);
            }
            var notInBed = !IsUsingBed(agent, out _);
            var worker = agent.Being;
            if (worker.Species.IsAnimal) {
                worker.Graphics.SetFacing(Rng.UFrom(Facing.Types));
                worker.Graphics.SetRotation(Rng.URange(-45f, 45f), true);
            } else {
                worker.Graphics.SetFacing(slot.Rotation);
                worker.Graphics.SetRotation(Rng.URange(-10f, 10f), true);
            }
            if (notInBed) {
                if (!slot.PutUser(worker)) {
                    // someone else in the bed?
                    D.Warn("Someone else was in the bed!?");
                    return Failed(T.AdRejInterrupted);
                }
                EnsureModifiers();
                agent.BeingData.StatsBeing.BloodRegenRate.TryAddModifier(inBedRestMod_BloodRegen);
                agent.BeingData.StatsBeing.BloodClotRate.TryAddModifier(inBedRestMod_BloodClot);
                agent.BeingData.StatsBeing.WoundHealRate.TryAddModifier(inBedRestMod_WoundHeal);
                string fx;
                if (agent.BB.Bool2NotSaved) {
                    fx = "HealFX";
                } else {
                    fx = "SleepFX";
                }
                if (fx != null && agent.BB.Particles == null) {
                    var data = new ParticlesSys.Data() {
                        Owner = worker,
                        IsEmitOnly = true,
                        EmitChance = 1,
                        PrefabName = fx,
                        Play = true,
                    };
                    agent.BB.SetParticles(data);
                    worker.AIAgent.HideAllInfo();
                }
                SetUsingBed(agent, slot.IsTemporary);
            }
            return InProgress;
        }

        private AIActionResult OnSlotUnavailable(AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.BeingData.StatsBeing.Sleep.Value < 0.1f) {
                // we are too deprived, let's fall back to the sleep on floor
                // by allowing the next action to execute
                goal.WithWorld(AIVarsH.TriedSleepingInBed, true);
                return Success;
            }
            // we're not sleepy enough to sleep on the floor, let's fail
            ApplyNoBedPenalty(agent, ticks);
            return Failed(T.NowhereToSleep);
        }


        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            // Enemies don't get to nap in our beds; guests do (see Guests.md).
            if (!agent.BeingData.IsFriendly) {
                return 0f;
            }
            // never go to sleep when someone is targeting you!
            if (agent.CombatUnit.EnemiesTargetingThis > 0) {
                return 0f;
            }
            return (agent.BeingData.StatsBeing.Sleep.Value + 0.001f) * 10f;
        }

        private void ApplySleepInterrupted(AIAgentComp agent, long ticks) {
            AddMentalEffect(agent, ticks, Consts.TicksPer8Hours, T.SleepInterrupted, -8);
        }
        public static void ApplyPrematureWakeup(AIAgentComp agent, long ticks) {
            AddMentalEffect(agent, ticks, Consts.TicksPer8Hours, T.SleptTooShort, -5);
        }

        public static void ApplyNoBedPenalty(AIAgentComp agent, long ticks) {
            AddMentalEffect(agent, ticks, Consts.TicksPer8Hours, T.NowhereToSleep, -15);
        }

        private void ApplyBedBonus(AIAgentComp agent, long ticks, bool isMakeshift) {
            if (isMakeshift) {
                AddMentalEffect(agent, ticks, Consts.TicksPer4Hours, T.UncomfortableSleep, -3);
            } else {
                AddMentalEffect(agent, ticks, Consts.TicksPer4Hours, T.SleptInBed, 3);
            }
        }
    }
}
