using System;
using System.Buffers;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.AI.Combat;
using Game.Systems.Creatures;
using Game.Utils;
using Game.CodeGen;
using KL.Randomness;
using KL.Utils;
using KL.Collections;
using System.Collections.Generic;
using Game.Systems.AI;
using System.Runtime.CompilerServices;
using Game.Systems.Conditions;
using UnityEngine;
using Game.Systems.Story;
using Game.Systems.Notify;
using Game.Systems.UIUX;
using Game.Systems.Creatures.Surgeries;
using Game.UI;

namespace Game.Systems.BodyParts {
    public sealed class Body {
        public BeingData Owner;
        public BodyPartSlot[] Slots;
        private BodyState bState;
        // Runtime only: the "incapacitated" notification, removed again on recovery or death
        private NotificationHandle incapNotification;
        public bool HasIncapacitationAlert => incapNotification.IsValid;
        // Runtime only: when the last alert went out, against flapping (IncapacitationAlert)
        private long lastIncapAlertTick;
        // public BodyState State => bState;
        public B<BodyState> StateB = new B<BodyState>(BodyState.None);
        public BeingSex Sex;
        /// <summary>
        /// When someone actually died
        /// </summary>
        public long DeathTick { get; private set; }

        public bool AddToBodyState(BodyState flags, bool updateBinding) {
            return SetBodyState(bState | flags, updateBinding);
        }

        public bool RemoveFromBodyState(BodyState flags, bool updateBinding) {
            return SetBodyState(bState & ~flags, updateBinding);
        }

        public bool SetBodyState(BodyState state, bool updateBinding) {
            if (state != bState) {
                bState = state;
                if (updateBinding) {
                    StateB.Set(state);
                }
                return true;
            }
            return false;
        }

        public void UpdateBodyStateBinding() {
            StateB.Set(bState);
        }

        public string DeathCause;
        // Runtime only: the death story event goes out from inside the fatal wound, before the
        // code that dealt it has named the death, so SetDeathCause fills its cause in afterwards
        private StoryEventData deathEvent;
        /// <summary>
        /// At what age someone should die
        /// </summary>
        public float DeathAge;
        public long BirthTick;
        public string SkinColorId;
        public string HairColorId;
        public string EyeColorLId;
        public string EyeColorRId;
        public float MaxLibido;
        /// <summary>
        /// Actual age out of stasis
        /// </summary>
        public float Age;
        public float AgePercent => Age / Owner.Species.LifeExpectancyYears;
        public BodyPartSlot BodySlot;
        public BodyPartSlot MindSlot;
        public BodyPartSlot ImplantSlot;
        public BodyPartSlot[] EyeSlots;
        public BodyPartSlot[] MobilizerSlots;
        public BodyFlags Flags;
        public bool IsAlive => (bState & BodyState.Alive) == BodyState.Alive;
        public bool IsInjured => (bState & BodyState.Injured) == BodyState.Injured;
        /// <summary>
        /// Unhealthy means that the body has missing or damaged body parts.
        /// </summary>
        public bool IsUnhealthy => (bState & BodyState.Unhealthy) == BodyState.Unhealthy;
        public bool IsRecovering => (bState & BodyState.Recovering) == BodyState.Recovering;
        /// <summary>
        /// Healthy means that there are no injuries that can be healed. Body could still have missing or irreparable body parts.
        /// </summary>
        public bool IsHealthy => (bState & (BodyState.Injured | BodyState.Recovering)) == BodyState.None;
        public bool CanHeal => !IsHealthy && HasHealableWounds;
        public bool IsIncapacitated => (bState & BodyState.Incapacitated) == BodyState.Incapacitated;
        public bool IsForcedToObey {
            get {
                if (Owner == null) {
                    return false;
                }
                if (Owner.Species?.IdH == SpeciesIdH.Mindjar) {
                    return true;
                }
                if (BetaWavesSys.IsUnderBetaWaves(Owner)) {
                    return true;
                }
                return HasWorkingObeyChip;
            }
        }
        public bool HasWorkingObeyChip =>
            ImplantSlot?.InstalledDef?.Def?.IdH == DefIdsH.ObjPartsImplantObeyChip
                && ImplantSlot.IsUsableWithParent;
        public bool IsMoodRegulated {
            get {
                if (Owner == null) {
                    return false;
                }
                if (ImplantSlot?.InstalledDef?.Def?.IdH == DefIdsH.ObjPartsImplantMoodRegulator) {
                    return ImplantSlot.IsUsableWithParent;
                }
                return false;
            }
        }
        public bool IsAbleToWork => (bState & BodyState.Alive) == BodyState.Alive
            && (bState & BodyState.Incapacitated) != BodyState.Incapacitated;
        public readonly List<AttackMethod> AttackMethods = new();
        // Bumped whenever AttackMethods changes, so a combat unit can re-pick its best attack
        // (a weapon equipped or dropped mid-fight, a job toggled)
        // (runtime state, not saved: SkipDumpInstance keeps it out of save/load comparisons)
        [SkipDumpInstance] public int AttackMethodsVersion;
        private readonly List<AttackMethod> disabledAttackMethods = new();
        public void ValidateAttackMethods() {
            AttackMethodsVersion++;
            bool canDoMelee = Owner.Mind.Jobs.CanDo(JobTypeIdH.Melee);
            bool canShoot = Owner.Mind.Jobs.CanDo(JobTypeIdH.Shooting);
            // remove invalid
            for (int i = AttackMethods.Count - 1; i >= 0; i--) {
                AttackMethod meth = AttackMethods[i];
                if (meth.IsMelee && !canDoMelee) {
                    // D.Warn("Removing melee attack method (agent can't do melee!): {0} | {1}", meth, this);
                    AttackMethods.RemoveAt(i);
                    disabledAttackMethods.Add(meth);
                } else if (!meth.IsMelee && !canShoot) {
                    // D.Warn("Removing ranged attack method (agent can't do shooting!): {0} | {1}", meth, this);
                    AttackMethods.RemoveAt(i);
                    disabledAttackMethods.Add(meth);
                }
            }
            // readd valid removed methods
            for (int i = disabledAttackMethods.Count - 1; i >= 0; i--) {
                AttackMethod meth = disabledAttackMethods[i];
                if (meth.IsMelee && canDoMelee) {
                    AttackMethods.Add(meth);
                    disabledAttackMethods.RemoveAt(i);
                } else if (!meth.IsMelee && canShoot) {
                    AttackMethods.Add(meth);
                    disabledAttackMethods.RemoveAt(i);
                }
            }
        }

