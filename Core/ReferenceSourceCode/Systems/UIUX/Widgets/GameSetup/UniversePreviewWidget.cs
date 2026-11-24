using System;
using System.Collections.Generic;
using System.Linq;
using Game.CodeGen;
using Game.Constants;
using Game.Data.Space;
using Game.Scenarios;
using Game.Systems.Space;
using Game.UI;
using Game.Utils;
using KL.Pool;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public class UniversePreviewWidget : WBase {
        [SerializeField] private WGroup container;
        [SerializeField] private UIScrollNav scrollNav;
        public Action<SpaceRegion> OnClickRegion;
        public readonly List<GameObject> spawnedObjects = new();
        public const float LineThickness = 4f;
        private readonly List<UILine> lines = new();
        private UISpaceObjectSelected selectedRegionMarker;
        private SpaceMap map;
        private Scenario scenario;
        private SpaceRegion selectedRegion;
        private float lastScale = -1f;

        private bool isSubscribed;

        private void DespawnSpawned() {
            foreach (var obj in spawnedObjects) {
                PrefabPool.Despawn(obj);
            }
            spawnedObjects.Clear();
            lines.Clear();
        }

        public void SelectRegion(SpaceRegion region) {
            OnSpaceObjClicked(region.SO);
            FocusOnSelectedSO(region.SO);
        }

        public void SelectRandomRegion(Rng rng) {
            var randRegion = StartingRegion.PickRandom(map, scenario, rng);
            if (randRegion != null) {
                OnSpaceObjClicked(randRegion.SO);
            }

            FocusOnSelectedSO(selectedRegion?.SO);
        }

        public void FocusOnSelectedSO(SpaceObject so) {
            if (so == null || so.UI == null) { return; }
            scrollNav.FocusOn(so.UI.Rekt, 1.25f);
        }

        public void Load(SpaceMap map, Scenario scenario) {
            this.map = map;
            this.scenario = scenario;
            if (!isSubscribed) {
                isSubscribed = true;
                A.S.Sig.SpaceObjectClicked.AddListener(OnSpaceObjClicked, gameObject);
            }
            DespawnSpawned();
            scrollNav.SetLimits(new Vector2(0.75f, 3f));
            var prefab = ResourceLibrary.FindPrefab<UISpaceObject>(UIPrefabId.UISpaceObject);

            Vector2 offset = Consts.V2Zero;
            float padding = 250;

            float minX = float.MaxValue;
            float maxX = float.MinValue;
            float minY = float.MaxValue;
            float maxY = float.MinValue;

            var spread = 110;

            foreach (var reg in map.Regions) {
                var p = offset + reg.Position * spread;

                minX = Mathf.Min(minX, p.x);
                minY = Mathf.Min(minY, p.y);
                maxX = Mathf.Max(maxX, p.x);
                maxY = Mathf.Max(maxY, p.y);
            }
            D.Warn("Min/Max: {0}:{1} - {2}:{3}", minX, minY, maxX, maxY);
            var widthX = maxX - minX;
            var widthY = maxY - minY;

            var center = new Vector2(widthX / 2f, widthY / 2f);
            offset = new Vector2(-minX + padding, -minY + padding);
            container.Rect.sizeDelta = new Vector2(
                widthX + padding * 2f,
                widthY + padding * 2f);

            var looks = new SpaceObjectLooks();

            var root = new SpaceObject();
            root.Id = 1;
            root.S = A.S;
            root.SetType(SpaceObjectType.Find(SpaceObjectTypeIdH.Universe));

            foreach (var reg in map.Regions) {
                var p = offset + reg.Position * spread;
                var so = new SpaceObject();
                so.Id = reg.Id;
                so.S = A.S;
                so.SetParent(root);
                so.Name = reg.Name;
                so.Location = reg.Position;
                so.SetType(SpaceObjectType.Find(SpaceObjectTypeIdH.Region));
                so.Region = reg;
                reg.SO = so;
                var regObj = PrefabPool.Spawn(prefab, container.Container);
                regObj.Load(container.Container, so,
                    looks, center, spread);
                so.SetUI(regObj);
                regObj.Rekt.localPosition = new Vector2(p.x, -p.y);
                spawnedObjects.Add(regObj.gameObject);
            }


            SpawnLinks(map.Regions);

        }

        private void SpawnLinks<T>(List<T> linkables)
                where T : ISpaceEntity {
            var addedLinks = new HashSet<int>();
            var linkPrefab = ResourceLibrary.FindPrefab<UILine>(UIPrefabId.UILineSpaceLink);
            foreach (T l in linkables) {
                foreach (var link in l.Links) {
                    // we have backlinks in opposite directions
                    if (!addedLinks.Add(link.GetHashCode())) { continue; }
                    var line = PrefabPool.Spawn(linkPrefab,
                        Consts.V3Zero, Consts.QId, container.Container);
                    bool isIsolated = SpaceLink.IsIsolatedRegion(link);
                    var colFrom = link.ColorFrom;
                    var colTo = link.ColorTo;
                    if (isIsolated) {
                        colFrom = colTo = Colors.WithA(Consts.SelectionGreyColor, 0.5f);
                    }
                    line.Load(
                        link.From.UIPosition,
                        link.To.UIPosition,
                        LineThickness,
                    colFrom, colTo);
                    line.Rect.localPosition = Consts.V3Zero;
                    line.Rect.localScale = Consts.V3One;

                    link.UpdateName(line.gameObject);

                    var li = LinkedResources.Instance;
                    if (isIsolated) {
                        // line.SetMaterial(li.spaceMapLinkIsolatedMat);
                        line.SetSprite(li.SpaceMapLinkIsolatedSprite, link.Length * 3f);
                    } else {
                        // line.SetMaterial(li.spaceMapLinkMat);
                        line.SetSprite(li.SpaceMapLinkSprite, 0f);
                    }

                    line.transform.SetAsFirstSibling();

                    lines.Add(line);
                    spawnedObjects.Add(line.gameObject);
                }
            }
        }

        public void UpdateWidget() {
            var scale = container.Container.localScale.x;
            if (Mathf.Approximately(scale, lastScale)) { return; }
            lastScale = scale;
            var th = LineThickness / scale;
            foreach (var line in lines) {
                line.SetThickness(th);
            }
        }

        public void Cleanup() {
            if (isSubscribed) {
                isSubscribed = false;
                A.S.Sig.SpaceObjectClicked.RemoveListener(OnSpaceObjClicked);
            }
            if (selectedRegionMarker != null) {
                PrefabPool.Despawn(selectedRegionMarker.gameObject);
                selectedRegionMarker = null;
            }
            DespawnSpawned();
            container.Container.sizeDelta = Consts.V2Zero;
            lastScale = -1f;
        }

        private void OnSpaceObjClicked(SpaceObject so) {
            // FocusOnSelectedSO(so);

            if (!SOUtils.HasHyperjumpRelay(so.Region)) {
                The.SysSig.HideTooltip.Send();
                UISounds.PlayActionDenied();
                WPopup.Create(true, IconId.CStop, T.OperationForbidden, T.WarningIncompatibleStartRegion,
                    T.IsolatedRegionTip_V2.F(Texts.Accented(T.SpaceObjectHyperjumprelay)
                        , Texts.Accented(T.ObjectsDevicesFtldrive)))
                    .Unique("starting_region")
                    .Spawn(UIPoolRoot.SystemPopupContainer);
                return;
            }
            if (!StartingRegion.CanStartIn(so.Region, scenario)) {
                The.SysSig.HideTooltip.Send();
                UISounds.PlayActionDenied();
                WPopup.Create(true, IconId.CStop, T.OperationForbidden,
                        T.WarningIncompatibleStartRegion)
                    .Unique("starting_region")
                    .Spawn(UIPoolRoot.SystemPopupContainer);
                return;
            }
            selectedRegion = so.Region;
            if (selectedRegionMarker == null) {
                var targetPrefab = ResourceLibrary.FindPrefab<UISpaceObjectSelected>();
                selectedRegionMarker = PrefabPool.Spawn(targetPrefab, container.Container);
            }
            selectedRegionMarker.LoadFor(so.UI);
            OnClickRegion(so.Region);
        }
    }
}
