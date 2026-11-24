using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Input;
using Game.Systems.Ships;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class StarmapMenu_Hyperjump : StarmapMenuBlock {
        private bool hyperQExpanded;
        private bool beaconsExpanded;
        public override int Order => 10;
        private Lifetime lifetime;
        private readonly ReturnBeacons beacons;

        public StarmapMenu_Hyperjump(StarmapMenuWidget parent, StarmapContext ctx, GameState s) : base(parent, ctx, s) {
            beacons = new ReturnBeacons(s);
        }

        public override void Load(int siblingIdx) {
            if (lifetime != null) {
                lifetime.Dispose();
            }
            lifetime = ctx.MenuLifetime.CreateChild("Hyperjump");
            var block = UISpawn.Block(lifetime, parent.WItems, siblingIdx);
            int idx = 0;

            var q = S.Query.GetHyperjumpQueue.Ask();
            bool hasTarget = q.GetNextJumpTarget() != null;
            bool hasBeacons = beacons.HasBeacons;

            WButtonText2 header = null;
            header = UISpawn.Widget<WButtonText2>(lifetime, hasTarget || hasBeacons ?
                UIPrefabId.WContent2HeaderButton : UIPrefabId.WContentHeaderButton, block.Container, idx++);
            header.Load(T.NounHyperjump, () => ResumeHyperjump(q, header), T.Resume);
            ConsiderShowingHeaderButton(q, header);

            // We mess up with the button, so let's ensure it's reset on deactivate
            lifetime.Add(new DisposableCallback(() => {
                header.Button.SetActiveFast(true);
            }));

            q.VisitWaypoints.Bind(lifetime, (p) => {
                ConsiderShowingHeaderButton(q, header);
            }, notifyImmediately: false);

            AddHyperjumpQueue(q, hasTarget, hasBeacons, block, idx++);

            AddReturnBeacons(beacons.Beacons, block, idx++);
        }

        private void AddReturnBeacons(SpaceObject[] beacons, WContentBlock block, int idx) {
            if (Arrays.IsEmpty(beacons)) { return; }

            var w = UISpawn.Widget<WContentListButton>(lifetime, UIPrefabId.WContent2ListButton, block.Container, idx);
            w.Load(GraphicsId.SpaceObjectsMiscReturnBeacon01, T.NounReturnBeacons, Units.XNum(beacons.Length));
            w.LoadOnToggle(lifetime, ToggleReturnBeacons, beaconsExpanded);

        }

        private bool ToggleReturnBeacons(Lifetime life, RectTransform rt, bool willShow, bool wasLoaded) {
            beaconsExpanded = willShow;
            if (wasLoaded || !willShow) { return wasLoaded; }
            int idx = 0;
            foreach (var beacon in beacons.Beacons) {
                AddBeacon(life, rt, idx++, beacon);
            }
            return true;
        }

        private void ConsiderShowingHeaderButton(HyperjumpQueue q, WButtonText2 header) {
            header.Button.SetActiveFast(
                q.VisitWaypoints && q.IsPaused
                && !S.Sys.ShipNav.WantsHyperspace && q.GetNextJumpTarget() != null);
        }

        private void ResumeHyperjump(HyperjumpQueue q, WButtonText2 header) {
            q.SetPaused(false);
            ConsiderShowingHeaderButton(q, header);
        }

        private void AddHyperjumpQueue(HyperjumpQueue q, bool hasTarget,
                bool hasBeacons, WContentBlock block, int idx) {
            if (!hasTarget) {
                var w = UISpawn.Widget<WIconText>(lifetime, hasBeacons ? UIPrefabId.WContent2Text : UIPrefabId.WContentText, block.Container, idx);
                w.Load(IconId.CHyperspaceEnter, T.NoDestination);
                return;
            }

            var w0 = UISpawn.Widget<WButtonIconText2>(lifetime, UIPrefabId.WContent2ListButton, block.Container, idx);
            w0.Load(IconId.CHyperspaceEnter, T.NounQueue);
            var listButton = w0.GetComponent<WContentListButton>();
            q.OnChange.Bind(lifetime, () => {
                var qc = q.Queue.Count;
                var target = q.GetNextJumpTarget();
                if (target == null) {
                    w0.Load(IconId.CHyperspaceEnter, T.NoDestination);
                } else {
                    string title;
                    if (target == q.WarpTarget) {
                        if (ctx.IsCrossRegionHyperjump) {
                            var p = q.Progress;
                            title = $"{T.VerbWarp}: {Texts.Accented(p.From.SO.DisplayName, false)} " +
                                $"{GlyphMap.ArrowRight} {Texts.Accented(p.To.SO.DisplayName, false)}";
                        } else {
                            title = $"{T.VerbWarp}: {target.SO.ParentPath(link: false)}";
                        }
                    } else {
                        title = $"{T.NextStop}: {target.SO.ParentPath(link: false)}";
                    }
                    w0.SetText(title);
                }
                if (qc > 1) {
                    w0.SetText2($"+{Units.Num(qc - 1)}");
                } else {
                    w0.SetText2(null);
                }
                RebuildQueue(listButton);
            });

            listButton.LoadOnToggle(lifetime, ToggleHyperjumpQueue, hyperQExpanded);
        }

        private void RebuildQueue(WContentListButton btn) {
            btn.RebuildContainer();
        }

        private bool ToggleHyperjumpQueue(Lifetime life, RectTransform tr, bool willShow, bool wasLoaded) {
            hyperQExpanded = willShow;
            if (wasLoaded || !willShow) { return wasLoaded; }
            int idx = 0;
            var q = S.Query.GetHyperjumpQueue.Ask();
            var hasQueue = q.Queue.Count > 1 || (!q.Progress.IsMoving && q.Queue.Count == 1);
            if (hasQueue) {
                var next = q.Queue.First;
                int attempts = 100;
                while (next != null && attempts-- > 0) {
                    var item = next.Value;
                    if (q.Progress.IsMoving && q.Queue.First == next) {
                        next = next.Next;
                        continue;
                    }

                    var so = next.Value.SO;
                    AddWaypoint(life, tr, idx++, q, so);
                    next = next.Next;
                }

            }

            AddVisitWaypoints(life, tr, idx++, q);

            if (hasQueue || (!A.IsInHyperspace && q.WarpTarget != null)) {
                AddClearAll(life, tr, idx++, q);
            }
            return true;
        }
        private void AddBeacon(Lifetime life, RectTransform tr, int idx, SpaceObject beacon) {
            var w = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button, tr, idx);
            w.Load(GraphicsId.SpaceObjectsMiscReturnBeacon01,
                beacon.Parent.ParentPath(link: false), null, () => {
                    var qc = QuickChoice.Create(T.NounReturnBeacon);
                    qc.AddButton(T.Show, () => {
                        S.Sys.Universe.ShowInStarmap(beacon.Parent);
                    }, autoClose: false);
                    var jump = T.JumpToPosition;
                    if (A.IsInHyperspace || !S.Sys.ShipNav.HasEngineForLayer(LayerForBeacon(beacon), out _)) {
                        jump = Texts.GreyStrike(jump);
                    }
                    qc.AddButton(jump, () => {
                        JumpToBeacon(beacon);
                    }, autoClose: false);
                    qc.AddButton($"{GlyphMap.SpriteWarning} {T.Destroy}", () => {
                        SOUtils.RemoveSO(beacon);
                        parent.Load();
                    });
                    S.UIQuery.ShowQuickChoice.Ask(qc);
                });
            UIUtils.AddTipFunc(lifetime, w, () => beacon.Parent.Info.UITooltip(false));
        }

        private void JumpToBeacon(SpaceObject beacon) {
            if (A.IsInHyperspace) {
                S.Sig.ShowErrorMessage.Send(T.ExitHyperspaceFirst);
                return;
            }

            var res = S.Query.JumpToReturnBeacon.Ask(beacon);
            if (!res.IsSuccess) {
                if (res.FailReason != null) {
                    S.Sig.ShowErrorMessage.Send(res.FailReason);
                } else {
                    UISounds.PlayActionDenied();
                }
                return;
            }
            S.UIQuery.CancelQuickChoice.Ask();
            UISounds.PlayDigitalConfirm();
        }

        private static string LayerForBeacon(SpaceObject beacon) {
            var sameRegion = SOUtils.RegionFor(beacon) == SOUtils.RegionFor(A.Player);
            return sameRegion ? UniverseLayer.Region : UniverseLayer.Universe;
        }

        private void AddWaypoint(Lifetime life, RectTransform tr, int idx, HyperjumpQueue q, Data.Space.SpaceObject so) {
            var w = UISpawn.Widget<WContentButtonWithX>(life, UIPrefabId.WContent2ButtonX, tr, idx);
            w.Button.Load(so.Icon, so.ParentPath(link: false), null);
            w.Button.SetOnClick(() => {
                S.UIQuery.StarmapFocusOn.Ask(so, false);
            });
            w.XButton.SetOnClick(() => {
                var res = q.Dequeue(so.SpaceEntity);
                if (!res.IsSuccess) {
                    if (res.FailReason != null) {
                        S.Sig.ShowErrorMessage.Send(res.FailReason);
                    } else {
                        UISounds.PlayActionDenied();
                    }
                }
            });
        }

        private void AddVisitWaypoints(Lifetime life, RectTransform tr, int idx, HyperjumpQueue q) {
            var tog = UISpawn.Widget<WToggleText>(life, UIPrefabId.WContent2Toggle, tr, idx);
            tog.Load(T.VisitWaypoints, q.VisitWaypoints, (y) => {
                q.VisitWaypoints.Set(y);
            });
            q.VisitWaypoints.Bind(life, (p) => {
                tog.SetValue(p, false);
            }, notifyImmediately: false);
            UIUtils.AddTip(life, tog, T.VisitWaypointsTip);
        }

        private void AddClearAll(Lifetime life, RectTransform tr, int idx, HyperjumpQueue q) {
            var w2 = UISpawn.Widget<WButtonText>(life, UIPrefabId.WContent2ButtonSimple, tr, idx);
            w2.Load(Texts.Italic(T.ClearAll), () => {
                if (q.Progress.IsMoving) {
                    var first = q.Queue.First?.Value;
                    if (first != null) {
                        q.Queue.Clear();
                        q.Queue.AddFirst(first);
                    }
                    S.Sig.HyperjumpQueueChanged.Send(q, true);
                } else {
                    q.CancelAll();
                }
            });
        }
    }
}
