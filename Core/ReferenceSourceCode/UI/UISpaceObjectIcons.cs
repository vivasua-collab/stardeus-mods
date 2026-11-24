using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Data.Space;
using Game.Systems.UIUX;
using Game.Utils;
using KL.Collections;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI {
    // Status icons of a starmap space object: the owner's flag in the status icon, and
    // everything else we know about it listed side by side, each with its own tooltip.
    public sealed class UISpaceObjectIcons {
        private readonly WBase owner;
        private readonly Image statusIcon;
        private readonly RectTransform container;
        private readonly List<MatType> mats = new();
        private readonly List<MatType> matsScratch = new();
        private readonly List<AutoMinerInfo> rigs = new();
        private Lifetime life;
        private SpaceObject so;
        private FactionDef faction;
        private int iconIdx;
        private float nextMatsCheckAt;

        public UISpaceObjectIcons(WBase owner, Image statusIcon, RectTransform container) {
            this.owner = owner;
            this.statusIcon = statusIcon;
            this.container = container;
        }

        public void Show(Lifetime parent, SpaceObject obj) {
            life?.Dispose();
            life = parent.CreateChild("Icons");
            so = obj;
            iconIdx = 0;
            mats.Clear();
            statusIcon.gameObject.SetActive(false);
            if (obj.Parent == null) {
                D.Err("Trying to show status icon for obj without parent: {0}", obj);
                return;
            }
            ShowFaction(obj);
            if (obj.Parent.Type.IdH == SpaceObjectTypeIdH.Region) {
                AddSectorIcons(obj);
            } else if (obj.Type.IdH == SpaceObjectTypeIdH.Region) {
                AddRegionIcons(obj);
            }
        }

        // Deposits get mined out without anyone refreshing the starmap, so the shown
        // materials are re-checked once a second while there are any
        public bool HaveMatsChanged() {
            if (mats.Count == 0 || GameTimer.unscaledTime < nextMatsCheckAt) {
                return false;
            }
            nextMatsCheckAt = GameTimer.unscaledTime + 1f;
            CollectKnownMats(so, matsScratch);
            if (matsScratch.Count != mats.Count) {
                return true;
            }
            for (int i = 0; i < mats.Count; i++) {
                if (mats[i] != matsScratch[i]) {
                    return true;
                }
            }
            return false;
        }

        public void Clear() {
            life?.Dispose();
            life = null;
            so = null;
            faction = null;
            mats.Clear();
            rigs.Clear();
        }

        private void ShowFaction(SpaceObject obj) {
            faction = OwnerFaction(obj);
            if (faction == null) {
                return;
            }
            statusIcon.sprite = Images.Sprite(faction.FlagIcon);
            statusIcon.gameObject.SetActive(true);
            // Dynamic, so the standing is current after trading with the starmap open
            Tooltip.CreateFor(life, owner, statusIcon.rectTransform, FactionTip);
        }

        private string FactionTip() {
            if (faction == null) {
                return null;
            }
            return $"{Texts.Title(faction.NameT)}{Texts.FactionTooltip(faction)}";
        }

        private static FactionDef OwnerFaction(SpaceObject obj) {
            if (obj.Type.IdH == SpaceObjectTypeIdH.Player) {
                return FactionDef.Player;
            }
            if (obj.Ship != null) {
                return obj.Ship.IsDerelict ? null : obj.Ship.Faction;
            }
            var fac = obj.Faction;
            if (fac == null || fac.IsPlayer
                    || obj.Type.IdH == SpaceObjectTypeIdH.HyperjumpRelay) {
                return fac;
            }
            if (obj.Sector != null && obj.Knowledge.FactionId == null) {
                // for old saves
                obj.Knowledge.FactionId = obj.FactionId;
            }
            // HasKnowledge first: reading Knowledge creates it, and it ends up in the save
            return obj.HasKnowledge && obj.Knowledge.FactionId != null ? fac : null;
        }

        private void AddSectorIcons(SpaceObject obj) {
            AddCommonIcons(obj);
            var pois = obj.Knowledge.POIs;
            if (Arrays.IsEmpty(pois)) {
                return;
            }
            foreach (var poi in pois) {
                var type = SpaceObjectType.Find(poi.Key);
                if (type == null) {
                    continue;
                }
                var tip = poi.Value > 1 ? $"{type.NameT} x{poi.Value}" : type.NameT;
                AddIcon(type.Icon, tip);
            }
        }

        private void AddRegionIcons(SpaceObject obj) {
            AddCommonIcons(obj);
        }

        private void AddCommonIcons(SpaceObject obj) {
            // Our own colony shows our flag instead of the mark
            if (obj.HasTerraformable && obj.Faction != FactionDef.Player) {
                AddIcon(IconId.WSpecial, T.FoundTerraformablePlanet);
            }
            // The new game universe preview asks the main menu's backdrop game, whose ships
            // are not in the previewed universe
            if (!UIShowing.MainMenu && obj.S.Query.HasNPCShipsAt.Ask(obj)) {
                AddIcon(IconId.CSpaceShip, T.StarmapIconShips);
            }
            AddAutoMinerIcon(obj);
            CollectKnownMats(obj, mats);
            foreach (var mat in mats) {
                AddIcon(mat.IconId, mat.NameT);
            }
            nextMatsCheckAt = GameTimer.unscaledTime + 1f;
        }

        // One drill icon when a deployed auto miner works anywhere in this sector or region
        // (STA-1879). Its tooltip is live: storage and depth change while the map is open.
        private void AddAutoMinerIcon(SpaceObject obj) {
            rigs.Clear();
            if (UIShowing.MainMenu || !obj.S.BB.Get(BBKeyH.MiningAuto, false)) {
                return;
            }
            AutoMinerList.CollectFor(obj.S, obj, rigs);
            if (rigs.Count == 0) {
                return;
            }
            var w = UISpawn.Widget<WImage>(life, UIPrefabId.WIcon, container, iconIdx++);
            w.Load(IconId.CDrill);
            UIUtils.AddTipFunc(life, w, () => so == null ? null
                : AutoMinerList.NodeTooltip(so.S, so, rigs));
        }

        private static void CollectKnownMats(SpaceObject obj, List<MatType> into) {
            into.Clear();
            if (obj.Region != null) {
                var matCounts = Caches.MatCounts;
                obj.Info.LoadKnownRegionMats(obj.Region, matCounts);
                foreach (var m in matCounts) {
                    into.Add(m.Key);
                }
                return;
            }
            var known = obj.Knowledge.Mats;
            if (Arrays.IsEmpty(known)) {
                return;
            }
            foreach (var id in known) {
                var mat = MatType.FastGet(id);
                if (mat != null) {
                    into.AddUniqueInefficient(mat);
                }
            }
        }

        private void AddIcon(string icon, string tip) {
            // Explicit index: despawned icons stay parented here, and a reused one would
            // otherwise keep its old position among the new ones
            var w = UISpawn.Widget<WImage>(life, UIPrefabId.WIcon, container, iconIdx++);
            w.Load(icon);
            UIUtils.AddTip(life, w, tip);
        }
    }
}
