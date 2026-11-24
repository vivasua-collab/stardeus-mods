using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Quests {
    // Asks for the supplies a scenario starts with to be allowed. They spawn forbidden so they
    // stay where the crew wakes up, and nobody eats or uses a forbidden item, so this is where
    // the player learns Allow. Only piles in the ship count (derelict loot stays forbidden on
    // purpose), and with nothing forbidden left the step completes on its own, so it cannot
    // stall the chain.
    public sealed class AllowItemsQuestStep : BaseQuestStep, ITickListener {
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(StepId,
                (s, c, l) => new AllowItemsQuestStep(s, c, l));
        }
        public const string StepId = "AllowItems";
        private readonly Def[] itemDefs;

        public AllowItemsQuestStep(GameState s, QuestStep step, bool isLoading)
                : base(StepId, s, step, isLoading) {
            var itemIds = config.GetStringSet(PropertyIdH.Definitions, null);
            if (Arrays.IsEmpty(itemIds)) {
                D.Throw("Failure to configure step, no item Definitions: {0}", step);
                return;
            }
            itemDefs = new Def[itemIds.Length];
            for (int i = 0; i < itemIds.Length; i++) {
                itemDefs[i] = The.Defs.Get(itemIds[i]);
                if (itemDefs[i] == null) {
                    D.Throw("Failure to configure step, def is not found: {0} -> {1}",
                        step, itemIds[i]);
                    return;
                }
            }
            RefreshTitle(null);
        }

        // Steps are built on load before the map is, so the first tick links the pile
        private bool isTitleStale = true;
        private Entity linked;

        private void RefreshTitle(Entity pile) {
            linked = pile;
            var link = pile != null ? Texts.LinkToEntity(pile) : itemDefs[0].CodexLink;
            step.Title = T.QuestStepAllowItems_V1.F(link);
            step.Desc = T.QuestStepAllowItemsTip_V1.F(link);
        }

        public override void OnStateChange(QuestStepState before, QuestStepState after) {
            if (after == QuestStepState.Active) {
                RefreshTitle(FirstForbidden());
            }
        }

        public void Tick(long currentTick) {
            // Inactive, the title keeps the codex link and nothing needs a scan
            if (!step.IsActive) { return; }
            var pile = FirstForbidden();
            if (isTitleStale || pile != linked) {
                isTitleStale = false;
                RefreshTitle(pile);
            }
            if (pile == null) {
                CompleteStep();
            }
        }

        private Entity FirstForbidden() {
            foreach (var def in itemDefs) {
                if (!S.Map.TryAllByDef(def, out var found)) { continue; }
                foreach (var e in found.Values) {
                    if (!e.IsActive) { continue; }
                    // A relocated pile stays forbidden in the carrier's hands and where it lands,
                    // so carrying it is not allowing it (STA-1789)
                    if (e is not Obj obj || obj.Pile?.IsForbidden != true) { continue; }
                    var pos = e.Carrier != null ? e.Carrier.PosIdx : e.PosIdx;
                    if (!EntityUtils.IsWithinMargin(pos, false, false)) { continue; }
                    return e;
                }
            }
            return null;
        }
    }
}
