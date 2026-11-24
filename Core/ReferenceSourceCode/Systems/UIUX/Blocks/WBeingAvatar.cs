using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Tools;
using Game.UI;
using KL.Utils;
using Game.Utils;
using UnityEngine;
using UnityEngine.UI;
using KL.UI;
using KL.Pool;
using TMPro;
using Game.Systems.Conditions;
using Game.CodeGen;
using Game.Input;

namespace Game.Systems.UIUX {
    [DisallowMultipleComponent]
    public class WBeingAvatar : WIconText, ICustomPoolable {
        [SerializeField] private UIHoverCallback hoverCallback;
        [SerializeField] private Button button;
        [SerializeField] private UIButtonWithOverlay activeOverlay;
        [SerializeField] private Image backgroundImage;
        // task / goal only
        [SerializeField] private Image statusIcon;
        // dead
        [SerializeField] private Image statusIconCenter;
        // fire + severe animated conditions
        [SerializeField] private Image statusIconAnimated;
        // condition (overrides trend arrow when active); trend arrow for organic beings
        [SerializeField] private Image sanityIcon;
        [SerializeField] private Image factionIcon;
        [SerializeField] private Image sanityFillImage;
        [SerializeField] private Slider sanitySlider;
        private GameState S;
        private BeingData beingData;
        public Being Being => beingData.Being;
        public bool IsSelected;
        private bool hasSanityBar;
        private Lifetime tipLife;
        // Why each status icon shows what it shows, for its tooltip
        private AvatarStatusKind statusKind;
        private ConditionState sanityCondition;
        private bool sanityIncapacitated;
        private bool showsWeapon;

        private void Awake() {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(OnClick);
            hoverCallback.SetCallback(OnHover, null);
            statusIconAnimated.sprite = Images.Sprite(IconId.CFire);
            // Set here rather than in the prefab: WBattleStation holds its own copy of the avatar.
            // Clicks still bubble up to the button.
            statusIcon.raycastTarget = true;
            sanityIcon.raycastTarget = true;
            // weapon previews are not square
            sanityIcon.preserveAspect = true;
        }

        private string StatusTip() {
            return BeingAvatarTips.StatusTip(S, beingData, statusKind);
        }

        private string SanityTip() {
            var sanityTip = BeingAvatarTips.SanityTip(beingData, sanityCondition, sanityIncapacitated);
            if (!showsWeapon) {
                return sanityTip;
            }
            var weaponTip = BeingAvatarTips.WeaponTip(beingData);
            return sanityTip == null ? weaponTip : $"{weaponTip}{GlyphMap.BRHR}{sanityTip}";
        }

        // A drafted being's weapon takes the sanity slot: what it fights with matters more then
        private string WeaponIcon() {
            if (beingData.Inventory?.Equipment.TryGetValue(InventorySlotIdH.Weapon, out var slot) == true
                    && slot.Item != null) {
                return slot.Item.Def.Preview;
            }
            return IconId.CFist;
        }

        public void SetAnimatedSprite(string icon) {
            if (icon == null) {
                statusIconAnimated.SetActiveFast(false);
            } else {
                statusIconAnimated.sprite = Images.Sprite(icon);
                statusIconAnimated.SetActiveFast(true);
            }
        }

        private void OnHover(bool isHovering) {
            if (beingData.Being != null) {
                EntityUtils.ShowEntityLink(null, beingData.Being, isHovering);
            }
        }

        public void SetSelected(bool yes) {
            if (IsSelected != yes) {
                IsSelected = yes;
                RefreshOverlay();
            }
        }

        // Drag-select preview overrides the selection outline until cleared
        private bool hasDragPreview;
        private bool dragPreview;

        public void SetDragPreview(bool yes) {
            if (hasDragPreview && dragPreview == yes) { return; }
            hasDragPreview = true;
            dragPreview = yes;
            RefreshOverlay();
        }

        public void ClearDragPreview() {
            if (!hasDragPreview) { return; }
            hasDragPreview = false;
            RefreshOverlay();
        }

        private void RefreshOverlay() {
            var show = hasDragPreview ? dragPreview : IsSelected;
            activeOverlay.AlwaysShow(show);
            activeOverlay.ForceOverlay(show);
        }

        private void OnClick() {
            The.Toolbox.Clear();
            BeingUtils.Select(beingData, true);
        }

