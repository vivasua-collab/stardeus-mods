using System;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using KL.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    [DisallowMultipleComponent]
    public class WResearchReq : WIconText, IPointerEnterHandler,
            IPointerExitHandler, IPointerClickHandler {
        [NonSerialized] public Mat Datoid;
        override protected bool IsPoolable => false;
        private int disk;
        private bool hasHover;
        public bool IsDatoid => disk == 0;
        private Def researchable;
        public Def Def;

        public void LoadDatoid(Mat datoid) {
            Load(datoid.Type.IconId, Units.Num(datoid.StackSize));
            Datoid = datoid;
            Def = datoid.Type.Def;
            SetResearchable();
        }

        private void SetResearchable() {
            var res = Def.Researchable?.Prerequisites[0];
            if (res != null) {
                researchable = The.Defs.Get(res);
            }
        }

        public void LoadDisk(int disk) {
            Load(IconId.CDisk, Units.Num(disk));
            this.disk = disk;
            Def = The.Defs.FastGet(DefIdsH.ObjectsDevicesDiskModuleMini);
            SetResearchable();
        }

        public void OnPointerEnter(PointerEventData eventData) {
            // only datoid is clickable
            if (hasHover) { return; }
            hasHover = true;
            The.SysSig.SetCursorOverride.Send(CursorTypeH.CursorFinger);
            UISounds.PlayHover(UISounds.HoverType.High);
        }

        public void OnPointerExit(PointerEventData eventData) {
            if (!hasHover) { return; }
            hasHover = false;
            The.SysSig.SetCursorOverride.Send(0);
        }

        private void OnDisable() {
            OnPointerExit(null);
        }

        public void OnPointerClick(PointerEventData eventData) {
            UISounds.PlayClick(UISounds.ClickType.High, true);
            if (IsDatoid) {
                var qc = QuickChoice.Create(Datoid.Type.NameT);
                qc.Widget = Rect;
                qc.AddButton(T.Find, () => {
                    if (!A.S.UIQuery.ShowInResearchTree.Ask(researchable)) {
                        UISounds.PlayActionDenied();
                    }
                });
                qc.Show();
            } else {
                var qc = QuickChoice.Create(T.ResearchComputingDisk);
                qc.Widget = Rect;
                qc.AddButton(T.Find, () => {
                    if (!A.S.UIQuery.ShowInResearchTree.Ask(The.Defs.FastGet(DefIdsH.ObjectsDevicesDiskModuleMini))) {
                        UISounds.PlayActionDenied();
                    }
                });
                qc.Show();
            }
        }

        public void SetLacking(bool yes) {
            textMain.color = yes ? Texts.TextColorRed : Texts.TextColorWhite;
        }

        public override string ToString() {
            return $"WResearchReq[{Def}]";
        }
    }
}
