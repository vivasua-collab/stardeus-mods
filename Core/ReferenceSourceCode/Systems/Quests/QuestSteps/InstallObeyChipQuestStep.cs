using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Creatures.Surgeries;
using Game.Systems.Items;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Quests {
    /// <summary>
    /// Drives the full ObeyChip surgery pipeline for the captured Crawler.
    /// On activation: picks an incapacitated Crawler, spawns the ObeyChip "from
    /// the colonist's pocket", registers a SurgicalOperation restricted to the
    /// giver colonist, then monitors for completion or failure.
    /// Persists CrawlerId / ChipObjId / SurgeryId across save/load.
    /// </summary>
    public sealed class InstallObeyChipQuestStep : BaseQuestStep,
            IPreCheckedQuestStep {
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(CheckId,
                (s, c, l) => new InstallObeyChipQuestStep(s, c, l));
        }

        public const string CheckId = "InstallObeyChipOnCrawler";

        private const string Key_CrawlerId = "CrawlerId";
        private const string Key_ChipObjId = "ChipObjId";
        private const string Key_SurgeryId = "SurgeryId";

        private readonly BeingData colonist;
        private bool isSubscribed;

        private uint crawlerId;
        private uint chipObjId;
        private int surgeryId;

        public InstallObeyChipQuestStep(GameState s, QuestStep step, bool isLoading)
                : base(CheckId, s, step, isLoading) {
            if (step.Data.Provider is ColonistQuestProvider cp) {
                colonist = cp.BeingData;
            }
            if (colonist == null) {
                D.Throw("InstallObeyChipOnCrawler: quest provider is not a ColonistQuestProvider | {0}", step);
            }

            step.Title = T.QuestStepExperimentWaitTitle_V1.F(colonist.Mind.LinkToShortName);
            step.Desc = T.QuestStepExperimentCrawlerChipWaitDesc_V3.F(colonist.Mind.LinkToShortName,
                Texts.LinkToCodex(DefIdsH.BeingsCrawler01), Texts.LinkToCodex(DefIdsH.ObjectsDevicesSurgeryHub));

            if (isLoading && step.IsActive) {
                RestoreFromData();
                Subscribe();
            }
        }

        private void RestoreFromData() {
            var d = step.Data.Data;
            if (d != null) {
                crawlerId = (uint) d.GetInt(DataKey(Key_CrawlerId), 0);
                chipObjId = (uint) d.GetInt(DataKey(Key_ChipObjId), 0);
                surgeryId = d.GetInt(DataKey(Key_SurgeryId), 0);
            }

            if (crawlerId != 0) {
                var crawlerData = S.Query.FindBeingData.Ask(crawlerId);
                if (crawlerData == null || !crawlerData.Body.IsAlive) {
                    FailStep();
                    return;
                }
            }

            if (surgeryId != 0) {
                var op = S.Query.GetSurgicalOp.Ask(surgeryId);
                if (op == null) {
                    // Surgery was cleaned up before save — try to rebuild
                    TryRebuildSurgery();
                }
            }
        }

        private void TryRebuildSurgery() {
            var crawlerData = crawlerId != 0 ? S.Query.FindBeingData.Ask(crawlerId) : null;
            if (crawlerData == null || !crawlerData.Body.IsAlive) {
                FailStep();
                return;
            }
            var chip = chipObjId != 0 ? S.Query.FindObjData.Ask(chipObjId) : null;
            if (chip == null) {
                // Chip was destroyed — spawn a fresh one
                chip = SpawnChip();
                if (chip == null) { FailStep(); return; }
            }
            RegisterSurgery(crawlerData, chip);
        }

        public override void OnStateChange(QuestStepState before, QuestStepState after) {
            if (after == QuestStepState.Active) {
                Activate();
            }
        }

        private void Activate() {
            if (colonist == null || colonist.Being == null || !colonist.Being.IsAlive) {
                return;
            }

            var crawlerData = FindCrawler();
            if (crawlerData == null) {
                D.Err("InstallObeyChipOnCrawler: no incapacitated Crawler found at activation");
                FailStep();
                return;
            }

            var chip = SpawnChip();
            if (chip == null) {
                D.Err("InstallObeyChipOnCrawler: failed to spawn ObeyChip");
                FailStep();
                return;
            }

            RegisterSurgery(crawlerData, chip);
            Subscribe();
        }

        private BeingData FindCrawler() {
            foreach (var b in S.Beings.Alive) {
                if (CaptureIncapacitatedCrawlerQuestStep.IsCapturableCrawler(b.Data)) {
                    return b.Data;
                }
            }
            return null;
        }

        private ObjData SpawnChip() {
            if (colonist.Being == null) { return null; }
            var spawnPos = EntityUtils.CenterOf(colonist.Being.PosIdx);
            var def = The.Defs.FastGet(DefIdsH.ObjPartsImplantObeyChip);
            if (def == null) {
                D.Err("InstallObeyChipOnCrawler: ObeyChip def not found");
                return null;
            }
            var chip = S.Objs.Create(spawnPos, def);
            if (chip == null) { return null; }
            chipObjId = chip.Data.Id;
            var data = step.Data.GetOrCreateData();
            data.SetInt(DataKey(Key_ChipObjId), (int) chipObjId);
            return chip.Data;
        }

        private void RegisterSurgery(BeingData crawlerData, ObjData chip) {
            crawlerId = crawlerData.Id;
            var data = step.Data.GetOrCreateData();
            data.SetInt(DataKey(Key_CrawlerId), (int) crawlerId);

            if (!crawlerData.Body.TryGetSlot(BodyPartSlotIdH.Implant, out var implantSlot)) {
                D.Err("InstallObeyChipOnCrawler: Crawler has no Implant slot: {0}", crawlerData);
                FailStep();
                return;
            }

            var op = new SurgicalOperation {
                TargetBeing = crawlerData,
                TargetSlot = implantSlot,
                ReservedPart = chip,
                OpType = SurgicalOpType.Install,
                RestrictedOperatorMindIds = new[] { colonist.Mind.Id }
            };

            if (S.Query.RegisterSurgicalOperation.Ask(op, true)) {
                surgeryId = op.Id;
                data.SetInt(DataKey(Key_SurgeryId), surgeryId);
            } else {
                D.Err("InstallObeyChipOnCrawler: failed to register SurgicalOperation: {0}", op);
                FailStep();
            }
        }

        private void Subscribe() {
            if (isSubscribed) { return; }
            isSubscribed = true;
            S.Sig.SurgicalOperationRegistered.AddListener(OnSurgicalOpChanged);
            S.Sig.BeingDied.AddListener(OnBeingDied);
        }

        private void OnSurgicalOpChanged(SurgicalOperation op, bool added) {
            if (!step.IsActive) { return; }
            if (op.Id != surgeryId) { return; }
            if (added) { return; }

            // Op was removed — check whether it succeeded
            var crawlerData = S.Query.FindBeingData.Ask(crawlerId);
            var def = crawlerData?.Body.ImplantSlot.InstalledDef?.Def;
            if (def != null && def.IdH == DefIdsH.ObjPartsImplantObeyChip) {
                CompleteStep();
            } else {
                FailStep();
            }
        }

        private void OnBeingDied(BeingData died) {
            if (!step.IsActive) { return; }
            if (died.Id != crawlerId) { return; }
            FailStep();
        }

        public void PreCheck() {
            // This step never pre-completes — it drives active work only.
        }

        public override void Cleanup() {
            if (!isSubscribed) { return; }
            isSubscribed = false;
            S.Sig?.SurgicalOperationRegistered.RemoveListener(OnSurgicalOpChanged);
            S.Sig?.BeingDied.RemoveListener(OnBeingDied);

            // If we're cleaning up while surgery is still live, cancel it
            if (surgeryId != 0) {
                var op = S.Query.GetSurgicalOp?.Ask(surgeryId);
                if (op != null && op.IsActive) {
                    S.Query.RegisterSurgicalOperation.Ask(op, false);
                }
            }
        }
    }
}
