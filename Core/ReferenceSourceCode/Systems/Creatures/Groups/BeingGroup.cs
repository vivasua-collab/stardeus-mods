using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems.BodyParts;
using Game.UI;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Creatures {
    public class BeingGroup {
        public readonly int IdH;
        public readonly string Id;
        public int SortOrder;
        public BeingState SkipStates;
        public BeingGroupFlags Flags;
        public Lifetime Lifetime;
        public string NameT;
        public string DecoratedName(bool hasDC) {
            var name = NameT;
            if (name == null) { return null; }
            if (hasDC) {
                name = Texts.Underscore(name);
            }
            if (IdH == BeingGroupIdH.Enemy) {
                return Texts.Red(name);
            }
            if (IdH == BeingGroupIdH.Bot || IdH == BeingGroupIdH.Colonist || IdH == BeingGroupIdH.Animal) {
                return Texts.Green(name);
            }
            return name;
        }
        public readonly GameState S;
        public readonly List<BeingData> Members = new();
        public BeingGroupUIWidget UI { get; internal set; }
        public int Size => Members.Count;
        public bool IsEmpty => Members.Count == 0;
        public bool AnyMembersVisible {
            get {
                for (int i = Members.Count - 1; i >= 0; i--) {
                    BeingData m = Members[i];
                    if (!m.HideAvatarButton) {
                        return true;
                    }
                }
                return false;
            }
        }

        public readonly bool IsNotControllable;

        public BeingGroup(GameState s, string id) {
            S = s;
            Id = id;
            IdH = Hashes.S(id);
            SkipStates = BeingState.InColony;
            IsNotControllable = IdH == BeingGroupIdH.Other || IdH == BeingGroupIdH.Enemy
                || IdH == BeingGroupIdH.Guest;
            SetName(null);
        }

        public int LoadMembers() {
            Members.Clear();
            int loaded = S.Query.LoadBeingsForGroup.Ask(this, Members);
            if (loaded > 0) {
                if (UI != null) {
                    UI.Load(this, UI.Number);
                }
            }
            return loaded;
        }

        public void SetName(string name) {
            if (name == null) {
                NameT = GroupNameForId(Id, false);
            } else {
                NameT = name;
            }
        }

        public static string GroupNameForId(string id, bool ignoreMissing) {
            switch (id) {
                case BeingGroupId.Bot: return T.BeingGroupWorker;
                case BeingGroupId.Colonist: return T.BeingGroupColonist;
                case BeingGroupId.Animal: return T.BeingGroupAnimal;
                case BeingGroupId.Guest: return T.BeingGroupGuest;
                case BeingGroupId.Enemy: return T.BeingGroupEnemy;
                case BeingGroupId.Other: return T.Dead;
                default: {
                    var trKey = $"being.group.{id}";
                    var tr = trKey.TOrNull();
                    if (tr == null) {
                        if (!ignoreMissing) {
                            D.Err("No being group name translation: {0}", trKey);
                        }
                        return id;
                    }
                    return tr;
                }
            }
        }

        public static void EnsureDefaults(GameState S, List<BeingGroup> beingGroups) {
            // D.Warn("Ensuring default groups");
            var existingGroups = beingGroups.ToDictionary(g => g.IdH);
            // bot
            if (!existingGroups.TryGetValue(BeingGroupIdH.Bot, out var g)) {
                g = new BeingGroup(S, BeingGroupId.Bot);
                g.SortOrder = 0;
                beingGroups.Add(g);
            }
            g.SkipStates = BeingState.Uninitialized | BeingState.InColony;
            g.Flags = BeingGroupFlags.PlayerFaction;
            // colonist
            if (!existingGroups.TryGetValue(BeingGroupIdH.Colonist, out g)) {
                g = new BeingGroup(S, BeingGroupId.Colonist);
                g.SortOrder = 1;
                beingGroups.Add(g);
            }
            g.SkipStates = BeingState.Uninitialized | BeingState.InColony;
            g.Flags = BeingGroupFlags.PlayerFaction;
            // animal
            if (!existingGroups.TryGetValue(BeingGroupIdH.Animal, out g)) {
                g = new BeingGroup(S, BeingGroupId.Animal);
                g.SortOrder = 2;
                beingGroups.Add(g);
            }
            g.SkipStates = BeingState.Uninitialized | BeingState.InColony;
            g.Flags = BeingGroupFlags.PlayerFaction;
            // guest - non-hostile visitors of another faction, kept out of the player groups
            // above so they don't read as crew (see Docs/Architecture/Systems/Guests.md)
            if (!existingGroups.TryGetValue(BeingGroupIdH.Guest, out g)) {
                g = new BeingGroup(S, BeingGroupId.Guest);
                g.SortOrder = 3;
                beingGroups.Add(g);
            }
            g.SkipStates |= BeingState.Hidden | BeingState.Stored;
            if (Tunables.BeingsListUI_ShowEnemies) {
                // enemy
                if (!existingGroups.TryGetValue(BeingGroupIdH.Enemy, out g)) {
                    g = new BeingGroup(S, BeingGroupId.Enemy);
                    g.SortOrder = 4;
                    beingGroups.Add(g);
                }
                g.SkipStates |= BeingState.Dead | BeingState.Hidden | BeingState.Stored | BeingState.InColony;
            }
            if (!existingGroups.TryGetValue(BeingGroupIdH.Other, out g)) {
                g = new BeingGroup(S, BeingGroupId.Other);
                g.SortOrder = 100;
                beingGroups.Add(g);
            }
            g.SkipStates |= BeingState.Hidden | BeingState.Stored;
        }

        public string Tooltip() {
            var sb = Caches.StringBuilder2;
            sb.Clear();
            sb.Append(Texts.Accented(NameT));
            sb.Append(' ');
            sb.Append(Texts.Grey(Units.XNum(Size)));
            bool addedHR = false;

            var sorted = SortedMembers();
            int count = 0;
            int total = 0;
            foreach (var m in sorted) {
                if (!m.Body.IsHealthy || m.Body.IsUnhealthy) {
                    total++;
                    if (count < 20) {
                        addedHR = AppendStatus(sb, addedHR, m,
                            BodyUtils.StateName(m.Body.StateB.Value, m.Species.IsBiological));
                        count++;
                    }
                }
            }
            if (total > count) {
                sb.AppendLine();
                sb.Append(Texts.Grey($"+{total - count}"));
            }

            return sb.ToString();
        }

        public IOrderedEnumerable<BeingData> SortedMembers() {
            return Members
                .OrderBy(a => a.Species.Name)
                .ThenBy(m => m.Mind.Name.ShortName);
        }

        private static bool AppendStatus(StringBuilder sb, bool addedHR, BeingData m, string status) {
            if (!addedHR) {
                addedHR = true;
                sb.Append(GlyphMap.BRHR);
            } else {
                sb.AppendLine();
            }
            sb.Append(BeingUtils.DecoratedBeingName(m, false));
            sb.Append(": ");
            sb.Append(status);
            return addedHR;
        }
        public static void AssignFor(BeingData b) {
            var isAlive = b.HasStateFlag(BeingState.Alive);
            if (b.IsPlayer || b.IsGuest) {
                if (!isAlive) {
                    var timeDead = G.Ticks - b.Body.DeathTick;
                    if (timeDead > Consts.TicksPer12Hours) {
                        b.GroupId = BeingGroupIdH.Other;
                        return;
                    }
                }
                if (b.IsGuest) {
                    // Non-hostile visitor of another faction - kept separate from the player's
                    // own Bot/Colonist/Animal groups so it doesn't read as crew.
                    b.GroupId = BeingGroupIdH.Guest;
                } else if (!b.Species.IsBiological) {
                    b.GroupId = BeingGroupIdH.Bot;
                } else if (b.Species.IsHumanoid) {
                    b.GroupId = BeingGroupIdH.Colonist;
                } else {
                    b.GroupId = BeingGroupIdH.Animal;
                }
            } else {
                if (isAlive) {
                    if (b.Mind.Faction.IsBlank || !Tunables.BeingsListUI_ShowEnemies) {
                        b.GroupId = BeingGroupIdH.Other;
                    } else {
                        b.GroupId = BeingGroupIdH.Enemy;
                    }
                } else {
                    b.GroupId = BeingGroupIdH.Other;
                }
            }
        }

        public void Dispose() {
            if (Lifetime != null) {
                Lifetime.Dispose();
                Lifetime = null;
            }
        }
        public override string ToString() {
            return $"BeingGroup[{Id}|{Size}]";
        }
    }
}
