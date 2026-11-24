using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Items;
using Game.Input;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine.UI;
using System.Linq;

namespace Game.Systems.UIUX {
    public sealed class MindTransferTab : BaseEntityTab {
        public const string TabId = "MindTransfer";
        private const string InputSubId = "MindTransferInput";
        private const string OutputSubId = "MindTransferOutput";

        [SelfInit]
        private static void Register() {
            RegisterTab(new MindTransferTab());
        }

        private Tile tile;
        private MindTransferComp comp;
        private GameState S;
        private WButtonIconText2 wInputBtn;
        private WButtonIconText2 wOutputBtn;

        public override string Id => TabId;
        public override int Order => 10;
        public override string TabName => T.MindTransfer;

        public override bool ShowFor(GameState s, Tile t) {
            if (!t.IsConstructed) { return false; }
            if (t.ENode?.IsReachable == false) { return false; }
            if (!t.TryGetComponent<MindTransferComp>(out var c)) { return false; }
            tile = t;
            comp = c;
            S = s;
            return true;
        }

        public override bool LoadContent(WGroup group) {
            comp.StateChangedB.Bind(lifetime, () => ReloadTab(), false);

            var mainBlock = UISpawn.Block(lifetime, group, 0);
            int mainIdx = 0;

            var progressRow = UISpawn.Widget<WIconText2Progress>(lifetime,
                UIPrefabId.WContent2Progress, mainBlock.Container, mainIdx++);
            progressRow.Load(IconId.CMindTransfer, T.NounProgress, comp.Progress);
            comp.ProgressB.Bind(lifetime, v => progressRow.SetPercent(v), false);

            wInputBtn = null;
            wOutputBtn = null;

            var inputBlock = UISpawn.Block(lifetime, group, 1);
            int inputIdx = 0;

            var inputHeader = UISpawn.Widget<WText>(lifetime,
                UIPrefabId.WContent2Header, inputBlock.Container, inputIdx++);
            inputHeader.Load(T.NounInput);

            if (comp.InputId == 0) {
                wInputBtn = UISpawn.Widget<WButtonIconText2>(lifetime,
                    UIPrefabId.WContent2Button, inputBlock.Container, inputIdx++);
                wInputBtn.Load(IconId.CCross, T.NounInput, T.Select,
                    () => ShowSlotSubPanel(wInputBtn, InputSubId, true));
                UIUtils.AddTip(lifetime, wInputBtn, Texts.Red(T.WarnInputWillBeDestroyed));
            } else {
                var inputRow = UISpawn.Widget<WIconText2>(lifetime,
                    UIPrefabId.WContent2Text2, inputBlock.Container, inputIdx++);
                inputRow.Load(comp.Input?.Def.Preview ?? IconId.CInfo,
                    comp.Input?.ListName ?? T.NounInput,
                    comp.IsInputLoaded ? T.StateLoaded : T.StateWaiting);
                UIUtils.AddTipFunc(lifetime, inputRow, () => comp.Input?.Tooltip(S));
            }

            var outputBlock = UISpawn.Block(lifetime, group, 2);
            int outputIdx = 0;

            var outputHeader = UISpawn.Widget<WText>(lifetime,
                UIPrefabId.WContent2Header, outputBlock.Container, outputIdx++);
            outputHeader.Load(T.NounOutput);

            if (comp.OutputId == 0) {
                wOutputBtn = UISpawn.Widget<WButtonIconText2>(lifetime,
                    UIPrefabId.WContent2Button, outputBlock.Container, outputIdx++);
                wOutputBtn.Load(IconId.CCross, T.NounOutput, T.Select,
                    () => ShowSlotSubPanel(wOutputBtn, OutputSubId, false));
            } else {
                var outputRow = UISpawn.Widget<WIconText2>(lifetime,
                    UIPrefabId.WContent2Text2, outputBlock.Container, outputIdx++);
                outputRow.Load(comp.Output?.Def.Preview ?? IconId.CInfo,
                    comp.Output?.ListName ?? T.NounOutput,
                    comp.IsOutputLoaded ? T.StateLoaded : T.StateWaiting);
                UIUtils.AddTipFunc(lifetime, outputRow, () => comp.Output?.Tooltip(S));
            }

            if (SubPanelWidget.IsOpenFor == InputSubId && wInputBtn != null) {
                LayoutRebuilder.ForceRebuildLayoutImmediate(group.Container);
                ShowSlotSubPanel(wInputBtn, InputSubId, true);
                var btn = wInputBtn;
                Coroutines.OnNextFrame(() => {
                    S.UIQuery.GetEntitySubPanel.Ask().RepositionWidget(btn, connectorOnly: false);
                });
            } else if (SubPanelWidget.IsOpenFor == OutputSubId && wOutputBtn != null) {
                LayoutRebuilder.ForceRebuildLayoutImmediate(group.Container);
                ShowSlotSubPanel(wOutputBtn, OutputSubId, false);
                var btn = wOutputBtn;
                Coroutines.OnNextFrame(() => {
                    S.UIQuery.GetEntitySubPanel.Ask().RepositionWidget(btn, connectorOnly: false);
                });
            }

            return true;
        }

