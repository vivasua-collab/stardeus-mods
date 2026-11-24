using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.CodeGen;
using UnityEngine;
using Game.Utils;
using Game.Systems.Drama;
using Game.Systems.Creatures;
using System.Linq;
using KL.Utils;
using KL.Grid;
using Game.Systems.AI;
using KL.Collections;
using Game.Systems.Mental;

namespace Game.Systems.Story {
    public sealed class PartyStoryEvent : StoryEvent {
        [SelfInit]
        private static void Register() {
            Register(new PartyStoryEvent());
        }

        public const string EventId = "Party";
        public override string Id => EventId;
        // A party of a cluster of friends takes at most this many
        private const int MaxClusterGuests = 6;
        // A birthday or wedding party fills up with acquaintances to this many invited
        private const int MinInvited = 5;

        // Everyone the party would like to have, most wanted first: the venue decides how many
        // of them come (InviteToVenue)
        private readonly List<BeingData> wanted = new();
        private readonly List<(BeingData guest, float closeness)> bonds = new();

        public override bool CheckConditions(StoryContext ctx, StoryEventDef def) {
            return ctx.S.Situation.Colonists > 2;
        }

        public override bool CheckHook(StoryContext ctx, StoryEventDef def, StoryEventHook hook) {
            if (hook.Target is not List<BeingCluster> clusters) {
                D.Warn("Hook target not list of being clusters! {0}", hook.Target);
                return false;
            }
            if (ctx.S.Query.GetDramaMemory.Ask().WantsParty.Length > 0) {
                return true;
            }
            foreach (var cl in clusters) {
                if (IsClusterCompatible(ctx, cl)) {
                    return true;
                }
            }
            return false;
        }

        private bool IsClusterCompatible(StoryContext ctx, BeingCluster cluster) {
            if (cluster.IsExpedition) { return false; }
            var cnt = cluster.FriendlySociable.Count;
            if (cnt < 3 || cnt > 20) { return false; }
            cnt = 0;
            int regPosCnt = ctx.S.Query.RegionPositions.Ask(cluster.RegId, Caches.RegPos);
            if (regPosCnt < 12) {
                // region is too small!
                D.Warn("Region too small: {0} | {1}", regPosCnt, cluster);
                return false;
            }
            var regPos = Caches.RegPos[0];
            if (ctx.S.Sys.Areas.IsOutdoors(regPos)) {
                // D.Warn("Outdoors: {0}", Pos.String(regPos));
                // not partying outdoors!
                return false;
            }
            if (!PartyVenue.IsAirComfortable(ctx.S, regPos)) {
                return false;
            }

            foreach (var b in cluster.FriendlySociable) {
                if (CanParticipate(b, false)) {
                    cnt++;
                    if (cnt > 2) {
                        return true;
                    }
                }
            }
            return false;
        }

        private bool CanParticipate(BeingData b, bool ignoreSafety) {
            if (b?.Being == null) { return false; }
            if (HasForceAssignedGoal(b)) { return false; }
            if (b.Being?.AIAgent.Goal?.IsNonInterruptable == true) {
                return false;
            }
            if (b.Species.IsAnimal) { return false; }
            if (b.HasStateFlag(BeingState.Dead)) { return false; }
            if (b.Body.IsIncapacitated) { return false; }
            if (!b.Mind.Jobs.CanDo(JobTypeIdH.Social)) { return false; }
            // don't host party outside
            if (!ignoreSafety && !b.Safety.IsActuallySafeAtWithoutEquipment(b.Being.PosIdx)) {
                return false;
            }
            return true;
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            var s = ctx.S;
            if (data.TargetOther is not List<BeingCluster> clusters) {
                data.FailEvent(s,
                    $"TargetOther is not a list of being clusters: {data.TargetOther}");
                return;
            }

            wanted.Clear();
            bool collected;
            var mem = s.Query.GetDramaMemory.Ask();
            if (mem.WantsParty.Length > 0) {
                collected = CollectCustomGuests(data, s, mem.WantsParty, mem.PartyTitles);
                if (collected) {
                    mem.ResetParty();
                }
            } else {
                collected = CollectClusterGuests(ctx, data, clusters);
            }
            var invited = collected && InviteToVenue(data, s);
            wanted.Clear();
            if (!invited) { return; }

            if (BeginParty(ctx, data)) {
                data.SetState(s, StoryEventState.Ongoing);
                Notify(ctx, data);
            } else {
                if (!data.IsFailed) {
                    data.FailEvent(s, "Failed to start a party");
                }
            }
        }

