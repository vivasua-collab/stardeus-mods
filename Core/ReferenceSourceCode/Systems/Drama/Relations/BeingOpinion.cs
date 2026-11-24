using System;
using Game.Constants;
using Game.Data;
using Game.Systems.Mental;
using Game.Systems.UIUX;
using Game.Utils;
using KL.Randomness;
using KL.Utils;
using MessagePack;
using UnityEngine;

namespace Game.Systems.Drama {
    [MessagePackObject]
    public sealed class BeingOpinion  {
        [Key("OM")] public uint OwnerMindId;
        [Key("TM")] public uint TargetMindId;
        [Key("TP")] public uint TicksInProximity;
        [Key("GI")] public int GoodInteractions;
        [Key("BI")] public int BadInteractions;
        [Key("GK")] public float GoodKarma;
        [Key("BK")] public float BadKarma;
        [Key("L")] public float Love;
        [Key("F")] public float Friendship;
        [Key("P")] public float Pet;
        [Key("H")] public float Hate;
        [Key("SA")] public float SexualAttraction;
        [Key("SAf")] public float SocialAffinity;
        [Key("Fl")] public BeingOpinionFlags Flags;
        [Key("NMT")] public long NextMournTick;
        /// <summary>
        /// Tick when this opinion was last meaningfully changed (interaction, proximity, flag).
        /// 0 means untracked (treated as maximally stale by the forgetting sweep).
        /// </summary>
        [Key("LC")] public long LastChangedTick;
        private Mind targetMind;
        [IgnoreMember] public Mind TargetMind {
            get {
                return targetMind ??= A.S.Query.FindMind.Ask(TargetMindId);
            }
            set {
                targetMind = value;
                TargetMindId = value?.Id ?? 0;
            }
        }
        private Mind ownerMind;
        [IgnoreMember] public Mind OwnerMind {
            get {
                return ownerMind ??= A.S.Query.FindMind.Ask(OwnerMindId);
            }
            set {
                ownerMind = value;
                OwnerMindId = value?.Id ?? 0;
            }
        }

        [IgnoreMember] public readonly BCallback Changed = new();

        /// <summary>
        /// Stamps LastChangedTick with the current game tick.
        /// Call whenever this opinion is meaningfully updated.
        /// </summary>
        public void MarkChanged() {
            LastChangedTick = G.Ticks;
            Changed.Invoke();
        }

        public string OpinionTip(BeingOpinion targetOp) {
            var sb = Caches.StringBuilder2;
            sb.Clear();
            var source = OwnerMind;
            var target = TargetMind;
            bool hasOpinion = false;
            sb.Append(Texts.SubTitle(T.OpinionAbout_V1.F(target.LinkToShortName)));
            // var statusText = StatusOf(targetOp, out var statusIcon);
            if (A.InstantBuild) {
                sb.AppendFormat("{0}: {1}\n", T.NounSocialAffinity, Units.PercentageColored(SocialAffinity, clamp: false));
                if (Love > 0f || SexualAttraction > 0f) {
                    sb.AppendFormat("{0}: {1}\n", T.NounAttraction, Units.PercentageColored(SexualAttraction, clamp: false));
                }
            }

            bool areFriends = AreFriendsWith(targetOp);
            if (areFriends) {
                sb.AppendFormat("{0} ({1}: {2})\n", T.StatusFriends, T.StrengthFriendship,
                    Texts.RelationshipStrength(Friendship, areFriends));
                hasOpinion = true;
            } else if (Friendship > 0.05f) {
                sb.AppendFormat("{0}: {1}\n", T.DevelopingFriendship,
                    Texts.RelationshipStrength(Friendship, areFriends));
                hasOpinion = true;
            }
            bool isNemesis = Flags.HasFlag(BeingOpinionFlags.Nemesis);
            if (isNemesis) {
                sb.AppendFormat("{0} ({1}: {2})\n", T.StatusNemesis, T.StrengthHate,
                    Texts.RelationshipStrength(Hate, isNemesis));
                hasOpinion = true;
            } else if (Hate > 0.05f) {
                sb.AppendFormat("{0}: {1}\n", T.DevelopingHate,
                    Texts.RelationshipStrength(Hate, isNemesis));
                hasOpinion = true;
            }

            var areLovers = AreLoversWith(targetOp);
            if (areLovers) {
                sb.AppendFormat("{0}\n", T.StatusLovers);
                hasOpinion = true;
            } else if (Flags.HasFlag(BeingOpinionFlags.Crush)) {
                sb.AppendFormat("{0}\n", T.StatusCrush);
                hasOpinion = true;
            }
            if (areLovers || Love > 0.05f) {
                sb.AppendFormat("{0}: {1}\n", areLovers ? T.StrengthLove : T.DevelopingLove,
                    Texts.RelationshipStrength(Love, areLovers));
                hasOpinion = true;
            }
            var areMarried = AreMarried(targetOp);
            if (areMarried) {
                sb.AppendFormat("{0}\n", T.StatusSpouse);
                hasOpinion = true;
            }

            if (source.Species.IsHumanoid && target.Species.IsPet) {
                var isMasterOf = Flags.HasFlag(BeingOpinionFlags.Pet);
                if (isMasterOf) {
                    sb.AppendFormat("{0}\n", T.FlagPet);
                    hasOpinion = true;
                }
            } else if (source.Species.IsPet && target.Species.IsHumanoid) {
                var isPetOf = Flags.HasFlag(BeingOpinionFlags.Master);
                if (isPetOf) {
                    sb.AppendFormat("{0}\n", T.StatusMaster);
                    hasOpinion = true;
                }
            }
            if (!hasOpinion) {
                sb.AppendLine(T.NoOpinion);
            }
            return sb.ToString();
        }

