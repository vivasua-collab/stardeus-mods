using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Drama;
using KL.Grid;
using KL.Randomness;
using UnityEngine;

namespace Game.Systems.AI {
    // How party guests stand (AIActionParty). Each one holds a tile of its own, which keeps
    // them from piling up, and chooses where to stand so that the party falls into small
    // groups of 2-3 that lean in and face each other, with a free tile between groups: a
    // room of conversations rather than a pile or a chess board. Every guest decides for
    // itself from where the others stand or are heading, so there is nothing to save or keep
    // in step. Docs/Architecture/Systems/Parties.md has the reasons.
    public static class PartyGroups {
        public const int MaxGroupSize = 3;
        // How far a guest leans toward its group, and how much per tick it moves there
        private const float Lean = 0.2f;
        private const float LeanStep = 0.04f;
        // Leaning forward or back would cover the face of whoever stands there
        private const float LeanVertical = 0.5f;
        // When its time to move on comes, a guest in a group stays this often...
        private const float StayInGroupChance = 0.65f;
        // ...and a lone guest goes to join somebody this often
        private const float LoneJoinChance = 0.75f;
        // Next to a group of another, a spot costs this much: groups keep a tile between them
        private const float OtherGroupPenalty = 5f;
        // Guests near the music bob along now and then
        private const float BobChance = 0.08f;
        private const int BobRadius = 4;

        private struct Guest {
            public Being Being;
            public int Pos;
            public int Group;
        }
        private static readonly List<Guest> guests = new();
        private static readonly List<int> groupSizes = new();
        private static readonly List<int> toVisit = new();
        private static readonly List<int> joinable = new();
        private static readonly List<float> joinWeights = new();

        public static bool IsGuestOf(Being b, int center) {
            var agent = b?.AIAgent;
            return agent != null && agent.CurrentAction?.IdH == AIActionParty.ActIdH
                && agent.BB.Int1 == center;
        }

        // Where the guest is going to stand next: its own tile when it stays, Pos.Invalid when
        // there is nowhere to go
        public static int PickSpot(GameState s, AIAgentComp agent, int center) {
            var me = agent.Being;
            var areaId = s.Sys.Areas.At(center);
            CollectGuests(s, me, center, areaId);
            var myGroup = guests.Count > 0 && guests[0].Being == me ? guests[0].Group : -1;
            var mySize = myGroup < 0 ? 0 : groupSizes[myGroup];
            var spot = Pos.Invalid;
            var movesOn = false;
            if (myGroup >= 0 && mySize <= MaxGroupSize) {
                if (mySize > 1) {
                    if (Rng.UChance(StayInGroupChance)) {
                        spot = me.PosIdx;
                    } else {
                        movesOn = true;
                    }
                } else if (!HasJoinable(myGroup) || !Rng.UChance(LoneJoinChance)) {
                    // waits for someone to come over
                    spot = me.PosIdx;
                }
            }
            if (spot == Pos.Invalid && !TryJoin(s, agent, center, areaId, myGroup, out spot)) {
                if (myGroup >= 0 && mySize <= MaxGroupSize && !movesOn) {
                    // a lone guest with no group to join keeps waiting
                    spot = me.PosIdx;
                } else {
                    // Every group is full: one that moves on starts a new one elsewhere, and
                    // others come over. Without it a party of full groups froze for hours.
                    spot = NewGroupSpot(s, agent, center, areaId);
                    if (spot == Pos.Invalid && myGroup >= 0 && mySize <= MaxGroupSize) {
                        spot = me.PosIdx;
                    }
                }
            }
            guests.Clear();
            groupSizes.Clear();
            return spot;
        }

        // While a guest stands: lean toward the guests it stands with, face one of them (else
        // the music or the middle of the party), and bob to the music
        public static void Stand(GameState s, AIAgentComp agent, int center, JukeboxComp music) {
            var me = agent.Being;
            var pos = agent.PosIdx;
            var sum = Vector2.zero;
            int count = 0;
            Being nearest = null;
            int nearestRank = int.MaxValue;
            for (int i = 0; i < 8; i++) {
                var np = Pos.NeighborAt(pos, i);
                if (Pos.IsOOB(np)) { continue; }
                if (!s.Beings.ByPos.TryGetValue(np, out var here)) { continue; }
                for (int j = 0; j < here.Count; j++) {
                    var b = here[j];
                    if (b == me || b.Motion.Progress.IsMoving || !IsGuestOf(b, center)) {
                        continue;
                    }
                    sum += Pos.ToVector(np);
                    count++;
                    // faces a guest beside it before one in front or behind, and those
                    // before a diagonal one (Pos.NeighborXY: N, E, W, S, then diagonals)
                    var rank = i == 1 || i == 2 ? 0 : i < 4 ? 1 : 2;
                    if (rank < nearestRank) {
                        nearest = b;
                        nearestRank = rank;
                    }
                }
            }
            var graphics = me.Graphics;
            var lean = Vector2.zero;
            if (count > 0) {
                var dir = (sum / count - Pos.ToVector(pos)).normalized;
                lean = new Vector2(dir.x * Lean, dir.y * Lean * LeanVertical);
            }
            var offset = graphics.PositionOffset;
            if (offset != lean) {
                graphics.SetOffset(Vector2.MoveTowards(offset, lean, LeanStep));
            }
            if (music != null && (!music.Entity.IsActive || !music.CanKeepUsing)) {
                music = null;
            }
            Vector2 lookAt;
            if (nearest != null) {
                lookAt = nearest.Position;
            } else if (music != null) {
                lookAt = music.Entity.Position;
            } else {
                lookAt = Pos.ToVector(center);
            }
            var facing = Facing.FromDirection(lookAt - me.Position, graphics.CurrentFacing);
            if (facing != graphics.CurrentFacing) {
                graphics.SetFacing(facing);
            }
            if (music != null && Rng.UChance(BobChance)
                    && Pos.SqrDistance(pos, music.WorkSpot) <= BobRadius * BobRadius) {
                graphics.Punch(Facing.ToOffset(facing, 1f), 0.1f,
                    Rng.URange(0.8f, 1.2f), Rng.URange(0.8f, 1.2f));
            }
        }