        public void AddAttackMethod(AttackMethodData amd,
                IAttackMethodProvider provider) {
            if (provider == null || Owner?.Mind?.Jobs == null) {
                return;
            }
            var meth = AttackMethod.CreateFor(provider, amd);
            if (meth.IsMelee) {
                // D.Warn("Adding melee attack method for {0}", Data);
                if (!Owner.Mind.Jobs.CanDo(JobTypeIdH.Melee)) {
                    disabledAttackMethods.Add(meth);
                    return;
                }
            } else {
                if (!Owner.Mind.Jobs.CanDo(JobTypeIdH.Shooting)) {
                    disabledAttackMethods.Add(meth);
                    return;
                }
            }
            AttackMethods.Add(meth);
            AttackMethodsVersion++;
        }

        public void RemoveAttackMethodsFor(IAttackMethodProvider provider) {
            AttackMethodsVersion++;
            for (int i = AttackMethods.Count - 1; i >= 0; i--) {
                AttackMethod meth = AttackMethods[i];
                if (meth.Provider == provider) {
                    AttackMethods.RemoveAt(i);
                }
            }
        }

        public int NumHealableWounds {
            get {
                int count = 0;
                foreach (var slot in Slots) {
                    if (slot.IsPartRemoved) { continue; }
                    if (slot.State.Wounds == null) { continue; }
                    for (int i = slot.State.Wounds.Count - 1; i >= 0; i--) {
                        Wound wound = slot.State.Wounds[i];
                        if (wound.CanHeal(Owner, false)) {
                            count++;
                        }
                    }
                }
                return count;
            }
        }
        public bool HasHealableWounds {
            get {
                if (Owner.Species.IsBiological) {
                    // can still stop bleeding / regenerate blood
                    if (Owner.StatsBeing.BloodAmount.Value
                            < Owner.StatsBeing.BloodCapacity.Value) {
                        return true;
                    }
                }
                foreach (var slot in Slots) {
                    if (slot.IsPartRemoved) { continue; }
                    if (slot.State.Wounds == null) { continue; }
                    for (int i = slot.State.Wounds.Count - 1; i >= 0; i--) {
                        Wound wound = slot.State.Wounds[i];
                        if (wound.CanHeal(Owner, false)) {
                            return true;
                        }
                    }
                }
                return false;
            }
        }
        public float InjuryPercent {
            get {
                int numHealthy = 0;
                int numTotal = 0;
                foreach (var slot in Slots) {
                    if (slot.Type.IsOptional) { continue; }
                    numTotal++;
                    if (!slot.IsPartRemoved && slot.State.WoundSeverity == Severity.Irrelevant) {
                        numHealthy++;
                    }
                }
                if (numTotal == 0) {
                    return 1f;
                }
                return 1f - (numHealthy / (float) numTotal);
            }
        }
        private readonly Dictionary<int, float> bestEssentialConditions = new();

        // How close the being is to going down: 1 unhurt, 0 at incapacitation. The worst of
        // its vital parts (each paired type by its better slot) and, for biologicals, its blood
        // above the incapacitation threshold. Combat retreat thresholds read this, because
        // InjuryPercent counts wounded slots: a human bleeding out read about 4% injured.
        public float VitalityPercent {
            get {
                bestEssentialConditions.Clear();
                for (int i = Slots.Length - 1; i >= 0; i--) {
                    var slot = Slots[i];
                    if (!slot.IsEssential) { continue; }
                    var cond = slot.IsPartRemoved ? 0f : slot.State.Condition;
                    if (!bestEssentialConditions.TryGetValue(slot.Type.IdH, out var best)
                            || cond > best) {
                        bestEssentialConditions[slot.Type.IdH] = cond;
                    }
                }
                var vitality = 1f;
                foreach (var cond in bestEssentialConditions.Values) {
                    vitality = Mathf.Min(vitality, cond);
                }
                if (Owner.Species.IsBiological) {
                    var cap = Owner.StatsBeing.BloodCapacity.Value;
                    if (cap > 0f) {
                        var blood = Mathf.InverseLerp(Tunables.BloodIncapacitatedPct, 1f,
                            Owner.StatsBeing.BloodAmount.Value / cap);
                        vitality = Mathf.Min(vitality, blood);
                    }
                }
                return vitality;
            }
        }

        public bool HasMissingOrDestroyedParts {
            get {
                foreach (var slot in Slots) {
                    if (slot.IsPartRemoved) {
                        if (!slot.Type.IsOptional) {
                            return true;
                        }
                    } else if (slot.State.IsDestroyed) {
                        return true;
                    }
                }
                return false;
            }
        }

        public int NumMissingBodyParts {
            get {
                int missing = 0;
                foreach (var slot in Slots) {
                    if (slot.IsPartRemoved && !slot.Type.IsOptional) {
                        missing++;
                    }
                }
                return missing;
            }
        }
        public bool HasRemovableParts {
            get {
                foreach (var slot in Slots) {
                    if (!slot.IsPartRemoved && slot.CanRemovePart(false)) {
                        return true;
                    }
                }
                return false;
            }
        }
        public bool IsEmpty {
            get {
                foreach (var slot in Slots) {
                    if (slot.Type.IsRemovable && !slot.IsPartRemoved && !slot.Type.IsOptional) {
                        return false;
                    }
                }
                return true;
            }
        }

