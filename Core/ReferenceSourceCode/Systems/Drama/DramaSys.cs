#if !PRODUCTION_BUILD
// #define DEBUG_INTERACTIONS
// #define DEBUG_OPPORTUNITY
// #define DEBUG_PROXIMITY
#endif
using System.Buffers;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.BodyParts;
using Game.Systems.Creatures;
using Game.Systems.Items;
using Game.Systems.Mental;
using Game.Systems.Reg;
using Game.Systems.Story;
using Game.Systems.UIUX;
using Game.Utils;
using KL.Grid;
using KL.Pool;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Drama {
    public sealed class DramaSys : GameSystem, ISaveableSpecial {
        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new DramaSys());
        }
        public const int ClusterUpdateTickRate = Consts.TicksPer5Minutes;
        private const string SysId = "DramaSys";
        public override string Id => SysId;
        private StoryContext ctx;
        private DramaSysData data;
        private DramaMemory memory;
        private readonly List<SocialInteraction> interactions = new();
        private readonly B<SocialInteraction> newInteractionAdded = new(null);
        private readonly Dictionary<Expedition, BeingCluster> expeditionClusters = new();
        private readonly Dictionary<int, BeingCluster> clusters = new();
        private readonly Dictionary<int, BeingChat> chats = new();
        private readonly List<SocialOpportunity> opportunities = new();
        private HashSet<BeingData> chatters = new();
        private readonly Dictionary<int, (BeingData, BeingData)> inProximity = new();
        /// <summary>
        /// clusters with > 1 being in BeingCluster.All
        /// </summary>
        private readonly List<BeingCluster> sociableIdleClusters = new();
        private readonly List<BeingCluster> sociableBusyClusters = new();
        /// <summary>
        /// All (idle + non-idle) sociable clusters
        /// </summary>
        private readonly List<BeingCluster> sociableClusters = new();
        private readonly List<BeingCluster> potentialClusters = new();
        private readonly ClassPool<ChatBubble> chatBubblePool = new();
        private Curve socialCooldownHoursCurve;
        private bool isDramaDisabled;
        private int socialInteractionMaxCountWindowTicks;
        private int socialInteractionCooldownTicks;

        public override void Unload() {
            foreach (var cl in clusters.Values) {
                cl.Clear();
            }
            foreach (var chat in chats.Values) {
                chat.Cleanup();
            }
            chats.Clear();
            chatBubblePool.Flush();
            clusters.Clear();
            sociableClusters.Clear();
            sociableBusyClusters.Clear();
            sociableIdleClusters.Clear();
            interactions.Clear();
            potentialClusters.Clear();
            The.SysSig.SettingChanged.RemoveListener(OnSettingChanged);
        }

        protected override void OnInitialize() {
            S.Sig.AfterLoadState.AddListener(OnLoad);
            S.Sig.BeingAshesForgotten.AddListener(OnAshesForgotten);
            socialCooldownHoursCurve = CurveDef.FastGet(CurveIdH.Drama_SocialEventCooldownHours);
            socialInteractionMaxCountWindowTicks = (int)
                (Tunables.SocialInteractionsMaxCountWindowHours * Consts.TicksPerHour);
            socialInteractionCooldownTicks = (int)
                (Tunables.SocialInteractionCooldownHours * Consts.TicksPerHour);
            The.SysSig.SettingChanged.AddListener(OnSettingChanged);
            // chatBubblePool.Debug = true;
        }

        private void OnAshesForgotten(ObjData ashes, Mind mind) {
            if (mind == null) { return; }
            if (mind.Owner?.HasStateFlag(BeingState.Alive) == true) {
                return;
            }
            var beings = S.Query.AllBeingData.Ask();
            var pos = ashes.Obj?.PosIdx ?? Pos.Invalid;
            var faction = mind.Faction;
            var tip = T.NoBurialCeremonyTip_V1.F(mind.Name.FullNameOrNameless);
            foreach (var b in beings.Values) {
                if (b.HasStateFlag(BeingState.Dead) || b.Body.HasFlag(BodyFlags.Missing)) {
                    continue;
                }
                if (b.Mind.Health == null) { continue; }
                if (b.Mind.Species.IsAnimal) { continue; }
                // Guests of another faction can still grieve a crew member they bonded with
                // (the mood effect below is gated on hasBond).
                if (b.Mind.Faction != faction && !b.IsGuest) { continue; }
                if (b.Mind.Traits.HasTrait(TraitIdH.Psychopath)) { continue; }
                if (b.Mind.Traits.HasTrait(TraitIdH.Enlightened)) { continue; }
                if (b.Mind.Traits.HasTrait(TraitIdH.Sociopath)) { continue; }
                // Comes on top of the grief for the same death (BeingsSys.ConsiderGriefing),
                // which is why it is small
                short effect = 2;
                bool hasBond = false;
                if (b.Mind.Bonds?.Important.Count > 0) {
                    foreach (var v in b.Mind.Bonds.Important) {
                        if (v.Value.Contains(mind)) {
                            hasBond = true;
                            switch (v.Key) {
                                case BeingOpinionFlags.Nemesis: {
                                    effect -= 10;
                                    break;
                                }
                                case BeingOpinionFlags.Friend: {
                                    effect += 5;
                                    break;
                                }
                                case BeingOpinionFlags.Lover: {
                                    effect += 8;
                                    break;
                                }
                                case BeingOpinionFlags.Spouse: {
                                    effect += 10;
                                    break;
                                }
                                case BeingOpinionFlags.Pet: {
                                    effect += 5;
                                    break;
                                }
                            }
                        }
                    }
                }
                if (hasBond) {
                    b.Mind.Health.AddEffect(S, MentalEffect.CreateWithIcon(pos,
                        IconId.CFuneralRibbon, G.Ticks, Consts.TicksPer3Days, T.NoBurialCeremony, -effect)
                            .WithTooltip(tip));
                }
            }
            TrustDeeds.FriendLeftWithoutCeremony(S, mind);
        }

        private void OnLoad(GameState state) {
            Ready.When(WhenH.BeingsLoaded, RestoreData);
            Ready.When(WhenH.AllSystemsLoaded, RestoreCooldown);
        }

        private void RestoreCooldown() {
            ctx = S.Query.GetStoryContext.Ask();
            if (data == null) {
                // we're starting a new game
                CooldownSocialInteractions();
            }
        }

        private void RestoreData() {
            bool isLoading = data != null;
            if (isLoading) {
                SocialInteractionData.Deserialize(S, data.Interactions, interactions);
                memory = DramaMemoryData.Deserialize(S, data.Memory);
            }
            memory ??= new DramaMemory();
            isDramaDisabled = S.Prefs.GetBool(Pref.DisableDrama, PrefH.DisableDrama, false);

            S.Query.GetDramaMemory.SetResponder(() => memory);
            S.Query.GenerateRelationshipsFor.SetResponder(GenRelationships);
            S.Clock.OnTick.AddListener(OnTick);
            S.Query.GetBeingCluster.SetResponder(GetClusterByRegId);
            S.Query.TryForceSocialEvent.SetResponder(TryForceEvent);
            S.Query.RegisterSocialInteraction.SetResponder(RegisterSocialInteraction);
            S.Sig.BeingProximityDurationChanged.AddListener(OnProximityDurationChange);
            S.Query.ChangeSocialOpportunity.SetResponder(ChangeSocialOpportunity);
            S.Query.FindSocialOpportunity.SetResponder(FindSocialOpportunity);
            S.Query.GetSocialOpportunities.SetResponder(() => opportunities);
            S.Query.GetSocialInteractions.SetResponder(() => interactions);
            S.Query.GetBeingChats.SetResponder(() => chats);
            S.Query.StartForcedChat.SetResponder(StartForcedChat);
            S.Query.EndForcedChat.SetResponder(EndForcedChat);
            S.Query.GetBeingProximity.SetResponder(() => inProximity);

            S.UIQuery.GetSocialInteractions.SetResponder(() => (interactions, newInteractionAdded));

            S.Query.GetSociableClusters.SetResponder(() => sociableClusters);
            S.Query.GetBeingClusters.SetResponder(() => clusters);
            S.Query.GetExpeditionClusters.SetResponder(() => expeditionClusters);
            S.Sig.EveryFrame.AddListener(EveryFrame);
        }

        private bool GenRelationships(List<BeingData> beings) {
            if (beings.Count < 2) { return false; }
            foreach (var b1 in beings) {
                foreach (var b2 in beings) {
                    if (b1.Mind.Health == null || b2.Mind.Health == null) { continue; }
                    if (b1.Id == 0 || b2.Id == 0) {
                        D.Err("Must finalize being data before generating relationships!");
                        return false;
                    }
                    if (b1.Id <= b2.Id) { continue; }
                    var rng = new Rng(Hashes.Quick(b1.Id, b2.Id));
                    var op1 = GenerateOpinion(b1, b2, rng);
                    var op2 = GenerateOpinion(b2, b1, rng);
                    bool makeLovers = false;
                    var noSpouseB1 = !b1.Mind.Bonds.Important.ContainsKey(BeingOpinionFlags.Spouse);
                    var noSpouseB2 = !b2.Mind.Bonds.Important.ContainsKey(BeingOpinionFlags.Spouse);
                    if (op1.Love > 0.75f && op2.Love > 0.75f) {
                        if (noSpouseB1 && noSpouseB2) {
                            op1.SetFlag(BeingOpinionFlags.Spouse);
                            op2.SetFlag(BeingOpinionFlags.Spouse);
                        } else if (rng.Chance(0.5f)) {
                            makeLovers = true;
                        }
                    } else if (op1.Love > 0.5f && op2.Love > 0.5f) {
                        float loverChance;
                        if (noSpouseB1 && noSpouseB2) {
                            loverChance = 0.15f + Mathf.Max(op1.Love, op2.Love);
                        } else {
                            loverChance = 0.33f;
                        }
                        if (rng.Chance(loverChance)) {
                            makeLovers = true;
                        }
                    }
                    if (makeLovers) {
                        op1.SetFlag(BeingOpinionFlags.Lover);
                        op2.SetFlag(BeingOpinionFlags.Lover);
                    }
                }
            }
            // create pet/master relationships
            foreach (var b in beings) {
                if (b.Mind.Health == null) { continue; }
                if (!b.Mind.Species.IsPet) { continue; }
                float maxMaster = 0f;
                BeingOpinion masterOp = null;
                foreach (var op in b.Mind.Bonds.Opinions.Values) {
                    if (op.Pet > maxMaster) {
                        maxMaster = op.Pet;
                        masterOp = op;
                    }
                }
                if (masterOp != null) {
                    masterOp.SetFlag(BeingOpinionFlags.Master);
                    if (masterOp.TargetMind.Bonds.Opinions.TryGetValue(b.Mind.Id, out var petOp)) {
                        petOp.SetFlag(BeingOpinionFlags.Pet);
                    }
                }
            }
            // finalize
            foreach (var b in beings) {
                b.Mind.Bonds?.RebuildImportant();
            }
            return true;
        }

        private BeingOpinion GenerateOpinion(BeingData b1, BeingData b2, Rng rng) {
            var op = S.Query.GetOpinionAbout.Ask(b1.Mind, b2.Mind);
            op.SetFlag(BeingOpinionFlags.Acquaintance);
            if (op.SocialAffinity > 0f) {
                op.Friendship = rng.Range(0.33f, 1f) * op.SocialAffinity;
                if (op.Friendship > 0.5f && rng.Chance(Mathf.Max(op.Friendship, op.SocialAffinity))) {
                    op.SetFlag(BeingOpinionFlags.Friend);
                }
            } else {
                op.Hate = rng.Range(0f, 1f) * -op.SocialAffinity;
                if (op.Hate > 0.75f && rng.Chance(op.Hate)) {
                    op.SetFlag(BeingOpinionFlags.Nemesis);
                }
            }
            if (b1.Species == b2.Species) {
                if (op.SexualAttraction > 0f) {
                    op.Love = rng.Range(0f, 1f) * op.SexualAttraction;
                    if (op.Love > 0.5f) {
                        op.SetFlag(BeingOpinionFlags.Crush);
                    }
                }
            }
            if (b1.Species.IsPet && b2.Species.IsHumanoid) {
                if (op.SocialAffinity > 0f) {
                    op.Pet = rng.Range(0f, 1f) * op.SocialAffinity;
                }
            }
            return op;
        }

        private void CooldownSocialInteractions(int ticks = 0) {
            if (ticks == 0) {
                ticks = (int) (S.Rng.Curve(socialCooldownHoursCurve)
                    * Consts.TicksPerHour);
            }
            ctx.CoolDownFlag(StoryEventFlags.Social, ticks);
        }

        private void EveryFrame() {
            if (chats.Count == 0) {
                return;
            }
            foreach (var chat in chats.Values) {
                chat.Update(S);
            }
        }

        private SocialOpportunity FindSocialOpportunity(BeingData being, int type) {
            if (being.Mind.Traits.HasTrait(TraitIdH.Introvert)) {
                return null;
            }
            var seeker = being.Being;
            if (seeker == null) {
                return null;
            }
            var ticks = G.Ticks;
            var maxOpCount = opportunities.Count;
            if (maxOpCount == 0) {
                return null;
            }
            var reqPool = ArrayPool<RegPathReq>.Shared;
            var regPathReqs = reqPool.Rent(maxOpCount);
            var resPool = ArrayPool<int>.Shared;
            var resPathLengths = resPool.Rent(maxOpCount);
            var opPool = ArrayPool<SocialOpportunity>.Shared;
            var validOps = opPool.Rent(maxOpCount);
            int validCnt = 0;
            for (int i = opportunities.Count - 1; i >= 0; i--) {
                var op = opportunities[i];
                if (op.IsExpired) {
                    opportunities.RemoveAt(i);
                    continue;
                }
                if (op.TypeH != type) { continue; }
                if (being == op.Being) { continue; }
                if (!op.CanBeTakenBy(being)) {
                    continue;
                }
                if (op.FunProvider != null) {
                    if (!op.FunProvider.CanAddUser) {
                        continue;
                    }
                    if (!op.FunProvider.CanUse(being.Being)) {
                        continue;
                    }
                    if (being.Being.Motion.HadNavFailureAt(op.FunProvider.WorkSpot)) {
                        continue;
                    }
                }
                var opPos = OpportunityPos(op);
                if (opPos < 0) {
                    continue;
                }
                validOps[validCnt] = op;
                regPathReqs[validCnt] = RegPathReq.To(seeker.PosIdx, opPos,
                    seeker.Motion.IsGrounded);
                validCnt++;
            }
            if (validCnt == 0) {
                reqPool.Return(regPathReqs);
                resPool.Return(resPathLengths);
                opPool.Return(validOps);
                return null;
            }

            SocialOpportunity oldest = null;
            long oldestStart = long.MaxValue;
            if (S.Query.RegPathLengths.Ask(regPathReqs, validCnt, resPathLengths)) {
                for (int i = 0; i < validCnt; i++) {
                    var dist = resPathLengths[i];
                    if (dist < 0 || dist > Tunables.SocialOpportunityMaxRegionDistance) {
                        continue;
                    }
                    var op = validOps[i];
                    if (op.StartTick < oldestStart) {
                        oldestStart = op.StartTick;
                        oldest = op;
                    }
                }
            }
            reqPool.Return(regPathReqs);
            resPool.Return(resPathLengths);
            opPool.Return(validOps);
            if (oldest == null) {
                return null;
            }
            if (!oldest.TryAddTaker(being)) {
                return null;
            }
            return oldest;
        }

        private static int OpportunityPos(SocialOpportunity op) {
            if (op.FunProvider != null) {
                return op.FunProvider.WorkSpot;
            }
            if (op.Slot != null) {
                return op.Slot.SlotPosIdx;
            }
            if (op.Being?.Being != null) {
                return op.Being.Being.PosIdx;
            }
            return Pos.Invalid;
        }

        private bool ChangeSocialOpportunity(SocialOpportunity op, bool add) {
            #if DEBUG_OPPORTUNITY
            D.Warn("OPPORTUNITY: Change: {0} -> {1}", op, add);
            #endif
            if (add) {
                #if DEBUG_OPPORTUNITY
                if (opportunities.Contains(op)) {
                    D.Err("Adding duplicate opportunity! {0}", op);
                }
                #endif
                opportunities.Add(op);
                return true;
            }

            #if DEBUG_OPPORTUNITY
            if (!op.IsExpired && !opportunities.Contains(op)) {
                D.Err("Removing opportunity that wasn't added! {0}", op);
            }
            #endif
            return opportunities.Remove(op);
        }

        private void OnProximityDurationChange(BeingData b1, BeingData b2, uint duration) {
            var h = BeingUtils.PairHash(b1, b2);
            // proximity just created
            var isChatting1 = chatters.Contains(b1);
            var isChatting2 = chatters.Contains(b2);
            var wantsChat = false;
            SocialChatContext ctx = null;
            if (!inProximity.ContainsKey(h)) {
                #if DEBUG_PROXIMITY
                D.Warn("Beings in proximity: {0} & {1} | {2}",
                    b1.Being, b2.Being, Units.TicksToTime(G.Ticks));
                #endif
                inProximity[h] = (b1, b2);
                S.Sig.BeingProximityChanged.Send(b1, b2, true);
                if (!isChatting1 && !isChatting2) {
                    BeingChat.HasSharedContext(b1, b2, out ctx);
                    wantsChat = true;
                }
            } else {
                // check if some peeps are in proximity and in a action that
                // has same chat context, if so, then let's start a chat
                if (!isChatting1 && !isChatting2 && BeingChat.HasSharedContext(
                        b1, b2, out ctx)) {
                    wantsChat = true;
                }
            }
            if (wantsChat && BeingChat.WillChat(b1, b2)) {
                chatters.Add(b1);
                chatters.Add(b2);
                var chat = BeingChat.Between(b1, b2, G.Ticks, ctx, chatBubblePool);
                chats.TryAdd(h, chat);
            }
        }

        private bool StartForcedChat(BeingData b1, BeingData b2, string context) {
            if (b1?.Being == null || b2?.Being == null || b1 == b2) {
                return false;
            }
            var h = BeingUtils.PairHash(b1, b2);
            if (chats.TryGetValue(h, out var existing) && existing.IsForced
                    && !existing.HasEnded) {
                return true;
            }
            // A forced chat barges into whatever either of them was saying
            var toRemove = Caches.IntList;
            toRemove.Clear();
            foreach (var chat in chats.Values) {
                if (chat.B1 == b1 || chat.B2 == b1 || chat.B1 == b2 || chat.B2 == b2) {
                    toRemove.Add(chat.PairHash);
                }
            }
            foreach (var tr in toRemove) {
                RemoveChat(tr, G.Ticks);
            }
            var forced = BeingChat.Forced(b1, b2, G.Ticks, context, chatBubblePool);
            chatters.Add(b1);
            chatters.Add(b2);
            chats[h] = forced;
            forced.Start(S);
            return true;
        }

        private bool EndForcedChat(BeingData b1, BeingData b2) {
            var h = BeingUtils.PairHash(b1, b2);
            if (!chats.TryGetValue(h, out var chat) || !chat.IsForced) {
                return false;
            }
            RemoveChat(h, G.Ticks);
            return true;
        }

        private void RemoveChat(int pairHash, long ticks) {
            if (!chats.Remove(pairHash, out var chat)) {
                return;
            }
            if (chat.HasStarted && !chat.HasEnded) {
                chat.End(S, ticks, silent: true);
            }
            chatters.Remove(chat.B1);
            chatters.Remove(chat.B2);
            chat.Cleanup();
        }

        private bool RegisterSocialInteraction(SocialInteraction soc) {
            #if DEBUG_INTERACTIONS
            D.Warn("SOC: Registering social interaction: {0}", soc);
            #endif
            if (soc.Icon == null || soc.Desc == null) {
                D.Err("SOC: Registering social interaction without icon or message! {0}", soc);
            }
            if (soc.AttitudeChange == 0f) {
                D.Err("SOC: Registering social interaction without attitude change! {0}", soc);
            }
            if (soc.InvolvedMinds.Length == 0) {
                D.Err("SOC: Registering social interaction without involved minds! {0}", soc);
                return false;
            }
            interactions.Add(soc);
            soc.CreatedAt = G.Ticks;
            var useWeights = soc.InvolvedMinds.Length == soc.ChangeWeights.Length;
            for (int i1 = 0; i1 < soc.InvolvedMinds.Length; i1++) {
                for (int i2 = 0; i2 < soc.InvolvedMinds.Length; i2++) {
                    if (i1 == i2) { continue; }
                    var b1 = soc.InvolvedMinds[i1];
                    var b2 = soc.InvolvedMinds[i2];
                    if (b1 == b2) {
                        D.Err("SOC: Involved minds has a duplicate! {0}",
                            string.Join(", ", soc.InvolvedMinds));
                        continue;
                    }
                    var op = S.Query.GetOpinionAbout.Ask(b1, b2);
                    if (op == null) {
                        D.Err("SOC: Won't apply social interaction. Null op for {0} -> {1}",
                            b1, b2);
                        continue;
                    }
                    var weight = 1f;
                    if (useWeights) {
                        weight = soc.ChangeWeights[i1];
                    }
                    b1.Bonds.RegisterInteraction(G.Ticks);
                    op.ApplySocialInteraction(S, soc, b1, b2, weight);
                }
            }
            newInteractionAdded.Set(soc);
            return true;
        }

        private void OnSettingChanged(string setting) {
            if (setting == Pref.DisableDrama) {
                var isOff = S.Prefs.GetBool(
                    Pref.DisableDrama, PrefH.DisableDrama, false);
                // D.Warn("Setting drama disabled: {0}->{1}", isDramaDisabled, isOff);
                isDramaDisabled = isOff;
            }
        }

        private StoryEventData TryForceEvent(string id) {
            var ev = StoryEventDef.Get(id);
            if (ev == null) {
                D.Err("Trying to force social event that does not exist by id: {0}", id);
                return null;
            }
            if (!ev.Flags.HasFlag(StoryEventFlags.Social)) {
                D.Err("Trying to force social event that has not Social flag: {0}=>{1}",
                    ev, ev.Flags);
                return null;
            }
            S.BB.Set(BBKeyH.DevForceHook, id);
            var data = TryStartSocialEvent(G.Ticks);
            if (!StoryEventData.IsOK(data)) {
                D.Err("Failed to force story event by id: {0} -> Data:({1})", id, data);
            }
            S.BB.Remove(BBKeyH.DevForceHook, id);
            return data;
        }

        private BeingCluster GetClusterByRegId(int regId) {
            if (!clusters.TryGetValue(regId, out var cluster)) {
                cluster = new BeingCluster(regId);
                clusters.Add(regId, cluster);
            }
            return cluster;
        }

        public void OnTick(long ticks) {
            bool updateClusters = ticks % ClusterUpdateTickRate == 3;
            if (updateClusters) {
                using (D.Timer("UpdateBeingClusters", 0.00001f)) {
                    UpdateClusters(ticks);
                }
            }
            if (!isDramaDisabled && !ctx.IsCoolingDown(StoryEventFlags.Social)) {
                TryStartSocialEvent(ticks);
            }
            TickChats(ticks);
        }

        private void TickChats(long ticks) {
            if (chats.Count == 0) {
                if (chatters.Count > 0) {
                    D.Err("We have {0} chatters while no chats! {1}",
                        chatters.Count,
                        string.Join(", ", chatters));
                    chatters.Clear();
                }
                return;
            }
            var toRemove = Caches.IntList;
            toRemove.Clear();
            foreach (var chat in chats.Values) {
                bool inProx;
                if (chat.IsForced) {
                    // Forced pairs are not tracked in inProximity. Its caller ends the
                    // chat, this only catches one that was never ended.
                    inProx = chat.B1.Being != null && chat.B2.Being != null
                        && Vectors.SqrDistance(chat.B1.Being.Position,
                            chat.B2.Being.Position) <= 4f;
                } else {
                    inProx = inProximity.ContainsKey(chat.PairHash);
                }
                if (!chat.Tick(S, ticks, inProx)) {
                    chatters.Remove(chat.B1);
                    chatters.Remove(chat.B2);
                    chat.Cleanup();
                    toRemove.Add(chat.PairHash);
                }
            }
            if (toRemove.Count > 0) {
                foreach (var tr in toRemove) {
                    chats.Remove(tr);
                }
            }
        }


        private List<BeingCluster> GetTargetClusters(bool idle, bool fallback) {
            if (idle) {
                return sociableIdleClusters;
            }
            if (expeditionClusters.Count == 0) {
                if (fallback) {
                    return sociableBusyClusters;
                }
                return sociableClusters;
            }
            potentialClusters.Clear();
            if (fallback) {
                potentialClusters.AddRange(sociableBusyClusters);
            } else {
                potentialClusters.AddRange(sociableClusters);
            }
            potentialClusters.AddRange(expeditionClusters.Values);
            return potentialClusters;
        }

        private StoryEventData TryStartSocialEvent(long ticks) {
            if (sociableClusters.Count == 0 && expeditionClusters.Count == 0) {
                CooldownSocialInteractions(Consts.TicksPer5Minutes);
                return null;
            }
            var rng = new Rng((uint) ticks);
            bool tryIdleFirst = sociableIdleClusters.Count > 0 && rng.Chance(0.5f);
            var hook = StoryEventHook.CreateFor(
                GetTargetClusters(idle: tryIdleFirst, fallback: false),
                    StoryEventFlags.Social)
                        .WithSeed(rng.Int)
                        .Instant()
                        .Optional();
            var forcedHook = S.BB.Get(BBKeyH.DevForceHook, string.Empty);
            var data = S.Query.AddStoryEventHook.Ask(hook);
            var isDataOK = StoryEventData.IsOK(data);
            if (!isDataOK && tryIdleFirst && (expeditionClusters.Count > 0
                    || sociableBusyClusters.Count > 0)) {
                // fallback to non-idle
                hook.Target = GetTargetClusters(idle: false, fallback: true);
                if (forcedHook != string.Empty) {
                    // re-force the hook, otherwise forced hook would be deleted
                    // after first add story event hook call
                    S.BB.Set(BBKeyH.DevForceHook, forcedHook);
                }
                data = S.Query.AddStoryEventHook.Ask(hook);
                if (StoryEventData.IsOK(data)) {
                    D.Warn("Drama: Fallback to all sociable clusters success: {0}",
                        data);
                }
            }
            if (StoryEventData.IsOK(data)) {
                CooldownSocialInteractions();
            } else {
                CooldownSocialInteractions(Consts.TicksPer15Minutes);
            }
            return data;
        }

        private void UpdateClusters(long ticks) {
            var beings = S.Query.AllBeingData.Ask();
            foreach (var cl in clusters.Values)  {
                cl.Clear();
            }
            var sociableTickFrom = ticks - socialInteractionMaxCountWindowTicks;
            var sociableMaxLastTick = ticks - socialInteractionCooldownTicks;

            foreach (var data in beings.Values) {
                if (data.Being == null) { continue; }
                var regId = S.Query.RegionAt.Ask(data.Being.PosIdx);
                var cluster = GetClusterByRegId(regId);
                cluster.Add(data, sociableMaxLastTick,
                    sociableTickFrom, Tunables.SocialInteractionsMaxCount);
            }
            sociableIdleClusters.Clear();
            sociableBusyClusters.Clear();
            sociableClusters.Clear();
            var expeditions = S.Sys.Expedition.Expeditions;

            // Remove outdated expedition clusters
            var checkExpeditions = true;
            int attempts = 100;
            while (checkExpeditions && attempts-- > 0) {
                checkExpeditions = false;
                foreach (var cl in expeditionClusters) {
                    if (!expeditions.Contains(cl.Key)) {
                        // disband
                        expeditionClusters.Remove(cl.Key);
                        checkExpeditions = true;
                        break;
                    }
                }
            }
            if (attempts < 1) {
                D.Err("Ran out of attempts while removing expedition clusters");
            }

            // Ensure existing expedition clusters
            foreach (var exp in expeditions) {
                // only flying shuttle will create clusters
                if (!exp.Shuttle.IsOut) { continue; }
                if (!expeditionClusters.TryGetValue(exp, out var cluster)) {
                    cluster = new BeingCluster(-1);
                    cluster.Expedition = exp;
                    expeditionClusters.Add(exp, cluster);
                }
                cluster.Clear();
                cluster.Flags |= BeingClusterFlags.Expedition;
                foreach (var mem in exp.Crew) {
                    cluster.Add(mem, sociableMaxLastTick,
                        sociableTickFrom, Tunables.SocialInteractionsMaxCount);
                }
                cluster.UpdateFlags();
                cluster.ApplyEffects(S, ticks);
            }

            foreach (var cl in clusters.Values) {
                cl.UpdateFlags();
                if (cl.IsSociable) {
                    sociableClusters.Add(cl);
                    if (cl.IsSociableIdle) {
                        sociableIdleClusters.Add(cl);
                    } else {
                        sociableBusyClusters.Add(cl);
                    }
                }
                cl.ApplyEffects(S, ticks);
            }
            var toRemove = Caches.IntList;
            toRemove.Clear();
            foreach (var inp in inProximity) {
                var bd1 = inp.Value.Item1;
                var bd2 = inp.Value.Item2;
                if (bd1.Being == null || bd2.Being == null) {
                    if (bd1.Being != bd2.Being) {
                        #if DEBUG_PROXIMITY
                        D.Warn("Beings no longer in proximity: {0} & {1} | {2}",
                            bd1.Being, bd2.Being, Units.TicksToTime(ticks));
                        #endif
                        toRemove.Add(inp.Key);
                    }
                    continue;
                }
                var p1 = bd1.Being.Position;
                var p2 = bd2.Being.Position;
                if (Vectors.SqrDistance(p1, p2) > 4) {
                    toRemove.Add(inp.Key);
                }
            }
            if (toRemove.Count > 0) {
                foreach (var tr in toRemove) {
                    if (inProximity.TryGetValue(tr, out var pair)) {
                        // end of proximity
                        S.Sig.BeingProximityChanged.Send(pair.Item1, pair.Item2, false);
                        inProximity.Remove(tr);
                    }
                }
            }
        }

        public void SaveSpecial(SystemsDataSpecial sd) {
            sd.Drama ??= new();
            sd.Drama.Interactions = SocialInteractionData.Serialize(interactions);
            sd.Drama.Memory = DramaMemoryData.Serialize(memory);
        }

        public void LoadSpecial(SystemsDataSpecial sd) {
            data = sd?.Drama;
        }
    }
}
