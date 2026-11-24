using System.Collections.Generic;
using System.Linq;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems.Items;
using Game.Systems.Mental;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class MLBoothTab : BaseEntityTab {
        public const string TabId = "MLBooth";
        private const string InputSubId = "MLBoothInput";

        [SelfInit]
        private static void Register() {
            RegisterTab(new MLBoothTab());
        }

        private MLBoothComp comp;
        private GameState S;
        private WButtonIconText2 wInputBtn;

        private Lifetime jobSublife;

        public override string Id => TabId;
        public override int Order => 10;
        public override string TabName => T.ObjectsDevicesMlbooth;

        public override bool ShowFor(GameState s, Tile t) {
            if (!t.IsConstructed) { return false; }
            if (t.ENode?.IsReachable == false) { return false; }
            if (!t.TryGetComponent<MLBoothComp>(out var c)) { return false; }
            comp = c;
            S = s;
            return true;
        }

        public override bool LoadContent(WGroup group) {
            comp.StateChangedB.Bind(lifetime, () => ReloadTab(), false);

            var statusBlock = UISpawn.Block(lifetime, group, 0);
            int statusIdx = 0;

            var header = UISpawn.Widget<WText>(lifetime,
                UIPrefabId.WContent2Header, statusBlock.Container, statusIdx++);
            header.Load(T.ObjectsDevicesMlbooth);

            var statusRow = UISpawn.Widget<WIconText2>(lifetime,
                UIPrefabId.WContent2Text2, statusBlock.Container, statusIdx++);
            UpdateStatusRow(statusRow);
            comp.StateChangedB.Bind(lifetime, () => UpdateStatusRow(statusRow), false);

            wInputBtn = null;

            var inputBlock = UISpawn.Block(lifetime, group, 1);
            int inputIdx = 0;

            var inputHeader = UISpawn.Widget<WText>(lifetime,
                UIPrefabId.WContent2Header, inputBlock.Container, inputIdx++);
            inputHeader.Load(T.NounInput);

            if (comp.InputId == 0) {
                wInputBtn = UISpawn.Widget<WButtonIconText2>(lifetime,
                    UIPrefabId.WContent2Button, inputBlock.Container, inputIdx++);
                wInputBtn.Load(IconId.CCross, T.Empty, T.Select,
                    () => ShowInputSubPanel(wInputBtn));
            } else {
                var state = comp.IsInputLoaded ? T.StateLoaded : T.StateWaiting;
                var itemRow = UISpawn.Widget<WIconText2>(lifetime,
                    UIPrefabId.WContent2Text2, inputBlock.Container, inputIdx++);
                itemRow.Load(comp.Input?.Def.Preview ?? IconId.CInfo,
                    comp.Input?.ListName ?? string.Empty, state);
                UIUtils.AddTipFunc(lifetime, itemRow, () => comp.Input?.Tooltip(S));
            }

            if (comp.InputId != 0) {
                var jobBlock = UISpawn.Block(lifetime, group, 2);
                int jobIdx = 0;

                var jobHeader = UISpawn.Widget<WText>(lifetime,
                    UIPrefabId.WContent2Header, jobBlock.Container, jobIdx++);
                jobHeader.Load(T.TrainSkill);

                if (jobSublife != null) {
                    jobSublife.Dispose();
                }
                jobSublife = lifetime.CreateChild("MLBooth.JobSub");

                SpawnJobSection(jobSublife, jobBlock.Container, ref jobIdx);
            }

            if (SubPanelWidget.IsOpenFor == InputSubId && wInputBtn != null) {
                LayoutRebuilder.ForceRebuildLayoutImmediate(group.Container);
                ShowInputSubPanel(wInputBtn);
                var btn = wInputBtn;
                Coroutines.OnNextFrame(() => {
                    S.UIQuery.GetEntitySubPanel.Ask().RepositionWidget(btn, connectorOnly: false);
                });
            }

            return true;
        }

        public override bool LoadFooter(WGroup group) {
            if (comp.InputId == 0) { return false; }
            var abortBtn = UISpawn.Widget<WButtonText>(lifetime,
                UIPrefabId.WPanelButton_Red, group.Container, 0);
            abortBtn.Load(T.Abort, () => comp.AbortTrain());
            return true;
        }

        private void UpdateStatusRow(WIconText2 row) {
            var gridOut = S.Situation.GridMaxOutput;
            var gridUse = S.Situation.GridConsumption;
            string title;
            string text2;
            if (comp.CurrentTrainCost == 0f) {
                title = T.Idle;
                text2 = null;
            } else {
                title = comp.TitleWithCost(T.EnergyConsumption,
                    comp.CurrentTrainCost, gridOut, gridUse);
                text2 = Units.XNum(comp.NumSkillsTrained);
            }
            row.Load(IconId.CMachineLearning, title, text2);
        }

        private void ShowInputSubPanel(WButtonIconText2 btn) {
            var subPanel = S.UIQuery.GetEntitySubPanel.Ask();
            if (subPanel.IsShowingFor(btn)) {
                subPanel.Close();
                return;
            }
            subPanel.SetTitle(T.NounInput);
            var l = subPanel.CreateLifetime(lifetime);

            var candidates = Caches.ObjDataList;
            candidates.Clear();
            comp.AddMindOutputOptions(candidates);

            if (candidates.Count == 0) {
                var emptyBlock = UISpawn.Block(l, subPanel.Container, 0);
                var emptyRow = UISpawn.Widget<WIconText>(l,
                    UIPrefabId.WContent2Text, emptyBlock.Container, 0);
                emptyRow.Load(IconId.CInfo, T.Empty);
            } else {
                var friendlyItems = new List<ObjData>();
                var hostileItems = new List<ObjData>();
                foreach (var c in candidates) {
                    if (c.Mind.Faction.IsPlayer) friendlyItems.Add(c);
                    else hostileItems.Add(c);
                }

                int blockIdx = 0;
                if (friendlyItems.Count > 0) {
                    AddMindBlock(l, subPanel.Container, blockIdx++, T.NavPermFriendly, friendlyItems);
                }
                if (hostileItems.Count > 0) {
                    AddMindBlock(l, subPanel.Container, blockIdx++, T.Hostile, hostileItems);
                }
            }

            subPanel.Show(btn, InputSubId);
        }

        private void AddMindBlock(Lifetime l, WGroup container, int blockIdx, string title,
                List<ObjData> items) {
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
                    foreach (var item in defItems) {
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
                            comp.InitializeForNewInput(captured);
                            if (comp.Input == null) { UISounds.PlayActionDenied(); return; }
                            S.UIQuery.CloseEntitySubPanel.Ask();
                            comp.StateChangedB.Invoke();
                        });
                        UIUtils.AddTipFunc(l, itemBtn, () => captured.Tooltip(S));
                    }
                    return true;
                }, false);
            }
        }

        private void SpawnJobSection(Lifetime jobLifetime, RectTransform container, ref int idx) {
            comp.EnsureWantedJobLevels();
            var gridOut = S.Situation.GridMaxOutput;
            var gridUse = S.Situation.GridConsumption;
            var jobs = comp.Input?.Mind?.Jobs;
            if (jobs == null) { return; }

            foreach (var job in JobType.All.Values
                    .Where(j => !j.Internal).OrderBy(j => j.NameT)) {
                byte jobLevel = 0;
                bool canTrain = false;
                float finalCost = 0f;
                bool maxedOut = false;
                JobSlot jobSlot = null;

                if (jobs.TryJob(job.IdHash, out var slot)) {
                    jobSlot = slot;
                    if (slot.Incapable) {
                        // the real level, as the booth trains from it: 0 here showed a
                        // skill at 3 as being trained to 3
                        jobLevel = slot.Level;
                        finalCost = comp.GetJobEnergyCost(-1);
                        canTrain = true;
                    } else if (!job.Unskilled) {
                        jobLevel = slot.Level;
                        finalCost = comp.GetJobEnergyCost(jobLevel);
                        if (jobLevel < JobSlot.MaxLevel) {
                            canTrain = true;
                        } else {
                            maxedOut = true;
                        }
                    } else {
                        // unskilled and capable = maxed
                        maxedOut = true;
                    }
                } else {
                    canTrain = true;
                    finalCost = comp.GetJobEnergyCost(-1);
                }

                if (maxedOut) {
                    var maxRow = UISpawn.Widget<WIconText2>(jobLifetime,
                        UIPrefabId.WContent2Text2, container, idx++);
                    maxRow.Load(job.IconId, Texts.Green(job.NameT), T.MaxedOut);
                    UIUtils.AddTip(jobLifetime, maxRow, T.SkillMaxedOut);
                } else if (canTrain) {
                    var capturedJobId = job.IdHash;
                    var capturedJobSlot = jobSlot;
                    var capturedJobLevel = jobLevel;
                    var capturedJob = job;
                    var name = comp.TitleWithCost(job.NameT, finalCost, gridOut, gridUse);
                    int minLevel = capturedJobLevel;
                    // a booth saved while an incapable slot showed level 0 can hold a
                    // wanted level below the real one
                    var wantLevel = System.Math.Max(comp.GetWantedLevel(job.IdHash), minLevel);
                    int maxLevel = job.Unskilled ? 1 : JobSlot.MaxLevel;

                    var amtW = UISpawn.Widget<WInputAmtIconText2>(jobLifetime,
                        UIPrefabId.WContent2Text2Amt, container, idx++);
                    amtW.Item.Load(job.IconId, name,
                        GetLevelDisplay(capturedJobSlot, capturedJobLevel, wantLevel));
                    amtW.Amt.Load(wantLevel, v => {
                        comp.SetWantedLevelDirect(capturedJobId, v);
                        amtW.Item.SetText2(GetLevelDisplay(capturedJobSlot, capturedJobLevel, v));
                    }, v => System.Math.Clamp(v, minLevel, maxLevel));
                    UIUtils.AddTipFunc(jobLifetime, amtW,
                        () => GetLevelProgressTip(capturedJob, comp.Input?.Mind?.Jobs));

                    comp.TrainingProgressB.Bind(jobLifetime, () => {
                        var currentJobs = comp.Input?.Mind?.Jobs;
                        if (currentJobs == null) { return; }
                        currentJobs.TryJob(capturedJobId, out var currentSlot);
                        var curLevel = currentSlot?.Level ?? 0;
                        var curWantLevel = comp.GetWantedLevel(capturedJobId);
                        amtW.Item.SetText2(GetLevelDisplay(currentSlot, curLevel, curWantLevel));
                    }, false);
                }
            }
        }

        private static string StripDefPrefix(string listName, string defName) {
            var prefix = defName + " " + GlyphMap.ArrowRight + " ";
            var at = listName.IndexOf(prefix, System.StringComparison.Ordinal);
            if (at < 0) { return listName; }
            return listName[..at] + listName[(at + prefix.Length)..];
        }

        private static string GetLevelDisplay(JobSlot slot, int level, int wantLevel) {
            var progress = slot?.ShownProgress ?? 0f;
            bool loves = slot?.Loves ?? false;
            bool incapable = slot?.Incapable ?? true;
            string statusSuffix = string.Empty;
            if (loves) {
                statusSuffix = Texts.Green(statusSuffix);// $" {Texts.Grey("|")} {Texts.Green(T.VerbLoves)}";
            } else if (incapable) {
                statusSuffix = Texts.Red(statusSuffix);// $" {Texts.Grey("|")} {Texts.Red(T.AdjIncapable)}";
            }
            string lvlText;
            string prog;
            if (level >= wantLevel) {
                lvlText = Units.Num(level);
                prog = Texts.Grey(Units.Percentage(progress));
            } else {
                lvlText = $"{Units.Num(level)}{GlyphMap.ArrowRight}{Units.Num(level + 1)}";
                prog = Texts.Accented(Units.Percentage(progress));
            }
            return $"{lvlText} {prog}{statusSuffix}";
        }

        private static string GetLevelProgressTip(JobType job, Jobs jobs) {
            if (jobs == null) { return null; }
            if (!jobs.TryJob(job.IdHash, out var slot)) { return null; }
            var prog = Texts.Accented(Units.Percentage(slot.ShownProgress));
            var tip = T.JobLevelProgress_V1.F(prog);
            if (slot.Loves) {
                tip = $"{Texts.Green(T.VerbLoves)}\n{tip}";
            } else if (slot.Incapable) {
                tip = $"{Texts.Red(T.AdjIncapable)}\n{tip}";
            }
            return tip;
        }

        protected override void Cleanup() {
            comp = null;
            S = null;
            wInputBtn = null;
        }
    }
}