        // Birthday and wedding parties: the honorees, then their friends and family, closest
        // first, then acquaintances they like when that is too few
        private bool CollectCustomGuests(StoryEventData data, GameState s,
                FrugalArray<Mind> minds, List<string> titles) {
            data.TempObj = titles == null ? null : string.Join(" / ", titles);
            for (int i = 0; i < minds.Length; i++) {
                var owner = minds[i]?.Owner;
                if (owner?.Body != null && CanParticipate(owner, true)
                        && !wanted.Contains(owner)) {
                    wanted.Add(owner);
                }
            }
            var honorees = wanted.Count;
            if (honorees == 0) {
                data.FailEvent(s, "Nobody to throw the party for", isHardFailure: false);
                return false;
            }

            bonds.Clear();
            for (int h = 0; h < honorees; h++) {
                var lead = wanted[h];
                var leadBonds = lead.Mind.Bonds;
                if (leadBonds == null) { continue; }
                foreach (var vip in leadBonds.Important) {
                    if (vip.Key == BeingOpinionFlags.Nemesis) { continue; }
                    if (vip.Key == BeingOpinionFlags.Pet) { continue; }
                    if (vip.Key == BeingOpinionFlags.Master) { continue; }
                    var vips = vip.Value;
                    for (int i = 0; i < vips.Length; i++) {
                        var v = vips[i];
                        var owner = v?.Owner;
                        if (owner == null || wanted.Contains(owner)) { continue; }
                        if (!CanParticipate(owner, true)) { continue; }
                        AddBond(owner, Closeness(leadBonds, v));
                    }
                }
            }
            bonds.Sort((a, b) => b.closeness.CompareTo(a.closeness));
            for (int i = 0; i < bonds.Count; i++) {
                wanted.Add(bonds[i].guest);
            }
            bonds.Clear();

            int invited = wanted.Count - honorees;
            if (invited < MinInvited) {
                var wGuests = Caches.WeightedBeingData;
                wGuests.Clear();
                for (int h = 0; h < honorees; h++) {
                    var leadBonds = wanted[h].Mind.Bonds;
                    if (leadBonds == null) { continue; }
                    foreach (var op in leadBonds.Opinions.Values) {
                        var mind = s.Query.FindMind.Ask(op.TargetMindId);
                        var owner = mind?.Owner;
                        if (owner?.Being == null) { continue; }
                        if (wanted.Contains(owner)) {
                            // already invited
                            continue;
                        }
                        if (CanParticipate(owner, true)) {
                            var w = 0.01f + Mathf.Clamp(
                                op.Friendship + op.Love - op.Hate, 0f, 2f);
                            if (wGuests.TryGetValue(owner, out var w2)) {
                                wGuests[owner] = Mathf.Max(w, w2);
                            } else {
                                wGuests.Add(owner, w);
                            }
                        }
                    }
                }
                int attempts = 100;
                while (invited < MinInvited && attempts-- > 0) {
                    if (!data.SeededRng.TryWeightedFrom(wGuests, out var guest)) {
                        break;
                    }
                    wGuests.Remove(guest);
                    wanted.Add(guest);
                    invited++;
                }
                wGuests.Clear();
            }

            if (invited == 0) {
                // won't party alone
                data.FailEvent(s, "Can't party alone (custom)", isHardFailure: false);
                return false;
            }
            return true;
        }

        private static float Closeness(BeingBonds leadBonds, Mind mind) {
            if (leadBonds.Opinions.TryGetValue(mind.Id, out var op)) {
                return op.Friendship + op.Love;
            }
            return 0f;
        }

        // A bond of two honorees counts as close as the closer of them holds it
        private void AddBond(BeingData guest, float closeness) {
            for (int i = 0; i < bonds.Count; i++) {
                if (bonds[i].guest == guest) {
                    if (closeness > bonds[i].closeness) {
                        bonds[i] = (guest, closeness);
                    }
                    return;
                }
            }
            bonds.Add((guest, closeness));
        }

        // A party that starts among friends hanging out together; the most social of them
        // hosts it
        private bool CollectClusterGuests(StoryContext ctx, StoryEventData data,
                List<BeingCluster> clusters) {
            var rng = data.SeededRng;
            var s = ctx.S;
            var cluster = clusters.Where(cl => IsClusterCompatible(ctx, cl))
                .OrderBy(cl => rng.Value).FirstOrDefault();
            if (cluster == null) {
                data.FailEvent(s, "No compatible being cluster", isHardFailure: false);
                return false;
            }
            BeingData leader = null;
            int highestSocial = -1;
            foreach (var bd in cluster.FriendlySociable) {
                if (!CanParticipate(bd, false)) { continue; }
                wanted.Add(bd);
                int social = bd.Mind.Jobs.TryJob(JobTypeIdH.Social, out var slot) ? slot.Level : 0;
                if (social > highestSocial) {
                    highestSocial = social;
                    leader = bd;
                }
                if (wanted.Count >= MaxClusterGuests) {
                    break;
                }
            }
            if (leader == null) {
                data.FailEvent(s, "Nobody in the cluster can party", isHardFailure: false);
                return false;
            }
            wanted.Remove(leader);
            wanted.Insert(0, leader);
            return true;
        }