        public static void Clear(AIAgentComp agent) {
            var being = agent.Being;
            if (being == null || !being.IsActive) { return; }
            var graphics = being.Graphics;
            if (graphics != null && graphics.PositionOffset != Vector2.zero) {
                graphics.SetOffset(Vector2.zero);
            }
        }

        // The guests of this party in its venue, where each stands or is heading, grouped by
        // touching tiles. The asking guest comes first when it stands on a spot of the venue:
        // one that only got there (the music's work spot is the center) has to move on.
        private static void CollectGuests(GameState s, Being me, int center, int areaId) {
            guests.Clear();
            groupSizes.Clear();
            var myPos = me.PosIdx;
            if (PartyVenue.IsInVenue(s, center, areaId, myPos) && PartyVenue.IsSpot(s, myPos)) {
                guests.Add(new Guest { Being = me, Pos = myPos, Group = -1 });
            }
            var all = s.Beings.Alive;
            for (int i = 0; i < all.Count; i++) {
                var b = all[i];
                if (b == me || !IsGuestOf(b, center)) { continue; }
                var p = IntendedPos(b);
                if (!PartyVenue.IsInVenue(s, center, areaId, p)) { continue; }
                guests.Add(new Guest { Being = b, Pos = p, Group = -1 });
            }
            for (int i = 0; i < guests.Count; i++) {
                if (guests[i].Group >= 0) { continue; }
                var group = groupSizes.Count;
                groupSizes.Add(0);
                SetGroup(i, group);
                toVisit.Add(i);
                while (toVisit.Count > 0) {
                    var j = toVisit[toVisit.Count - 1];
                    toVisit.RemoveAt(toVisit.Count - 1);
                    for (int k = 0; k < guests.Count; k++) {
                        if (guests[k].Group < 0 && AreTouching(guests[j].Pos, guests[k].Pos)) {
                            SetGroup(k, group);
                            toVisit.Add(k);
                        }
                    }
                }
            }
        }

        private static void SetGroup(int i, int group) {
            var g = guests[i];
            g.Group = group;
            guests[i] = g;
            groupSizes[group]++;
        }

        // A walker counts where it is heading
        private static int IntendedPos(Being b) {
            var progress = b.Motion.Progress;
            if (progress.IsMoving && progress.GoalPosIdx > 0) {
                return progress.GoalPosIdx;
            }
            return b.PosIdx;
        }

        private static bool AreTouching(int a, int b) {
            Pos.ToXY(a, out var ax, out var ay);
            Pos.ToXY(b, out var bx, out var by);
            return Mathf.Abs(ax - bx) <= 1 && Mathf.Abs(ay - by) <= 1;
        }

        private static bool HasJoinable(int myGroup) {
            for (int g = 0; g < groupSizes.Count; g++) {
                if (g != myGroup && groupSizes[g] < MaxGroupSize) { return true; }
            }
            return false;
        }

        // Joins a group that has room, friends first and lone guests before pairs
        private static bool TryJoin(GameState s, AIAgentComp agent, int center, int areaId,
                int myGroup, out int spot) {
            spot = Pos.Invalid;
            joinable.Clear();
            joinWeights.Clear();
            for (int g = 0; g < groupSizes.Count; g++) {
                if (g == myGroup || groupSizes[g] >= MaxGroupSize) { continue; }
                joinable.Add(g);
                var weight = 1f + 2f * Affinity(agent, g) + (groupSizes[g] == 1 ? 1f : 0f);
                joinWeights.Add(weight);
            }
            while (joinable.Count > 0) {
                var i = PickWeighted();
                if (TryJoinGroup(s, agent, center, areaId, joinable[i], out spot)) {
                    break;
                }
                joinable.RemoveAt(i);
                joinWeights.RemoveAt(i);
            }
            joinable.Clear();
            joinWeights.Clear();
            return spot != Pos.Invalid;
        }

