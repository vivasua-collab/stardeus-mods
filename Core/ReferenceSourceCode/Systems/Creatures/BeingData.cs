#if !PRODUCTION_BUILD
// #define DEBUG_NEEDS
// #define DEBUG_CONDITIONS
#endif
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Systems.AI;
using Game.Systems.AI.Combat;
using Game.Systems.BodyParts;
using Game.Systems.Conditions;
using Game.Systems.Items;
using Game.Systems.Mental;
using Game.Systems.Motion;
using Game.Systems.Stats;
using Game.Utils;
using Game.CodeGen;
using KL.Signals;
using KL.Utils;
using Unity.Mathematics;
using Game.Systems.Path;
using Game.Input;
using System.Linq;
using Game.Systems.Story;
using Game.Systems.UIUX;
using Game.Systems.Notify;

namespace Game.Systems.Creatures {
    /// <summary>
    /// Everything related to Being that can be saved / loaded or generated
    /// without creating a Being instance and spawning it on the map
    /// </summary>
    public sealed class BeingData : IAbilityHolder {
        public static uint LastId;
        public uint Id;
        public StatsBeing StatsBeing;
        public Species Species;
        public BeingState StateFlags;
        /// <summary>
        /// If Being is spawned, these flags will have to be updated every tick
        /// </summary>
        public BeingEnvFlags EnvFlags;
        public Being Being;
        public Body Body;
        public BeingLooks Looks;
        public BeingInventory Inventory;
        public Counters Counters;
        public int GroupId;
        public Mind Mind;
        /// <summary>
        /// never use this for comparisons in runtime, it will be 0 after loading
        /// and restoring mind. Use Mind.Id instead.
        /// </summary>
        public uint MindId;
        public string SearchString => Strings.ToSearchString($"{Name}{Species}");
        /// <summary>
        /// When we save / load, we will update this. Also when we remove
        /// the being entity, we will store the last known position so we
        /// can readd the being there.
        /// For realtime position in the game use being.Motion.Position.
        /// <seealso cref="MotionControl.Position"/>
        /// <seealso cref="Being.Motion"/>
        /// <seealso cref="MotionSys"/>
        /// </summary>
        public Position LastPosition;
        public long InStasisFromTick;
        public bool HideAvatarButton;
        public bool LogGoalChanges;
        public bool LogIdleGoals;
        public List<string> GoalChanges;
        public bool IsPlayer => EffectiveFaction.IsPlayer;
        /// <summary>
        /// A temporary non-hostile guest of a different faction (e.g. delivery quest passenger).
        /// The role lives on the Mind (identity), so it follows mind transfers.
        /// </summary>
        public bool IsGuest => Mind != null && Mind.Role == ColonyRole.Passenger;
        // A guest the player has not turned on (CombatSys.TurnAgainstPlayer makes it
        // AntiPlayerTemp). It stays out of fights, or shelters with the crew when a
        // predator goes for it; a turned guest fights like a restrained colonist.
        public bool IsProtectedGuest => IsGuest
            && Mind.Faction.IdH != FactionIdH.AntiPlayerTemp;
        // Asleep in a pod. Anything that picks a random being has to skip them: to the rest
        // of the game they are a name on a manifest, not someone things can happen to. The
        // only thing allowed to touch a sleeper is their own array's stability
        // (ManifestSys.HarmHour). Enforced for time by ConditionsSys.OnTick and
        // BeingsSys.OnTick, and backstopped by ConditionState.CreateFor.
        public bool IsInStasis => Mind != null && Mind.Role == ColonyRole.InStasis;
        /// <summary>
        /// Beings that take part in colony social life (drama, relationships, clustering as
        /// friendlies) and must not trigger hostile combat just by being present: our own
        /// colonists plus non-hostile guests.
        /// </summary>
        public bool IsFriendly => IsPlayer || IsGuest;
        // Only our own crew leaves forbidden items alone. Guests eat the forbidden food of a
        // derelict survivor zone. See Docs/Architecture/Systems/Forbidding.md
        public bool CaresAboutForbidden => IsPlayer && !IsGuest;
        private FactionDef lastEffectiveFaction;
        public FactionDef EffectiveFaction {
            get {
                if (Mind == null) {
                    D.Err("Mind is not set when trying to get EffectiveFaction! {0}", this);
                    if (lastEffectiveFaction != FactionDef.Player) {
                        lastEffectiveFaction = FactionDef.Player;
                        OnFactionChanged();
                    }
                    return FactionDef.Player;
                }
                var fac = Mind.Faction;
                if (fac == null) {
                    D.Err("Mind.Faction is null when trying to get EffectiveFaction! {0}", this);
                    if (lastEffectiveFaction != FactionDef.Blank) {
                        lastEffectiveFaction = FactionDef.Blank;
                        OnFactionChanged();
                    }
                    return FactionDef.Blank;
                }
                if (fac.IsBlank) {
                    if (lastEffectiveFaction != fac) {
                        lastEffectiveFaction = fac;
                        OnFactionChanged();
                    }
                    return fac;
                }
                // A restrained being (AntiPlayerTemp) fights back even when it trusts the AI
                // or is chipped: counted as Player, it would join the crew's army and the
                // restraint fight would have no enemy
                if (fac.IsPlayer || fac.IdH == FactionIdH.AntiPlayerTemp
                        || (StatsBeing.AIAlignment.Value < 0.99f && !Body.IsForcedToObey)) {
                    if (lastEffectiveFaction != fac) {
                        lastEffectiveFaction = fac;
                        OnFactionChanged();
                    }
                    return fac;
                }
                // forced player
                if (lastEffectiveFaction != FactionDef.Player) {
                    lastEffectiveFaction = FactionDef.Player;
                    OnFactionChanged();
                }
                return FactionDef.Player;
            }
        }

