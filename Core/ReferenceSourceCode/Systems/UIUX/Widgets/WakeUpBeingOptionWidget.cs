using System;
using System.Linq;
using Game.Constants;
using Game.Systems.Backstories;
using Game.Systems.Creatures;
using Game.Systems.Mental;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class WakeUpBeingOptionWidget : BaseWidget {
        [SerializeField] private WButtonText btnChoose;
        [SerializeField] private WEntityHeader wEntityHeader;
        [SerializeField] private ScrollRect scroll;
        [SerializeField] private WGroup wItems;
        private BeingData being;
        private Lifetime lifetime;
        private Action<BeingData> onChoose;

        private void Choose() {
            onChoose.Invoke(being);
        }

        public void Load(Lifetime parentLife, BeingData being, Action<BeingData> onChoose) {
            btnChoose.Load(T.SelectThisColonist, Choose);
            this.being = being;
            this.onChoose = onChoose;
            if (lifetime != null) {
                lifetime.Dispose();
            }
            lifetime = parentLife.CreateChild($"WakeUpOption_{being.Id}");
            scroll.verticalNormalizedPosition = 1f;

            wEntityHeader.Load((Sprite) null, being.Mind.Name.FullNameOrNameless);
            being.Avatar.Load(wEntityHeader.Image);
            wEntityHeader.SetText2(being.UI.GetBio(true, true));
            wEntityHeader.AutoResize();

            int idx = 0;
            LoadPersona(idx++);
            LoadJobs(idx++);
        }
        private void LoadJobs(int siblingIdx) {
            var block = UISpawn.Block(lifetime, wItems, siblingIdx);
            int idx = 0;

            var header = UISpawn.Widget<WText>(lifetime, UIPrefabId.WContent2Header, block.Container, idx++);
            header.Load(T.NounJobs);

            foreach (var job in being.Mind.Jobs.JobSlots.Values.OrderBy(j => j.Job.Unskilled)
                    .ThenByDescending(j => j.Level).ThenBy(j => j.Job.NameT)) {
                if (job.Job.Flags.HasFlag(JobTypeFlags.Internal)) {
                    continue;
                }
                job.GetNameAndLevelText(out var jobName, out var lvlText, out var tip);
                var w = UISpawn.Widget<WIconText2>(lifetime, UIPrefabId.WContent2Text2, block.Container, idx++);
                w.Load(job.Job.IconId, jobName, lvlText);
                if (job.LacksAbilities) {
                    UIUtils.AddTipFunc(lifetime, w, job.Tooltip);
                } else {
                    UIUtils.AddTip(lifetime, w, tip);
                }
            }
        }
        private void LoadPersona(int siblingIdx) {
            var d = being;
            var hasRoles = !Arrays.IsEmpty(d.Mind.Experience.Roles);
            var hasTraits = !Arrays.IsEmpty(d.Mind.Traits?.Slots);
            if (!hasRoles && !hasTraits) {
                return;
            }
            var block = UISpawn.Block(lifetime, wItems, siblingIdx);
            int idx = 0;

            var header = UISpawn.Widget<WButtonText2>(lifetime, UIPrefabId.WContentHeaderButton, block.Container, idx++);
            header.Load(T.Persona, () => BeginRename(header.Button.transform as RectTransform), T.Rename);

            if (hasRoles) {
                foreach (var role in d.Mind.Experience.Roles) {
                    var rw = UISpawn.Widget<WIconText2>(lifetime, UIPrefabId.WContentText2, block.Container, idx++);
                    rw.Load(BeingRole.PhaseIcon(role.Key),role.Value.NameT, Texts.Grey(Texts.AgePhase(role.Key, d.Species)));
                    UIUtils.AddTip(lifetime,rw, role.Value.RoleTooltip(
                        role.Key, d.Species, d.Mind.Jobs));
                    // TODO tooltip
                }
            }
            if (hasTraits) {
                foreach (var slot in d.Mind.Traits.Slots) {
                    var tw = UISpawn.Widget<WIconText>(lifetime, UIPrefabId.WContentText, block.Container, idx++);
                    tw.Load(slot.SlotIcon, slot.Trait.NameT);
                    UIUtils.AddTip(lifetime, tw, slot.Trait.Tooltip(d.Mind.Jobs));
                    // TODO tooltip
                }
            }
        }
        private void BeginRename(RectTransform widget) {
            being.UI.ShowRenameCtx(widget, () => {
                wEntityHeader.Load((Sprite) null,
                    being.Mind.Name.FullNameOrNameless);
                being.Avatar.Load(wEntityHeader.Image);
            });
        }
    }
}
