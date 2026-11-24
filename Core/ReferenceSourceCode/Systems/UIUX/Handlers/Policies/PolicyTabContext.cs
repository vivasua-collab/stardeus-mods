using System;
using System.Collections.Generic;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Policies;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
    // Everything a policy tab needs to render one scope. Passed instead of a long
    // parameter list so new tabs (Schedule, ...) can use what they need without
    // changing the BasePolicyTab signature.
    public sealed class PolicyTabContext {
        public GameState S;
        public PoliciesSys Sys;
        public Lifetime Life;
        public WGroup Content;
        public WGroup Footer;
        public PolicyScope Scope;
        // Set when Scope is Being.
        public BeingData Being;
        // Set when Scope is Being or Species.
        public Species Species;
        // What this scope is called in the header above the tab.
        public string ScopeName;
        // Beings currently visible in the left list, for scope-wide warnings.
        public List<BeingData> ListedBeings;
        // First free block index in Content. The handler puts the "what am I
        // configuring" header above whatever the tab spawns.
        public int StartGroupIdx;
        // Rebuilds the whole tab, e.g. after switching the policy origin.
        public Action Rebuild;
        // A value is still being dragged: publish it to the AI, but don't strip gear
        // on intermediate values and don't rebuild the widget under the cursor.
        public Action ApplyLive;
        // A discrete change that keeps the tab layout as it is. Publishes and enforces
        // only - refreshing anything on screen is the tab's own business.
        public Action ApplyCommit;
        // A discrete change that alters what the whole tab shows. A tab that only
        // respawned part of itself should call ApplyCommit + RelayoutContent instead.
        public Action ApplyAndRebuild;
        // Resizes the content scroll after the tab respawned some of its widgets,
        // without moving the scroll position.
        public Action RelayoutContent;
    }
}
