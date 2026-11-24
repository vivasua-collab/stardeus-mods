using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Game.Constants;
using Game.Data;
using Game.Modifiers;
using Game.Rendering;
using Game.Scenarios;
using Game.Systems.Backstories;
using Game.Systems.Creatures;
using Game.Systems.Mental;
using Game.UI;
using Game.Utils;
using KL.Pool;
using KL.UI;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class StartingBeingWidget : BaseWidget {
        [SerializeField] private WButton wCloseBtn;
        [SerializeField] private WEntityHeader wEntityHeader;
        [SerializeField] private WButtonIcon btnRandomize;
        [SerializeField] private ScrollRect scroll;
        [SerializeField] private WGroup wItems;
        private GameObject panelBlock;
        public Action OnRandomize;
        private StartingBeingData being;
        private Lifetime lifetime;
        private Action afterRename;
        public void Initialize(GameObject block) {
            panelBlock = block;
            btnRandomize.Load(IconId.CDice, Randomize);
            wItems.DestroyChildren();
            wCloseBtn.Load(Close);
            block.SetActiveFast(false);
        }

        private void Randomize() {
            OnRandomize();
        }

        public void Show(Lifetime parentLife, StartingBeingData being, Action afterRename) {
            this.afterRename = afterRename;
            this.being = being;
            if (lifetime != null) {
                lifetime.Dispose();
            }
            lifetime = parentLife.CreateChild("StartingBeing");
            UIUtils.AddTip(lifetime,btnRandomize, T.Randomize);
            scroll.verticalNormalizedPosition = 1f;
            Load(being);
            panelBlock.SetActiveFast(true);
        }

        private void Load(StartingBeingData being) {
            wEntityHeader.Load((Sprite) null, being.Data.Mind.Name.FullName);
            being.Data.Avatar.Load(wEntityHeader.Image);
            wEntityHeader.SetText2(being.Data.UI.GetBio(true, true));
            wEntityHeader.AutoResize();

            int idx = 0;
            LoadPersona(idx++);
            // A sleeper is identity only until woken, as on their in-game panel: skills and
            // traits are revealed on the wake-up choice screen
            if (!being.IsSleeper) {
                LoadJobs(idx++);
            }
        }
        private void LoadJobs(int siblingIdx) {
            var block = UISpawn.Block(lifetime, wItems, siblingIdx);
            int idx = 0;

            var header = UISpawn.Widget<WText>(lifetime, UIPrefabId.WContent2Header, block.Container, idx++);
            header.Load(T.NounJobs);

            foreach (var job in being.Data.Mind.Jobs.JobSlots.Values.OrderBy(j => j.Job.NameT)) {
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
            var d = being.Data;
            var isSleeper = being.IsSleeper;
            var hasRoles = !Arrays.IsEmpty(d.Mind.Experience.Roles);
            var hasTraits = !isSleeper && !Arrays.IsEmpty(d.Mind.Traits?.Slots);
            if (!hasRoles && !hasTraits && !isSleeper) {
                return;
            }
            var block = UISpawn.Block(lifetime, wItems, siblingIdx);
            int idx = 0;

            var header = UISpawn.Widget<WButtonText2>(lifetime, UIPrefabId.WContentHeaderButton, block.Container, idx++);
            header.Load(T.Persona, () => BeginRename(header.Button.transform as RectTransform), T.Rename);

            if (hasRoles) {
                foreach (var role in d.Mind.Experience.Roles) {
                    var rw = UISpawn.Widget<WIconText2>(lifetime, UIPrefabId.WContentText2, block.Container, idx++);
                    rw.Load(BeingRole.PhaseIcon(role.Key),
                        role.Value.NameT, Texts.Grey(Texts.AgePhase(role.Key, d.Species)));
                    UIUtils.AddTip(lifetime, rw, role.Value.RoleTooltip(
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
            if (isSleeper) {
                var sw = UISpawn.Widget<WIconText>(lifetime, UIPrefabId.WContentTextBlockIcon, block.Container, idx++);
                sw.Load(IconId.CCryogenics, T.BeingInStasisUnrevealed);
            }
        }
        private void BeginRename(RectTransform widget) {
            being.Data.UI.ShowRenameCtx(widget, () => {
                wEntityHeader.Load((Sprite) null,
                    being.Data.Mind.Name.FullName);
                being.Data.Avatar.Load(wEntityHeader.Image);
                afterRename?.Invoke();
            });
        }

        public void Close() {
            panelBlock.SetActiveFast(false);
        }
    }
}