        private bool ChangeAttitude(GameState s, SocialInteraction soc,
                Mind b1, Mind b2, float weight) {
            var karma = soc.AttitudeChange * weight;
            if (karma == 0f) { return false; }
            if (karma > 0f) {
                GoodKarma += karma;
            } else if (karma < 0f) {
                BadKarma += karma;
            }
            var rng1 = new Rng(Hashes.Quick((int) b1.Id, Primes.Prime012));
            var rng2 = new Rng(Hashes.Quick((int) b2.Id, Primes.Prime012));
            var socTicket1 = rng1.Range(1, 6);
            var socTicket2 = rng2.Range(1, 6);
            var opposites = Mathf.Abs(socTicket1 - socTicket2);
            var oppPercent = Mathf.InverseLerp(0f, 4f, opposites);
            if (SexualAttraction > 0f) {
                var loveRate = Tunables.SocialLoveRate * (oppPercent + 0.15f);
                var loveChange = karma * SexualAttraction * loveRate;
                ChangeLove(s, b1, b2, loveChange, soc.AttitudeMul_Love);
            }
            if (SocialAffinity > 0f) {
                var friendRate = Tunables.SocialFriendshipRate * (1.15f - oppPercent);
                var change = karma * SocialAffinity * friendRate;
                ChangeFriendship(s, b1, b2, change, soc.AttitudeMul_Friendship);
                ChangePet(s, b1, b2, change * 0.75f, soc.AttitudeMul_Pet);
            } else if (SocialAffinity < 0f)  {
                var hateRate = Tunables.SocialHateRate * (oppPercent + 0.15f);
                var hateChange = karma * SocialAffinity * hateRate;
                ChangeHate(s, b1, b2, hateChange, soc.AttitudeMul_Hate);
            }
            return true;
        }

        public void ApplySocialInteraction(GameState s, SocialInteraction soc, Mind b1, Mind b2, float weight) {
            // D.Warn("Applying social interaction: {0} | w: {1}", soc, weight);
            // The tally is the pair's shared history, so it counts on both sides even where
            // one side is not moved by it (a stalker is not upset by being caught, STA-1921)
            if (soc.AttitudeChange > 0f) {
                GoodInteractions++;
            } else if (soc.AttitudeChange < 0f) {
                BadInteractions++;
            }
            if (weight == 0f) { return; }
            bool changed = TrySetFlag(BeingOpinionFlags.Acquaintance);

            if (soc.AttitudeChange != 0f) {
                changed |= ChangeAttitude(s, soc, b1, b2, weight);
            }
            changed |= ChangeLove(s, b1, b2, soc.LoveChange * weight);
            changed |= ChangeHate(s, b1, b2, soc.HateChange * weight);
            changed |= ChangeFriendship(s, b1, b2, soc.FriendshipChange * weight);
            changed |= ChangePet(s, b1, b2, soc.OwnershipChange * weight);
            if (changed) {
                MarkChanged();
            }
        }

