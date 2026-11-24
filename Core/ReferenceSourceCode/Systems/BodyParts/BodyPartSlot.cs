#if !PRODUCTION_BUILD
// #define DEBUG_DAMAGE
// #define DEBUG_NEEDS
#endif
using System.Collections.Generic;
using Game.Achievements;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.Systems.AI.Combat;
using Game.Systems.Conditions;
using Game.Systems.Stats;
using Game.Utils;
using Game.CodeGen;
using KL.Randomness;
using KL.Utils;
using Unity.Mathematics;
using Game.Systems.Items;
using Game.Systems.Mental;
using UnityEngine;
using Game.Components;
using Game.Input;
using System;
using Game.Data.Factions;
using Game.Systems.Creatures;
using Game.Data.Economy;
using Game.UI;
using Game.Systems.Naming;
using Game.Systems.Traits;
using Game.Systems.Creatures.Surgeries;
using Game.Systems.UIUX;

namespace Game.Systems.BodyParts {
    public sealed class BodyPartSlot : IStatSource, IAttackMethodProvider,
            IAbilitySourceSlot, INeedSourceSlot {
        public BodyPartDef InstalledDef;
        private BodyPartState state;
        public BodyPartState State => state;
        private B<BodyPartState> stateB;
        public B<BodyPartState> StateB => stateB ??= new(state);
        public readonly string Id;
        public int SideH;
        public readonly int IdH;
        public string NameT;
        public string SideT;
        public bool IsBodyPart => true;
        public float Quality => state?.Quality ?? 0f;
        public readonly BodyPartType Type;
        /// <summary>
        /// Effective size of this slot: Type.Size unless the owning species overrides it via
        /// Species.BodyPartSlotSizes. Drives hit weighting, bleed rate, burn chance and raid
        /// protection, so always prefer it over Type.Size for a slot that sits in a body.
        /// </summary>
        public readonly float Size;
        [SkipDumpInstance]
        public BodyPartSlot AttachedToPart;
        [SkipDumpInstance]
        public BodyPartSlot[] AttachedParts;
        public readonly Body Body;
        public bool IsPartRemoved;
        public bool IsDestroyed;
        /// <summary>
        /// True when the part in this slot was put there by a completed surgical operation,
        /// false when it came from being generation, a scenario or the sandbox tools. Body
        /// achievements count only what the player actually operated on, see
        /// <see cref="BodyAchievements"/>.
        /// </summary>
        public bool IsPartSurgicallyInstalled;
        public string SlotName => NameT;
        public string SlotType => T.NounBodyPart;
        public string SlotIcon => Type.IconId;
        public string StatSourceNameT => NameT;
        public string ItemName => InstalledDef == null ? T.Empty : InstalledDef.NameT;
        public IAbilitySourceItem AbilitySourceItem => InstalledDef;
        /// <summary>
        /// The species of the mind currently in this body, or null when there is none. What a
        /// mind holding slot contributes belongs to the mind, so this never falls back to the
        /// body species: borrowing the frame is how a crawler minded human ended up with the
        /// human Fun need and no mental health to go with it.
        /// </summary>
        private Species mindSpecies => Body.Owner.Mind?.Species;
        public void SetState(BodyPartState state) {
            this.state = state;
            stateB?.Set(state);
        }

        public bool IsUsableWithParent {
            get {
                if (state == null) { return false; }
                return state.IsUsableWithParent;
            }
        }

        public INeedSourceItem NeedSourceItem => InstalledDef;

        public bool HasAttachedSlots => !Arrays.IsEmpty(AttachedParts);
        public bool HasAttachedParts {
            get {
                if (!Arrays.IsEmpty(AttachedParts)) {
                    for (int i = AttachedParts.Length - 1; i >= 0; i--) {
                        var ap = AttachedParts[i];
                        if (!ap.IsPartRemoved) {
                            return true;
                        }
                    }
                }
                return false;
            }
        }

        public bool IsUsableSelf => !IsPartRemoved && state.IsUsableSelf;
        public bool HasModerateDamage => !IsPartRemoved && state.HasModerateDamage;
        public bool HasCriticalDamage => !IsPartRemoved && state.HasCriticalDamage;

        /// <summary>
        /// True when this slot can never hold a working part again: the slot itself was
        /// destroyed (DestroySlot surgical failure), or its installed part has an Irreparable
        /// wound (which never heals, see Wound.CanHeal) and also can't be removed and replaced
        /// (welded, or the slot type isn't Removable).
        /// </summary>
        public bool IsBeyondRepair {
            get {
                if (IsDestroyed) { return true; }
                if (IsPartRemoved || state == null || !state.IsDestroyed) { return false; }
                return !CanRemovePart(false);
            }
        }

        public bool HasMissingOrDestroyedParent {
            get {
                if (AttachedToPart == null) {
                    return false;
                }
                if (AttachedToPart.IsPartRemoved) {
                    return true;
                }
                if (AttachedToPart.state.IsDestroyed) {
                    return true;
                }
                return AttachedToPart.HasMissingOrDestroyedParent;
            }
        }

        /// <summary>
        /// The slot this one hangs from, at any distance, that can never hold a working part
        /// again, or null. Nothing below it can work, so a part there can only come out: a
        /// failed surgery destroyed a shoulder, and players kept fitting arms and hands below
        /// it that did nothing (STA-1749).
        /// </summary>
        public BodyPartSlot BeyondRepairAncestor {
            get {
                for (var p = AttachedToPart; p != null; p = p.AttachedToPart) {
                    if (p.IsBeyondRepair) { return p; }
                }
                return null;
            }
        }

        private string DecoratePartName(string name) {
            var hasPendingOp = A.S.Query.FindSurgicalOperation.Ask(this, true) != null;
            if (!hasPendingOp) {
                return name;
            }
            name = $"{Texts.Accented(GlyphMap.TriangleRight, bold: false)} {name}";
            return name;
        }

        public string TooltipLine {
            get {
                string txt;
                if (InstalledDef == null) {
                    string emptyText;
                    if (Type.IsOptional) {
                        emptyText = Texts.Grey(T.Empty);
                    } else {
                        emptyText = Texts.Red(T.Missing);
                    }
                    txt = $"{Texts.Grey(NameT)} {Texts.Yellow(GlyphMap.ArrowRight)} {emptyText}";
                    return DecoratePartName(txt);
                }
                string name = InstalledDef.NameT;

                if (!state.IsUsableSelf) {
                    if (state.IsDestroyed) {
                        name = Texts.Red(Texts.Strike(name));
                    } else {
                        name = Texts.Yellow(Texts.Strike(name));
                    }
                } else {
                    if (!state.IsUsableParent) {
                        name = Texts.Strike(name);
                    }
                }
                var wndCnt = Arrays.Count(state.Wounds);
                var scarCnt = Arrays.Count(state.Scars);
                if (state.IsInfected) {
                    name = $"{name} ({Texts.Red(T.AdjInfected)})";
                }
                var q = Units.PercentageColored(state.Quality, clamp: false);
                string injury = null;
                if (wndCnt > 0) {
                    var sev = Severities.NameForNegative(state.WoundSeverity);
                    if (state.WoundSeverity >= Severity.Critical) {
                        sev = Texts.Red(sev);
                    } else {
                        sev = Texts.Yellow(sev);
                    }
                    injury = sev;
                }
                if (wndCnt > 0 && scarCnt > 0) {
                    name = Texts.Yellow(name);
                    name = $"{name} ({q}, {injury}, {Texts.Yellow(T.NounScars)}: {Texts.Yellow(scarCnt)})";
                } else if (scarCnt > 0) {
                    name = $"{name} ({q}, {Texts.Yellow(T.NounScars)}: {Texts.Yellow(scarCnt)})";
                } else if (wndCnt > 0) {
                    name = Texts.Yellow(name);
                    name = $"{name} ({q}, {injury})";
                } else {
                    name = $"{name} ({q})";
                }
                txt = $"{Texts.Grey(NameT)} {Texts.Yellow(GlyphMap.ArrowRight)} {name}";
                return DecoratePartName(txt);
            }
        }

        public bool IsRequiredForBeingAlive;
        public bool IsEssential;
        public bool HoldsMind;
        public bool IsInternal;

        private readonly List<Stat> AffectedStats = new();
        private BodyPartSlot(string id, Body body, BodyPartType type) {
            Id = id;
            Type = type;
            var slotSizes = body.Owner.Species?.BodyPartSlotSizes;
            if (slotSizes != null && slotSizes.TryGetValue(type.Id, out var sizeOverride)) {
                Size = sizeOverride;
            } else {
                Size = type.Size;
            }
            IsEssential = Type.Flags.HasFlag(BodyPartFlags.Essential);
            HoldsMind = Type.Flags.HasFlag(BodyPartFlags.HoldsMind);
            IsInternal = Type.Flags.HasFlag(BodyPartFlags.Internal);
            IsRequiredForBeingAlive = HoldsMind
                || (IsEssential && body.Owner.Species.IsBiological);
            var sideIdx = id.IndexOf('_');
            if (sideIdx != -1) {
                var sideId = id.Substring(sideIdx + 1);
                SideH = Hashes.S(sideId);
                if (int.TryParse(sideId, out int sideNum)) {
                    SideT = $"#{sideNum}";
                } else {
                    SideT = $"side.{sideId.ToLowerInvariant()}".T();
                }
                NameT = $"{Type.NameT} ({SideT})";
            } else {
                NameT = Type.NameT;
            }
            IdH = Hashes.S(id);
            Body = body;
        }

        public string ShowBodyPartTooltip() {
            if (IsPartRemoved) { return null; }
            var sb = Caches.StringBuilder2;
            sb.Clear();
            // sb.AppendFormat("{0}: <b>{1}</b>\n",
            //     T.PartInstalled,
            sb.AppendFormat(Texts.SubTitle(InstalledDef?.Def?.CodexLink));

            sb.AppendFormat("{0}: <b>{1}</b>\n", T.NounQuality, Units.Percentage(state.Quality, clamp: false));
            sb.AppendFormat("{0}: <b>{1}</b>\n", T.PartCondition, Units.Percentage(state.Condition));
            // what it provides is listed below either way, as what it would give in use: an arm
            // below a destroyed shoulder looked like it worked (STA-1749)
            if (!state.IsUsableSelf) {
                sb.AppendLine(Texts.Red(T.BodyPartUnusable));
            } else if (!state.IsUsableParent && AttachedToPart != null) {
                sb.AppendLine(Texts.Red(T.BodyPartUnusableViaParent_V1.F(AttachedToPart.NameT)));
            }

            var owner = Body.Owner;
            var species = owner?.Mind?.Species ?? owner?.Species;
            InstalledDef?.AddInfo(sb, species, state.Quality, false);

            #if !PRODUCTION_BUILD
            // sb.Append(D.DumpInstance(State));
            #endif
            return sb.ToString();
        }

        /// <returns>true if condition has changed</returns>
        public bool UpdateCondition(bool isLoading, bool updateBody = true,
                bool forceUpdateHierarchy = false) {
            var isUsable = true;
            if (IsPartRemoved) {
                return false;
            }
            // Otherwise we will still get our functions from body parts that
            // are not attached as limbs
            // if ((Type.Flags & BodyPartFlags.Limb) == BodyPartFlags.Limb) {
                if (AttachedToPart == null) {
                    if (IdH != BodyPartSlotIdH.Body) {
                        D.Err("Non-body is not attached to anything! {0}",
                            D.DumpInstance(this));
                        isUsable = false;
                    }
                } else {
                    // a missing parent state is expected since surgery queue
                    isUsable = IsParentUsable();
                }
            // }
            if (state.UpdateCondition(isUsable, Type) || forceUpdateHierarchy) {
                UpdateHierarchy(forceUpdateHierarchy);
                UpdateModifiers();
                UpdateStatSources();
                UpdateNeedSources();
                if (updateBody) {
                    Body.CheckCondition(isLoading);
                }
                return true;
            }
            return false;
        }

        // Limbs hang from their parent and go down with it. Organs sit inside it and only stop
        // when it is gone: a torso wrecked by a beating and starvation must not shut its lungs,
        // kidneys and gut down too.
        private bool IsParentUsable() {
            var parentState = AttachedToPart.state;
            if (parentState == null) { return false; }
            if (IsInternal) { return !parentState.IsDestroyed; }
            return parentState.IsUsableWithParent;
        }

        private void UpdateHierarchy(bool forcePropagate) {
            if (AttachedParts != null) {
                for (int i = AttachedParts.Length - 1; i >= 0; i--) {
                    BodyPartSlot attPart = AttachedParts[i];
                    attPart.UpdateCondition(false, false,
                        forceUpdateHierarchy: forcePropagate);
                }
            }
        }

        public static BodyPartType TypeFor(string slotId) {
            // this could be something like Eye_L
            var idxOfUnderscore = slotId.IndexOf('_');
            string typeId;
            if (idxOfUnderscore != -1) {
                typeId = slotId[..idxOfUnderscore];
            } else {
                typeId = slotId;
            }
            return BodyPartType.Get(typeId);
        }

        public static BodyPartSlot NewFor(string slotId, Body body) {
            var type = TypeFor(slotId);
            if (type == null) {
                D.Err("Could not find body part slot type {0} defined for species {1}",
                    slotId, body.Owner.Species);
                return null;
            }
            var s = new BodyPartSlot(slotId, body, type);
            return s;
        }

        /// <summary>
        /// THIS IS NOT CALLED WHEN GAME IS LOADED! Only when part is installed.
        /// To add stuff after game was loaded see
        ///
        /// </summary>
        /// <see cref="AfterLoad"/>
        /// <param name="def"></param>
        /// <param name="state"></param>
        public bool InstallPart(GameState s, BodyPartDef def, BodyPartState state) {
            if (def.SlotType != Type) {
                D.Err("Trying to install wrong body part into a slot {0} => {1} ({2})",
                    Type.Id, def.SlotType.Id, def);
                return false;
            }
            if (InstalledDef != null) {
                D.Err("Cannot install a part when something else is installed already! Remove the part first: {0}", this);
                return false;
            }
            // D.Warn("Installing part {0} with state {1} in to {2} | {3}", def.Id, state, Body, Body.Owner);
            InstalledDef = def;
            IsPartRemoved = false;
            // The surgical overload below flips this back on once the operation went through.
            IsPartSurgicallyInstalled = false;
            this.state = state;
            stateB?.Set(state);

            if (Type.IdH == BodyPartIdH.Battery) {
                var charge = Body.Owner.StatsBeing.BatteryCharge;
                // let's give some charge
                if (charge.Value < 25f) {
                    charge.UpdateBase(25f);
                }
            }

            if (AddAbilitySources()) {
                Body.Owner.AfterAbilitiesChanged();
            }
            if (AddNeedSources(true)) {
                Body.Owner.AfterNeedsChanged(s);
            }
            AddAttackMethods();
            AddStatSources();
            AddStatModifiers();
            if (Type.IdH == BodyPartIdH.Eye) {
                Body.Owner.Being?.Looks.Rebuild(force: true, clearCache: true);
            }
            ApplyDrivenLook(def, true);
            return true;
        }

        public bool InstallPart(GameState s, ObjData partData, BeingData worker,
                bool canFail, out bool partConsumed, SurgicalOpType opType = SurgicalOpType.Undefined) {
            return InstallPart(s, partData, worker, canFail, out partConsumed, out _, opType, null);
        }

        // failure is the complication rolled for this install (None when it went cleanly).
        // performerLink names an automated device when there is no worker, so its
        // complications still get reported.
        public bool InstallPart(GameState s, ObjData partData, BeingData worker,
                bool canFail, out bool partConsumed, out SurgicalOpFailureType failure,
                SurgicalOpType opType, string performerLink) {
            partConsumed = false;
            failure = SurgicalOpFailureType.None;
            // the part could have been destroyed during swap, then we'll refuse to install the part right away
            if (IsDestroyed || BeyondRepairAncestor != null) { return false; }

            if (canFail && !CheckBodyPartOpSuccess(worker, partData.BodyPart, partData.BodyPartState, isExtract: false, out var rng)) {
                // now we will have to roll how bad the failure was and apply one or more SurgicalOpFailureTypes
                failure = SurgeryUtils.ApplySurgicalOpFailure(rng, worker, partData.BodyPart, partData.BodyPartState, this, isExtract: false, opType: opType, performerLink: performerLink);
            }


            bool wasPartDestroyed = failure == SurgicalOpFailureType.DestroyPart;

            if (wasPartDestroyed) {
                // despawn / deactivate the part
                if (partData.Obj != null) {
                    partData.State = ObjState.Destroyed;
                    s.Objs.Destroy(partData.Obj);
                } else {
                    s.Query.DeactivateObjData.Ask(partData, ObjState.Destroyed);
                }
                partConsumed = true;
            }

            if (wasPartDestroyed) {
                return true;
            }

            if (failure == SurgicalOpFailureType.DestroySlot) {
                IsDestroyed = true;
                UpdateCondition(false, false, forceUpdateHierarchy: true);
                return true;
            }

            if (failure == SurgicalOpFailureType.Rejection) {
                return true;
            }

            if (!wasPartDestroyed) {
                if (HoldsMind && InstalledDef == null) {
                    InsertMind(s, partData, worker);
                }
                if (InstallPart(s, partData.BodyPart, partData.BodyPartState)) {
                    // despawn / deactivate the part
                    if (partData.Obj != null) {
                        partData.State = ObjState.Destroyed;
                        s.Objs.Destroy(partData.Obj);
                    } else {
                        s.Query.DeactivateObjData.Ask(partData, ObjState.Destroyed);
                    }
                    partConsumed = true;
                    UpdateCondition(false, false, forceUpdateHierarchy: true);
                    IsPartSurgicallyInstalled = true;
                    BodyAchievements.OnPartInstalled(this);
                    TrustDeeds.PartInstalled(s, this);
                    return true;
                }
            }
            return false;
        }

        private void InsertMind(GameState s, ObjData partData, BeingData worker) {
            var prevMind = Body.Owner.Mind;
            var o = Body.Owner;
            var m = partData.Mind;
            Rng rng = null;
            if (m == null) {
                // Let's assume this is a blank mind that we crafter or bought
                // we will not generate the whole thing
                rng ??= new Rng(Hashes.Quick(Primes.Prime007, partData.Id));
                D.Warn("Trying to install part that hold mind but mind data is null! Generating new mind: {0} -> {1}",
                    partData, o);
                Species species;
                if (partData.BodyPart.IsBiological && Arrays.Count(partData.BodyPart.TargetSpecies) == 1) {
                    // we have target species for this
                    species = Species.Get(partData.BodyPart.TargetSpecies[0]);
                } else {
                    // robots will just get their species from frame
                    species = Body.Owner.Species;
                }
                m = s.Query.GenBabyMind.Ask(new GenBeingVars(species, BeingType.Newborn), rng);
                s.Query.RegisterMind.Ask(m);
                // A mind grown for this body belongs to whoever owns the body. Pulling a
                // mind-holding part leaves the patient with a Mind.Blank whose faction is
                // Blank, so the surgeon identifies the colony doing the grafting - without
                // this a blank crawler brain would take whatever mind generation happened to
                // default to rather than the colony that installed it.
                m.SetFaction(FactionForGrownMind(prevMind, worker));
            } else {
                Mind.UnlinkFromObj(m, partData);
            }
            if (m.Sex == null && !Arrays.IsEmpty(m.Species.Sexes)) {
                rng ??= new Rng(Hashes.Quick(Primes.Prime007, partData.Id));
                m.Sex = m.Species.RandomSex(rng);
                if (m.AttractedToSex == null) {
                    var orient = m.Species.RandomSexualOrientation(rng);
                    m.AttractedToSex = orient.Attractions(m.Species, m.Sex);
                }
                D.Warn("Setting mind without sex, assigned random: {0}", m);
            }
            m.SetOwner(o);
            m.LoadMentalStats();
            if (m.Health != null) {
                m.Health.Initialize();
                if (m.Health.Sanity.Value == 0f) {
                    D.Warn("Sanity was 0! Will set to random");
                    m.Health.Sanity.UpdateBase(m.Health.SanityBaseline.Value + Rng.URange(-0.15f, 0.15f));
                }
            } else {
                o.StatsBeing.Sanity.IsUnused = true;
                o.StatsBeing.SanityBaseline.IsUnused = true;
                o.StatsBeing.SanityTarget.IsUnused = true;
            }
            // apply the change
            if (m.Faction == null || m.Faction.IsBlank) {
                D.Err("Faction is null after installing mind! {0}", m);
                m.SetFaction(FactionDef.Player);
            } else {
                m.SetFaction(m.Faction);
            }
            m.Name.UpdateName();
            o.Being.Name.SetName(m.Name.ShortName);
            BeingTraits.TransferBodyBoundTraits(prevMind, m);
            s.Sig.MindInstalled.Send(m, partData, Body.Owner);
            BodyAchievements.OnMindInserted(m, partData, Body.Owner);
            Body.Owner.UI.ResetBio();
            // Reset the component's cached MentalHealth even when the incoming mind has none
            // (a crawler brain): going through m.Health would skip exactly that case and leave
            // the component pointing at the mind that was just removed.
            if (o.Being != null && o.Being.TryGetComponent<MentalHealthComp>(out var mhComp)) {
                mhComp.AfterReplaceMind();
            }
        }

        /// <summary>
        /// The faction for a freshly grown mind-holding part: whoever previously owned the
        /// body if that is still known, otherwise the surgeon performing the graft, otherwise
        /// the player as a last resort.
        /// </summary>
        private static FactionDef FactionForGrownMind(Mind prevMind, BeingData worker) {
            var prev = prevMind?.Faction;
            if (prev != null && !prev.IsBlank) {
                return prev;
            }
            var surgeon = worker?.EffectiveFaction;
            if (surgeon != null && !surgeon.IsBlank) {
                return surgeon;
            }
            return FactionDef.Player;
        }

        private bool AddNeedSources(bool expectMissing) {
            // only add need sources when being is spawned
            if (Body.Owner.Being == null) { return false; }
            if (InstalledDef == null) { return false; }
            bool added = false;
            if (HoldsMind) {
                // the mind brings its own mental needs on top of what the part provides
                var sp = mindSpecies;
                // D.Warn("Adding mental need sources for {0}: {1} ({2})",
                //     Body.Owner, sp.Name, D.Dump(sp.AddsNeeds));
                if (sp == null) {
                    D.Err("Mind holding slot has a part installed but no mind species: {0} -> {1}",
                        this, Body.Owner);
                } else {
                    added |= AddNeedSourcesFrom(sp.AddsNeeds, expectMissing);
                }
            }
            added |= AddNeedSourcesFrom(InstalledDef.AddsNeeds, expectMissing);
            return added;
        }

        /// <summary>
        /// Drops everything the mind holding slot contributes on behalf of the mind, so the
        /// incoming one can rebuild it with AddMindSources. Must run while the outgoing mind is
        /// still in place: needs and abilities are keyed by slot and could be torn down at any
        /// time, but stat modifiers are matched by the StatModifier objects cached on the
        /// Species, so after the swap there would be no way to reach the ones already applied.
        /// An empty slot is left alone on purpose: it contributes nothing either way, and it is
        /// the state a mind graft passes through between TryRemovePart and InstallPart. Tearing
        /// the needs down there would revoke the incoming mind mental health for one call and
        /// hand it back a freshly built one. See Mind.SetOwner.
        /// </summary>
        public void RemoveMindSources() {
            if (!HoldsMind || InstalledDef == null) { return; }
            RemoveNeeds();
            RemoveAbilities();
            RemoveStatModifiers();
            RemoveStatSources();
        }

        /// <summary>
        /// Rebuilds what RemoveMindSources dropped, reading the mind that is in the body now.
        /// AddStatSources goes through UpdateModifiers, which puts the stat modifiers back, so
        /// this must not add them again - every modifier would land twice.
        /// </summary>
        public void AddMindSources() {
            if (!HoldsMind || InstalledDef == null) { return; }
            AddNeedSources(false);
            AddAbilitySources();
            AddStatSources();
        }

        private void RemoveStatSources() {
            for (int i = AffectedStats.Count - 1; i >= 0; i--) {
                Stat st = AffectedStats[i];
                st.Sources.Remove(this);
                AffectedStats.RemoveAt(i);
                st.SourcesChanged();
            }
        }

        private bool AddNeedSourcesFrom(string[] needs, bool expectMissing) {
            if (needs == null) { return false; }
            bool added = false;
            foreach (var needId in needs) {
                var src = NeedSource.CreateFor(needId, this);
                if (src.Need != null && !src.ExistsIn(Body.Owner.NeedSources, expectMissing)) {
                    if (Body.Owner.AddNeedSource(src)) {
                        added = true;
                    }
                }
            }
            return added;
        }

        private void AddStatSources() {
            UpdateModifiers();
            if (HoldsMind) {
                var sp = mindSpecies;
                // D.Warn("Adding mental stat sources for: {0} {1} ({2})",
                //     Body.Owner, sp, sp.StatSources);
                AddStatSourcesFrom(sp?.StatSources);
            }
            AddStatSourcesFrom(InstalledDef.StatSources);
        }

        private void AddStatSourcesFrom(StatSourceData[] sources) {
            if (sources == null) { return; }
            var stats = Body.Owner.StatsBeing;
            foreach (var ss in sources) {
                var stat = stats.AddStatSource(ss.StatId, this);
                if (stat != null) {
                    AffectedStats.Add(stat);
                    stat.SourcesChanged();
                    stat.Apply();
                }
            }
        }

        private void UpdateNeedSources() {
            if (!HoldsMind && Arrays.IsEmpty(InstalledDef.AddsNeeds)) { return; }
            // this triggers a bug, let's always add all need sources for installed body parts
            // if (IsUsableWithParent) {
                AddNeedSources(false);
            // } else {
            //     RemoveNeeds();
            // }
        }

        private void UpdateModifiers() {
            if (!HoldsMind && InstalledDef.StatModifiers == null) { return; }
            if (IsUsableWithParent) {
                AddStatModifiers();
            } else {
                RemoveStatModifiers();
            }
        }

        private void UpdateStatSources() {
            if (!HoldsMind && InstalledDef.StatSources == null) { return; }
            var stats = Body.Owner.StatsBeing;
            for (int i1 = stats.AllStats.Length - 1; i1 >= 0; i1--) {
                Stat stat = stats.AllStats[i1];
                if (!Arrays.IsEmpty(stat.Sources)) {
                    for (int i = stat.Sources.Count - 1; i >= 0; i--) {
                        IStatSource ss = stat.Sources[i];
                        if (ss == this) {
                            stat.SourcesChanged();
                        }
                    }
                }
            }
        }

        public void AddAttackMethods() {
            if (InstalledDef.AttackMethods == null) { return; }
            foreach (var ats in InstalledDef.AttackMethods) {
                Body.AddAttackMethod(ats, this);
            }
        }

        private bool AddAbilitySources() {
            bool added = false;
            if (HoldsMind) {
                // Species abilities belong to the mind, exactly like its needs: a crawler mind
                // in a human body cannot Eat or Research just because the body used to.
                // AddNeedSources logs when a mind holding slot has a part but no mind species.
                var sp = mindSpecies;
                // D.Warn("Adding mental ability sources for {0}: {1} ({2})",
                //     Body.Owner, sp.Name, D.Dump(sp.AddsAbilities));
                added |= AddAbilitySourcesFrom(sp?.AddsAbilities);
            }
            added |= AddAbilitySourcesFrom(InstalledDef.AddsAbilities);
            return added;
        }

        private bool RemoveAbilities() {
            var srcs = Body.Owner.AbilitySources;
            bool removed = false;
            for (int i = srcs.Count - 1; i >= 0; i--) {
                AbilitySource src = srcs[i];
                if (src.Slot == this) {
                    srcs.RemoveAt(i);
                    removed = true;
                }
            }
            return removed;
        }

        private bool AddAbilitySourcesFrom(string[] abilities) {
            if (abilities == null) { return false; }
            bool added = false;
            for (int i = abilities.Length - 1; i >= 0; i--) {
                string ab = abilities[i];
                var src = AbilitySource.CreateFor(ab, this);
                if (src.Ability != null) {
                    Body.Owner.AbilitySources.Add(src);
                    added = true;
                }
            }
            return added;
        }

        public bool CanRemovePart(bool warn) {
            if (InstalledDef == null) {
                return false;
            }
            if ((state.Flags & BodyPartStateFlags.Welded) == BodyPartStateFlags.Welded) {
                if (warn) {
                    D.Err("Trying to remove a welded body part! {0}", this);
                }
                return false;
            }
            if ((Type.Flags & BodyPartFlags.Removable) != BodyPartFlags.Removable) {
                if (warn) {
                    D.Err("Trying to remove non-removable body part! {0}", this);
                }
                return false;
            }
            // if (!Arrays.IsEmpty(AttachedParts)) {
            //     for (int i = AttachedParts.Length - 1; i >= 0; i--) {
            //         var ap = AttachedParts[i];
            //         if (!ap.IsPartRemoved) {
            //             if (warn) {
            //                 D.Err("Trying to remove a part that has something attached! {0} -> {1}",
            //                     this, ap);
            //             }
            //             return false;
            //         }
            //     }
            // }
            return true;
        }

        public bool TryRemovePart(GameState s, out Obj spawnedPart, BeingData worker, bool canFail,
                out SurgicalOpFailureType failure, bool isForSwap = false,
                SurgicalOpType opType = SurgicalOpType.Undefined) {
            return TryRemovePart(s, out spawnedPart, worker, canFail, out failure, isForSwap, opType, null);
        }

        // performerLink names an automated device when there is no worker, so its
        // complications still get reported.
        public bool TryRemovePart(GameState s, out Obj spawnedPart, BeingData worker, bool canFail,
                out SurgicalOpFailureType failure, bool isForSwap,
                SurgicalOpType opType, string performerLink) {

            failure = SurgicalOpFailureType.None;
            if (InstalledDef == null || state == null) {
                D.Err("Trying to remove body part with null def / state: {0}", opType);
                spawnedPart = null;
                return false;
            }
            if (canFail && !CheckBodyPartOpSuccess(worker, InstalledDef, state, isExtract: true, out var rng)) {
                // now we will have to roll how bad the failure was and apply one or more SurgicalOpFailureTypes
                failure = SurgeryUtils.ApplySurgicalOpFailure(rng, worker, InstalledDef, state, this, isExtract: true, opType: opType, performerLink: performerLink);
            }

            bool isWelded = failure == SurgicalOpFailureType.WeldPart;

            if (!isWelded && TryRemovePart(out var part, out var _state, isForSwap)) {
                if (part == null || part.Def == null) {
                    spawnedPart = null;
                    return false;
                }

                var data = s.Query.GenObjFull.Ask(part.Def, Rng.Unseeded);
                data.BodyPart = part;
                data.BodyPartState = _state;
                var removedMind = TransferMindAfterPartRemoval(s, data);
                if (Body.Owner.Being == null) {
                    spawnedPart = s.Objs.Create(Body.Owner.LastPosition.Exact, data);
                } else {
                    spawnedPart = s.Objs.Create(Body.Owner.Being.Position, data);
                }
                if (removedMind != null) {
                    removedMind.ObjId = spawnedPart.Data.Id;
                }
                if (SurgeryUtils.IsSwappedOutPartDamage(failure, true, opType, this)) {
                    s.Sys.Log.AddLine(spawnedPart, SurgeryUtils.DescribeFailure(failure, part));
                }

                // D.Warn("state quality vs data quality: {0} == {1}", state.Quality, data.Quality);
                return true;
            }
            spawnedPart = null;
            return false;
        }

        private bool CheckBodyPartOpSuccess(BeingData worker, BodyPartDef part,
                BodyPartState state, bool isExtract, out Rng rng) {
            var chance = EstimateChanceOfSuccess(worker, state, isExtract, isEstimate: false);
            var h = Hashes.Quick(isExtract ? Primes.Prime010 : Primes.Prime002, Body.Owner.Id);
            h = Hashes.Quick(h, Type.IdH);
            if (part != null) {
                h = Hashes.Quick(h, part.IdH);
            }
            // every tick outcome can be different, but we want repeatable outcome when it happens
            // for the same part in the same tick
            h = Hashes.Quick(h, (int) G.Ticks);
            rng = new Rng(h);

            return rng.Chance(chance);
        }

        public float EstimateChanceOfSuccess(BeingData worker, BodyPartState state, bool isExtract, bool isEstimate) {
            float chance;
            float risk = isExtract ? Type.ExtractRisk : Type.ImplantRisk;
            if (risk <= 0f) {
                risk = isExtract ? Tunables.BodyPartRisk_Extract : Tunables.BodyPartRisk_Implant;
            }
            if (isExtract && HoldsMind && A.S.BB.Get(BBKeyH.CorticalOverride, false)) {
                // Cortical Override: pulling a living mind out of its body stops
                // being butchery and becomes a supported procedure.
                risk -= Tunables.CorticalOverrideRiskReduction;
            }
            int skill = 0;
            if (worker != null) {
                var skillId = Body.Owner.Species.IsBiological ? JobTypeIdH.Medical : JobTypeIdH.Robotics;
                if (worker.Mind.Jobs.TryJob(skillId, out var job)) {
                    skill = job.Level;
                }
            }
            if (!isEstimate || worker != null) {
                var skillEffectId = isExtract
                    ? CurveIdH.Surgery_BodyPartExtract_SkillEffect
                    : CurveIdH.Surgery_BodyPartImplant_SkillEffect;
                risk -= CurveDef.FastGet(skillEffectId).Evaluate(skill);
            }
            var c1Id = isExtract ? CurveIdH.Surgery_BodyPartExtract_QualityEffect : CurveIdH.Surgery_BodyPartImplant_QualityEffect;
            var qualitySuccessMul = state == null ? 1f : CurveDef.FastGet(c1Id).Evaluate(state.Quality);
            var c2id = isExtract ? CurveIdH.Surgery_BodyPartExtract_ConditionEffect : CurveIdH.Surgery_BodyPartImplant_ConditionEffect;
            var condSuccessMul = state == null ? 1f : CurveDef.FastGet(c2id).Evaluate(state.Condition);
            var ageSuccessMul = CurveDef.FastGet(CurveIdH.Surgery_SurgerySuccessChanceMul_AgePercent).Evaluate(Body.AgePercent);
            var fatigueSuccessMul = CurveDef.FastGet(CurveIdH.Surgery_SurgerySuccessChanceMul_SurgicalFatigue).Evaluate(Body.Owner.StatsBeing.SurgicalFatigue.Value);
            chance = 1f - risk;
            chance *= ageSuccessMul;
            chance *= qualitySuccessMul;
            chance *= condSuccessMul;
            chance *= fatigueSuccessMul;
            // the surgeon's hands, eyes and the light at the operating spot, so a maimed or
            // half-blind surgeon, or one working in the dark, botches more operations
            if (worker?.StatsBeing != null) {
                chance *= worker.StatsBeing.SurgerySuccess.Value;
            }
            chance = Mathf.Clamp(chance, Tunables.SurgerySuccessChanceMin, Tunables.SurgerySuccessChanceMax);
            return chance;
        }

        /// <returns>Removed mind (old one)</returns>
        public Mind TransferMindAfterPartRemoval(GameState s, ObjData data) {
            if (!HoldsMind) { return null; }
            BeingName bname;

            var removedMind = Body.Owner.Mind;
            removedMind.FormerOwnerId = Body.Owner.Id;
            Mind.RegisterIfNecessary(s, removedMind);

            // save all stats
            removedMind.SaveMentalStats();

            if (data != null) {
                Mind.LinkToObj(removedMind, data);
                removedMind.Owner = null;
                bname = BeingName.Copy(data.Mind.Name);
            } else {
                bname = BeingName.BlankFor(null);
            }
            var blankMind = Mind.Blank(bname, Body.Owner.Species);
            blankMind.SetOwner(Body.Owner);
            blankMind.PreviousId = removedMind.Id;
            BeingTraits.TransferBodyBoundTraits(removedMind, blankMind);
            Body.Owner.Being?.Name.SetName(Body.Owner.Mind.Name.ShortName);
            s.Sig.MindRemoved.Send(removedMind, Body.Owner, data);
            Body.Owner.UI.ResetBio();
            return removedMind;
        }

        /// <returns>false if body part has other parts attached</returns>
        public bool TryRemovePart(out BodyPartDef partDef, out BodyPartState _state,
                bool isForSwap = false) {
            var part = InstalledDef;
            partDef = part;
            _state = state;
            var isDisposal = Body.HasFlag(BodyFlags.Missing);
            if (state?.IsInfected == true) {
                state.SetQuality(0f);
            }
            if (!isDisposal && !isForSwap && !CanRemovePart(true)) {
                return false;
            }
            // D.Warn("Removing part {0} with state {1} from {2} | {3}",
            //     part?.Id, state, Body, Body.Owner);
            if (!isDisposal) {
                RemoveStatModifiers();
            }
            InstalledDef = null;
            IsPartRemoved = true;
            IsPartSurgicallyInstalled = false;
            if (part == null) {
                return false;
            }
            if (isDisposal) {
                return true;
            }
            bool removedNeed = false;
            bool removedAb = RemoveAbilities();
            if (HoldsMind || !Arrays.IsEmpty(partDef.AddsNeeds)) {
                removedNeed = RemoveNeeds();
            }
            RemoveStatSources();

            if (!isForSwap && removedAb) {
                Body.Owner.AfterAbilitiesChanged();
            }
            if (removedNeed) {
                Body.Owner.AfterNeedsChanged(The.R.State);
            }
            Body.RemoveAttackMethodsFor(this);
            state = null;
            stateB?.Set(null);
            UpdateHierarchy(forcePropagate: true);
            if (!isForSwap) {
                Body.CheckCondition(!Ready.Gen);
            }
            if (Type.IdH == BodyPartIdH.Eye) {
                Body.Owner.Being?.Looks.Rebuild(force: true, clearCache: true);
            }
            ApplyDrivenLook(part, false);
            return true;
        }

        /// <summary>
        /// Applies/clears the look part a BodyPartDef drives while installed (LooksPart/
        /// LooksSprite). No-op for parts that don't declare a LooksPart, or on a species whose
        /// looks have no matching part (e.g. a Tail body part on a species that bakes its tail
        /// into the body sprite). See Docs/Architecture/Systems/BeingLooks.md.
        /// </summary>
        private void ApplyDrivenLook(BodyPartDef def, bool installed) {
            if (def?.LooksPartType == null) { return; }
            var data = Body.Owner;
            var looks = data?.Looks;
            if (looks == null) { return; }
            var lp = looks.Get(def.LooksPartType);
            if (lp == null) { return; }
            lp.SpriteId = installed
                ? BeingLooks.SpriteForBodyType(looks.BodyType, def.LooksSprite)
                : null;
            data.Being?.Looks.Rebuild(force: true, clearCache: true);
        }

        private bool RemoveNeeds() {
            var nsrc = Body.Owner.NeedSources;
            bool removedNeed = false;
            for (int i = nsrc.Count - 1; i >= 0; i--) {
                var src = nsrc[i];
                if (src.Slot == this) {
                    #if DEBUG_NEEDS
                    D.Warn("Removing need source: {0} -> {1}", src, Body.Owner);
                    #endif
                    nsrc.RemoveAt(i);
                    removedNeed = true;
                }
            }

            return removedNeed;
        }

        public bool IsLoaded;


        public void AfterLoad() {
            if (IsLoaded) {
                return;
            }
            IsLoaded = true;
            if (InstalledDef != null) {
                // has to go before restoring statssys data
                bool isParentUsable = true;
                if (AttachedToPart != null) {
                    if (!AttachedToPart.IsLoaded) {
                        AttachedToPart.AfterLoad();
                    }
                    isParentUsable = IsParentUsable();
                }
                state.UpdateCondition(isParentUsable, Type);
                AddAbilitySources();
                AddStatSources();
                // this produces a bug where need sources are never added!
                // if (IsUsableWithParent) {
                    AddNeedSources(true);
                // }
                AddAttackMethods();
                ApplyDrivenLook(InstalledDef, true);
            }
        }
        private void AddStatModifiers() {
            if (InstalledDef == null) { return; }
            if (HoldsMind) {
                AddStatModifiersFrom(mindSpecies?.StatModifiers);
            }
            AddStatModifiersFrom(InstalledDef.StatModifiers);
        }
        private void AddStatModifiersFrom((int, StatModifier)[] mods) {
            if (mods == null) { return; }
            var stats = Body.Owner.StatsBeing;
            foreach (var ss in mods) {
                var statId = ss.Item1;
                if (stats.Find(statId, out var stat)) {
                    stat.TryAddModifier(ss.Item2);
                }
            }

        }
        private void RemoveStatModifiers() {
            if (InstalledDef == null) { return; }
            if (HoldsMind) {
                RemoveStatModifiersFrom(mindSpecies?.StatModifiers);
            }
            RemoveStatModifiersFrom(InstalledDef.StatModifiers);
        }

        private void RemoveStatModifiersFrom((int, StatModifier)[] mods) {
            if (mods == null) { return; }
            var stats = Body.Owner.StatsBeing;
            foreach (var ss in mods) {
                var statId = ss.Item1;
                if (stats.Find(statId, out var stat)) {
                    stat.RemoveModifier(ss.Item2, false);
                }
            }
        }

        public void UpdateStat(Stat stat) {
            if (InstalledDef == null) {
                D.Err("Trying to update stat when installed def is null for slot: {0}. Stat: {1}",
                    Id, stat);
                return;
            }
            if (!HoldsMind && InstalledDef.StatSources == null) {
                D.Err("Trying to update stat when installed def has no stat sources: {0}. Installed: {1}. Stat: {2}",
                    Id, InstalledDef.Id, stat);
                return;
            }
            if (GetStatBaseFor(stat, state, out float toAdd, out float limit)) {
                stat.CompoundBase(toAdd, limit, expectNonZero: IsUsableWithParent);
            }
        }
        private bool GetStatBaseFor(Stat stat, BodyPartState state,
                out float toAdd, out float limit) {
            if (HoldsMind) {
                if (TryGetStatBaseFromSources(stat, state, mindSpecies?.StatSources, out toAdd, out limit)) {
                    return true;
                }
            }
            if (TryGetStatBaseFromSources(stat, state, InstalledDef.StatSources, out toAdd, out limit)) {
                return true;
            }
            D.Err("Could not get stat base from body part {0} for stat {1}", Id, stat);
            return false;
        }

        private bool TryGetStatBaseFromSources(Stat stat, BodyPartState state,
                StatSourceData[] sources, out float toAdd, out float limit) {
            if (state == null || Arrays.IsEmpty(sources)) {
                toAdd = 0f;
                limit = 0f;
                return false;
            }
            try {
                for (int i = sources.Length - 1; i >= 0; i--) {
                    StatSourceData src = sources[i];
                    if (src.StatId == stat.Id) {
                        var amt = BodyPartStatMath.WithQuality(src, state.Quality);
                        if (BodyPartStatMath.DependsOnState(src)) {
                            if (state.IsUsableSelf && state.IsUsableParent) {
                                amt = BodyPartStatMath.WithCondition(src, amt, state.Condition);
                                toAdd = amt;
                                if (src.Amount == 0f) {
                                    D.Err("Amount is 0 for stat source: {0} -> {1}. Stat: {2}",
                                        this, src, stat);
                                }
                            } else {
                                // D.Warn("Amount is 0 for unusable source: {0} -> {1}. Stat: {2}",
                                //     this, src, stat);
                                toAdd = 0f;
                            }
                        } else {
                            toAdd = amt;
                            if (toAdd == 0f) {
                                D.Err("Amount is 0 for stat source with 0 stat mul: {0} -> {1}. Stat: {2}",
                                    this, src, stat);
                            }
                        }
                        limit = src.CompoundLimit;
                        return true;
                    }
                }
            } catch (Exception ex) {
                D.LogEx(ex, "Failed loading stat base from sources: {0} | State: {1} | Sources: {2}",
                    stat, state, sources?.Length);
            }
            toAdd = 0f;
            limit = 0f;
            return false;
        }

        public bool GetEffectFor(Stat stat, out float toAdd, out float limit) {
            return GetStatBaseFor(stat, state, out toAdd, out limit);
        }

        // What this part will add to the stat once its being spawns and AfterLoad registers the
        // part's stat sources, and 0 once it is registered. A raider is priced before it spawns
        // (CombatRaid.ProtectionOf), and a bot's armor is mostly its parts' stat sources.
        public float PendingStatEffect(Stat stat) {
            if (InstalledDef == null || (stat.Sources != null && stat.Sources.Contains(this))) {
                return 0f;
            }
            if (HoldsMind && TryGetStatBaseFromSources(stat, state, mindSpecies?.StatSources,
                    out var mindAdd, out _)) {
                return mindAdd;
            }
            return TryGetStatBaseFromSources(stat, state, InstalledDef.StatSources,
                out var add, out _) ? add : 0f;
        }

        public override string ToString() {
            return $"BodyPartSlot[{Type}->{InstalledDef}]";
        }

        public bool HasRequiredAbilities(CombatUnit unit) {
            return IsUsableWithParent;
        }

        /// <returns>true if wound was added or changed</returns>
        public bool TryAddOrUpdateWound(INamedEntity source, DamageType damageType,
                Severity sev, long ticks, float force,
                WoundFlags flags = WoundFlags.None, int conditionId = 0) {
            if (IsPartRemoved || InstalledDef == null) { return false; }
            if ((Body.Flags & BodyFlags.Invincible) == BodyFlags.Invincible) {
                return false;
            }
            if (TryGetWound(damageType, out var w)) {
                if (InstalledDef.IsIndestructible) {
                    if (sev >= Severity.Irreparable) {
                        sev = Severity.Critical;
                    }
                } else {
                    if (sev == Severity.Fatal && !IsRequiredForBeingAlive) {
                        sev = Severity.Irreparable;
                    } else if (sev == Severity.Irreparable && IsRequiredForBeingAlive) {
                        sev = Severity.Fatal;
                    }
                }
                if (w.Severity < sev || w.Flags != flags) {
                    w.Severity = sev;
                    w.Flags = flags;
                    w.HealAmt = 0;
                    if (source != null) {
                        w.Source = source.EntityName;
                    }
                    var wasAlive = Body.IsAlive;
                    if (InstalledDef.IsBiological) {
                        Wound.UpdateScarSeverity(ref w);
                    }
                    state.UpdateWound(w);
                    UpdateCondition(!Ready.Gen);
                    if (wasAlive && !Body.IsAlive) {
                        LogCauseOfDeath(w);
                    }
                    return true;
                }
                return false;
            } else {
                return TryAddWound(source, damageType, sev, ticks, force, out _, flags, conditionId);
            }
        }

        /// <returns>true if flags changed</returns>
        public bool TrySetWoundFlags(DamageType type, WoundFlags flags) {
            if (TryGetWound(type, out var w)) {
                if (w.Flags != flags) {
                    w.Flags = flags;
                    state.UpdateWound(w);
                    UpdateCondition(false, false);
                    return true;
                }
            }
            return false;
        }

        public bool TryGetWound(DamageType type, out Wound w) {
            if (state?.Wounds == null) {
                w = default;
                return false;
            }
            for (int i = state.Wounds.Count - 1; i >= 0; i--) {
                var wnd = state.Wounds[i];
                if (wnd.Type == type) {
                    w = wnd;
                    return true;
                }
            }
            w = default;
            return false;
        }
        // by how many levels of severity can we increase a wound
        private static Curve severityCurve;
        // whether a hit wounds at all, from the same damage over defense ratio
        private static Curve woundChanceCurve;
        // armor never makes a being immune, and nothing lands every time
        private const float MinWoundChance = 0.05f;
        private const float MaxWoundChance = 0.95f;
        // the best armor still leaves some weak spots
        private const float MaxWeakPointCover = 0.9f;
        // keeps the ratio finite for a part with no resistance and no armor
        private const float MinDefense = 1f;

        // penetration: the share of the worn armor the weapon ignores (AttackMethod
        // ArmorPenetration). The part's own resistance is never penetrated. A share, not a flat
        // amount off each armor layer's rating, keeps the damage over defense ratio this model
        // is built on.
        public byte GetHitSeverityIncrease(DamageType type, float dmg, float penetration = 0f) {
            #if DEBUG_DAMAGE
            if (dmg == 0f) {
                D.Warn("Zero damage!");
            }
            #endif
            if (IsPartRemoved || dmg == 0f) {
                return 0;
            }

            var dmgRes = InstalledDef.DamageResistanceFor(type, state.Quality);
            float armor = 0f;
            var stats = Body.Owner.StatsBeing;
            switch (type) {
                case DamageType.Sharp: {
                        armor = stats.ArmorSharp.Value;
                        break;
                    }
                case DamageType.Blunt: {
                        armor = stats.ArmorBlunt.Value;
                        break;
                    }
                case DamageType.Energy: {
                        armor = stats.ArmorEnergy.Value;
                        break;
                    }
                case DamageType.Explosive:
                case DamageType.Piercing: {
                        armor = stats.ArmorPiercing.Value;
                        break;
                    }
            }
            if (penetration > 0f) {
                armor *= 1f - Mathf.Clamp01(penetration);
            }
            // higher misfortune = higher chance to receive damage to the body part
            var misfortune = Body.Owner.IsFriendly ? 0.75f : 1.25f;
            // Good armor closes its weak spots: fewer gaps and fewer crits get through
            // (WeakPointCover). Both used to ignore armor entirely, so a serious wound was as
            // likely in the best armor as in a basic suit.
            var exposed = 1f - Mathf.Clamp(stats.WeakPointCover.Value, 0f, MaxWeakPointCover);
            // A lucky hit finds a gap in the armor, which then counts for less in both the
            // severity and the chance to wound. (It used to halve the severity instead, which
            // turned the attacker's luckiest hits into glancing blows.)
            if (Rng.UChance(0.1f * misfortune * exposed)) {
                armor *= 0.5f;
                #if DEBUG_DAMAGE
                D.Warn("Lucky! armor negated by 50% -> {0}", armor);
                #endif
            } else if (Rng.UChance(0.01f * misfortune * exposed)) {
                armor *= 0.05f;
                #if DEBUG_DAMAGE
                D.Warn("Lucky! armor negated by 95% -> {0}", armor);
                #endif
            }
            // A crit is twice the damage, so armor still counts against it. It used to add a
            // flat +2 severity and skip the wound chance, the same against any armor.
            var isCrit = Rng.UChance(0.08f * misfortune * exposed);
            if (isCrit) {
                dmg *= 2f;
                #if DEBUG_DAMAGE
                D.Warn("Lucky! Critical hit, damage doubled -> {0}", dmg);
                #endif
            }
            // Severity follows the damage over the defense (DefenseRatioSeverity). It used to be
            // the damage rating minus the defense rating, which saturated: once a weapon beat a
            // defense by 5 steps every hit maxed out and armor stopped mattering, and a light
            // weapon could never break even a weak part like an eye.
            var defense = Mathf.Max(MinDefense, dmgRes + armor);
            var ratio = dmg / defense;
            severityCurve ??= CurveDef.FastGet(CurveIdH.Attacks_DefenseRatioSeverity);
            var sevValue = severityCurve.Evaluate(ratio);
            // rounded up with the chance of the fraction: a fixed weapon against a fixed part
            // still varies, and the outcome moves smoothly with the numbers instead of flipping
            // at a rounding edge
            var effectiveAttackRating = (int) sevValue;
            if (Rng.UChance(sevValue - effectiveAttackRating)) {
                effectiveAttackRating++;
            }
            if (isCrit && effectiveAttackRating < (int) Severity.Irreparable) {
                effectiveAttackRating++;
            }
            if (effectiveAttackRating < 1) {
                effectiveAttackRating = 1;
            } else if (effectiveAttackRating > (int) Severity.Fatal) {
                effectiveAttackRating = (int) Severity.Fatal;
            }
            byte maxSevIncrease = (byte) effectiveAttackRating;
            #if DEBUG_DAMAGE
            D.Warn("attack attempt: {0}, dmg: {1}, DamRes: {2}, arm: {3}, defense: {4} MaxSev: {5}. BP: {6}",
                type, dmg, dmgRes, armor, defense, maxSevIncrease, this);
            #endif
            // Whether the hit wounds at all follows the same ratio (DefenseRatioWoundChance), so
            // every armor point counts the same way twice over. It used to subtract the armor
            // from the damage, which made a cliff: about 95% until the armor nearly matched the
            // damage, then 10% within a few points, and nothing past that mattered.
            woundChanceCurve ??= CurveDef.FastGet(CurveIdH.Attacks_DefenseRatioWoundChance);
            var dmgChance = Mathf.Clamp(woundChanceCurve.Evaluate(ratio),
                MinWoundChance, MaxWoundChance);
            if (Rng.UChance(dmgChance)) {
                #if DEBUG_DAMAGE
                D.Warn("Dealing dmg: {0}/{1}. Ch: {2}. MaxSev: {3} | {4}",
                    dmg, defense, dmgChance, maxSevIncrease, this);
                #endif
                return maxSevIncrease;
            }
            #if DEBUG_DAMAGE
            D.Warn("Skipping dmg: {0}/{1}. Ch: {2}. MaxSev: {3} | {4}",
                dmg, defense, dmgChance, maxSevIncrease, this);
            #endif
            return 0;
        }

        public bool AddScar(Scar scar) {
            if (InstalledDef == null) {
                return false;
            }
            var qualityBefore = state.Quality;
            state.AddScar(scar);
            if (state.Quality != qualityBefore) {
                OnQualityChanged();
            }
            return true;
        }

        // Quality only feeds stats when they are recalculated, and a quality change on its own
        // doesn't trigger that the way a condition change does
        public void OnQualityChanged() {
            if (IsPartRemoved) { return; }
            UpdateStatSources();
            UpdateAttackMethodsQuality();
        }

        private void UpdateAttackMethodsQuality() {
            var quality = state.Quality;
            for (int i = Body.AttackMethods.Count - 1; i >= 0; i--) {
                AttackMethod am = Body.AttackMethods[i];
                if (am.Provider == this) {
                    // D.Warn("Adjusting attack method quality {0}->{1} | {2}",
                    //     am.Stats.Quality.Value, quality, am);
                    am.Stats.Quality.UpdateBase(quality);
                    foreach (var stat in am.Stats.AllStats) {
                        stat.Apply();
                    }
                }
            }
        }


        // maxSeverity: the worst the wound may end at, unless it already was worse. A hit capped
        // by its weapon's FatalHitChanceMul must not escalate past the cap.
        public bool TryAddWound(INamedEntity source, DamageType type,
                Severity sev, long when, float force,
                out Wound wound, WoundFlags flags = WoundFlags.None, int conditionId = 0,
                Severity maxSeverity = Severity.Fatal) {
            if (InstalledDef == null || sev < Severity.Mild) {
                wound = default;
                return false;
            }
            if ((Body.Flags & BodyFlags.Invincible) == BodyFlags.Invincible) {
                wound = default;
                return false;
            }

            var wasAlive = Body.IsAlive;

            // 1. get the right wound, or create one
            wound = state.WoundFor(type);
            wound.AddCausingCondition(conditionId);
            if (source != null) {
                wound.Source = source.EntityName;
            } else {
                D.Err("WoundSource is null!");
            }

            // some parts aren't damageable beyond critical
            var isIndestructible = InstalledDef.IsIndestructible;

            bool canBeFatal = isIndestructible ? false : Rng.UChance(force - 0.5f);
            var sevBefore = wound.Severity;
            var wasSettled = wound.IsSettled;
            sev = Severities.Accumulate(wound.Severity, sev, force, canBeFatal);
            if (sev > maxSeverity && sev > sevBefore) {
                sev = Severities.Bigger(maxSeverity, sevBefore);
            }

            // Irreparable and Fatal are one rung, named by whether the slot can kill. Done after
            // Accumulate, whose own escalation may land on either.
            if (isIndestructible) {
                if (sev >= Severity.Irreparable) {
                    sev = Severity.Critical;
                }
            } else if (sev == Severity.Fatal && !IsRequiredForBeingAlive) {
                sev = Severity.Irreparable;
            } else if (sev == Severity.Irreparable && IsRequiredForBeingAlive) {
                sev = Severity.Fatal;
            }
            wound.Severity = sev;
            if (sev > sevBefore || sevBefore >= Severity.Irreparable) {
                // a worse wound starts healing over, as in TryAddOrUpdateWound, and a destroyed
                // part hit again hurts afresh (WoundPain)
                wound.HealAmt = 0f;
            }

            if ((InstalledDef.Flags & BodyPartFlags.Biological) == BodyPartFlags.Biological) {
                var bCapLiters = Body.Owner.Species.BloodCapacityLiters;
                var chance = Wound.GetBleedChance(wound.Severity, type, bCapLiters,
                    out var rateLitersPerH);
                if (Rng.UChance(chance)) {
                    // already a share of the blood capacity: the same wound costs every
                    // species the same part of its blood (it used to scale by capacity
                    // twice, so a human bled seven times a dog's share)
                    rateLitersPerH *= math.lerp(0.05f, 0.25f, Size);
                    wound.BleedLitersPerHour = Maths.Max(
                        wound.BleedLitersPerHour, rateLitersPerH);
                }
            }

            if (wound.CreatedAt == 0) {
                wound.CreatedAt = when;
            }
            wound.Flags = flags;
            if (InstalledDef.IsBiological) {
                Wound.UpdateScarSeverity(ref wound);
            }
            state.UpdateWound(wound);
            if (!UpdateCondition(!Ready.Gen) && wasSettled) {
                // nothing about the part changed, but it hurts again (WoundPain), so the
                // being is injured again
                Body.CheckCondition(!Ready.Gen);
            }
            Body.Owner.OnWoundsChanged();
            if (wasAlive && !Body.IsAlive) {
                LogCauseOfDeath(wound);
            }
            return true;
        }

        private void LogCauseOfDeath(Wound wound) {
            // D.Warn("Fatal wound inflicted! {0} | {1}", wound, Body.Owner);
            if (Body.Owner.Mind.IsBlank) {
                D.Err("Mind is blank when inflicting fatal wound: {0}", Body.Owner);
            } else {
                Body.Owner.Mind.OnDeath(wound.Source, G.Ticks);
            }
        }

        public ObjData BetterPart(ObjData a, ObjData b) {
            var aPart = a.BodyPart;
            var bPart = b.BodyPart;
            var aState = a.BodyPartState;
            var bState = b.BodyPartState;

            if (!aPart.IsCompatibleWith(this, out var isAVanilla)) {
                return b;
            }
            if (!bPart.IsCompatibleWith(this, out var isBVanilla)) {
                return a;
            }
            if (aState.WoundSeverity > bState.WoundSeverity) {
                return b;
            }
            if (bState.WoundSeverity > aState.WoundSeverity) {
                return a;
            }
            if (aState.Condition > bState.Condition) {
                return a;
            }
            if (bState.Condition > aState.Condition) {
                return b;
            }
            if (isAVanilla && !isBVanilla) {
                return a;
            }
            if (isBVanilla && !isAVanilla) {
                return b;
            }
            var marketA = MarketItem.FindFor(a.Def);
            var marketB = MarketItem.FindFor(b.Def);
            // choose more expensive part
            if (marketA != null && marketB != null) {
                if (marketB.Value * aState.Quality > marketA.Value * bState.Quality) {
                    return b;
                }
            }
            return a;
        }

        public bool RemoveWound(DamageType t) {
            if (state?.RemoveWound(t) == true) {
                Body.Owner.OnWoundsChanged();
                return true;
            }
            return false;
        }
    }
}
