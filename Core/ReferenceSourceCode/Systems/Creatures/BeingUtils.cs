using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Input;
using Game.Systems.AI;
using Game.Systems.AI.Combat;
using Game.Systems.Backstories;
using Game.Systems.BodyParts;
using Game.Systems.Conditions;
using Game.Systems.Items;
using Game.Systems.Mental;
using Game.Systems.Naming;
using Game.Systems.Notify;
using Game.Systems.Story;
using Game.Systems.Traits;
using Game.UI;
using Game.Utils;
using KL.Collections;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Creatures {
    public static class BeingUtils {
        public static bool SelectFocused(BeingData beingData) {
            var S = The.R.State;
            if (beingData.Being == null) {
                return ShowProfile(beingData);
            }
            if (!S.Query.IsExclusivelySelected.Ask(beingData.Being)) {
                Caches.BeingList.Clear();
                Caches.BeingList.Add(beingData.Being);
                S.Query.SelectBeings.Ask(Caches.BeingList);
            }
            EntityUtils.CameraFocusOn(beingData.Being);
            return true;
        }

        public static void Select(BeingData beingData, bool focusOnSecondClick) {
            var S = The.R.State;
            if (beingData.Being == null) {
                // if we don't deselect, it can cause a switchback bug when clicking
                // back and forth between two beings of which one is on expedition.
                // Suppress the deferred auto-close first so the panel we open below
                // is not torn down by EntitySelectionHandler.Update() on the next frame.
                S.UIQuery.SuppressEntityPanelAutoClose.Ask();
                S.Query.DeselectAny.Ask();
                ShowProfile(beingData);
                return;
            }
            if (S.Query.IsExclusivelySelected.Ask(beingData.Being)) {
                if (focusOnSecondClick) {
                    EntityUtils.CameraFocusOn(beingData.Being);
                }
            } else {
                S.Sig.SelectEntityUnfocused.Send(beingData.Being);
            }
        }
        // Whose name reads as one of ours: our own faction, or a colonist being restrained
        // (AntiPlayerTemp). A restrained guest keeps reading as a guest.
        public static bool ReadsAsCrew(BeingData data) {
            var fac = data.Mind?.Faction;
            if (fac == null) { return true; }
            return fac.IsPlayer || (fac.IdH == FactionIdH.AntiPlayerTemp && !data.IsGuest);
        }

        public static string DecoratedBeingName(BeingData beingData,
                bool markSelected, string name = null) {
            name ??= beingData.Mind.Name.FullNameOrNameless;
            if (beingData.Being != null && beingData.Being.AIAgent.DirectControl.IsUnderDirectControl) {
                name = Texts.Underscore(name);
            }
            bool isAccented = false;
            if (markSelected && beingData.Being != null) {
                if (The.R.Query.IsSelected.Ask(beingData.Being)) {
                    name = Texts.Accented(name, false);
                    isAccented = true;
                }
            }
            // Anyone who is not crew is marked the way the name above them is, so a list of
            // mixed beings tells guests, enemies and colonists apart
            FactionDef faction = null;
            if (!ReadsAsCrew(beingData)) {
                faction = beingData.EffectiveFaction;
                if (!isAccented) {
                    if (beingData.IsGuest) {
                        name = Texts.Green(name);
                    } else if (!faction.IsPlayer) {
                        name = Texts.Red(name);
                    }
                }
            }
            if (beingData.HasStateFlag(BeingState.Dead)) {
                name = $"{GlyphMap.SpriteSkull} {name}";
            } else {
                if (beingData.Body.IsIncapacitated) {
                    name = $"{GlyphMap.SpriteCross} {name}";
                }
                beingData.Body.CheckWounds(out var canHeal, out var isCrippled);
                if (canHeal) {
                    name = $"{GlyphMap.SpriteBlood} {name}";
                } else if (isCrippled) {
                    name = $"{GlyphMap.SpriteCrutch} {name}";
                }
            }
            if (faction != null && !faction.IsInternal) {
                name = $"{faction.Glyph} {name}";
            }
            return name;
        }

        public static void AddInitialMentalBoostFor(BeingData data, int days) {
            if (data.Mind.Health == null) { return; }
            var eff = MentalEffect.Create(
                data.Being?.PosIdx ?? -1, G.Ticks, days * Consts.TicksPerDay,
                T.MoodEffectHappyToBeAlive, +20);
            The.R.Sig.AddMentalEffect.Send(data, eff);
        }

        /// <param name="assignResult">true if assignment was success</param>
        /// <returns>true if no further actions are needed</returns>
        public static bool TryAssignGoal(long ticks, AIGoal goal, AIAgentComp agent, bool force = true) {
            if (!agent.DirectControl.IsControllable) {
                return false;
            }
            if (!goal.IsAllowedFor(agent)) {
                return false;
            }
            var planner = agent.S.Query.GetAIPlanner.Ask();

            AIState prevWorldState = default;
            bool needsRestoreWorldState = false;
            if (goal.State == AIGoalState.InProgress) {
                D.Warn("Reassigning a goal that is already in progress! will need to reset its vars: {0}", goal);
                needsRestoreWorldState = true;
                prevWorldState = goal.WorldState;
                goal.ResetWorldState();
            }

            if (!planner.TryFormulateFor(goal, agent, ticks, out var plan)) {
                if (needsRestoreWorldState) {
                    D.Warn("Restoring goal state after failed to formulate a plan: {0}->{1}",
                        prevWorldState, goal);
                    goal.WorldState = prevWorldState;
                }
                return false;
            }
            // do we need to remove the old agent from the goal?
            if (goal.Agent != null) {
                if (goal.Agent == agent) {
                    // we already are doing it
                    D.Warn("Trying to assign a goal that is already assigned to the same worker: {0} -> {1}",
                        goal, agent.Being);
                    return true;
                }
                // it was assigned to another agent
                if (!AIGoal.Interrupt(goal, ticks)) {
                    return false;
                }
            }
            // free the current agent from any ongoing work/
            if (agent.Goal != null && !AIGoal.Interrupt(agent.Goal, ticks)) {
                return false;
            }
            // now we have an unassigned task and an unassigned agent
            if (agent.SetGoal(goal, plan, ticks, force: true)) {
                if (force) {
                    goal.ForceAssigned();
                }
                PlaySelectSoundFor(agent.Being);
                // ctx.WantedCursor = null;
                return true;
            } else {
                D.Warn("Failed to force assign a goal (could not set) {0} / {1}",
                    goal, agent);
                goal.ClearCounters();
                goal.ChangeStateTo(AIGoalState.Pending, ticks, true);
                return false;
            }
        }

        public static bool ShowProfile(BeingData beingData) {
            UIUtils.CloseFullScreenUI();
            return A.S.UIQuery.ShowBeingPanel.Ask(beingData);
            // // being is despawned? on expedition or something?
            // if (!beingData.Body.HasFlag(BodyFlags.Missing)) {
            //     S.Sig.ShowBeingProfileUI.Send(beingData, false);
            //     return true;
            // }
            // return false;
        }

        public static string NameWithInjuryColor(string name, float injuryPct, bool isDead) {
            var color = Color.Lerp(Consts.ColorNameHealthy, Consts.ColorNameInjured,
                injuryPct);
            if (isDead) {
                name = Texts.Strike(name);
            }
            return Texts.WithColor(name, Colors.ToHex3(color, '#'));
        }

        public static void UpdateFlagsFor(GameState s, BeingData data) {
            var being = data.Being;
            if (being == null) {
                D.Err("BeingData without Being in BeingEnv.UpdateFor! {0}", data);
                return;
            }
            if (being.IsDead) {
                data.EnvFlags = BeingEnvFlags.None;
                if (!being.Motion.IsImmobilized) {
                    being.Motion.Immobilize(ImmobilizeReasonIdH.Dead, true);
                }
                return;
            }
            var stats = data.StatsBeing;
            var motion = being.Motion;
            var pos = being.PosIdx;
            var temp = s.GetTemp(pos);
            data.StatsBeing.Temp.UpdateBase(temp);

            var envFlags = BeingEnvFlags.None;
            if (data.Species.IsBiological) {
                var minO2 = stats.O2Min.Value;
                motion.MinO2 = minO2;
                if (stats.O2.Value < minO2) {
                    envFlags |= BeingEnvFlags.LowO2;
                }
                var tempMin = stats.TempMin.Value;
                motion.MinTemp = tempMin;
                if (tempMin > temp) {
                    envFlags |= BeingEnvFlags.TooCold;
                }
            }

            if (Ready.AreasInitial && s.Sys.Areas.IsOutdoors(pos)) {
                envFlags |= BeingEnvFlags.Outdoors;
                if (data.Mind.Health != null &&
                        G.Ticks % Consts.TicksPerHour == 29) {
                    if (data.HasTrait(TraitIdH.Astrophobe)) {
                        data.Mind.Health.AddEffect(s, MentalEffect.CreateWithIcon(pos,
                            IconId.CNoSparkles, G.Ticks, Consts.TicksPerHour,
                                TraitDef.MoodReason(T.HadSpacewalk, TraitIdH.Astrophobe), -4));
                    } else if (data.HasTrait(TraitIdH.Astrophile)) {
                        data.Mind.Health.AddEffect(s, MentalEffect.CreateWithIcon(pos,
                            IconId.CSparkles, G.Ticks, Consts.TicksPerHour,
                                TraitDef.MoodReason(T.HadSpacewalk, TraitIdH.Astrophile), +4));
                    }
                }
            }

            var tempMax = stats.TempMax.Value;
            motion.MaxTemp = tempMax;
            if (temp > tempMax) {
                envFlags |= BeingEnvFlags.TooHot;
            }

            if ((envFlags &
                    (BeingEnvFlags.TooCold | BeingEnvFlags.TooHot | BeingEnvFlags.LowO2)) == BeingEnvFlags.None) {
                envFlags |= BeingEnvFlags.Safe;
                // if we are safe, it does not mean that safebase is true
                // for biological with equipment, we will update that
                if (data.Species.IsHumanoid) {
                    var minO2Base = stats.O2Min.BaseValue;
                    if (minO2Base > 0f) {
                        var o2 = s.GetO2(pos);
                        if (minO2Base > o2) {
                            envFlags |= BeingEnvFlags.UnsafeBase;
                        }
                    } else {
                        if (temp < stats.TempMin.BaseValue) {
                            envFlags |= BeingEnvFlags.UnsafeBase;
                        } else if (temp > stats.TempMax.BaseValue) {
                            envFlags |= BeingEnvFlags.UnsafeBase;
                        }
                    }
                }
            } else {
                envFlags |= BeingEnvFlags.UnsafeBase;
            }


            if (being.AIAgent.CombatUnit.State != CombatUnitState.Unaware) {
                var army = being.AIAgent.CombatUnit.Army;
                if (army != null && (army.Flags & CombatFlags.Friendly) == CombatFlags.None) {
                    envFlags |= BeingEnvFlags.InCombat;
                }
            }

            if (being.IsAlive && !being.Motion.IsImmobilized
                    // an Obey Chip puts an animal under the player's direct orders (STA-1680)
                    && (!being.Data.Mind.Species.IsAnimal || being.Data.Body.HasWorkingObeyChip)
                    && being.Data.IsPlayer
                    && !being.Data.ConditionsPreventControl) {
                envFlags |= BeingEnvFlags.Controllable;
            }

            data.UpdateFlags(envFlags);
        }

        public static void EnsureIncapableOf(BeingData data, int jobId) {
            if (data.Mind.Jobs.TryJob(jobId, out var job)) {
                job.Flags |= JobAffinityFlags.Incapable;
                job.Level = 0;
                job.Priority = 0;
                data.Body.ValidateAttackMethods();
            } else {
                D.Err("Being does not have this job type: {0} => {1}",
                    data.Species, JobTypeIdH.ResolveName(jobId));
            }
        }
        public static void EnsureCapableOf(BeingData data, int jobId, byte level = 1) {
            RemoveIncapabilitySources(data.Mind, jobId);
            if (data.Mind.Jobs.TryJob(jobId, out var job)) {
                job.Flags &= ~JobAffinityFlags.Incapable;
                if (job.Level < level) {
                    job.Level = level;
                }
                if (job.Priority < 3) {
                    job.Priority = 3;
                }
            } else {
                data.Mind.Jobs.AddJob(JobType.Get(jobId), level);
                // D.Err("Being does not have this job type: {0} => {1}",
                //     data.Species, JobTypeIdH.ResolveName(jobId));
            }
        }

        // A being forced to be capable of a job should not keep the backstory or trait that says
        // it is not, or its tooltips would contradict the job list. Abilities come from body
        // parts, so they cannot be fixed here.
        private static void RemoveIncapabilitySources(Mind mind, int jobId) {
            if (!JobType.TryGet(jobId, out var jobType)) { return; }
            var roles = mind.Experience?.Roles;
            if (roles != null) {
                foreach (var phase in roles.Keys.ToArray()) {
                    if (JobAffinities.Contains(roles[phase].IncapableJobs, jobType)) {
                        roles.Remove(phase);
                    }
                }
            }
            var traits = mind.Traits;
            if (traits != null) {
                for (int i = traits.Slots.Count - 1; i >= 0; i--) {
                    var slot = traits.Slots[i];
                    if (JobAffinities.Contains(slot.Trait.IncapableJobs, jobType)) {
                        traits.Uninstall(slot);
                    }
                }
            }
        }

        public static void UpdateBodyInfoIcon(Being being) {
            string iconId = null;
            string text = null;
            var body = being.Data.Body;
            if (body.IsAlive) {
                if (body.IsIncapacitated) {
                    iconId = IconId.CIncapacitated;
                    text = T.BeingStateIncapacitated;
                } else {
                    // check if maybe we are immobilized
                    being.Motion.UpdateInfoIcon();
                }
                if (iconId != null) {
                    being.Persona.ShowInfoIcon(iconId, text);
                } else {
                    being.Persona.HideInfoIcon();
                }
            } else {
                string icon;
                string msg;
                if (being.Data.Species.IsBiological || !body.IsEmpty) {
                    icon = IconId.CSkullTransparent;
                    msg = T.InfoBeingDead;
                } else {
                    icon = IconId.CBody;
                    msg = T.Empty;
                }
                being.Persona.ShowInfoIcon(icon, msg);
            }
        }

        public static bool CanBeSentOnExpeditionEver(BeingData bd) {
            if (bd.HasStateFlag(BeingState.Dead)) { return false; }
            if (bd.Body.HasFlag(BodyFlags.Missing)) { return false; }
            if (bd.Species.IsAnimal) { return false; }
            var jobs = bd.Mind.Jobs;
            if (!jobs.CanDo(JobTypeIdH.Expedition) && !jobs.CanDo(JobTypeIdH.Mining)) {
                return false;
            }
            return true;
        }

        public static bool CanBeSentOnExpedition(Being b, bool isExploration) {
            if (b.AIAgent.DirectControl.IsUnderDirectControl) { return false; }
            if (b.IsDead) {
                return false;
            }
            if (b.Data.SkipSimulate) {
                return false;
            }
            var jobs = b.Data.Mind.Jobs;
            if (!b.Data.IsPlayer) {
                return false;
            }
            if (b.Motion.IsImmobilized) {
                return false;
            }
            if (b.Data.Body.IsIncapacitated) {
                return false;
            }
            if (isExploration) {
                if (!jobs.CanDo(JobTypeIdH.Expedition)) {
                    return false;
                }
            } else {
                if (!b.Data.HasAbility(AbilityIdH.Mine)) {
                    return false;
                }
                if (!jobs.CanDo(JobTypeIdH.Mining)) {
                    return false;
                }
            }
            if (b.Data.Species.IsBiological) {
                if (b.Data.StatsBeing.SpaceSuitIntegrity.Value < 0.99999f) {
                    return false;
                }
            }
            return true;
        }

        public static bool IsTired(BeingData data) {
            // Not really tired, but let's include that
            if (data.Body.IsIncapacitated) { return true; }
            if (!data.Species.IsBiological) { return false; }
            // Tiredness
            if (data.StatsBeing.Sleep.Value < 0.3f) {
                return true;
            } else if (data.StatsBeing.Hunger.Value < 0.3f) {
                return true;
            }
            return false;
        }

        public static int GenInventoryFor(BeingData data, Rng rng,
                GenItemFlags equipment) {
            if (data.Inventory == null) { return 0; }
            return GenEquipment(data, rng, equipment, true);
        }

        // Every slot is set before any of them applies its effects: the set's colors are
        // settled in between, and applying effects copies the item color into the looks.
        private static int GenEquipment(BeingData data, Rng rng, GenItemFlags equipment,
                bool applyEffects) {
            var inventory = data.Inventory;
            int generated = 0;
            foreach (var slot in inventory.Equipment.Values) {
                var obj = The.R.Query.GenInventoryItem.Ask(rng, slot, equipment);
                if (obj != null) {
                    slot.Set(obj);
                    generated++;
                }
            }
            inventory.MatchHelmetColorToSuit();
            if (applyEffects) {
                foreach (var slot in inventory.Equipment.Values) {
                    if (slot.Item != null) {
                        slot.ApplyItemEffects();
                    }
                }
            }
            inventory.UpdateLooks();
            return generated;
        }

        public static BeingData GenBeingWithInventory(int speciesIdH,
                BeingType bType, FactionDef faction,
                GenItemFlags equipment = GenItemFlags.Casual
                    | GenItemFlags.SpaceReady | GenItemFlags.Weaponized,
                ColonyRole role = ColonyRole.Unassigned) {
            var vars = new GenBeingVars(Species.Get(speciesIdH), bType);
            vars.Faction = faction;
            vars.Body = faction.GenBodyVars;
            vars.Role = role;
            return GenBeingWithInventory(vars, Rng.Unseeded, equipment);
        }

        public static BeingData GenBeingWithInventory(GenBeingVars vars, Rng rng,
                GenItemFlags equipment) {
            var data = The.R.Query.GenBeingFull.Ask(vars, rng);
            if (data.Inventory != null && equipment != GenItemFlags.None) {
                GenEquipment(data, rng, equipment, true);
            }
            #if !PRODUCTION_BUILD
            DebugTraits.RegisterBeing(data);
            DebugRoles.RegisterBeing(data);
            #endif
            return data;
        }

        public static void FillWeighedBodyParts(Body body,
                Dictionary<BodyPartSlot, float> wbp,
                bool skipInternal,
                Func<BodyPartSlot, bool> inclusionCheck) {
            for (int i = body.Slots.Length - 1; i >= 0; i--) {
                BodyPartSlot part = body.Slots[i];
                if (!part.IsPartRemoved) {
                    if (skipInternal && part.IsInternal) { continue; }
                    if (inclusionCheck == null || inclusionCheck(part)) {
                        wbp.Add(part, part.Size);
                    }
                }
            }
        }

        /// <summary>
        /// Collects the internal parts sitting inside the part that was hit, weighted by size.
        /// Matches on the **resolved** parent slot rather than the part type's declared parent:
        /// a type says where it normally lives (Brain and Jaw say Head), but a species without
        /// that part attaches it somewhere else - Body.IsAttachedTo falls a headless Crawler's
        /// brain and maw back to the Body. Comparing declared types made those, and the Implant
        /// of every robot, permanently unreachable by damage while surgery could still touch
        /// them. Every other consumer (AIActionStab, SurgeriesHandler, BodyTab) already reads
        /// the resolved AttachedToPart.
        /// </summary>
        public static void FillWeighedBodyPartsInternal(Body body,
                BodyPartSlot partHit, Dictionary<BodyPartSlot, float> wbp,
                Func<BodyPartSlot, bool> inclusionCheck) {
            for (int i = body.Slots.Length - 1; i >= 0; i--) {
                BodyPartSlot subPart = body.Slots[i];
                if (!subPart.IsPartRemoved && subPart.IsInternal
                        && subPart.AttachedToPart == partHit) {
                    if (inclusionCheck == null || inclusionCheck(subPart)) {
                        wbp.Add(subPart, subPart.Size);
                    }
                }
            }
        }

        public static bool PlaySelectSoundFor(Being b) {
            if (b.IsDead) { return false; }
            if (b.Data.Body.IsIncapacitated) { return false; }
            if (!b.Data.IsPlayer) { return false; }
            // D.Warn("Playing being select sound: {0}", b);
            Sounds.PlayRandomAt(b.Position, b.Species.SelectSounds);
            return true;
        }
        public static bool DealHistoricalDamage(GameState s, BeingData data,
                DamageType dmgType, int woundTimeAgo,
                int numWounds, INamedEntity wndSrc, params Severity[] severities) {
            var body = data.Body;
            var woundTime = G.Ticks - woundTimeAgo;
            int attempts = 100;
            wndSrc ??= NamedEntity.For(Scar.RandomSource(data.Species, Rng.Unseeded));
            while (numWounds > 0 && attempts-- > 0) {
                foreach (var slot in body.Slots.OrderBy(s => Rng.UValue)) {
                    if (slot.TryAddWound(wndSrc, dmgType, Rng.UFrom(severities),
                            woundTime, 1f, out var wound)) {
                        numWounds--;
                        break;
                    }
                }
            }

            body.CheckCondition(false);
            return body.IsAlive;
        }
        public static void RemoveAllBodyParts(Being patient, int partSpawnPos) {
            bool hasMore;
            int attempts = 0;
            do {
                hasMore = false;
                foreach (var slot in patient.Data.Body.Slots) {
                    if (slot.IsPartRemoved) { continue; }
                    if (!slot.CanRemovePart(false)) { continue; }
                    if (slot.HasAttachedParts) {
                        hasMore = true;
                        continue;
                    }
                    if (slot.TryRemovePart(patient.S, out var spawnedPart, null, true, out _)) {
                        if (spawnedPart != null) {
                            var pos = EntityUtils.CenterOf(partSpawnPos)
                                + Rng.UInsideUnitCircle() * 0.33f;
                            spawnedPart.Transform.TeleportTo(pos);
                            spawnedPart.Graphics.TeleportTo(pos);
                        }
                    }
                }
            } while (hasMore && attempts++ < 50);
            D.Warn("Removed all body parts in {0} attempts: {1}", attempts, patient);
        }

        public static bool Eliminate(GameState s, BeingData data, string causeOfDeath, long ticks) {
            var wasAlive = data.StateFlags.HasFlag(BeingState.Alive);
            data.StateFlags &= ~BeingState.Alive;
            data.StateFlags |= BeingState.Dead;
            data.StateFlags |= BeingState.Hidden;
            data.Body.Flags |= BodyFlags.Missing;
            if (causeOfDeath != null) {
                data.Mind.OnDeath(causeOfDeath, ticks);
            }
            s.Query.AssignBedSlot.Ask(data, null);
            s.Query.AssignBotDock.Ask(data, null);
            s.Query.ClearSurgicalOpsFor.Ask(data);
            if (wasAlive) {
                s.Sig.BeingDied.Send(data);
            }
            DestroyEntity(s, data);
            return true;
        }

        public static void DestroyEntity(GameState s, BeingData data) {
            DestroyEntity(s, data, false);
        }

        public static void DestroyEntity(GameState s, BeingData data, bool keepInventory) {
            if (!keepInventory) {
                data.Inventory?.Destroy(s);
            }
            if (data.Being != null) {
                var agentGoal = data.Being.AIAgent.Goal;
                if (agentGoal != null) {
                    if (agentGoal.IsNonInterruptable) {
                        // Interrupt refuses it while the being lives, but a being leaving the
                        // ship still has to end it, or the action never deactivates and a
                        // breakdown's alert, condition and story event stay for good
                        AIGoal.Cancel(agentGoal, G.Ticks);
                    } else {
                        AIGoal.Interrupt(agentGoal, G.Ticks);
                    }
                }
                if (data.Being.Goal != null) {
                    // won't be possible to retry this anyway
                    data.Being.Goal.Flags |= AIGoalFlags.NonRetryable;
                    AIGoal.Cancel(data.Being.Goal, G.Ticks);
                }
            }
            // Deactivate, don't remove: the being may come back (e.g. a deserter's shuttle
            // returns), and a removed condition is gone for good while the wounds, diseases
            // and immunities that justified it stay on the data. Same shape as
            // BeingStorage.Store and RaidPayload.Stash; restored via BeingData.RestoreConditions
            // on the way back in (see DeserterUtils.ReturnDeserter). Runs after the goal
            // teardown above so a goal that removes its own condition (e.g. Sleeping) still
            // finds it registered and gets its OnEnd cleanup.
            foreach (var c in data.Conditions) {
                s.Sig.ActivateCondition.Send(c.Value, false);
            }
            if (data.Being == null) { return; }
            s.Beings.Destroy(data.Being);
        }

        public static double ValueOf(GameState s, BeingData data, bool includeInventory) {
            double value = data.Species.BeingValueBase;

            foreach (var slot in data.Body.Slots) {
                var itemValue = s.Market.ValueOf(slot.InstalledDef?.Def)
                    * (slot.State?.Quality ?? 1f);
                // if (itemValue > 0f && slot.HoldsMind && slot.State.IsUsableSelf) {
                //     // FIXME TODO: actual mind value
                //     if (data.Mind.Faction.IsPlayer) {
                //         itemValue *= 1f;
                //     } else if (!data.Mind.Species.IsBiological) {
                //         // usable enemy core
                //         itemValue *= 2f;
                //     }
                // }
                value += itemValue;
            }

            if (includeInventory && data.Inventory != null) {
                foreach (var slot in data.Inventory.Equipment) {
                    value += s.Market.ValueOf(slot.Value.Item?.Def);
                }
            }

            // D.Warn("Value of being: {0} => {1}", data, value);

            return value;
        }

        public static bool RemoveFromShip(GameState s, BeingData data,
                BeingState extraFlags = BeingState.Uninitialized) {
            return RemoveFromShip(s, data, extraFlags, false);
        }

        /// <param name="keepInventory">the being may come back wearing what it took (a
        /// deserter, like a being in stasis); otherwise its gear is destroyed with it</param>
        public static bool RemoveFromShip(GameState s, BeingData data,
                BeingState extraFlags, bool keepInventory) {
            data.StateFlags |= extraFlags;
            data.StateFlags |= BeingState.Hidden;
            data.Body.Flags |= BodyFlags.Missing;
            s.Query.AssignBedSlot.Ask(data, null);
            s.Query.AssignBotDock.Ask(data, null);
            DestroyEntity(s, data, keepInventory);
            return true;
        }

        public static bool Kill(GameState s, BeingData data, DamageType dmgType,
                INamedEntity woundSource, string causeOfDeath,
                Func<BodyPartSlot, bool> bodyPartAffectedCheck, int bleedCount = 2) {
            var body = data.Body;
            var woundTime = G.Ticks;
            if (woundSource == null) {
                D.Err("Wound source is null when calling Kill!");
            }
            foreach (var slot in body.Slots) {
                if (bodyPartAffectedCheck?.Invoke(slot) != false) {
                    if (slot.TryAddWound(woundSource, dmgType, Severity.Fatal, woundTime, 1f,
                        out var wound)) {
                    }
                }
            }

            for (int i = 0; i < bleedCount; i++) {
                body.Bleed(Rng.URange(0.5f, 0.7f));
            }

            body.CheckCondition(false);
            if (body.IsAlive) {
                D.Err("Failed to kill in first attempt, doing double tap for {0}", data);
                var slot = data.Species.IsBiological ? BodyPartSlotIdH.Brain : BodyPartSlotIdH.Core;
                if (body.TryGetSlot(slot, out var brain)) {
                    brain.TryAddWound(woundSource, dmgType, Severity.Fatal, woundTime, 1f, out _);
                }
                if (body.TryGetSlot(BodyPartSlotIdH.Body, out var bodySlot)) {
                    bodySlot.TryAddWound(woundSource, dmgType, Severity.Fatal, woundTime, 1f, out _);
                }
                body.CheckCondition(false);
                if (body.IsAlive) {
                    D.Err("Fuck man, we have a Bruce Willis or Chuck Norris, can't kill it! {0}", data);
                    return false;
                }
            }

            body.SetDeathTick(woundTime);
            // Without a cause of its own, the fatal wound's source already recorded stays
            if (causeOfDeath != null) {
                body.Owner.Mind.OnDeath(causeOfDeath, woundTime);
            }

            return true;
        }

        /// <summary>
        /// Never call this before spawning a being!
        /// </summary>
        public static void KillHistorical(GameState S, BeingData data, int woundTimeAgo, Rng r,
                params DamageType[] damageTypes) {
            if (data.Id == 0) {
                D.Err("Trying to kill a being that wasn't ever spawned as historical! Won't save/load properly! {0}", data);
            }
            var body = data.Body;
            body.Flags &= ~BodyFlags.Invincible;
            var woundTime = G.Ticks - woundTimeAgo;
            var src = NamedEntity.For(Scar.RandomSource(data.Species, r));
            foreach (var slot in body.Slots) {
                if (woundTimeAgo != 0 && !data.Species.IsBiological && r.Chance(0.5f)) {
                    if (slot.CanRemovePart(false) && !slot.HasAttachedParts) {
                        if (slot.TryRemovePart(out var def, out _)) {
                            if (def.HoldsMind) {
                                slot.TransferMindAfterPartRemoval(S, null);
                            }
                        }
                        continue;
                    }
                }
                if (slot.IsEssential && slot.IsInternal) {
                    if (r.Chance(0.5f)) {
                        if (slot.TryAddWound(src, r.From(damageTypes),
                            r.OneOf(Severity.Irreparable, Severity.Fatal),
                            woundTime, 1f, out var wound)) {
                        }
                    }
                }
                if (data.Species.IdH == SpeciesIdH.Crawler && slot.Type.IdH == BodyPartSlotIdH.Body) {
                    // crawler has to brain, let's make sure the body is damaged enough
                    if (slot.TryAddWound(src, r.From(damageTypes),
                            Severity.Fatal, woundTime, 1f, out var wound)) {
                    }
                } else if (slot.HoldsMind) {
                    if (slot.TryAddWound(src, r.From(damageTypes),
                            Severity.Fatal, woundTime, 1f, out var wound)) {
                    }
                } else if (r.Chance(0.5f)) {
                    if (slot.TryAddWound(src, r.From(damageTypes),
                            r.OneOf(Severity.Mild, Severity.Moderate, Severity.Severe),
                            woundTime, 1f, out var wound)) {
                    }
                }
            }
            body.SetDeathTick(woundTime);
            if (body.Owner.Mind.LastCauseOfDeath == null) {
                body.Owner.Mind.OnDeath(T.DetailsUnknown, woundTime);
            }

            ApplyHistoricalDecay(S, data, woundTimeAgo, woundTime);

            if (data.Being != null) {
                body.Bleed(0.5f);
                body.Bleed(0.7f);
                body.CheckCondition(false);
            }
            body.ValidateAttackMethods();
        }

        private static void ApplyHistoricalDecay(GameState S, BeingData data,
                int woundTimeAgo, long woundTime) {
            bool isBio = data.Species.IsBiological;
            var condId = Hashes.S(isBio ? ConditionId.Rotting : ConditionId.Corroding);
            float devDays = isBio
                ? Tunables.RottingDevelopmentDays
                : Tunables.CorrodingDevelopmentDays;
            float qualityLossPerWeek = isBio
                ? Tunables.RottingQualityLossPerWeek
                : Tunables.CorrodingQualityLossPerWeek;
            float conditionLossPerWeek = isBio
                ? Tunables.RottingConditionLossPerWeek
                : Tunables.CorrodingConditionLossPerWeek;

            // Includes ConditionDecay.DecayGraceBand's worth of days, so a corpse younger than
            // that shows no rot at all - same silent grace band as the live simulation.
            float totalDelayDays = devDays + ConditionDecay.GraceBandDays(conditionLossPerWeek);
            float deadDays = woundTimeAgo / (float) Consts.TicksPerDay;
            if (deadDays < totalDelayDays) { return; }

            ConditionState.CreateFor(S, condId, data, Severity.Mild, false);
            if (data.Conditions.TryGetValue(condId, out var cond)) {
                cond.StartTick = woundTime;
            }

            float decayDays = deadDays - totalDelayDays;
            float weeksFraction = decayDays / 7f;
            float qualityLoss = Mathf.Clamp(qualityLossPerWeek * weeksFraction, 0f, 0.9f);
            float conditionLoss = Mathf.Clamp(conditionLossPerWeek * weeksFraction, 0f, 1f);
            var source = NamedEntity.For("Decay");
            foreach (var slot in data.Body.Slots) {
                if (slot.IsPartRemoved || slot.State == null) { continue; }
                slot.State.SetQuality(Mathf.Max(0.05f, slot.State.Quality - qualityLoss));
                if (conditionLoss > 0f) {
                    slot.State.Decay = Mathf.Min(1f,
                        Mathf.Max(slot.State.Decay, ConditionDecay.DecayGraceBand) + conditionLoss);
                    slot.TryAddOrUpdateWound(source, DamageType.Decay,
                        ConditionDecay.ConditionLossToSeverity(slot.State.Decay),
                        woundTime, 1f, WoundFlags.None, conditionId: condId);
                }
            }
            ConditionDecay.SyncConditionState(data, condId);
        }

        public static bool NeedsCharge(BeingData data) {
            var stats = data.StatsBeing;
            var capv = stats.BatteryCapacity.Value;
            return capv > 0f && stats.BatteryCharge.Value < capv;
        }

        public static bool TryCharge(BeingData data, float rate) {
            // TODO particles and sounds
            if (NeedsCharge(data)) {
                var stats = data.StatsBeing;
                var bc = stats.BatteryConsumption;
                // compensate battery consumption for grid tick (10 ticks) + charge
                return stats.BatteryCharge.TryIncrease(bc.Value * 10f + rate);
            }
            return false;
        }

        public static bool DamageBodyPart(GameState s, BeingData data,
                int slotIdH, Rng rng, DamageType dmgType, Severity sev,
                INamedEntity source,
                float subHitChanceMultiplier = 1f,
                int conditionId = 0) {
            var body = data.Body;
            BodyPartSlot internalHit = null;
            if (!body.TryGetSlot(slotIdH, out var partHit)) {
                D.Err("Failed to get body part by id h: {0} ({1}) from {2}",
                    slotIdH, BodyPartSlotIdH.ResolveName(slotIdH), data);
                return false;
            }
            var subHitchance = partHit.Type.SubpartHitChance;

            if (subHitchance > 0f && subHitChanceMultiplier > 0f) {
                subHitchance *= subHitChanceMultiplier;
                var wbp = Caches.WeightedBodyParts;
                if (rng.Chance(subHitchance)) {
                    wbp.Clear();
                    FillWeighedBodyPartsInternal(body, partHit, wbp, null);
                    if (wbp.Count == 0) {
                        D.Err("No sub parts for body part with sub hit chance: {0} -> {1}",
                            subHitchance, partHit.Type);
                    } else {
                        internalHit = Rng.UWeightedFrom(wbp);
                    }
                }
            }
            ApplyDamageToPart(s, partHit, source, dmgType, sev, conditionId);
            if (internalHit != null) {
                if (sev > Severity.Mild) {
                    if (rng.Chance(0.5f)) {
                        if (Severities.TryDecrease(sev, out var newS)) {
                            sev = newS;
                        }
                    }
                }
                ApplyDamageToPart(s, internalHit, source,
                    dmgType, sev, conditionId);
            }

            if (sev >= Severity.Moderate || dmgType == DamageType.Piercing) {
                body.Bleed(0.25f);
            }
            return true;
        }

        public static void DamageRandomBodyPart(GameState s, BeingData data,
                Rng rng,
                DamageType dmgType, Severity sev,
                INamedEntity source,
                float subHitChanceMultiplier = 1f,
                Func<BodyPartSlot, bool> inclusionCheck = null,
                bool skipInternal = true,
                int conditionId = 0) {
            var body = data.Body;
            var wbp = Caches.WeightedBodyParts;
            wbp.Clear();

            FillWeighedBodyParts(body, wbp, skipInternal, inclusionCheck);

            // TODO calculate armor, etc
            // Hit force tells how strong was the blow compared to the armor we have

            if (!rng.TryWeightedFrom(wbp, out var partHit)) {
                D.Err("Could not find random body part for inclusion check - no parts matched! {0}", data);
                return;
            }

            BodyPartSlot internalHit = null;
            var subHitchance = partHit.Type.SubpartHitChance;

            if (subHitchance > 0f) {
                subHitchance *= subHitChanceMultiplier;
                if (rng.Chance(subHitchance)) {
                    wbp.Clear();
                    FillWeighedBodyPartsInternal(body, partHit, wbp, inclusionCheck);
                    if (!Rng.Unseeded.TryWeightedFrom(wbp, out internalHit)) {
                        D.Warn("Could not find random INTERNAL body part for inclusion check - no parts matched! Parent part hit: {0}", partHit);
                    }
                }
            }

            // D.Err("Dealing Damage: {0} -> {1} -> {2} -> {3}. Hit {4} / {5}. Force {6}",
            //     method.Type, method.Stats.DPS.Value, attacker, target, partHit, internalHit,
            //         hitForce);

            // Reduce hit force on the way further
            ApplyDamageToPart(s, partHit, source, dmgType, sev, conditionId);
            if (internalHit != null) {
                if (sev > Severity.Mild) {
                    if (rng.Chance(0.5f)) {
                        if (Severities.TryDecrease(sev, out var newS)) {
                            sev = newS;
                        }
                    }
                }
                ApplyDamageToPart(s, internalHit, source,
                    dmgType, sev, conditionId);
            }

            // Blood on the floor needs somebody on the map to bleed. A body that is not
            // (a sleeper in a stasis pod) still takes the damage above.
            if ((sev >= Severity.Moderate || dmgType == DamageType.Piercing)
                    && data.Being?.IsActive == true) {
                body.Bleed(0.25f);
            }

            // TODO add pain for biologicals
            // - figure out the resistance to the given attack type
        }

        /// <summary>
        /// Creates or advances a condition for the damaged body part
        /// </summary>
        public static bool ApplyDamageToPart(GameState s, BodyPartSlot partHit,
            INamedEntity attacker,
            DamageType dmgType, Severity sev, int conditionId) {

            // D.Warn("Damaging: {0} -> {1}", partHit.InstalledDef.Id, sev);

            if (partHit.TryAddWound(attacker, dmgType, sev, G.Ticks, 1f, out var wound,
                    conditionId: conditionId)) {
                if (attacker != null) {
                    var msg = T.InflictWoundLog_V5.F(
                        attacker.EntityName,
                        Severities.NameFor(sev, false),
                        DamageTypes.NameFor(dmgType),
                        partHit.Body.Owner.Mind.CombatLogName,
                        partHit.SlotName
                    );
                    s.Sys.Log.AddLine(Texts.ReplaceBoldWithAccented(msg));
                }
                return true;
            }
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static BeingAvatar AvatarFor(GameState s, BeingData data) {
            return s.Query.GetBeingAvatar.Ask(data);
        }

        public static bool TryOrderEquipItem(Being being, ObjData item) {
            if (!being.AIAgent.DirectControl.IsControllable) {
                return false;
            }
            var goal = AIGoal.DirectOrder(JobTypeIdH.Task, IconId.CClothing,
                    T.AbilityEquipitem,
                    being.AIAgent, G.Ticks)
                .WithWorld(AIVarsH.HasFoundEquipment, true)
                .WithWorldToDesired(AIVarsH.IsItemEquipped, false, true)
                .WriteInitialState();
            var planner = being.S.Query.GetAIPlanner.Ask();
            if (planner.TryFormulateFor(goal, being.AIAgent, G.Ticks, out var plan)) {
                being.AIAgent.DropGoal();
                being.AIAgent.SetGoal(goal, plan, G.Ticks);
                being.AIAgent.BB.SetTargetObj(item, goal);
                return true;
            }
            return false;
        }
        public static Being SpawnBeingWithEquipment(GameState S, int species, int pos, Rng rng = null,
                GenItemFlags equipment = GenItemFlags.Casual | GenItemFlags.SpaceReady | GenItemFlags.Weaponized) {
            return SpawnBeingWithEquipment(S, species, EntityUtils.ToVector2(pos), rng, equipment);
        }
        public static Being SpawnBeingWithEquipment(GameState S, int species, Vector2 pos, Rng rng = null,
                GenItemFlags equipment = GenItemFlags.Casual | GenItemFlags.SpaceReady | GenItemFlags.Weaponized,
                GenBeingVars vars = null) {
            rng ??= Rng.Unseeded;
            if (vars == null) {
                vars = new GenBeingVars(Species.Get(species), BeingType.Friendly);
                vars.Body = new GenBeingBodyVars();
            }
            // vanilla
            // vars.Body = new GenBeingBodyVars();
            var data = The.R.Query.GenBeingFull.Ask(vars, rng);
            if (data.Inventory != null) {
                // don't have to do that
                // slot.ApplyItemEffects();
                GenEquipment(data, rng, equipment, false);
            }
            var being = S.Beings.Create(pos, data);
            return being;
        }

        public static Being SpawnBeing(GameState S, int speciesId, int pos, Rng rng = null,
                GenBeingBodyVars? bodyVars = null) {
            rng ??= Rng.Unseeded;
            var vars = new GenBeingVars(Species.Get(speciesId), BeingType.Friendly);
            vars.Body = bodyVars ?? FactionDef.Player.GenBodyVars;
            var data = S.Query.GenBeingFull.Ask(vars, rng);
            return S.Beings.Create(pos, data);
        }
        public static void GenerateDeadBeings(GameState S, int numBeings,
                Rng rng, int spawnPos, float chanceInfected, float chanceWeaponized) {
            var podPos = EntityUtils.CenterOf(spawnPos);
            var faction = S.Sys.Diplomacy.RandomFactionForTrade();
            var human = Species.Get(SpeciesIdH.Human);
            var dmgType = DamageUtils.RandomEnv(rng, true);
            for (int i = 0; i < numBeings; i++) {
                var flags = GenItemFlags.Casual | GenItemFlags.SpaceReady;
                if (rng.Chance(chanceWeaponized)) {
                    flags |= GenItemFlags.Weaponized;
                }
                var data = GenBeingWithInventory(
                    new GenBeingVars(human, BeingType.Neutral), rng, flags);
                data.Mind.SetFaction(faction);
                S.Beings.Create(podPos, data);
                var woundTimeAgo = rng.Range(Consts.TicksPerDay, Consts.TicksPerWeek);
                KillHistorical(S, data, woundTimeAgo, rng, dmgType);
                if (rng.Chance(chanceInfected)) {
                    ConditionState.CreateFor(S, ConditionIdH.CrawlerInfected,
                        data, Severity.Irrelevant, false);
                }
            }
        }

        /// <param name="data">if not null, log entry will be created</param>
        public static void ShowColonyGreetingFor(GameState s, Being being,
                StoryEventData data = null) {
            var msg = T.JoinedColony_V1.F(Texts.LinkToBeing(being.Data,
                Texts.Yellow(being.Data.Mind.Name.ShortName)));
            Notification.BuildFor(being.Data, null, msg, Consts.TicksPerDay,
                    NotificationType.Info)
                .Group("col_join", icon: IconId.CParty)
                .Show();
            // s.Sig.ShowFanfare.Send(msg);
            s.Sys.Log.AddLine(being, msg);

            if (data != null) {
                data.CreateLogEntry(being.Definition.Preview, msg,
                    being.Data.ColonyGreeting);
                data.TargetBeing = being.Data;
            }
            // A dialog rather than a popup: it shows their actual face, and this is the
            // moment the player meets them
            var name = Texts.LinkToBeing(being.Data,
                Texts.Yellow(being.Data.Mind.Name.FullNameOrNameless));
            var text = $"{Texts.Title(T.JoinedColony_V1.F(name))}{being.Data.UI.GetBio(false, true)}";
            var exp = being.Data.Mind.Experience;
            if (exp != null && !Arrays.IsEmpty(exp.Roles)) {
                text = $"{text}\n{T.PersonaLogBackstory_V1.F(exp.BackstoryFor(being.Data))}";
            }
            The.Dialog.ShowSimple(null, text).Being = being.Data;
        }

        public static void IncrementCounterFor(BeingData beingData, AIAction act) {
            if (act.IsWork) {
                beingData.Counters.Increment(CounterIdH.TicksWorkedMonth);
                beingData.Counters.Increment(CounterIdH.TicksWorkedTotal);
            } else if (act.IsRest) {
                beingData.Counters.Increment(CounterIdH.TicksIdleMonth);
                beingData.Counters.Increment(CounterIdH.TicksIdleTotal);
            }
        }

        public static void NoticeWorkerOfTheMonth(GameState s, BeingData b, long ticks) {
            var bPos = b.Being?.PosIdx ?? b.LastPosition.Idx;
            b.Mind.Health.AddEffect(s, MentalEffect.CreateWithIcon(bPos,
                IconId.CUpgrade, ticks, Consts.TicksPer5Days, T.NounEmployeeOtm, +15));

            var text = $"{Texts.Green(T.NounEmployeeOtm)}: {Texts.LinkToBeing(b)}";
            s.Sig.ShowFanfare.Send(text);

            var evText = $"{T.NounEmployeeOtm}: {b.Mind.Name.ShortName}";
            Notification.BuildFor(b, null, evText, Consts.TicksPerDay, NotificationType.Info)
                .Show();
        }

        public static bool HasAbilities(BeingData beingData, FrugalArray<int> ra) {
            for (int i = 0; i < ra.Length; i++) {
                if (!beingData.HasAbility(ra[i])) {
                    return false;
                }
            }
            return true;
        }

        // Names only the abilities the being does not have. Unspawned data has no
        // AbilitySources yet, so it lists all of them.
        public static void AppendLackingAbilities(StringBuilder sb, BeingData beingData,
                FrugalArray<int> abilities) {
            sb.Append(T.LackingAbilities);
            sb.Append(": ");
            bool first = true;
            for (int i = 0; i < abilities.Length; i++) {
                int ab = abilities[i];
                if (beingData != null && beingData.HasAbility(ab)) { continue; }
                if (!first) {
                    sb.Append(", ");
                }
                first = false;
                sb.Append(Texts.Red(Ability.Get(ab).NameT));
            }
        }

        public static Being Recreate(BeingData data) {
            if (data.Being == null) {
                return null;
            }
            var s = data.Being.S;
            // Hide so we won't destroy
            data.StateFlags |= BeingState.Hidden;
            var pos = data.Being.PosIdx;
            // Destroying deselects, and a being revived on a surgery table should stay selected
            bool wasSelected = s.Query.IsExclusivelySelected.Ask(data.Being);
            // Destroy old being and recreate it for correct re-initialization
            s.Beings.Destroy(data.Being, true);
            data.StateFlags &= ~BeingState.Hidden;
            D.Warn("Respawning being: {0}", data);
            var being = s.Beings.Create(pos, data);
            if (wasSelected && being != null) {
                s.Sig.SelectEntityUnfocused.Send(being);
            }
            return being;
        }

        public static int PairHash(BeingData b1, BeingData b2) {
            if (b1.Id > b2.Id) {
                return Hashes.Quick(b1.Id, b2.Id);
            } else {
                return Hashes.Quick(b2.Id, b1.Id);
            }
        }

        public static void FacePosition(Being being, Vector2 pos, bool opposite = false) {
            var f = Facing.FromDirection(pos - being.Position, being.Graphics.CurrentFacing);
            if (opposite) {
                f = Facing.Opposite(f);
            }
            being.Graphics.SetFacing(f);
        }

        /// <summary>
        /// Gives a robotic being flight by swapping every mobilizer that lacks it for a MiniJet
        /// (Compatibility Robotic, no size limit, so it fits a Cleaning Bot's single slot and a
        /// Robot's large body alike). Returns false for biological species - they need a jet
        /// suit instead - and for bodies with no mobilizer slot. Species that already fly (their
        /// standard mobilizer is a jet) are a no-op.
        /// </summary>
        public static bool EnsureFlying(BeingData data) {
            if (data.HasAbility(AbilityIdH.Fly)) { return true; }
            if (data.Species.IsBiological) { return false; }
            bool flies = false;
            // D.Warn("Ensuring flight for {0}", data);
            foreach (var slot in data.Body.Slots) {
                if (slot.Type.IdH != BodyPartSlotIdH.Mobilizer) { continue; }
                if (Arrays.ContainsSafe(slot.InstalledDef?.AddsAbilities, AbilityId.Fly)) {
                    flies = true;
                    continue;
                }
                // D.Warn("Replacing mobilizer to ensure flight for {0} (was: {1})", data, slot.InstalledDef);
                if (BodyPartDef.TryForDef(DefIdsH.ObjPartsMobilizerMiniJet, out var part)) {
                    slot.InstalledDef = part;
                    flies = true;
                } else {
                    D.Err("MiniJet part not available! PANIC!");
                }
            }
            return flies;
        }
        public static string BodyTooltip(BeingData data, bool includePersona = false) {
            var body = data.Body;
            var sbParts = Caches.StringBuilder2;
            sbParts.Clear();
            foreach (var slot in body.Slots) {
                sbParts.AppendLine(slot.TooltipLine);
            }
            var sb = Caches.StringBuilder;
            sb.Clear();
            if (includePersona) {
                sb.Append(data.UI.GetBio(forStarting: true, shortVersion: false));
                sb.Append(GlyphMap.BRHR);
            }
            if (data.HasStateFlag(BeingState.Dead)) {
                AddCauseOfDeath(body, sb);
            }
            sb.Append(sbParts.ToString());
            return sb.ToString();
        }

        public static void AddCauseOfDeath(Body body, StringBuilder sb) {
            sb.AppendFormat("{0}\n", T.DeathCause_V1.F(body.DeathCause ?? T.Unknown));
            sb.AppendFormat("{0}: {1}\n", T.TimeOfDeath, Texts.Accented(body.DeathTick != 0 ?
                Units.TicksAgo((int)(G.Ticks - body.DeathTick)) : T.Unknown));
        }

        /// <summary>
        /// Immediately turns a being hostile (Pirate faction) and starts a raid.
        /// Used by sandbox/dev tooling and combat tests.
        /// </summary>
        public static void MakeHostile(Being being) {
            if (!being.IsActive || !being.HasData) { return; }
            var fac = FactionDef.Get(FactionId.Pirate);
            being.Data.Mind.SetFaction(fac);
            var cu = being.AIAgent.CombatUnit;
            if (cu.Army != null) {
                cu.Army.RemoveUnit(cu, CombatUnitState.Unaware);
            }
            being.S.Sys.Combat.StartRaidBy(being);
        }
        public static Color SanityColor(float sanity) {
            var col = Color.Lerp(Consts.ColorChartRed, Consts.ColorGreenBar, sanity);
            return col;
        }

    }
}