        private bool ChangeFriendship(GameState s, Mind b1, Mind b2, float amt, float mul = 0f) {
            if (amt == 0f) { return false; }
            if (mul > 0f) { amt *= mul; }
            // D.Warn("Changing friendship: {0} += {1} (mul: {2})| {3} x {4}",
            //     Friendship, amt, mul, b1.Name, b2.Name);
            var newFriendship = Mathf.Clamp01(Friendship + amt);
            if (amt > 0f) {
                newFriendship = OpinionLimits.CapGrowth(Friendship, newFriendship,
                    OpinionLimits.MaxFriendship(SocialAffinity));
            }
            if (newFriendship != Friendship) {
                Friendship = newFriendship;
                OnFriendshipChange(s, b1, b2, newFriendship);
                return true;
            }
            return false;
        }

        private bool ChangeHate(GameState s, Mind b1, Mind b2, float amt, float mul = 0f) {
            if (amt == 0f) { return false; }
            if (mul > 0f) { amt *= mul; }
            var newHate = Mathf.Clamp01(Hate + amt);
            if (amt > 0f) {
                newHate = OpinionLimits.CapGrowth(Hate, newHate,
                    OpinionLimits.MaxHate(SocialAffinity));
            }
            if (newHate != Hate) {
                Hate = newHate;
                OnHateChange(s, b1, b2, newHate);
                return true;
            }
            return false;
        }

        private bool ChangeLove(GameState s, Mind b1, Mind b2, float amt, float mul = 0f) {
            if (amt == 0f) {
                return false;
            }
            if (mul > 0f) { amt *= mul; }
            // Orientation incompatibility is permanent: love must not grow without attraction
            if (amt > 0f && SexualAttraction <= 0f) { return false; }
            var newLove = Mathf.Clamp01(Love + amt);
            if (amt > 0f) {
                newLove = OpinionLimits.CapGrowth(Love, newLove,
                    OpinionLimits.MaxLove(SexualAttraction));
            }
            if (newLove != Love) {
                Love = newLove;
                OnLoveChanged(s, b1, b2, newLove);
                return true;
            }
            return false;
        }

        private bool ChangePet(GameState s, Mind b1, Mind b2, float amt, float mul = 0f) {
            if (amt == 0f) { return false; }
            if (!b1.Species.IsPet || !b2.Species.IsHumanoid) {
                return false;
            }
            if (mul > 0f) { amt *= mul; }
            bool ignoreOwnershipChange = false;
            if (b1.Bonds.Important.TryGetValue(BeingOpinionFlags.Master, out var masters))  {
                if (masters.First != b2) {
                    ignoreOwnershipChange = true;
                }
            }
            if (!ignoreOwnershipChange) {
                var newPet = Mathf.Clamp01(Pet + amt);
                if (newPet == Pet) {
                    return false;
                }
                Pet = newPet;
                if (newPet == 1f && !Flags.HasFlag(BeingOpinionFlags.Master)) {
                    if (b2.Bonds.Opinions.TryGetValue(b1.Id, out var op2)) {
                        SetFlag(BeingOpinionFlags.Master);
                        op2.SetFlag(BeingOpinionFlags.Pet);
                        BeingBonds.AnnounceNew(s, b1, b2, BeingOpinionFlags.Pet);
                    }
                }
                return true;
            }

            return false;
        }

        public bool TrySetFlag(BeingOpinionFlags flag) {
            if ((Flags & flag) == flag) { return false; }
            Flags |= flag;
            if ((BeingOpinionFlags.Important & flag) == flag) {
                OwnerMind.Bonds.AddImportant(flag, this);
            }
            MarkChanged();
            return true;
        }

        public void SetFlag(BeingOpinionFlags flag) {
            if (!TrySetFlag(flag)) {
                D.Warn("Trying to set flag when it's already set: {0} for {1}", flag, this);
            }
        }
        public void RemoveFlag(BeingOpinionFlags flag) {
            if ((Flags & flag) == BeingOpinionFlags.None) {
                D.Warn("Trying to remove flag when it's not set: {0} for {1}", flag, this);
                return;
            }
            Flags &= ~flag;
            if ((BeingOpinionFlags.Important & flag) == flag) {
                OwnerMind.Bonds.RemoveImportant(flag, this);
            }
            MarkChanged();
        }

        private void OnHateChange(GameState s, Mind b1, Mind b2, float newHate) {
            if (newHate != OpinionLimits.NemesisAtHate || !TrySetFlag(BeingOpinionFlags.Nemesis)) {
                return;
            }
            BeingBonds.AnnounceNew(s, b1, b2, BeingOpinionFlags.Nemesis);
        }