        private void OnFactionChanged() {
            Being?.Transmit(null, MsgIdH.FactionChanged);
        }

        private BeingUI ui;
        public BeingUI UI => ui ??= new BeingUI(this);
        public readonly List<NeedSource> NeedSources = new();
        public DockableConfig Dockable;
        public BeingAvatar Avatar => Looks.Avatar ??= A.S.Query.GetBeingAvatar.Ask(this);
        public BeingSurgeryPreview SurgeryPreview => Looks.SurgeryPreview ??= new BeingSurgeryPreview();
        public bool RemoveNeedSource(NeedSource ns) {
            for (int i = NeedSources.Count - 1; i >= 0; i--) {
                NeedSource src = NeedSources[i];
                if (src.Need == ns.Need && src.Slot == ns.Slot) {
                    NeedSources.RemoveAt(i);
                    #if DEBUG_NEEDS
                    D.Warn("Removing need source: {0} -> {1}", ns, this);
                    #endif
                    return true;
                }
            }
            D.Err("Failed Removing need source: {0} -> {1}", ns, this);
            return true;

        }
        public bool AddNeedSource(NeedSource ns) {
            for (int i = NeedSources.Count - 1; i >= 0; i--) {
                NeedSource src = NeedSources[i];
                if (src.Need == ns.Need && src.Slot == ns.Slot) {
                    D.Err("Trying to add a duplicate need source! {0}", ns);
                    return false;
                }
            }
            #if DEBUG_NEEDS
            D.Warn("Adding need source: {0} -> {1}", ns, this);
            #endif
            NeedSources.Add(ns);
            return true;
        }
        public readonly List<AbilitySource> AbilitySources = new();
        /// <summary>
        /// Never set or remove conditions here directly! Use AddCondition / RemoveCondition!
        /// <seealso cref="AddCondition(GameState, ConditionState)"/>
        /// <seealso cref="RemoveCondition(GameState, int)"/>
        /// </summary>
        //[Obsolete("Don't use this directly!", true)]
        [SkipDumpInstance]
        public readonly Dictionary<int, ConditionState> Conditions = new();
        [SkipDumpInstance]
        public readonly Dictionary<int, float> ConditionProgress = new();

        private BCallback conditionsListChangedB;
        /// <summary>
        /// Fires when active conditions or wounds on this being are added, removed, or change.
        /// Always available — no need to access the Being entity. Bind with notifyImmediately=false.
        /// </summary>
        public BCallback ConditionsListChangedB => conditionsListChangedB ??= new BCallback();

        private BCallback developingConditionsChangedB;
        /// <summary>
        /// Fires when a new developing condition appears in ConditionProgress.
        /// Always available — no need to access the Being entity. Bind with notifyImmediately=false.
        /// </summary>
        public BCallback DevelopingConditionsChangedB => developingConditionsChangedB ??= new BCallback();

