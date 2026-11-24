using Game.Constants;
using Game.Data;
using Game.Systems.Backstories;
using Game.Systems.Creatures;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public class EntityOverview_BeingPersona : EntityOverviewBlock {
        public const string BlockId = "BeingPersona";
        public override int Order => -100;
        public override string Id => BlockId;
        private bool personaExpanded = true;

        [SelfInit]
        private static void Register() {
            Register(new EntityOverview_BeingPersona());
        }

        // Backstory is identity and stays visible for a never-woken sleeper; traits are
        // what they turn out to be like and wait for the wake-up choice
        public override bool ShowForUnrevealed => true;

        public override bool ShowFor(GameState s, BeingData being) {
            return true;
        }

        public override void LoadContent(EntityOverviewContext ctx, WGroup group, ref int gIdx) {
            var being = ctx.Being;
            if (being == null) { return; }
            var lifetime = ctx.Lifetime;
            var hasRoles = !Arrays.IsEmpty(being.Mind.Experience.Roles);
            var hasTraits = !Arrays.IsEmpty(being.Mind.Traits?.Slots)
                && !StasisUtils.IsUnrevealed(being);
            var block = UISpawn.Block(lifetime, group, gIdx++);
            int idx = 0;

            var header = UISpawn.Widget<WContentListButtonWithButton>(lifetime, UIPrefabId.WContentHeaderExpandableButton, block.Container, idx++);
            header.Item.Load(T.Persona, null);
            header.Button.Load(T.Rename, () => BeginRename(being, header.Button.transform as RectTransform));
            // Only the player's own beings can be renamed (BeingUI.ShowRenameCtx). Set on
            // every load, as the pooled header goes back and forth between beings
            header.Button.gameObject.SetActiveFast(being.IsPlayer);
            header.Item.LoadOnToggle(lifetime, (life, container, willShow, wasLoaded) => {
                personaExpanded = willShow;
                if (!willShow) { return wasLoaded; }
                if (wasLoaded) { return true; }
                int cIdx = 0;
                if (hasRoles) {
                    foreach (var role in being.Mind.Experience.Roles) {
                        var rw = UISpawn.Widget<WIconText2>(life, UIPrefabId.WContentText2, container, cIdx++);
                        rw.Load(BeingRole.PhaseIcon(role.Key), role.Value.NameT, Texts.Grey(Texts.AgePhase(role.Key, being.Species)));
                        UIUtils.AddTip(life, rw, role.Value.RoleTooltip(role.Key, being.Mind.Species, being.Mind.Jobs));
                    }
                }
                if (hasTraits) {
                    foreach (var slot in being.Mind.Traits.Slots) {
                        var tw = UISpawn.Widget<WIconText>(life, UIPrefabId.WContentText, container, cIdx++);
                        tw.Load(slot.SlotIcon, slot.Trait.NameT);
                        UIUtils.AddTip(life, tw, slot.Trait.Tooltip(being.Mind.Jobs));
                    }
                }
                if (!hasRoles && !hasTraits) {
                    var w = UISpawn.Widget<WIconText>(life, UIPrefabId.WContentText, container, cIdx++);
                    w.Load(IconId.CMoodNeutral, T.AdjUnremarkable);
                    UIUtils.AddTip(life, w, T.BeingUnremarkableInfo);
                }
                return true;
            }, personaExpanded);
        }
        private void BeginRename(BeingData being, RectTransform widget) {
            being.UI.ShowRenameCtx(widget, null);
        }
    }
}
