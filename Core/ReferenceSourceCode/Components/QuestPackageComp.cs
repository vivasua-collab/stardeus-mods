using Game.Systems;
using Game.Constants;
using Game.Data;
using Game.CodeGen;
using KL.Utils;
using Game.Systems.AI;
using Game.Systems.Quests;
using Game.Systems.Rendering;
using Game.Systems.Story;
using Game.Utils;
using Game.Systems.UIUX;

namespace Game.Components {
    public sealed class QuestPackageComp : BaseComponent<QuestPackageComp>,
            IDisposableComp, IOpenable, IAIGoalProvider {
        private bool isOpen;
        public bool IsOpen => isOpen;
        public uint QuestPackageId;
        public uint QuestId;
        public string QuestTitle;
        public int ContentsSeed;

        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new QuestPackageComp());
        }

        public bool CanDisposeNow(out string reason) {
            if (isOpen) {
                reason = null;
                return true;
            }
            reason = T.MustOpenFirst;
            return false;
        }

        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetBool("IsOpened", isOpen);
            data.SetUInt("QuestPackageId", QuestPackageId);
            data.SetUInt("QuestId", QuestId);
            data.SetString("QuestTitle", QuestTitle);
            data.SetInt("ContentsSeed", ContentsSeed);
        }

        protected override void OnLoad(ComponentData data) {
            isOpen = data.GetBool("IsOpened", false);
            QuestPackageId = data.GetUInt("QuestPackageId", 0);
            QuestId = data.GetUInt("QuestId", 0);
            QuestTitle = data.GetString("QuestTitle", null);
            ContentsSeed = data.GetInt("ContentsSeed", 0);
        }

        public void AssignQuestPackage(QuestPackage pack) {
            if (pack == null) {
                QuestPackageId = 0;
                QuestId = 0;
                QuestTitle = null;
                return;
            }

            QuestPackageId = pack.Id;
            var questData = pack.Step?.Data;
            QuestId = questData?.Id ?? 0;
            QuestTitle = questData?.Title;
            if (pack.Seed != 0) {
                ContentsSeed = pack.Seed;
            } else if (ContentsSeed == 0) {
                ContentsSeed = Hashes.Quick(Primes.Prime020, EntityId);
                pack.Seed = ContentsSeed;
            } else {
                pack.Seed = ContentsSeed;
            }
        }

        public override string ToString() {
            return " * QuestPackage";
        }

        public override void OnLateReady(bool wasLoaded) {
            if (ContentsSeed == 0) {
                ContentsSeed = Hashes.Quick(Primes.Prime020, EntityId);
            }
            Ready.When(WhenH.ObjsLoaded, () => {
                UpdateGraphic();
            });
            Obj.Light.SwitchLight(!isOpen);
            // A Safe Box arrives on its own (a quest delivery, a gift) and waits to be
            // opened; UnseenObjectsSys points at it until the player looks. A derelict's box
            // spawns while Ready.Game is off, so ApproachDerelictShipQuestStep tracks it
            if (!wasLoaded && Ready.Game && !isOpen) {
                UnseenObjectsSys.For(S)?.Track(Obj);
            }
        }


        public bool TryOrderOpen() {
            if (TryGetGoal(out var g)) {
                return false;
            }
            g = CreateGoal(JobTypeIdH.Hauling, IconId.COpenPackage, T.Open, this)
                .WithWorldToDesired(AIVarsH.IsContainerOpen, false, true)
                .ByPlayer(true)
                .WithRequiredAbilities(AbilityIdH.Carry, AbilityIdH.DumbLabor)
                .WriteInitialState();

            return entity.TrySetGoal(g);
        }


        private void UpdateGraphic() {
            if (isOpen) {
                Obj.Graphics.ChangeRenderer(Renderers.Sprites,
                    SpriteOrder.ObjBelowFlat);
                // To make things appear in the right place
                Obj.Graphics.OverrideGraphic($"{Obj.Graphics.GraphicId}_Open");
            }
        }

        public override void OnRemove() {
            if (QuestPackageId != 0) {
                S.Query.DestroyQuestPackage.Ask(QuestPackageId);
            }
        }

        public void Open(Being opener) {
            if (isOpen) { return; }
            var pack = QuestPackageId == 0 ? null : S.Query.FindQuestPackage.Ask(QuestPackageId);
            if (pack?.State == QuestPackageState.Spawned) {
                AssignQuestPackage(pack);
                if (S.Query.OpenQuestPackage.Ask(this)) {
                    MarkOpen();
                    QuestPackageId = 0;
                    return;
                }
                LogFallbackOpen(pack, "normal quest package open failed");
            } else {
                LogFallbackOpen(pack, pack == null
                    ? "quest package record missing"
                    : $"quest package state is {pack.State}");
            }

            MarkOpen();

            if (QuestTitle != null) {
                WPopup.Warning("Orphaned Safety Box",
                    $"This safety box was supposed to belong to {Texts.Accented(QuestTitle)} quest, but the quest is not available anymore.<br><br>Diagnostics were logged, please submit an in-game bug report.");
            }
            QuestPackageId = 0;
            CapsuleOpenHelper.Start(S, this, Obj, opener, ContentsSeed,
                isQuestPackage: true);
        }

        private void MarkOpen() {
            isOpen = true;
            UpdateGraphic();
            Obj.Light.SwitchLight(false);
        }

        private void LogFallbackOpen(QuestPackage pack, string reason) {
            var quest = pack?.Step?.Data;
            D.Err(
                "Quest package open failed, using capsule fallback. Reason: {0}; PackageId: {1}; SavedQuestId: {2}; SavedQuestTitle: {3}; CurrentQuestId: {4}; CurrentQuestTitle: {5}; CurrentQuestState: {6}; StepId: {7}; PackageState: {8}; Obj: {9}",
                reason,
                QuestPackageId,
                QuestId,
                QuestTitle ?? T.Unknown,
                quest?.Id ?? 0,
                quest?.Title ?? T.Unknown,
                quest?.State.ToString() ?? T.Unknown,
                pack?.Step?.Def?.Id ?? T.Unknown,
                pack?.State.ToString() ?? T.Unknown,
                Entity);
        }


        public void OnGoalChange(AIGoal goal, AIGoalState from, AIGoalState to) {
        }

        public void OnGoalLoad(AIGoal goal) {
        }
    }
}