        public void Load(Lifetime life, GameState s, BeingData data, bool isSecondary) {
            // D.Warn("Loading being avatar: {0}", beingData);
            S = s;
            beingData = data;
            name = $"BeingAvatar_{data.Name}";
            var capturedData = data;
            data.Avatar.Load((sprite) => {
                if (beingData == capturedData) {
                    image.sprite = sprite;
                }
            });
            lastStatusIcon = null;
            lastSanityIcon = null;
            sanityConditionIcon = null;
            sanityTrendIcon = null;
            lastIcon = null;
            hasSanityBar = false;
            // Mind.Faction, not EffectiveFaction: the latter collapses to Player for an
            // obedient/forced-to-obey guest, which would flag them as one of ours.
            if (data.IsGuest) {
                factionIcon.sprite = Images.Sprite(data.Mind.Faction.FlagIcon);
                factionIcon.SetActiveFast(true);
            } else {
                factionIcon.SetActiveFast(false);
            }
            sanitySlider.SetActiveFast(false);
            sanityIcon.SetActiveFast(false);
            statusIcon.SetActiveFast(false);
            statusIconAnimated.SetActiveFast(false);
            // WBattleStation reloads with the same lifetime on every operator change, and an equal
            // tooltip can't be added twice
            tipLife?.Dispose();
            tipLife = life.CreateChild($"BeingAvatarTips_{data.Id}");
            Tooltip.CreateFor(tipLife, this, statusIcon.rectTransform, StatusTip);
            Tooltip.CreateFor(tipLife, this, sanityIcon.rectTransform, SanityTip);
            data.SetAvatarWidget(this, isSecondary);
            if (data.Being?.Flammable.IsOnFire == true) {
                SetAnimatedSprite(IconId.CFire);
            }
            UpdateName();
            UpdateStatus();
            if (beingData.Mind.Health != null) {
                beingData.Mind.Health.TrendIcon.Bind(life, t => {
                    SetSanityTrendSprite(t);
                }, notifyImmediately: false);
            }
        }

        private void UpdateSanityBar() {
            var wantsBar = beingData.Mind.Health != null;
            if (wantsBar) {
                var sanity = beingData.StatsBeing.Sanity.Value;
                SetSanityTrendSprite(beingData.Mind.Health.TrendIcon.Value);
                if (!hasSanityBar) {
                    hasSanityBar = true;
                    UpdateSanitySlider(sanity);
                    sanitySlider.SetActiveFast(true);
                } else {
                    UpdateSanitySlider(sanity);
                }
            } else {
                if (hasSanityBar) {
                    hasSanityBar = false;
                    sanitySlider.SetActiveFast(false);
                    SetSanityTrendSprite(null);
                }
            }
        }

        // Feeds the trend source (organic beings via reactive bind + UpdateSanityBar).
        // Visibility is resolved through RenderSanityIcon so condition can override.
        private void SetSanityTrendSprite(string trend) {
            sanityTrendIcon = trend;
            RenderSanityIcon();
        }

        private void SetSanityConditionIcon(string icon) {
            sanityConditionIcon = icon;
            RenderSanityIcon();
        }

        // Single arbiter for sanityIcon: condition wins over trend arrow.
        private void RenderSanityIcon() {
            var icon = sanityConditionIcon ?? sanityTrendIcon;
            if (lastSanityIcon == icon) return;
            lastSanityIcon = icon;
            if (icon != null) {
                sanityIcon.sprite = Images.Sprite(icon);
                sanityIcon.SetActiveFast(true);
            } else {
                sanityIcon.SetActiveFast(false);
            }
        }

        private void UpdateSanitySlider(float sanity) {
            sanitySlider.value = sanity;
            sanityFillImage.color = BeingUtils.SanityColor(sanity);
        }

        public void UpdateName() {
            // some versions of avatar widget won't have a being name
            if (textMain == null) {
                return;
            }
            SetText(beingData.Mind.Name.ShortName);
            if (beingData.Being?.AIAgent.DirectControl.IsUnderDirectControl == true) {
                textMain.fontStyle |= FontStyles.Underline;
            } else {
                textMain.fontStyle &= ~FontStyles.Underline;
            }
            if (beingData.HasStateFlag(BeingState.Dead)) {
                textMain.fontStyle |= FontStyles.Strikethrough;
            } else {
                textMain.fontStyle &= ~FontStyles.Strikethrough;
            }
            // if (beingData.CanControl) {
            //     textMain.color = Consts.ColorWhite;
            // } else {
            //     textMain.color = Texts.TextColorGrey;
            // }
        }

        public override string ToString() {
            return $"WBeingAvatar[{beingData}]";
        }

