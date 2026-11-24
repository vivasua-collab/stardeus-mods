using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using MessagePack;
using UnityEngine;

namespace Game.Research {
    [MessagePackObject]
    public class ResearchProgress {
        [IgnoreMember] public bool HasResearchable { get; private set; }
        [IgnoreMember] public Researchable Researchable { get; private set; }
        [IgnoreMember] public bool IsDiscovered =>
            !Researchable.IsDiscoverable || IsMarkedDiscovered
                // Tier 0 research will not become discovered when internal progress is full,
                // because it starts with full internal progress
                || (InternalUnlocked && Def.Tier > 0);
        [Key("Id")] public string Id;
        [Key("IsMarkedDiscovered")] public bool IsMarkedDiscovered;
        [IgnoreMember] public int IdH;
        [Key("Unlocked")] public bool InternalUnlocked;
        private Def def;
        [IgnoreMember] public Def Def => def ??= The.Defs.TryGet(Id);
        [IgnoreMember] public bool IsUnlocked => InternalUnlocked || A.InstantBuild;
        [IgnoreMember] public int UnlockValue => Researchable.UnlockValue;
        [IgnoreMember] public int Storage => Researchable.Storage;
        [IgnoreMember] public bool IsLackingDisk;
        [IgnoreMember] public bool IsBookmarked;
        [IgnoreMember] public Action ChangeCallback;
        [IgnoreMember] public GameState S;
        [IgnoreMember] public bool CanBeUnlockedNow;

        public void SetResearchable(Researchable res) {
            HasResearchable = true;
            Researchable = res;
            if (res?.IsCostless == true) {
                InternalUnlocked = true;
            }
        }

        public void Remove() {
            InternalUnlocked = false;
        }

        public void Discover(GameState s) {
            IsMarkedDiscovered = true;
            s.Sig.ResearchChanged.Send(Def);
        }

        public void Unlock(bool notifyUnlockEffects = false) {
            if (!InternalUnlocked) {
                //var s = The.R.State;
                foreach (var def in Def.Unlockables) {
                    S.AddNewBlueprint(def.Id);
                }
                S.Research.UpdateTierCounts();
                S.Research.ApplyUnlockEffectsFor(Researchable, notifyUnlockEffects);
            }
            InternalUnlocked = true;
            S.Research.ResetCache();
            S.Research.UpdateTierCounts();
            The.R.Sig.ResearchChanged.Send(Def);
            // S.Sys.Research.UI.Rebuild();
        }

        public struct ResearchProgressComparer : IComparer<ResearchProgress> {
            public int Compare(ResearchProgress x, ResearchProgress y) {
                var t1 = x.Def.Tier;
                var t2 = y.Def.Tier;
                if (t1 > t2) { return 1; }
                if (t1 < t2) { return -1; }
                return string.Compare(x.Def.NameT, y.Def.NameT);
            }
        }
        public static readonly ResearchProgressComparer Comparer = default;

        public void Changed() {
            ChangeCallback?.Invoke();
        }

        public override string ToString() {
            return Id;
        }
    }
}