        private readonly Dictionary<int, bool> essentialPartsBeyondRepair = new();

        /// <summary>
        /// True when the being is incapacitated and can never recover on its own or via
        /// surgery: every slot of some essential (non-mind-holding) body part type is
        /// <see cref="BodyPartSlot.IsBeyondRepair"/>. Mind-holding / required-for-alive slots
        /// are excluded - a permanently wrecked one already kills the being via
        /// <see cref="CheckCondition"/> instead of leaving it stuck incapacitated.
        /// </summary>
        public bool IsBeyondRepair {
            get {
                if (!IsAlive || !IsIncapacitated) { return false; }
                essentialPartsBeyondRepair.Clear();
                for (int i = Slots.Length - 1; i >= 0; i--) {
                    var slot = Slots[i];
                    if (slot.IsRequiredForBeingAlive || !slot.IsEssential) { continue; }
                    if (essentialPartsBeyondRepair.TryGetValue(slot.Type.IdH, out var beyond)
                            && !beyond) {
                        continue;
                    }
                    essentialPartsBeyondRepair[slot.Type.IdH] = slot.IsBeyondRepair;
                }
                foreach (var beyond in essentialPartsBeyondRepair.Values) {
                    if (beyond) { return true; }
                }
                return false;
            }
        }

        /// <summary>
        /// The first beyond-repair essential slot, for warning tooltips. Only meaningful when
        /// <see cref="IsBeyondRepair"/> is true; not on the hot path so it just re-scans.
        /// </summary>
        public BodyPartSlot FirstBeyondRepairSlot {
            get {
                if (!IsBeyondRepair) { return null; }
                foreach (var slot in Slots) {
                    if (!slot.IsRequiredForBeingAlive && slot.IsEssential && slot.IsBeyondRepair) {
                        return slot;
                    }
                }
                return null;
            }
        }

        public bool CheckWounds(out bool canHeal, out bool isCrippled) {
            bool hasWounds = false;
            canHeal = false;
            isCrippled = false;
            if (Owner.Species.IsBiological) {
                // can still stop bleeding / regenerate blood
                if (Owner.StatsBeing.BloodAmount.Value
                        < Owner.StatsBeing.BloodCapacity.Value) {
                    canHeal = true;
                }
            }

            foreach (var slot in Slots) {
                if (canHeal && isCrippled && hasWounds) {
                    return hasWounds;
                }
                if (slot.IsPartRemoved) {
                    if (!slot.Type.IsOptional) {
                        isCrippled = true;
                    }
                    continue;
                }
                if (slot.State?.Wounds == null) { continue; }
                for (int i = slot.State.Wounds.Count - 1; i >= 0; i--) {
                    Wound wound = slot.State.Wounds[i];
                    hasWounds = true;
                    if (!canHeal && wound.CanHeal(Owner, true)) {
                        canHeal = true;
                    } if (!isCrippled && !wound.CanHeal(Owner, false)) {
                        isCrippled = true;
                    }
                }
            }
            return hasWounds;
        }

        public bool HasSurgeryFacilities(out int deviceDef) {
            var sys = The.R.Sys;
            deviceDef = 0;
            if (Owner.Species.IsBiological) {
                if (sys.Slots.HasCompatibleFor(SlotDesignationH.BodyModSpot_Organic, Owner)) {
                    return true;
                }
                deviceDef = DefIdsH.ObjectsDevicesSurgeryHub;
            } else {
                if (sys.Slots.HasCompatibleFor(SlotDesignationH.BodyModSpot_Robotic, Owner)) {
                    return true;
                }
                deviceDef = DefIdsH.ObjectsDevicesAssemblyHub;
            }
            return false;
        }

        public void Bleed(float radius) {
            var b = Owner.Being;
            // Only the blood on the floor: a being without an entity (stashed in a capsule,
            // hidden, or destroyed while still bleeding) keeps its conditions and can be hurt or
            // killed, there is just no floor under it. That used to log an error every few ticks.
            if (b == null || !b.IsActive) {
                return;
            }
            var sp = Owner.Species;
            var bloodSprite = sp.BloodSpriteRoot;
            if (bloodSprite == null) {
                D.Err("Species {0} does not have blood sprite!", sp.Id);
                bloodSprite = GraphicsId.DirtBloodSplash01.Replace("01", "");
                sp.BloodSpriteRoot = bloodSprite;
            } else {
                if (b != null && b.IsActive) {
                    var bleedPos = b.Position + Rng.UInsideUnitCircle().normalized * radius;
                    if (EntityUtils.IsWithinBounds(bleedPos)) {
                        b.S.Sys.Dirt.Add(DirtData.Create(bleedPos,
                            Images.RandomMatching(bloodSprite)));
                    }
                }
            }
        }
        public void AddExcrementsOnFloor() {
            Vector2 pos = Owner.Being?.Position ?? Owner.LastPosition.Exact;
            // D.Warn("Dropping poop at {0} {1}", pos, Owner.Name);
            // FIXME maybe not only human waste?
            The.R.Sys.Dirt.Add(DirtData.Create(pos, Images.RandomMatching(
                GraphicsId.DirtHumanWaste01, 2), Rng.URange(0.75f, 1.25f)));
        }
        public void SetAge(long ticksNow, int ageTicks) {
            Age = (uint) (ageTicks / (float) Consts.TicksPerYear);
            BirthTick = ticksNow - ageTicks;
        }
        private void NotifyDeath(GameState s, Being b) {
            if (!Ready.Gen) {
                // D.Warn("Dead during gen: {0}", Being);
                return;
            }
            s.Sig.BeingDied.Send(Owner);
            if (!Owner.IsFriendly) { return; }

            var name = Owner.Mind.Name;
            var title = T.EvtDeathTitle_V1.F(name.FullName);
            deathEvent = StoryEventData.CreateCustom(IconId.CSkull, title,
                DeathEventDesc(T.DetailsUnknown));
            s.Sig.AddCustomStoryEvent.Send(deathEvent, false);

            if (b != null) {
                s.Sys.Log.AddLine(b, Texts.Red(title), true);
            } else {
                s.Sys.Log.AddLine(Texts.Red(title));
            }
            s.Sig.ShowFanfare.Send(Texts.Red(T.EvtDeathTitle_V1.F(
                Texts.LinkToBeing(Owner, name.FullName))));

            Notification.BuildFor(Owner, null, title,
                Consts.TicksPerDay, NotificationType.Warning)
                    .Group("b_dead", T.Dead, IconId.CSkull)
                .Show();
        }

