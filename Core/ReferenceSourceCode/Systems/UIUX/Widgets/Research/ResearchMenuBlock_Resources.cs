using System;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Systems.Space;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class ResearchMenuBlock_Resources : ResearchMenuBlock {
        public override int Order => 1;
        private bool datoidsExpanded = true;
        private Lifetime lifetime;

        public ResearchMenuBlock_Resources(ResearchMenuWidget parent,
            ResearchContext ctx, GameState s) : base(parent, ctx, s) { }

        public override void Load(int siblingIdx) {
            if (lifetime != null) {
                lifetime.Dispose();
            }
            lifetime = ctx.MenuLifetime.CreateChild("Resources");
            var block = UISpawn.Block(lifetime, parent.WItems, siblingIdx);
            int idx = 0;

            var header = UISpawn.Widget<WText>(lifetime, UIPrefabId.WContent2Header, block.Container, idx++);
            header.Load(T.NounResources);

            AddDisk(block, idx++);
            AddDatoids(block, idx++);

        }
        private void AddDatoids(WContentBlock block, int idx) {
            var w0 = UISpawn.Widget<WButtonIconText2>(lifetime,
                UIPrefabId.WContent2ListButton, block.Container, idx);
            w0.Load(GraphicsId.ObjDatoidsDatoidBasic, T.AvailableDatoids);
            w0.SetText2(null);
            var listButton = w0.GetComponent<WContentListButton>();

            listButton.LoadOnToggle(lifetime, ToggleDatoids, datoidsExpanded);
        }

        private bool ToggleDatoids(Lifetime life, RectTransform container, bool willShow, bool wasLoaded) {
            datoidsExpanded = willShow;
            if (!willShow) { return wasLoaded; }
            if (!wasLoaded) {
                int idx = 0;
                foreach (var dc in S.Research.DatoidCounts) {
                    idx = AddDatoid(life, container, dc.Key, dc.Value, idx);
                }
            }
            return true;
        }

        private int AddDatoid(Lifetime life, RectTransform block, MatType type, BInt amt, int idx) {
            if (amt == 0 && !S.Research.IsDefUnlocked(type.Def)) {
                return idx;
            }
            var w4 = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button,
                block, idx++);

            w4.Load(type.IconId, type.NameT, null, () => {
                S.UIQuery.ShowInResearchTree.Ask(type.Def);
            });
            amt.Bind(life, (v) => {
                w4.SetText2(Units.Num(v));
            });
            UIUtils.AddCodexTooltip(life, w4.gameObject, w4, type.Def);
            return idx;
        }

        private void AddDisk(WContentBlock block, int idx) {
            var w0 = UISpawn.Widget<WIconText2Progress>(lifetime,
                UIPrefabId.WContent2Progress, block.Container, idx);
            w0.Load(IconId.CDisk, T.ComputerResourcesStorage, 0f, T.NotAvailable);

            S.Research.UpdateUsedDisk();

            var b2 = new C2<int, int>(lifetime, S.Research.UsedDisk, A.Grid.TotalDisk, (used, total) => {
                w0.SetPercent((float) used / total, autoText: false);
                w0.Item.SetText2(Units.XOutOfY(used, total));
            });
            UIUtils.AddTipFunc(lifetime, w0, () => {
                var sb = Caches.StringBuilder;
                sb.Clear();
                sb.AppendLine(T.AvailableStorage_V1.F(A.Grid.TotalDisk - S.Research.UsedDisk));
                sb.Append(Texts.Grey(T.ResearchDiskSpaceTip_V1.F(
                    Texts.LinkToCodex(DefIdsH.ObjectsDevicesDiskModuleMini, T.NounDiskModulesPlural))));
                return sb.ToString();
            });
        }
    }
}
