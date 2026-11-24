#if !PRODUCTION_BUILD
#define DEBUG_UI
#endif
using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Utils;
using KL.Utils;
using Game.Systems.Ships;

namespace Game.Systems.Diplomacy {
    public sealed class GiftChoiceUI {
        public readonly GameState S;
        public readonly NPCShip Ship;
        public readonly FactionDef Faction;
        public readonly GiftMode Mode;
        public readonly GiftDirection Direction;
        public readonly List<GiftPackage> Choice = new();
        public string SelectPackageText {
            get {
                switch (Direction) {
                    case GiftDirection.PlayerToNPC: {
                        return Texts.Accented(T.OfferThisPackage);
                    }
                    case GiftDirection.NPCToPlayer: {
                        return Texts.Accented(T.TakeThisPackage);
                    }
                    default: {
                        D.Err("Unhandled gift direction: {0}", Direction);
                        return T.Select;
                    }
                }
            }
        }

        public GiftGen Generator => new GiftGen(S, Ship, Mode, Direction);

        public static GiftChoiceUI For(GameState s, NPCShip ship, GiftMode mode, GiftDirection direction) {
            return new GiftChoiceUI(s, ship, ship?.Faction, mode, direction);
        }

        // No ship on the other side (a planet space port): Ship and Generator are unusable,
        // options are built with GiftPackage.ForFaction
        public static GiftChoiceUI ForFaction(GameState s, FactionDef faction, GiftMode mode,
                GiftDirection direction) {
            return new GiftChoiceUI(s, null, faction, mode, direction);
        }

        private GiftChoiceUI(GameState s, NPCShip ship, FactionDef faction, GiftMode mode,
                GiftDirection direction) {
            S = s;
            Ship = ship;
            Faction = faction;
            Mode = mode;
            Direction = direction;
        }

        public void AddOption(GiftPackage package) {
            if (!package.IsCancel) {
                int dup = Choice.FindIndex(p => !p.IsCancel && p.HasSameContents(package));
                if (dup >= 0) {
                    // Same contents, different slot consequences - keep the one better for
                    // the player. Giving: earlier slots have higher accept chance and bigger
                    // relationship gain. Receiving: later slots cost less relationship.
                    if (Direction == GiftDirection.PlayerToNPC) {
                        return;
                    }
                    Choice.RemoveAt(dup);
                }
            }
            Choice.Add(package);
            RenumberOptions();
        }

        private void RenumberOptions() {
            int num = 0;
            foreach (var p in Choice) {
                if (p.IsCancel) { continue; }
                p.Renumber(num++);
            }
        }
    }
}