        // The venue decides how many come: a small room takes fewer guests, but a bigger room
        // is tried first (PartyVenue). The most wanted come first; those who cannot get there
        // stay away.
        private bool InviteToVenue(StoryEventData data, GameState s) {
            var host = wanted[0];
            if (!PartyVenue.TryFind(s, host, wanted.Count, out var venue)) {
                data.FailEvent(s, "No room for a party", isHardFailure: false);
                return false;
            }
            var reach = s.Sys.Path.Reg.Reach;
            int invited = 0;
            for (int i = 0; i < wanted.Count && invited < venue.Capacity; i++) {
                var guest = wanted[i];
                if (i > 0 && !reach.For(guest.Being.Motion).CanReach(venue.Center)) {
                    continue;
                }
                data.AddParticipant(guest);
                invited++;
            }
            if (invited < PartyVenue.MinGuests) {
                data.FailEvent(s, $"Too few guests can get to the party: {invited}",
                    isHardFailure: false);
                return false;
            }
            data.TargetBeing = host;
            // kept with the event too, for its music (OnTick)
            data.GetOrCreateData().SetInt(AIActionParty.PartyPosVar, venue.Center);
            return true;
        }

        private bool BeginParty(StoryContext ctx, StoryEventData data) {
            var numParticipants = Arrays.Count(data.Participants);
            if (numParticipants < PartyVenue.MinGuests) {
                data.FailEvent(ctx.S,
                    $"Too few participants for a party! {numParticipants}");
                return false;
            }
            var center = CenterOf(data);
            // every guest finds its own spot around the center (AIActionParty)
            int partying = 0;
            foreach (var mind in data.Participants) {
                if (mind.Owner?.Being == null) { continue; }
                var partyGoal = AIGoal.DirectOrder(JobTypeIdH.Social, IconId.CParty, T.NounParty,
                        mind.Owner.Being.AIAgent, ctx.Ticks)
                    .WithWorldToDesired(AIVarsH.HadParty, false, true);
                partyGoal.WithVar(AIActionParty.PartyPosVar, center);
                if (!ctx.S.Query.ForceGoal.Ask(partyGoal, false)) {
                    D.Err("Failed assigning party goal to: {0}", mind);
                } else {
                    partyGoal.Flags &= ~AIGoalFlags.ForceAssigned;
                    partying++;
                }
            }
            if (partying < 2) {
                D.Err("Too few started partying! {0}", partying);
                return false;
            }

            return true;
        }

        // The venue's center (PartyVenue.Center)
        public static int CenterOf(StoryEventData data) {
            return data.Data?.GetInt(AIActionParty.PartyPosVar, Pos.Invalid) ?? Pos.Invalid;
        }

        public override void OnTick(StoryContext ctx, StoryEventData data) {
            int partying = 0;
            foreach (var mind in data.Participants) {
                if (mind.Owner?.Being?.AIAgent.Goal?.Name == T.NounParty) {
                    partying++;
                }
            }
            if (partying < 2) {
                // done partying
                data.SetState(ctx.S, StoryEventState.Ending);
                ctx.S.Sys.Log.AddLine(T.EventEnded_V1.F(T.NounParty));
                return;
            }
            // The music plays for the party. Renewed here rather than saved: after a load the
            // next tick turns it back on, and a party that ends some other way than OnEnd stops
            // it within the hour.
            var music = PartyVenue.MusicFor(ctx.S, CenterOf(data));
            if (music != null) {
                music.KeepOnUntilTick = ctx.Ticks + Consts.TicksPerHour;
            }
        }

        protected override void OnEnd(StoryContext ctx, StoryEventData data) {
            var music = PartyVenue.MusicFor(ctx.S, CenterOf(data));
            if (music != null) {
                music.KeepOnUntilTick = 0;
            }
            var soc = SocialInteraction.For(data)
                .WithAttitude(0.1f);
            ctx.S.Query.RegisterSocialInteraction.Ask(soc);
        }

        private void Notify(StoryContext ctx, StoryEventData data) {
            BuildLogEntry(data);
            ctx.S.Sys.Log.AddLine(data.LogEntry.Desc);
            // data.AddEventNotification(ctx, IconId.CParty, data.LogEntry.Title,
            //     priority: Priority.Low,
            //     showPopup: false, showFanfare: false, addLogLine: true);
        }

        private void BuildLogEntry(StoryEventData data) {
            var names = Texts.ListAnd(data.Participants, (m) =>
                m.LinkToShortName);
            string title;
            string desc;
            if (data.TempObj is string occasions) {
                title = T.NounPartyCustom_V1.F(occasions);
                desc = T.PartyCustomDesc_V2.F(names, occasions);
            } else {
                title = T.NounParty;
                desc = T.PartyDesc_V1.F(names);
            }
            data.CreateLogEntry(IconId.CParty, title, desc);
        }
    }
}