        private string DeathEventDesc(string cause) {
            return T.EvtDeathDesc_V4.F(Owner.Species.Name,
                Owner.Mind.Name.FullNameOrNameless, (int) Age, cause);
        }

        public void SetDeathCause(string cause, long ticks) {
            DeathCause = cause;
            // A cause recorded on a later tick (disposing of the corpse) is not what killed them
            if (deathEvent != null && ticks == DeathTick) {
                deathEvent.LogEntry.Desc = DeathEventDesc(cause);
            }
        }

        public static Body BlankFor(BeingData owner) {
            var body = new Body();
            body.Owner = owner;
            var slotTypes = owner.Species.BodyPartSlotTypes;
            var pool = ArrayPool<BodyPartSlot>.Shared;
            var tempParts = pool.Rent(slotTypes.Length);
            int cnt = 0;
            for (int i = 0; i < slotTypes.Length; i++) {
                string st = slotTypes[i];
                var slot = BodyPartSlot.NewFor(st, body);
                if (slot != null) {
                    tempParts[cnt++] = slot;
                }
            }
            body.Slots = new BodyPartSlot[cnt];
            Array.Copy(tempParts, body.Slots, cnt);

            // resolve attached parts (O(n^2))
            for (int i = body.Slots.Length - 1; i >= 0; i--) {
                var slot = body.Slots[i];
                // if (slot.Type.Flags.HasFlag(BodyPartFlags.Internal)) { continue; }
                cnt = 0;
                for (int i2 = body.Slots.Length - 1; i2 >= 0; i2--) {
                    if (i == i2) { continue; }
                    var slot2 = body.Slots[i2];
                    // if (slot2.Type.Flags.HasFlag(BodyPartFlags.Internal)) { continue; }
                    if (body.IsAttachedTo(slot2, slot)) {
                        tempParts[cnt++] = slot2;
                        slot2.AttachedToPart = slot;
                    }
                }
                if (cnt > 0) {
                    slot.AttachedParts = new BodyPartSlot[cnt];
                    Array.Copy(tempParts, slot.AttachedParts, cnt);
                }
            }

            body.LinkSlots();

            pool.Return(tempParts, true);
            return body;
        }

        private bool IsAttachedTo(BodyPartSlot part, BodyPartSlot parent) {
            // perhaps the attached part is not contained in the body? then we will fallback
            // to Body. Use case: robot has no head, but eye is attached to head.
            var partParent = part.Type.AttachedToPart;
            bool hasPartParent = false;
            foreach (var slot in Slots) {
                if (slot.Type == partParent) {
                    // we have a part of this type, but this is not attached to it
                    hasPartParent = true;
                    break;
                }
            }
            // we checked all the slots and there is no part at all that we
            // are supposed to be attached to. In this case let's say we want
            // to be attached to the body
            if (!hasPartParent) {
                // Fallback
                partParent = BodyPartType.Get("Body");
            }
            if (partParent == parent.Type
                    && (parent.SideH == 0 || (parent.SideH == part.SideH))) {
                return true;
            }
            return false;
        }

        public void RemoveDisability(bool updateBinding) {
            var b = Owner.Being;
            // D.Warn("Removing disability for: {0} | {1}", this, b);
            RemoveFromBodyState(BodyState.Incapacitated, updateBinding);
            IncapacitationAlert.Clear(ref incapNotification);
            if (b == null) { return; }
            b.AIAgent.CombatUnit.SetState(CombatUnitState.Unaware);
            bool wantsVertical = true;
            if (b.AIAgent.CurrentAction != null) {
                if (b.AIAgent.CurrentAction.IdH == AIActionSleepInBed.ActIdH) {
                    wantsVertical = false;
                }
            }
            if (wantsVertical) {
                b.Graphics.SetVertical(true);
            }
            b.Looks.TogglePosFilters(true, true);
            b.Persona.HideInfoIcon();
            b.Motion.Immobilize(ImmobilizeReasonIdH.Disabled, false);
            if (b.S.Query.IsSelected.Ask(b)) {
                UIUtils.RefreshSelectedEntityUI();
            }
            if (!Owner.IsFriendly) {
                // a raider that comes to after the raid leaves; next tick, out of the body
                // check that is running now
                var s = b.S;
                s.Clock.OnNextTick(() => s.Sys.Combat.OnHostileRecovered(b));
            }
            // We'll try to start combat in more organic ways
            // if (!Owner.IsPlayer && !b.Motion.IsImmobilized
            //         && !b.S.Sys.Combat.IsHostileCombatInProgress) {
            //     D.Warn("Disability removed, let's go fight! {0} | {1}",
            //         Owner, this);
            //     b.S.Sys.Combat.StartRaidBy(b);
            // }
        }