        /// <summary>Called when wounds are added or removed on any body part slot of this being.</summary>
        public void OnWoundsChanged() => conditionsListChangedB?.Invoke();

        internal void NotifyConditionsListChanged() => conditionsListChangedB?.Invoke();
        internal void NotifyDevelopingConditionsChanged() => developingConditionsChangedB?.Invoke();
        public BeingSafety Safety;
        /// <summary>
        /// Tile ID of where the being is stored
        /// </summary>
        public int StorageId;
        public string Name => Mind.Name.DisplayName;
        private WBeingAvatar avatarWidget;
        private WBeingAvatar avatarWidget2;
        // public WBeingAvatar AvatarWidget => avatarWidgetOverride ?? avatarWidget;
        public void UpdateAvatarName() {
            avatarWidget?.UpdateName();
            avatarWidget2?.UpdateName();
        }
        public void SetAvatarAnimatedSprite(string iconId) {
            avatarWidget?.SetAnimatedSprite(iconId);
            avatarWidget2?.SetAnimatedSprite(iconId);
        }
        public void UpdateAvatarStatus() {
            avatarWidget?.UpdateStatus();
            avatarWidget2?.UpdateStatus();
        }
        public void SetAvatarWidget(WBeingAvatar avatar, bool isSecondary) {
            if (isSecondary) {
                avatarWidget2 = avatar;
            } else {
                avatarWidget = avatar;
            }
        }
        public string CategoryTitle {
            get {
                if (IsPlayer) {
                    if (Species.IsBiological) {
                        if (Species.IsAnimal) {
                            return T.BeingGroupAnimal;
                        } else {
                            return T.NounColonist;
                        }
                    } else {
                        return T.NounWorker;
                    }
                } else {
                    if (Body.MindSlot?.IsPartRemoved == true) {
                        return T.GroupOther;
                    }
                    return T.NounEnemy;
                }
            }
        }
        public BeingStorageFlags StorageFlags {
            get {
                BeingStorageFlags flags = BeingStorageFlags.None;
                if (HasStateFlag(BeingState.Alive)) {
                    flags |= BeingStorageFlags.Alive;
                } else {
                    flags |= BeingStorageFlags.Dead;
                }
                if (Species.IsBiological) {
                    flags |= BeingStorageFlags.Biological;
                } else {
                    flags |= BeingStorageFlags.Robotic;
                }
                return flags;
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasStateFlag(BeingState flag) {
            return (StateFlags & flag) == flag;
        }
        public void Tick(GameState S, long ticks) {
            float ageIncr = 1f / Consts.TicksPerYear;
            if (HasStateFlag(BeingState.Alive)) {
                AgeBeing(S, ticks, ageIncr);
                if (!HasStateFlag(BeingState.Hidden)) {
                    BeingUtils.UpdateFlagsFor(S, this);
                    Inventory?.Update();
                    Mind.Health?.Tick(ticks);
                } else if (HasStateFlag(BeingState.Expedition)) {
                    // On expedition: no map Being, so skip flag updates and inventory,
                    // but still run mental health so sanity gravitates normally.
                    Mind.Health?.Tick(ticks);
                }
            }
            #if !PRODUCTION_BUILD
            if (Being != null && Being.IsAlive) {
                // validations
                foreach (var c in Conditions) {
                    if (!c.Value.IsRegistered) {
                        D.Err("Unregistered condition on alive active being! {0}: {1}", c, Being);
                    }
                }
            }
            #endif
        }

        private void AgeBeing(GameState S, long ticks, float ageIncr) {
            // Mind.Age (not Mind.BirthTick) is what every UI age display uses. Mind.Age
            // freezes while a core is detached/in storage, but BirthTick keeps counting
            // wall-clock time, so basing the birthday on BirthTick could report a stale
            // "years old" figure that disagrees with the overview after a core transplant.
            // Trigger and report off Mind.Age's whole-year rollover instead, so the two
            // always agree.
            var mindYearsBefore = Mind.AgeYears(Mind.Age);
            var bodyYearsBefore = Mind.AgeYears(Body.Age);
            Body.Age += ageIncr;
            Mind.Age += ageIncr;
            var mindYearsAfter = Mind.AgeYears(Mind.Age);
            if (mindYearsAfter != mindYearsBefore) {
                UI?.ResetBio();
                if ((Body.Flags & BodyFlags.Missing) == BodyFlags.None) {
                    OnBirthday(S, mindYearsAfter, ticks);
                }
            }
            if (Mind.AgeYears(Body.Age) != bodyYearsBefore) {
                UI?.ResetBio();
            }
        }

        public void OnBirthday(GameState S, int years, long ticks) {
            // D.Warn("OnBirthday: {0} / {1}", this, Being);
            if (Species.IsBiological && !Species.IsWild && Mind.Health != null) {
                S.Sig.AddMentalEffect.Send(this,
                    MentalEffect.Create(Being?.PosIdx ?? -1, ticks,
                        Consts.TicksPer2Days, T.MyBirthday, +25));
                if (!Mind.Faction.IsPlayer) { return; }
                var ctx = S.Query.GetStoryContext.Ask();
                var evt = StoryEventDef.Get("Social/Party");
                if (!Species.IsPet && Mind.Jobs.CanDo(JobTypeIdH.Social)) {
                    var dramaMem = S.Query.GetDramaMemory.Ask();
                    dramaMem.RequestParty(T.NounBirthday, Mind);
                    ctx.ClearRecentSeen(evt, ticks);
                    ctx.PrioritizeEvent(evt, "Birthday");
                }
            }
            if (!Mind.Faction.IsPlayer) { return; }
            var msg = T.EventBirthday_V2.F(Mind.Name.FullName, years);
            Notification.BuildFor(Mind.Owner, IconId.CParty, msg, Consts.TicksPerDay, NotificationType.Info)
                .Group("bday")
                .Show();
            S.Sys.Log.AddLine(msg);
        }

        [SkipDumpInstance]
        public readonly Signal3<BeingData, BeingEnvFlags, BeingEnvFlags> OnEnvChange
            = new("BeingData.OnEnvChange");
        public void UpdateFlags(BeingEnvFlags newFlags) {
            if (newFlags != EnvFlags) {
                // D.Warn("Updating flags {0}->{1} for {2}", Flags, newFlags, this);
                var oldFlags = EnvFlags;
                EnvFlags = newFlags;
                // armor shows outdoors, in unsafe places and in combat, and only then slows
                Inventory?.UpdateShownStatModifiers();
                OnEnvChange.Send(this, oldFlags, newFlags);
            }
        }

        /// <summary>
        /// Mostly for crawlers so they can't go through doors with permissions
        /// while not aggressive.
        /// </summary>
        public void SetHostile(bool hostile) {
            Being.Motion.SetPermission(NavPermission.Friendly, !hostile);
            Being.Motion.SetPermission(NavPermission.Enemy, hostile);
            Being.S.Sig.BeingChanged.Send(Being);
        }

        public void ApplyForActiveBeing(GameState s, bool isLoading) {
            // D.Warn("Applying for active being: {0}", this);
            if (Species.IsWild) {
                SetHostile(true);
            }
            // Being.Equipment?.SetFavColor(Persona.FavColorId);
            s.Sig.ActivateStats.Send(StatsBeing, true);
            Mind.Health?.Initialize();
            Mind.AfterSetFaction(true);
        }

        public void ApplyForSpawnedBeing(GameState s, bool isLoading) {
            // Being.Worker?.UpdateFlags();
            // D.Warn("Applying for spawned being: {0}: {1}", isLoading, this);
            Safety ??= new BeingSafety(s, this);
            if (Mind == null) {
                D.Err("Trying to spawn a being without a mind: {0}", this);
                var name = BeingName.BlankFor(Species);
                Mind = Mind.Blank(name, Species);
                Mind.Name = name;
            } else if (Mind.Name == null) {
                D.Err("Trying to spawn a being without a name on the mind! {0}", this);
                var name = BeingName.BlankFor(Species);
                Mind.Name = name;
            }
            Mind.Name.UpdateName();
            Being.Name.SetName(Mind.Name.ShortName);
            AfterLoad(s, isLoading);
            BeingUtils.UpdateFlagsFor(s, this);
        }

        public void AfterLoad(GameState s, bool isLoading) {
            // Restore stuff that is dynamic, such as AbilitySources
            // D.Warn("Calling after load for {0}: {1}", isLoading, this);
            Body.AfterLoad();
            Inventory?.AfterLoad(isLoading);
            Mind.Traits.AfterLoad();
            // Body.AfterLoad has repopulated the need sources, so the mind holding slot has had
            // its say by now.
            SyncMentalHealthWithNeeds();

            Mind.Traits.ApplyModifiersAfterLoad(s, isLoading);
            Mind.Experience.ApplyModifiersAfterLoad(s, isLoading);

            AfterAbilitiesChanged();
            AfterNeedsChanged(s);
            if (isLoading) {
                RestoreConditions(s);
            }
        }
        // Death stops every condition that did not ask to outlive its owner. Called from both
        // ways a being can die: with an entity (ConditionsComp, on HealthWentToZero) and
        // without one - a stasis sleeper, or any other body that was never on the map.
        public void DeactivateConditionsOnDeath() {
            foreach (var c in Conditions) {
                if (c.Value.TickAfterDeath) { continue; }
                c.Value.Flags |= ConditionFlags.Inactive;
            }
        }
        public void RestoreConditions(GameState s, bool ignoreRegistered = false) {
            foreach (var c in Conditions) {
                if (ignoreRegistered && c.Value.IsRegistered) {
                    continue;
                }
                s.Sig.ConditionAdded.Send(c.Value, false);
            }
        }
        public void AfterNeedsChanged(GameState s) {
            // D.Warn("Needs changed for {0}: {1}", this, D.Dump(NeedSources));
            s.Sig.NeedsChanged.Send(Being);
            StatsBeing.ApplyNeeds(NeedSources);
        }

        // Conscious but without a usable body part that gives Move (e.g. lost both legs).
        // Such a being can't reach a bed or food on its own, so it is cared for like a
        // patient. Incapacitated beings also lack Move, but have their own rescue flow.
        public bool IsBedridden => Being != null && Species.IsBiological && Body.IsAlive
            && !Body.IsIncapacitated
            && Being.Motion.IsImmobilizedBy(ImmobilizeReasonIdH.NoAbility);

        public void AfterAbilitiesChanged() {
            if (Being == null) { return; }
            bool canFly = false;
            bool canMove = false;
            if (!Body.IsIncapacitated) {
                for (int i = 0; i < AbilitySources.Count; i++) {
                    AbilitySource abs = AbilitySources[i];
                    if (!abs.Slot.IsUsableWithParent) { continue; }
                    if (abs.Ability.IdH == AbilityIdH.Move) { canMove = true; }
                    else if (abs.Ability.IdH == AbilityIdH.Fly) { canFly = true; }
                }
            }
            Being.Motion.SetAbleToFly(canFly);
            Being.Motion.Immobilize(ImmobilizeReasonIdH.NoAbility, !canMove);
            Mind.Jobs.AbilitiesChanged(this);
            Being.AIAgent.ReloadActions();
        }

        public void SetBeing(Being being) {
            // D.Warn("Setting being: {0} -> {1}", this, being);
            Being = being;
            if (being != null) {
                being.SetData(this);
                being.Motion.SetBeingData(this);
                being.AIAgent.BeingData = this;
                being.Looks.SetLooks(Species.LooksDef, Looks);
            }
        }
        public bool HasCondition(int idH) {
            return Conditions.ContainsKey(idH);
        }

        private bool conditionsDirty = true;
        private bool workBlockCached;
        private bool controlBlockCached;
        public bool ConditionsPreventColonyWork {
            get {
                if (conditionsDirty) {
                    UpdateConditionEffects();
                }
                return workBlockCached;
            }
        }

        private void UpdateConditionEffects() {
            workBlockCached = false;
            controlBlockCached = false;
            foreach (var c in Conditions.Values) {
                if (c.Condition.PreventsColonyWork) {
                    workBlockCached = true;
                }
                if (c.Condition.PreventsControl) {
                    controlBlockCached = true;
                }
            }
            conditionsDirty = false;
        }

        /// <summary>
        /// Only used for updating EnvFlags, never used directly.
        /// Use Being.AIAgent.DirectControl.IsControllable instead.
        /// </summary>
        public bool ConditionsPreventControl {
            get {
                if (conditionsDirty) {
                    UpdateConditionEffects();
                }
                return controlBlockCached;
            }
        }

        /// <summary>
        /// A being has mental health exactly when it has the MentalHealth need, which its mind
        /// species grants. Surgery and mind swaps are covered by the need's own add and remove
        /// hooks, but a freshly loaded being never goes through an "add", so a save whose mind
        /// no longer grants the need would otherwise keep its sanity forever.
        /// </summary>
        public void SyncMentalHealthWithNeeds() {
            if (Mind == null) { return; }
            if (HasNeed(NeedIdH.MentalHealth)) {
                AINeedMentalHealth.Grant(this);
            } else {
                AINeedMentalHealth.Revoke(this);
            }
        }

        public bool HasNeed(int needIdH) {
            // TODO cache if used frequently
            for (int i = NeedSources.Count - 1; i >= 0; i--) {
                NeedSource n = NeedSources[i];
                if (n.Need.IdH == needIdH) {
                    return true;
                }
            }
            return false;
        }

        public bool AddCondition(GameState S, ConditionState cond) {
            cond.Owner = this;
            var idH = cond.Condition.IdH;
            if (Conditions.TryAdd(idH, cond)) {
                #if DEBUG_CONDITIONS
                D.Warn("Adding condition: {0} | {1}", cond.Condition.NameT, this);
                #endif
                conditionsDirty = true;
                if (cond.Condition.PreventsControl) {
                    if (Being?.AIAgent.DirectControl.IsUnderDirectControl == true) {
                        Being.AIAgent.DirectControl.SetDirectControl(false);
                    }
                }
                S.Sig.ConditionAdded.Send(cond, true);
                NotifyConditionsListChanged();
                NotifyDevelopingConditionsChanged();
                Being?.Looks.OnConditionChange(idH);
                if (cond.Condition.Incapacitates) {
                    Body.CheckCondition(false);
                }
                return true;
            }
            return false;
        }

        public bool RemoveCondition(GameState S, int idH) {
            bool removed = false;
            if (ConditionProgress.Remove(idH)) {
                removed = true;
            }
            if (Conditions.TryGetValue(idH, out var state) && Conditions.Remove(idH)) {
                #if DEBUG_CONDITIONS
                D.Warn("Removing condition: {0} | {1}",
                    ConditionIdH.ResolveName(idH), this);
                #endif
                conditionsDirty = true;
                // A hidden being (stasis pod, stashed raid payload) has its conditions
                // deactivated - unregistered from ConditionsSys - until it is restored. If it
                // is destroyed while hidden (RaidPayload.Discard), there is no registration to
                // drop and no world for the condition's end effects to apply to, so the signal
                // would only make ConditionsSys log a missing Being/Condition pair.
                if (state.IsRegistered) {
                    S.Sig.ConditionRemoved.Send(this, idH);
                }
                Being?.Looks.OnConditionChange(idH);
                Body.CheckCondition(false);
                NotifyConditionsListChanged();
                NotifyDevelopingConditionsChanged();
                removed = true;
            }
            return removed;
        }

        public override string ToString() {
            return $"BeingData[{Mind?.Name?.DisplayName}|{Species?.Name}|{StateFlags}]";
        }

        public bool HasAbility(int abilityId) {
            for (int i = AbilitySources.Count - 1; i >= 0; i--) {
                AbilitySource abs = AbilitySources[i];
                if (abs.Ability.IdH == abilityId && abs.Slot.IsUsableWithParent) {
                    return true;
                }
            }
            return false;
        }

        public bool HasTrait(int traitIdH) {
            return Mind.Traits?.HasTrait(traitIdH) == true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasEnvFlag(BeingEnvFlags flags) {
            return (EnvFlags & flags) == flags;
        }

        public void AfterEntityRemoved(GameState s) {
            s.Sig.ActivateStats.Send(StatsBeing, false);
        }

        public bool SkipSimulate => HasStateFlag(BeingState.Dead) || HasStateFlag(BeingState.Hidden);

        [SkipDumpInstance]
        public string ColonyGreeting {
            get {
                var sb = Caches.StringBuilder;
                sb.Clear();
                sb.AppendLine(T.PersonaLogSpecies_V1.F(Texts.Accented(Species.Name)));
                sb.AppendLine(T.PersonaLogFullname_V1.F(Texts.Accented(Mind.Name.FullName)));
                sb.AppendLine(T.PersonaLogAge_V1.F(Texts.Accented(Body.Age)));
                sb.AppendLine(T.PersonaLogBackstory_V1.F(
                    Mind.Experience.BackstoryFor(this)));
                return sb.ToString();
            }
        }

        public enum SortMode {
            Name,
            ConditionAsc,
            ConditionDesc,
            // Must go last
            Group,
            Unsorted,
        }
        public static SortMode BeingSortMode {
            get {
                var mode = CachedPlayerPrefs.GetInt("being.sort.mode", (int) SortMode.Group);
                return (SortMode) mode;
            }
            set {
                CachedPlayerPrefs.SetInt("being.sort.mode", (int) value);
            }
        }

        private static readonly List<string> sortModeOptions = new();
        public static List<string> SortModeOptions {
            get {
                if (sortModeOptions.Count == 0) {
                    foreach (SortMode opt in Enum.GetValues(typeof(SortMode))) {
                        if (opt == SortMode.Unsorted) { continue; }
                        if (opt == SortMode.Group) { continue; }
                        sortModeOptions.Add(SortTextFor(opt));
                    }
                }
                return sortModeOptions;
            }
        }


        private static string SortTextFor(SortMode sortMode) {
            switch (sortMode) {
                case SortMode.Group: return T.SortGroup;
                case SortMode.Name: return T.SortName;
                case SortMode.ConditionAsc: return $"{T.PartCondition} {GlyphMap.ArrowUp}";
                case SortMode.ConditionDesc: return $"{T.PartCondition} {GlyphMap.ArrowDown}";
                default: return T.Unknown + sortMode.ToString();
            }
        }
        public static IEnumerable<BeingData> SortedBy(GameState s,
                IEnumerable<BeingData> values,
                SortMode sortMode) {
            switch (sortMode) {
                case SortMode.ConditionAsc: {
                    return values.OrderBy(v => {
                        return BodyCondition(v);
                    }).ThenBy((v) => v.Mind.Name.ShortName);
                }
                case SortMode.ConditionDesc: {
                    return values.OrderByDescending(v => {
                        return BodyCondition(v);
                    }).ThenBy((v) => v.Mind.Name.ShortName);
                }
                case SortMode.Group: {
                    return values.OrderBy(v => v.Species.Name)
                        .ThenBy(v => v.Mind.Name.ShortName);
                }
                case SortMode.Name:
                default: {
                    return values.OrderBy(v => v.Mind.Name.ShortName);
                }
            }
        }
        private static float BodyCondition(BeingData v) {
            return v.Body.BodySlot.Quality;
            // return 1f - v.Body.InjuryPercent;
        }
        public bool MatchesSearchQuery(string searchQuery) {
            if (searchQuery == null) { return true; }
            if (Species.Def.SearchNameT.Contains(searchQuery)) { return true; }
            if (Mind != null) {
                if (Strings.ToSearchString(Mind.Name.FullName)
                        .Contains(searchQuery)) {
                    return true;
                }
            }
            return false;
        }
        private bool? isStorable;
        public bool IsStorable() {
            if (isStorable.HasValue) {
                return isStorable.Value;
            }
            if (Being != null) {
                isStorable = Being.HasComponent<StorableBeingComp>();
            } else {
                var cfg = Species.Def.ComponentConfigFor(
                    ComponentIdH.StorableBeing, false);
                isStorable = cfg != null;
            }
            return isStorable.Value;
        }

        public bool GetMostImportantCondition(out ConditionState condition) {
            condition = null;
            Priority condImp = Priority.None;
            foreach (var cnd in Conditions.Values) {
                var imp = cnd.Condition.Importance;
                if (imp == Priority.None) {
                    continue;
                }
                if (imp > condImp) {
                    condition = cnd;
                    condImp = imp;
                } else if (imp == condImp) {
                    if (cnd.Severity > condition.Severity) {
                        condition = cnd;
                    }
                }
            }
            return condition != null;
        }

        public void UnsetAvatar(WBeingAvatar avatar) {
            if (avatarWidget == avatar) {
                avatarWidget = null;
            }
            if (avatarWidget2 == avatar) {
                avatarWidget2 = null;
            }
        }
    }
}