        public override bool LoadFooter(WGroup group) {
            if (comp.InputId == 0 && comp.OutputId == 0) { return false; }
            var abortBtn = UISpawn.Widget<WButtonText>(lifetime,
                UIPrefabId.WPanelButton_Red, group.Container, 0);
            abortBtn.Load(T.Abort, () => comp.AbortExchange());
            return true;
        }

        private void ShowSlotSubPanel(WBase btn, string subId, bool isInput) {
            var subPanel = S.UIQuery.GetEntitySubPanel.Ask();
            if (subPanel.IsShowingFor(btn)) {
                subPanel.Close();
                return;
            }
            subPanel.SetTitle(isInput ? T.NounInput : T.NounOutput);
            var l = subPanel.CreateLifetime(lifetime);

            var candidates = Caches.ObjDataList;
            candidates.Clear();
            if (isInput) {
                comp.AddMindInputOptions(candidates);
            } else {
                comp.AddMindOutputOptions(candidates);
            }

            if (candidates.Count == 0) {
                var emptyBlock = UISpawn.Block(l, subPanel.Container, 0);
                var emptyRow = UISpawn.Widget<WIconText>(l,
                    UIPrefabId.WContent2Text, emptyBlock.Container, 0);
                emptyRow.Load(IconId.CInfo, T.Empty);
            } else {
                var emptyItems = new List<ObjData>();
                var friendlyItems = new List<ObjData>();
                var hostileItems = new List<ObjData>();
                foreach (var c in candidates) {
                    if (c.Mind?.IsBlank != false) emptyItems.Add(c);
                    else if (c.Mind.Faction.IsPlayer) friendlyItems.Add(c);
                    else hostileItems.Add(c);
                }

                int blockIdx = 0;
                if (!isInput && emptyItems.Count > 0) {
                    AddMindBlock(l, subPanel.Container, blockIdx++, T.Empty, emptyItems, isInput);
                }
                if (friendlyItems.Count > 0) {
                    AddMindBlock(l, subPanel.Container, blockIdx++, T.NavPermFriendly, friendlyItems, isInput);
                }
                if (hostileItems.Count > 0) {
                    AddMindBlock(l, subPanel.Container, blockIdx++, T.Hostile, hostileItems, isInput);
                }
            }

            subPanel.Show(btn, subId);
        }

        private void AddMindBlock(Lifetime l, WGroup container, int blockIdx, string title,
                List<ObjData> items, bool isInput) {
            var block = UISpawn.Block(l, container, blockIdx);
            int idx = 0;
            var header = UISpawn.Widget<WText>(l,
                UIPrefabId.WContent2Header, block.Container, idx++);
            header.Load(title);

            var groups = new Dictionary<Def, List<ObjData>>();
            var defOrder = new List<Def>();
            foreach (var item in items) {
                if (!groups.TryGetValue(item.Def, out var list)) {
                    list = new List<ObjData>();
                    groups[item.Def] = list;
                    defOrder.Add(item.Def);
                }
                list.Add(item);
            }
            defOrder.Sort((a, b) => string.Compare(a.NameT, b.NameT,
                System.StringComparison.Ordinal));

            foreach (var def in defOrder) {
                var capturedDef = def;
                var defItems = groups[def];
                var groupBtn = UISpawn.Widget<WContentListButton>(l,
                    UIPrefabId.WContent2ListButton, block.Container, idx++);
                groupBtn.Load(capturedDef.Preview, capturedDef.NameT, defItems.Count.ToString());
                groupBtn.LoadOnToggle(l, (life, innerContainer, willShow, wasLoaded) => {
                    if (!willShow) { return wasLoaded; }
                    if (wasLoaded) { return true; }
                    int itemIdx = 0;
                    foreach (var item in defItems.OrderByDescending(i => i.Quality)) {
                        var captured = item;
                        var q = captured.BodyPartState == null
                            ? captured.Quality : captured.BodyPartState.Quality;
                        var itemBtn = UISpawn.Widget<WButtonIconText2>(l,
                            UIPrefabId.WContent2Button, innerContainer, itemIdx++);
                        itemBtn.Load(captured.Def.Preview,
                            StripDefPrefix(captured.ListName, capturedDef.NameT),
                            Units.Percentage(q, clamp: false), () => {
                            if (captured.BodyPartState != null
                                    && captured.BodyPartState.WoundSeverity > Severity.Mild) {
                                S.Sig.ShowErrorMessage.Send(T.CondDamaged);
                                return;
                            }
                            if (isInput) {
                                comp.Input = captured;
                                if (comp.Input == null) { UISounds.PlayActionDenied(); return; }
                            } else {
                                comp.Output = captured;
                                if (comp.Output == null) { UISounds.PlayActionDenied(); return; }
                            }
                            S.UIQuery.CloseEntitySubPanel.Ask();
                            comp.StateChangedB.Invoke();
                        });
                        UIUtils.AddTipFunc(l, itemBtn, () => captured.Tooltip(S));
                    }
                    return true;
                }, false);
            }
        }

        private static string StripDefPrefix(string listName, string defName) {
            var prefix = defName + " " + GlyphMap.ArrowRight + " ";
            var at = listName.IndexOf(prefix, System.StringComparison.Ordinal);
            if (at < 0) { return listName; }
            return listName[..at] + listName[(at + prefix.Length)..];
        }

        protected override void Cleanup() {
            tile = null;
            comp = null;
            S = null;
            wInputBtn = null;
            wOutputBtn = null;
        }
    }
}