        public bool Incapacitate(string icon,
                int immobilizeReasonIdH, string reason, bool isLoading) {
            // D.Warn("Incapacitating: {0} | {1} | {2}", this, Owner, reason);
            if (!isLoading) {
                if ((bState & BodyState.Incapacitated) == BodyState.Incapacitated) {
                    return false;
                }
                if (!Owner.HasStateFlag(BeingState.Alive)) { return false; }
            }
            if ((bState & BodyState.Alive) == BodyState.Alive) {
                AddToBodyState(BodyState.Incapacitated, !isLoading);
            }
            var b = Owner.Being;
            if (b == null) {
                return true;
            }
            if (b.AIAgent.Goal != null) {
                var actIdH = b.AIAgent.CurrentAction?.IdH ?? 0;
                // don't interrupt healing task when incapacitating
                if (actIdH != AIActionUseHealingDevice.ActIdH
                        && actIdH != AIActionSleepInBed.ActIdH) {
                    AIGoal.Interrupt(b.AIAgent.Goal, G.Ticks);
                }
            }
            b.AIAgent.CombatUnit.SetState(CombatUnitState.Incapacitated);
            b.Graphics.SetVertical(false);
            b.Looks.TogglePosFilters(false, true);
            b.Persona.ShowInfoIcon(icon, reason);
            if (!isLoading) {
                b.Motion.Fail(T.BeingStateIncapacitated);
            }
            if (b.AIAgent.DirectControl.IsUnderDirectControl) {
                b.AIAgent.DirectControl.SetDirectControl(false);
            }
            b.Motion.Immobilize(immobilizeReasonIdH, true);
            if (!isLoading && Owner.HasStateFlag(BeingState.Alive) && Ready.Gen) {
                // show this on next frame so it won't show damage after incapacitation
                if (G.ClockSpeed < ClockSpeed.Unlimited) {
                    Coroutines.OnNextFrame(() => {
                        b.S.Sys?.Log.AddLine(b, Texts.Red(reason));
                    });
                } else {
                    b.S.Sys.Log.AddLine(b, Texts.Red(reason));
                }
                IncapacitationAlert.Show(b.S, Owner, reason, ref incapNotification,
                    ref lastIncapAlertTick);
            }
            Owner.UpdateAvatarStatus();
            if (b.S.Query.IsSelected.Ask(b)) {
                UIUtils.RefreshSelectedEntityUI();
            }
            return true;
        }

        private void CleanKill(bool notify) {
            D.Log("Killing: {0}", this);
            Being b = Owner.Being;
            var s = b?.S ?? The.R.State;
            if (!GameState.IsCurrent(s)) {
                D.Err("Killing a being in non-current state! {0}", Owner);
                return;
            }
            if (Owner.Species.IsHumanoid && Owner.IsFriendly) {
                s.Situation.HumanDeaths++;
            }
            DeathTick = G.Ticks;
            // Death has its own notification
            IncapacitationAlert.Clear(ref incapNotification);
            Owner.StateFlags &= ~BeingState.Alive;
            Owner.StateFlags |= BeingState.Dead;
            RemoveFromBodyState(BodyState.Alive, notify);
            // if (Owner.IsPlayer) {
                // S.Situation.AddTension(1f, "Death");
            // }
            NotifyDeath(s, b);
            Owner.UI?.ResetBio();
            Owner.Looks.Avatar?.Build(Owner);
            Owner.UpdateAvatarStatus();
            if (b == null) {
                // No entity means no ConditionsComp to hear the HealthWentToZero transmitted
                // below, so without this a condition would go on ticking on the corpse - a
                // sleeper killed in their pod kept taking organ wounds for days after death.
                Owner.DeactivateConditionsOnDeath();
                return;
            }
            BeingUtils.UpdateFlagsFor(s, Owner);
            b.Graphics.SetVertical(false);
            b.Looks.TogglePosFilters(false, true);
            if (b.Graphics.IsCarrying()) {
                if (b.Graphics.Carrying is Obj o) {
                    o.Pile?.ReaddToInventoryIfNeeded();
                }
                b.Graphics.DropIfCarrying();
            }
            b.AIAgent.CombatUnit.SetState(CombatUnitState.Incapacitated);
            if (b.AIAgent.Goal != null) {
                AIGoal.Interrupt(b.AIAgent.Goal, G.Ticks);
            }
            b.AIAgent.RemoveAgent();
            if (b.AIAgent.DirectControl.IsUnderDirectControl) {
                b.AIAgent.DirectControl.SetDirectControl(false);
            }
            b.AIAgent.Transmit(b.Persona, MsgIdH.HealthWentToZero);
            s.Beings.Alive.Remove(b);
            s.Sig.BeingChanged.Send(b);
            b.Motion.Fail(T.Dead);
            b.Motion.Immobilize(ImmobilizeReasonIdH.Dead, true);
            b.HideAllInfo();
            b.Persona.ShowInfoIcon(IconId.CSkullTransparent, T.InfoBeingDead);
        }

        private readonly Dictionary<int, int> essentialPartCounts = new();
        private readonly Dictionary<int, int> criticalPartCounts = new();