        public void PoolDeactivate() {
            statusIconAnimated.SetActiveFast(false);
            // a mid-drag group rebuild drops this avatar from the preview's reach
            ClearDragPreview();
            beingData.UnsetAvatar(this);
            // D.Warn("Deactivating being avatar: {0}", beingData);
            gameObject.SetActiveFast(false);
        }

        [ContextMenu("Update Status")]
        public void UpdateStatus() {
            var isIncapacitated = beingData.Body.IsIncapacitated;
            UpdateSanityBar();

            // Dead: skull only, clear all other overlays
            if (beingData.HasStateFlag(BeingState.Dead)) {
                statusIconCenter.sprite = Images.Sprite(IconId.CSkull);
                statusIconCenter.SetActiveFast(true);
                ShowStatus(null);
                sanityConditionIcon = null;
                sanityTrendIcon = null;
                sanityCondition = null;
                sanityIncapacitated = false;
                showsWeapon = false;
                RenderSanityIcon();
                sanitySlider.SetActiveFast(false);
                return;
            }
            statusIconCenter.SetActiveFast(false);

            // Condition resolution: animated overlay for severe, sanity slot for the rest
            string conditionIcon = null;
            string animatedIcon = null;
            bool isSleeping = false;
            if (beingData.GetMostImportantCondition(out ConditionState cond)) {
                isSleeping = cond.Condition.IdH == ConditionIdH.Sleeping;
                if (cond.Condition.Importance >= Priority.Third) {
                    // severe conditions (fire, mental breakdown, crawler, hacked): animated overlay only
                    animatedIcon = cond.Condition.Icon;
                } else {
                    conditionIcon = ResolveConditionIcon(cond);
                }
            }
            // Incapacitation always wins the sanity slot
            if (isIncapacitated) {
                conditionIcon = IconId.CIncapacitated;
            }
            sanityCondition = conditionIcon != null ? cond : null;
            sanityIncapacitated = isIncapacitated;
            var agent = beingData.Being?.AIAgent;
            showsWeapon = !isIncapacitated && agent?.DirectControl?.IsUnderDirectControl == true;
            if (showsWeapon) {
                conditionIcon = WeaponIcon();
            }
            SetAnimatedSprite(animatedIcon);
            SetSanityConditionIcon(conditionIcon);

            // Task slot: only shows when the being is active and able to work
            if (isIncapacitated || isSleeping) {
                ShowStatus(null);
                return;
            }
            if (beingData.HasStateFlag(BeingState.Expedition)) {
                ShowStatus(IconId.CPickaxe, AvatarStatusKind.Expedition);
                return;
            }
            if (agent != null) {
                if (agent.DirectControl?.IsUnderDirectControl == true) {
                    ShowStatus(IconId.CDraft, AvatarStatusKind.DirectControl);
                    return;
                }
                // Player units fight on the side while keeping their current (often Idle) goal
                if (agent.CombatUnit?.AttackTarget != null) {
                    ShowStatus(IconId.CFist, AvatarStatusKind.Combat);
                    return;
                }
            }
            var g = agent?.Goal;
            if (g == null || g.Type == AI.AIGoalType.Idle || g.Type == AI.AIGoalType.Personal) {
                ShowStatus(IconId.CTime, AvatarStatusKind.Idle);
            } else {
                ShowStatus(g.GoalIcon, AvatarStatusKind.Goal);
            }
        }

        private string ResolveConditionIcon(ConditionState cond) {
            var idH = cond.Condition.IdH;
            if (idH == ConditionIdH.Injured || idH == ConditionIdH.Damaged) {
                beingData.Body.CheckWounds(out _, out var isCrippled);
                if (isCrippled) return IconId.CCrutch;
            }
            return cond.Condition.Icon;
        }

        private string lastStatusIcon;
        private string lastSanityIcon;
        private string sanityConditionIcon;
        private string sanityTrendIcon;

        private void ShowStatus(string icon, AvatarStatusKind kind = AvatarStatusKind.None) {
            statusKind = kind;
            if (lastStatusIcon == icon) {
                return;
            }
            lastStatusIcon = icon;
            if (icon != null) {
                statusIcon.sprite = Images.Sprite(icon);
                statusIcon.SetActiveFast(true);
            } else {
                statusIcon.SetActiveFast(false);
            }
        }

        public void PoolActivate() {
            // D.Warn("Activating being avatar: {0}", beingData);
            gameObject.SetActiveFast(true);
        }
    }
}