        private void OnFriendshipChange(GameState s, Mind b1, Mind b2, float newFriendship) {
            if (newFriendship < OpinionLimits.FriendFromFriendship) {
                if (newFriendship < 0.15f && Flags.HasFlag(BeingOpinionFlags.Friend)) {
                    RemoveFlag(BeingOpinionFlags.Friend);
                }
                return;
            }
            if (Flags.HasFlag(BeingOpinionFlags.Friend)) {
                return;
            }
            SetFlag(BeingOpinionFlags.Friend);
            if (b2.Bonds.Opinions.TryGetValue(b1.Id, out var op2)) {
                if (op2.Flags.HasFlag(BeingOpinionFlags.Friend)) {
                    BeingBonds.AnnounceNew(s, b1, b2, BeingOpinionFlags.Friend);
                }
            }
        }

        private void OnLoveChanged(GameState s, Mind b1, Mind b2, float newLove) {
            if (newLove > OpinionLimits.CrushAboveLove && SexualAttraction > 0f) {
                TrySetFlag(BeingOpinionFlags.Crush);
            } else {
                if (newLove < 0.15f) {
                    if ((Flags & BeingOpinionFlags.Lover) == BeingOpinionFlags.Lover) {
                        RemoveFlag(BeingOpinionFlags.Lover);
                    }
                    if ((Flags & BeingOpinionFlags.Crush) == BeingOpinionFlags.Crush) {
                        RemoveFlag(BeingOpinionFlags.Crush);
                    }
                }
            }
        }

        public static bool MakeLovers(GameState s, BeingOpinion op1, BeingOpinion op2) {
            if (op1.Flags.HasFlag(BeingOpinionFlags.Lover)) {
                return false;
            }
            if (op2.Flags.HasFlag(BeingOpinionFlags.Lover)) {
                return false;
            }
            // new lovers!
            op1.SetFlag(BeingOpinionFlags.Lover);
            op2.SetFlag(BeingOpinionFlags.Lover);
            BeingBonds.AnnounceNew(s, op1.OwnerMind, op2.OwnerMind, BeingOpinionFlags.Lover);
            return true;
        }

        public bool AreFriendsWith(BeingOpinion other) {
            return (Flags & other.Flags & BeingOpinionFlags.Friend) == BeingOpinionFlags.Friend;
        }
        public bool AreLoversWith(BeingOpinion other) {
            return (Flags & other.Flags & BeingOpinionFlags.Lover) == BeingOpinionFlags.Lover;
        }
        public bool AreMarried(BeingOpinion other) {
            return (Flags & other.Flags & BeingOpinionFlags.Spouse) == BeingOpinionFlags.Spouse;
        }

        public string StatusOf(BeingOpinion otherOp, out string icon) {
            if (Flags == BeingOpinionFlags.None) {
                icon = IconId.CQuestion;
                return T.StatusUnfamiliar;
            }
            if (Flags.HasFlag(BeingOpinionFlags.Pet)) {
                icon = IconId.CPaw;
                return T.Pet;
            }
            if (Flags.HasFlag(BeingOpinionFlags.Master)) {
                icon = IconId.CPaw;
                return T.StatusMaster;
            }
            if (Flags.HasFlag(BeingOpinionFlags.Spouse)) {
                icon = IconId.CRings;
                return T.StatusSpouse;
            }
            if (Flags.HasFlag(BeingOpinionFlags.Nemesis)) {
                icon = IconId.CFist;
                return T.StatusNemesis;
            }
            if (AreLoversWith(otherOp)) {
                icon = IconId.CRelationships;
                return T.StatusLovers;
            }
            if (Flags.HasFlag(BeingOpinionFlags.Crush)) {
                icon = IconId.CHeart;
                return T.StatusCrush;
            }
            if (AreFriendsWith(otherOp)) {
                icon = IconId.CThumbsUp;
                return T.StatusFriends;
            }
            if (Flags.HasFlag(BeingOpinionFlags.Acquaintance)) {
                icon = IconId.CWave;
                return T.StatusAcquaintance;
            }
            D.Err("Unhandled BeingOpinion.Flags status: {0}", Flags);
            icon = IconId.CWarning;
            return Flags.ToString();
        }

        public void Validate() {
            if (SexualAttraction <= 0f) {
                if (Love > 0f) {
                    D.Err("Fixing being opinion, love is {0} while attraction {1} | {2}->{3}",
                        Love, SexualAttraction, OwnerMind, targetMind);
                    Love = 0f;
                }
                if (Flags.HasFlag(BeingOpinionFlags.Crush)) {
                    RemoveFlag(BeingOpinionFlags.Crush);
                }
                if (Flags.HasFlag(BeingOpinionFlags.Lover)) {
                    RemoveFlag(BeingOpinionFlags.Lover);
                }
            }
        }
    }
}