        public void CheckCondition(bool isLoading) {
            int numDamagedParts = 0;
            int missingParts = 0;
            int totalParts = 0;
            var wasAlive = IsAlive;
            // Not for a being with no entity: Beings.Destroy marks a living being removed from
            // the map (escaped raider, burnt up, a destroyed duelist) Dead, and its bleeding keeps
            // ticking after death. "Fixing" it back to Alive with no Being made BeingData.Tick
            // log an error every tick, forever, for every such being.
            if (wasAlive && Owner.HasStateFlag(BeingState.Dead) && Owner.Being != null
                    && ((Flags & BodyFlags.Missing) != BodyFlags.Missing)
                    && !Owner.HasStateFlag(BeingState.Hidden)) {
                D.Err("Dead being with alive body! Fixing: {0} | {1}", this, Owner);
                Owner.StateFlags &= ~BeingState.Dead;
                Owner.StateFlags |= BeingState.Alive;
                Owner.Being?.Motion.Immobilize(ImmobilizeReasonIdH.Dead, false);
            }
            // I had && !isLoading here. Why?
            var wasIncapacitated = IsIncapacitated;// && !isLoading;
            var wasInjured = IsInjured;
            var wasRecovering = IsRecovering;
            var shouldBeInjured = false;
            var shouldBeAlive = true;
            var wasUnhealthy = IsUnhealthy;
            var shouldBeUnhealthy = false;
            var shouldBeIncapacitated = false;
            var stateBefore = bState;
            var healthyParts = 0;
            var isBio = Owner.Species.IsBiological;

            // lost more than half of blood
            if (isBio && Owner.StatsBeing.BloodAmount.Value
                    < Owner.StatsBeing.BloodCapacity.Value * Tunables.BloodIncapacitatedPct) {
                shouldBeIncapacitated = true;
            }

            Severity maxSev = Severity.Irrelevant;
            essentialPartCounts.Clear();
            criticalPartCounts.Clear();
            for (int i = Slots.Length - 1; i >= 0; i--) {
                BodyPartSlot slot = Slots[i];
                if (slot.IsRequiredForBeingAlive) {
                    if (!criticalPartCounts.TryGetValue(slot.Type.IdH, out var cnt)) {
                        criticalPartCounts[slot.Type.IdH] = 0;
                        cnt = 0;
                    }
                    if (slot.IsPartRemoved || slot.State.IsDestroyed) {
                        // no count increase!
                    } else {
                        criticalPartCounts[slot.Type.IdH] = cnt + 1;
                    }
                }
                // Not an else: a vital part (a human's Body, Head, Heart) also goes down at
                // Critical damage, as a robot's frame does, instead of fighting on until it dies
                if (slot.IsEssential) {
                    if (!essentialPartCounts.TryGetValue(slot.Type.IdH, out var cnt)) {
                        essentialPartCounts[slot.Type.IdH] = 0;
                        cnt = 0;
                    }
                    if (slot.IsPartRemoved || slot.State.HasCriticalDamage) {
                        // no count increase!
                    } else {
                        essentialPartCounts[slot.Type.IdH] = cnt + 1;
                    }
                }
                var isOpt = slot.Type.IsOptional;
                if (!isOpt) {
                    totalParts++;
                }
                if (slot.IsPartRemoved) {
                    if (!isOpt) {
                        missingParts++;
                    }
                } else {
                    if (!Arrays.IsEmpty(slot.State.Wounds)) {
                        // a destroyed part whose pain settled stays unhealthy, not injured,
                        // or ConditionInjured would be recreated right after it ends
                        if (!shouldBeInjured && WoundPain.HasUnsettledWound(slot.State)) {
                            shouldBeInjured = true;
                        }
                        var wSev = slot.State.WoundSeverity;
                        maxSev = Severities.Bigger(maxSev, wSev);
                    }
                    if (slot.HasModerateDamage) {
                        numDamagedParts++;
                    } else {
                        if (!isOpt) {
                            healthyParts++;
                        }
                    }
                }
            }

            if (numDamagedParts > 0 || missingParts > 0) {
                var adjTotalParts = Mathf.Min(totalParts, 10);
                // The backstop behind pain shock (PainShock), for beings that feel too little
                // pain to go down from it (a Pain Inhibitor, a trait). Set high enough that pain
                // downs everyone else first: it was a third of the parts, at most 3, which downed
                // most beings before pain did.
                int maxDamagedParts = Mathf.FloorToInt(
                    adjTotalParts * Tunables.IncapDamagedPartsShare);
                if (maxDamagedParts < 1) {
                    maxDamagedParts = 1;
                } else if (maxDamagedParts > (int) Tunables.IncapMaxDamagedParts) {
                    maxDamagedParts = (int) Tunables.IncapMaxDamagedParts;
                }
                int maxMissingParts = Mathf.FloorToInt(adjTotalParts / 2f);
                if (maxMissingParts < 2) {
                    maxMissingParts = 2;
                }

                // D.Warn("Max disabled / missing parts for {0}: {1}/{2}",
                //     Owner.Species.Name, maxDisabledParts, maxMissingParts);

                if (numDamagedParts > maxDamagedParts || missingParts > maxMissingParts) {
                    shouldBeIncapacitated = true;
                }
            }

            if (maxSev > Severity.Irrelevant || missingParts > 0) {
                shouldBeUnhealthy = true;
            }

            if (shouldBeInjured) {
                AddToBodyState(BodyState.Injured, false);
                // The condition, not the body state flag, is what heals wounds. Deriving it
                // from the wound list on every call rather than only on the
                // !wasInjured -> shouldBeInjured edge means the two can no longer drift apart
                // permanently - a being whose condition was dropped elsewhere while wounds
                // remained (BeingUtils.DestroyEntity used to wipe conditions on desertion)
                // would otherwise look "already handled" forever, since the flag never
                // clears while wounds exist. Invincible bodies are skipped because
                // ConditionInjured.Tick removes itself for them, which would loop.
                int condId = isBio
                    ? ConditionIdH.Injured : ConditionIdH.Damaged;
                if (!Owner.HasCondition(condId) && !HasFlag(BodyFlags.Invincible)) {
                    if (maxSev < Severity.Mild) {
                        D.Err("Should be injured but max severity is: {0}", maxSev);
                        maxSev = Severity.Mild;
                    }
                    // allowInStasis: this is the body keeping track of its own wounds, not a
                    // decision to afflict anyone. A sleeper is wounded by their array's
                    // stability (ManifestSys.HarmHour) and the wounds still have to be
                    // tracked so they heal once they are woken.
                    ConditionState.CreateFor(The.R.State, condId,
                        Owner, maxSev, true, allowInStasis: true);
                }
            } else if (wasInjured) {
                RemoveFromBodyState(BodyState.Injured, false);
            }

            if (wasUnhealthy && !shouldBeUnhealthy) {
                RemoveFromBodyState(BodyState.Unhealthy, false);
            } else if (!wasUnhealthy && shouldBeUnhealthy) {
                AddToBodyState(BodyState.Unhealthy, false);
            }

            if (!shouldBeIncapacitated) {
                foreach (var c in Owner.Conditions) {
                    if (c.Value.Condition.Incapacitates) {
                        shouldBeIncapacitated = true;
                    }
                }
                // check essential body parts counts
                if (!shouldBeIncapacitated) {
                    foreach (var bpc in essentialPartCounts.Values) {
                        if (bpc == 0) {
                            // we have a missing body part!
                            shouldBeIncapacitated = true;
                            break;
                        }
                    }
                }
            }

            if (shouldBeAlive) {
                // check critical body parts counts
                foreach (var bpc in criticalPartCounts.Values) {
                    if (bpc == 0) {
                        // we have a missing critical body part!
                        shouldBeAlive = false;
                        break;
                    }
                }
            }

            if (Owner.Species.IsBiological) {
                var shouldBeRecovering = shouldBeAlive && !shouldBeInjured
                    && !shouldBeIncapacitated
                    && Owner.StatsBeing.BloodAmount.Value
                        < Owner.StatsBeing.BloodCapacity.Value;

                if (!wasRecovering && shouldBeRecovering) {
                    AddToBodyState(BodyState.Recovering, false);
                } else if (wasRecovering && !shouldBeRecovering) {
                    RemoveFromBodyState(BodyState.Recovering, false);
                }
            }

            bool needRecreateBeing = false;

            if (wasAlive) {
                // perhaps kill?
                if (!shouldBeAlive) {
                    TryDropWeapon();
                    CleanKill(false);
                }
            } else {
                // perhaps resurrect?
                var b = Owner.Being;
                if (shouldBeAlive) {
                    AddToBodyState(BodyState.Alive, false);
                    Owner.StateFlags |= BeingState.Alive;
                    Owner.StateFlags &= ~BeingState.Dead;
                    Owner.Looks.Avatar?.Build(Owner);
                    Owner.UpdateAvatarStatus();

                    Owner.Safety ??= new BeingSafety(Owner.Being.S, Owner);
                    if (Owner.Species.IsBiological) {
                        // Resurrection does not put blood back - it only guarantees waking
                        // up just above the lethal threshold. A being that bled out revives
                        // incapacitated instead of fully healed. See ConditionBleeding.
                        var minBlood = Owner.StatsBeing.BloodCapacity.Value * Tunables.BloodDeadBodyPct;
                        if (Owner.StatsBeing.BloodAmount.Value < minBlood) {
                            Owner.StatsBeing.BloodAmount.UpdateBase(minBlood);
                        }
                    }
                    RestoreConditionsOnResurrect();
                    if (b != null) {
                        b.Motion.Immobilize(ImmobilizeReasonIdH.Dead, false);
                        if (!isLoading) {
                            needRecreateBeing = true;
                        }
                    }
                } else {
                    // dead and should remain dead
                    if (isLoading) {
                        b?.AIAgent.RemoveAgent();
                    }
                }
            }

            if (shouldBeAlive) {
                if (shouldBeIncapacitated) {
                    if (!wasIncapacitated || isLoading) {
                        var icon = IconId.CIncapacitated;
                        if (isLoading) {
                            DropWeaponNextTick();
                        } else {
                            TryDropWeapon();
                        }
                        Incapacitate(icon, ImmobilizeReasonIdH.Disabled,
                            T.BeingStateIncapacitated, isLoading);
                        Owner.Being?.Looks.Rebuild(true, true);
                        if (!wasIncapacitated && !isLoading) {
                            Owner.Being?.S.Sig.BeingIncapacitated.Send(Owner);
                        }
                    } else {
                        // for modifying body parts, extra check
                        // in case we were incapacitated but weren't immobilized
                        var b = Owner.Being;
                        if (b != null && !b.Motion.IsImmobilized) {
                            b.Motion.Immobilize(ImmobilizeReasonIdH.Disabled, true);
                        }
                    }
                } else {
                    if (wasIncapacitated) {
                        RemoveDisability(false);
                        Owner.Being?.Looks.Rebuild(true, true);
                    }
                }
            }

            UpdateBodyStateBinding();

            Owner.AfterAbilitiesChanged();

            if (needRecreateBeing) {
                BeingUtils.Recreate(Owner);
            } else if (stateBefore != bState) {
                Owner.Being?.Transmit(null, MsgIdH.BodyConditionChanged);
            }

        }