        private static int PickWeighted() {
            float total = 0f;
            for (int i = 0; i < joinWeights.Count; i++) {
                total += joinWeights[i];
            }
            var roll = Rng.UValue * total;
            for (int i = 0; i < joinWeights.Count; i++) {
                roll -= joinWeights[i];
                if (roll <= 0f) { return i; }
            }
            return joinWeights.Count - 1;
        }

        // How much the guest likes the members of a group, 0..1
        private static float Affinity(AIAgentComp agent, int group) {
            var opinions = agent.BeingData.Mind?.Bonds?.Opinions;
            if (opinions == null) { return 0f; }
            float sum = 0f;
            int n = 0;
            for (int i = 0; i < guests.Count; i++) {
                var guest = guests[i];
                if (guest.Group != group) { continue; }
                n++;
                var mind = guest.Being.Data?.Mind;
                if (mind != null && opinions.TryGetValue(mind.Id, out var op)) {
                    sum += Mathf.Clamp01(op.Friendship + op.Love);
                }
            }
            return n == 0 ? 0f : sum / n;
        }

        private static bool TryJoinGroup(GameState s, AIAgentComp agent, int center, int areaId,
                int group, out int spot) {
            spot = Pos.Invalid;
            float best = float.MinValue;
            for (int i = 0; i < guests.Count; i++) {
                var member = guests[i];
                if (member.Group != group) { continue; }
                for (int n = 0; n < 8; n++) {
                    var p = Pos.NeighborAt(member.Pos, n);
                    if (!IsFreeSpot(s, agent, center, areaId, p)) { continue; }
                    var score = JoinScore(agent.Being, p, group, center);
                    if (score > best) {
                        best = score;
                        spot = p;
                    }
                }
            }
            return spot != Pos.Invalid;
        }

        // Beside a member beats in front of or behind one, which beats a diagonal; touching
        // two members makes an L of three. Touching another group costs a lot. In front of or
        // behind reads as a queue in the 3/4 view, so it wins only when no spot beside is
        // free: the gap is wider than the jitter and the pull to the center together.
        private static float JoinScore(Being me, int p, int group, int center) {
            float score = Rng.UValue;
            int members = 0;
            Pos.ToXY(p, out var px, out var py);
            for (int i = 0; i < guests.Count; i++) {
                var other = guests[i];
                if (other.Being == me) { continue; }
                Pos.ToXY(other.Pos, out var ox, out var oy);
                var dx = Mathf.Abs(ox - px);
                var dy = Mathf.Abs(oy - py);
                if (dx > 1 || dy > 1) { continue; }
                if (dx == 0 && dy == 0) {
                    // someone is already heading there
                    return float.MinValue;
                }
                if (other.Group != group) {
                    score -= OtherGroupPenalty;
                    continue;
                }
                members++;
                if (dy == 0) {
                    score += 4.5f;
                } else if (dx == 0) {
                    score += 2f;
                } else {
                    score += 1f;
                }
            }
            // closing an L of three is as good as lengthening a row
            if (members > 1) {
                score += 1.5f;
            }
            return score - 0.15f * Pos.Distance(p, center);
        }

        // A spot away from everyone, close to the middle of the party, to start a group at.
        // When every spot touches someone, the least crowded one.
        private static int NewGroupSpot(GameState s, AIAgentComp agent, int center, int areaId) {
            var me = agent.Being;
            Pos.ToXY(center, out var cx, out var cy);
            var r = PartyVenue.Radius;
            var spot = Pos.Invalid;
            float best = float.MinValue;
            for (int x = cx - r; x <= cx + r; x++) {
                for (int y = cy - r; y <= cy + r; y++) {
                    if (x < 0 || y < 0 || x >= Pos.GridW || y >= Pos.GridH) { continue; }
                    var p = Pos.FromXY(x, y);
                    if (!IsFreeSpot(s, agent, center, areaId, p)) { continue; }
                    float score = Rng.UValue * 1.5f - 0.3f * Pos.Distance(p, center);
                    for (int i = 0; i < guests.Count; i++) {
                        var other = guests[i];
                        if (other.Being == me || !AreTouching(other.Pos, p)) { continue; }
                        if (other.Pos == p) {
                            // someone is already heading there
                            score = float.MinValue;
                            break;
                        }
                        score -= OtherGroupPenalty;
                    }
                    if (score > best) {
                        best = score;
                        spot = p;
                    }
                }
            }
            return spot;
        }

        private static bool IsFreeSpot(GameState s, AIAgentComp agent, int center, int areaId,
                int p) {
            if (!PartyVenue.IsInVenue(s, center, areaId, p)) { return false; }
            if (!PartyVenue.IsSpot(s, p)) { return false; }
            if (p != agent.PosIdx && s.Sys.Path.IsSpotTaken(p, agent.Being.Id)) {
                return false;
            }
            if (agent.Motion.HadNavFailureAt(p)) { return false; }
            return agent.IsFeelingSafeAt(p);
        }
    }
}
