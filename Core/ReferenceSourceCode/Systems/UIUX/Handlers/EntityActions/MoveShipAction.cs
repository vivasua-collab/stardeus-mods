using System.Collections.Generic;
using System.Linq;
using Game.CodeGen;
using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using KL.Grid;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class MoveShipAction : BaseEntityAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new MoveShipAction());
        }

        public const string ActionId = "MoveShip";
        public override string Id => ActionId;

        protected override bool ApplyFor(GameState s, Tile tile, out EntityActionData data) {
            tile = EntityUtils.ParentObj(tile);

            if (!tile.IsConstructed || tile.ENode?.IsReachable == false) {
                data = EntityActionData.Unavailable;
                return false;
            }

            if (tile.TryGetComponent<ShipEngineComp>(out var engine) && engine.IsManeuvering) {
                data = new EntityActionData(this, (l, p, i) => CreateEngine(l, p, i, engine), prio: Priority.High);
                return true;
            }

            if (!s.Research.IsUnlocked(DefIdsH.ResearchSpaceTravelLocalManeuvering)) {
                data = EntityActionData.Unavailable;
                return false;
            }

            if (tile.TryGetComponent<BridgeControlsComp>(out _)) {
                data = new EntityActionData(this, (l, p, i) => CreateBridge(l, p, i), prio: Priority.High);
                return true;
            }

            data = EntityActionData.Unavailable;
            return false;
        }

        protected override bool ApplyForMany(GameState s, List<Tile> tiles, out EntityActionData data) {
            ShipEngineComp foundEngine = null;
            bool foundBridge = false;

            foreach (var t in tiles) {
                var tile = EntityUtils.ParentObj(t);
                if (!tile.IsConstructed || tile.ENode?.IsReachable == false) { continue; }

                if (foundEngine == null
                        && tile.TryGetComponent<ShipEngineComp>(out var engine)
                        && engine.IsManeuvering) {
                    foundEngine = engine;
                }

                if (!foundBridge && tile.TryGetComponent<BridgeControlsComp>(out _)) {
                    foundBridge = true;
                }
            }

            if (foundEngine != null) {
                var engine = foundEngine;
                data = new EntityActionData(this, (l, p, i) => CreateEngine(l, p, i, engine), prio: Priority.High);
                return true;
            }

            if (foundBridge && s.Research.IsUnlocked(DefIdsH.ResearchSpaceTravelLocalManeuvering)) {
                data = new EntityActionData(this, (l, p, i) => CreateBridge(l, p, i), prio: Priority.High);
                return true;
            }

            data = EntityActionData.Unavailable;
            return false;
        }

        private WBase CreateEngine(Lifetime l, RectTransform p, int i, ShipEngineComp engine) {
            var widget = SpawnButton(l, true, p, i);
            var onePiece = A.S.Sys.ShipNav.IsInOnePiece;
            var label = onePiece ? T.VerbMoveShip : T.MoveSection;
            widget.Load(IconId.CPropulsion, label, () => {
                ShowLocalManeuverChoiceFor(engine, onePiece, false, true);
            });
            return widget;
        }

        private WBase CreateBridge(Lifetime l, RectTransform p, int i) {
            var widget = SpawnButton(l, true, p, i);
            widget.Load(IconId.CPropulsion, T.VerbMoveShip, () => {
                TryLocalManeuver();
            });
            return widget;
        }

        public static bool TryLocalManeuver(WBase widget = null) {
            var s = A.S;

            var engines = s.Sys.ShipNav.Engines;
            ShipEngineComp firstEngine = null;
            ShipEngineComp firstChargedEngine = null;
            int maneuveredSection = -1;
            foreach (var eng in engines) {
                if (!eng.IsManeuvering) { continue; }
                if (!eng.ENode.IsReachable) { continue; }
                var engAnchor = eng.FindAnchor();
                if (Pos.IsOOB(engAnchor)) { continue; }
                int sectionId = s.Sys.Areas.IslandAt(engAnchor);
                if (sectionId == 0) { continue; }
                if (maneuveredSection == -1) {
                    maneuveredSection = sectionId;
                } else if (maneuveredSection != sectionId) {
                    WPopup.Warning(T.ResearchLocalmaneuvering, T.ManualActionRequired,
                        T.LocalManeuveringMultipleSections_V2.F(
                            eng.Tile.Definition.CodexLink, Texts.Accented(T.MoveSection)));
                    // multiple sections with maneuvering controllers
                    return false;
                }
                firstEngine ??= eng;
                if (firstChargedEngine == null && eng.ManeuveringCharge == 1f) {
                    firstChargedEngine = eng;
                }
            }
            if (firstEngine == null) {
                A.S.Sig.ShowErrorMessage.Send(T.ShipNavNoLocalManeuverTip);
                return false;
            }
            bool canRecenter = s.Sys.Areas.IslandCount == 1;
            bool canRotate = canRecenter;
            if (!canRotate) {
                int islandsWithinMargin = 0;
                foreach (var isle in s.Sys.Areas.Islands.Values) {
                    var firstPos = isle.First();
                    if (EntityUtils.IsWithinMargin(firstPos, false, false)) {
                        islandsWithinMargin++;
                    }
                }
                // if there are more than 2 islands within margin, there can
                // be a collision during rotation
                canRotate = islandsWithinMargin < 2;
            }
            ShowLocalManeuverChoiceFor(firstChargedEngine ?? firstEngine, true,
                canRecenter, canRotate,  widget);
            return true;
        }

        public static void ShowLocalManeuverChoiceFor(ShipEngineComp engine,
                bool wholeShip, bool canRecenter, bool canRotate, WBase widget = null) {
            bool isOnePiece = A.S.Sys.ShipNav.IsInOnePiece;
            var qc = QuickChoice.Create(T.ResearchLocalmaneuvering);
            qc.Widget = widget?.Rect;
            var eng = engine;
            qc.AddIconButton(IconId.CPropulsion, isOnePiece ? T.VerbMoveShip : T.MoveSection, eng.BeginMoveSection);
            if (canRecenter || (isOnePiece && EntityUtils.IsWithinMargin(engine.Tile.PosIdx))) {
                qc.AddIconButton(IconId.CMove, T.RecenterShip, () => {
                    var s = The.R.State;
                    if (CmdRecenterShip.TryFindShipCenter(s, out var center)) {
                        s.CmdQ.Enqueue(new CmdRecenterShip(center));
                    }
                }, tip: T.RecenterShipTip);
            }
            if (canRotate || isOnePiece) {
                qc.AddIconButton(IconId.WArrowLeft, T.InputRotateleft, () => eng.BeginRotatePreview(-1));
                qc.AddIconButton(IconId.WArrowRight, T.InputRotateright, () => eng.BeginRotatePreview(1));
            }
            A.S.UIQuery.ShowQuickChoice.Ask(qc);
        }
    }
}