        // A being can come into the world already down (back from an expedition with a
        // Critical vital wound) while its graphics are not built yet, and unequipping rebuilds
        // its looks, which threw. The drop waits a tick.
        private void DropWeaponNextTick() {
            var b = Owner.Being;
            if (b == null) { return; }
            b.S.Clock.OnNextTick(() => {
                if (Owner.Being == b && b.IsActive && IsIncapacitated) {
                    TryDropWeapon();
                }
            });
        }

        private void TryDropWeapon() {
            if (Owner.Inventory != null) {
                var b = Owner.Being;
                if (b != null) {
                    // The weapon slot policy decides whether the dropped weapon stays
                    // forbidden. Resolve it before unequipping, while the slot is still
                    // the one we are asking about.
                    bool forbid = Tunables.AutoForbidDroppedWeapons;
                    if (Owner.Inventory.Equipment.TryGetValue(InventorySlotIdH.Weapon,
                            out var weaponSlot)) {
                        var policy = b.S.Query.EquipmentPolicyFor.Ask(Owner, weaponSlot.Type);
                        if (policy != null) {
                            forbid = policy.ForbidWhenDropped;
                        }
                    }
                    // drop the weapon when you're going down
                    if (Owner.Inventory.TryUnequip(b.S, InventorySlotIdH.Weapon, out var obj)
                            && obj != null
                            && obj.TryGetComponent<StorableObjComp>(out var storable)) {
                        storable.IsForbidden = forbid;
                    }
                }
            }
        }

        public bool TryGetSlot(int slotIdH, out BodyPartSlot slot) {
            for (int i = Slots.Length - 1; i >= 0; i--) {
                BodyPartSlot s = Slots[i];
                if (s.IdH == slotIdH) {
                    slot = s;
                    return true;
                }
            }
            slot = null;
            return false;
        }

        /// <summary>
        /// [Obsolete("Use idH version")]
        /// </summary>
        public bool TryGetSlot(string slotId, out BodyPartSlot slot) {
            return TryGetSlot(Hashes.S(slotId), out slot);
        }

        public void AfterLoad() {
            foreach (var bps in Slots) {
                bps.AfterLoad();
            }
            LinkSlots();
            // hide the stats we don't have body parts for
            foreach (var stat in Owner.StatsBeing.AllStats) {
                if (!stat.IsUnused && stat.RequiredBodyPartId != 0
                        && !HasBodyPart(stat.RequiredBodyPartId)) {
                    stat.MarkUnused();
                }
            }
            // sanity check
            if (IsAlive) {
                foreach (var cond in Owner.Conditions) {
                    if (cond.Value.IsInactive) {
                        D.Err("Being is alive, but condition is inactive. Reenabling: {0} -> {1}", cond, this);
                        cond.Value.Flags &= ~ConditionFlags.Inactive;
                    }
                }
            }
        }

        private void LinkSlots() {
            var eyeSlots = new FrugalArray<BodyPartSlot>();
            var mobilizerSlots = new FrugalArray<BodyPartSlot>();
            foreach (var bps in Slots) {
                if (bps.Type.IdH == BodyPartSlotIdH.Body) {
                    BodySlot = bps;
                } else if (bps.HoldsMind) {
                    MindSlot = bps;
                } else if (bps.Type.IdH == BodyPartSlotIdH.Implant) {
                    ImplantSlot = bps;
                } else if (bps.Type.IdH == BodyPartSlotIdH.Mobilizer) {
                    mobilizerSlots.Add(bps);
                } else if (bps.Type.IdH == BodyPartSlotIdH.Eye) {
                    eyeSlots.Add(bps);
                }
            }
            if (eyeSlots.Length > 0) {
                EyeSlots = eyeSlots.ToArray();
            }
            if (mobilizerSlots.Length > 0) {
                MobilizerSlots = mobilizerSlots.ToArray();
            }
        }

        public bool HasBodyPart(int bodyPartIdH) {
            for (int i = Slots.Length - 1; i >= 0; i--) {
                BodyPartSlot s = Slots[i];
                if (s.Type.IdH == bodyPartIdH) {
                    return true;
                }
            }
            return false;
        }

        public bool HasSlot(string bodySlotId) {
            for (int i = Slots.Length - 1; i >= 0; i--) {
                BodyPartSlot s = Slots[i];
                if (s.Id == bodySlotId) {
                    return true;
                }
            }
            return false;
        }

        public override string ToString() {
            return $"Body[{bState}|{Flags}|Parts:{Slots.Length}]";
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasFlag(BodyFlags f) {
            return (Flags & f) == f;
        }

        public void SetDeathTick(long ticks) {
            if (BirthTick > ticks) {
                D.Err("Trying to set death tick to less than birth tick! {0} < {1}",
                    ticks, BirthTick);
                BirthTick = ticks - Consts.TicksPerYear;
            }
            DeathTick = ticks;
        }
        private void RestoreConditionsOnResurrect() {
            Caches.IntList.Clear();
            var conditions = Owner.Conditions;
            foreach (var c in conditions) {
                if (c.Value.Flags.HasFlag(ConditionFlags.RemoveOnResurrect)) {
                    D.Warn("Will remove condition after resurrect: {0}",
                        c.Value.Condition.NameT);
                    Caches.IntList.Add(c.Key);
                } else {
                    c.Value.Flags &= ~ConditionFlags.Inactive;
                }
            }
            foreach (var c in Caches.IntList) {
                Owner.RemoveCondition(A.S, c);
            }
        }
        public bool CanPerformMassAction(SurgicalOpType opType) {
            if (opType == SurgicalOpType.ReplaceIrreparableAndMissing
                    || opType == SurgicalOpType.FillMissing) {
                if (!Owner.IsPlayer) {
                    return false;
                }
                if (!BodySlot.IsUsableWithParent) {
                    return false;
                }
            }
            if (opType == SurgicalOpType.RemoveAll) {
                if (Owner.HasStateFlag(BeingState.Alive) && Owner.IsPlayer
                        && Owner.Species.IsBiological) {
                    return false;
                }
                if (!HasRemovableParts) {
                    return false;
                }
            }
            return true;
        }
        public bool HasSlotsForOperation(SurgicalOpType opType) {
            var slots = Slots;
            for (int i = 0; i < slots.Length; i++) {
                var slot = slots[i];
                // below a slot that can never work again a part only comes out (STA-1749)
                switch (opType) {
                    case SurgicalOpType.RemoveAll:
                        if (!slot.IsPartRemoved && slot.CanRemovePart(false)) { return true; }
                        break;
                    case SurgicalOpType.RemoveIrreparable:
                        if (!slot.IsPartRemoved
                                && slot.CanRemovePart(false)
                                && (slot.State.IsDestroyed
                                    || slot.BeyondRepairAncestor != null)) { return true; }
                        break;
                    case SurgicalOpType.FillMissing:
                        if (slot.IsPartRemoved && !slot.Type.IsOptional && !slot.IsDestroyed
                                && slot.BeyondRepairAncestor == null) { return true; }
                        break;
                    case SurgicalOpType.ReplaceIrreparableAndMissing:
                        if (slot.IsPartRemoved && !slot.IsDestroyed && !slot.Type.IsOptional
                                && slot.BeyondRepairAncestor == null) { return true; }
                        if (!slot.IsPartRemoved && slot.CanRemovePart(false)
                                && (slot.State.IsDestroyed
                                    || slot.BeyondRepairAncestor != null)) { return true; }
                        break;
                }
            }
            return false;
        }

    }
}
