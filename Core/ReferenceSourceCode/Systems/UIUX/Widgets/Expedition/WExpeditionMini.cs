using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Expeditions;
using Game.Input;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    /// <summary>
    /// Prefab: UIPrefabId.WExpeditionMini
    /// One instance is spawned per active expedition into the persistent HubRightContainer.
    /// The widget stays alive for the full duration of the expedition and is driven by
    /// ExpeditionsHandler. All bindings are owned by the per-expedition child Lifetime
    /// supplied to Load().
    /// </summary>
    public sealed class WExpeditionMini : WBase {
        // Progress bar showing shuttle storage fill — only shown for mining stages.
        public WProgress Progress;
        // Icon = MatType preview icon for mining expeditions, IconId.CDestination for
        // exploration. Text binds to BaseExpeditionStage.MiniWidgetText.
        // Click opens a QuickChoice with details and an optional Recall button.
        public WButtonIconText Button;
        // Status icon bound to BaseExpeditionStage.MiniWidgetStatusIcon.
        public WImage IconStatus;

        private GameState S;
        private Expedition exp;
        private Lifetime widgetLife;
        private Lifetime stageLife;

        public void Load(GameState s, Expedition expedition, Lifetime life) {
            S = s;
            exp = expedition;
            widgetLife = life;

            // Immediately render current stage, then re-render on every OnChange.
            exp.OnChange.Bind(life, OnExpeditionChange);

            // Tooltip registered once for the widget's lifetime; BuildTooltip reads
            // live values each time it is evaluated (on hover), so it stays accurate
            // across stage changes without being re-registered.
            UIUtils.AddTipFunc(life, Button, BuildTooltip);
        }

        private void OnExpeditionChange() {
            if (exp.IsDone || !exp.IsValid) {
                return;
            }

            // Dispose previous per-stage bindings.
            stageLife?.Dispose();
            stageLife = null;

            if (exp.CurrentStage is not BaseExpeditionStage stage) {
                return;
            }

            stageLife = widgetLife.CreateChild("expmini.stage");

            var actionRequired = exp.Context?.IsActionRequired == true;

            // --- Status icon ---
            var statusIcon = actionRequired ? IconId.CBell : stage.MiniWidgetStatusIcon;
            if (!string.IsNullOrEmpty(statusIcon)) {
                IconStatus.Load(statusIcon);
            }

            // --- Button icon + text + click ---
            var buttonIcon = GetButtonIcon();
            if (actionRequired) {
                Button.Load(buttonIcon, T.VerbCalling, OnButtonClick);
            } else {
                Button.Load(buttonIcon, stage.MiniWidgetText.Value ?? "...", OnButtonClick);
                stage.MiniWidgetText.Bind(stageLife, text => {
                    Button.SetText(text ?? "...");
                }, notifyImmediately: false);
            }

            // --- Storage progress bar ---
            if (!actionRequired && stage.MiniWidgetShowsStorage) {
                Progress.SetActive(true);
                var storage = exp.Shuttle.Tile.MaterialStorage;
                UpdateStorageProgress(storage);
                storage.OnChange.Bind(stageLife, () => UpdateStorageProgress(storage));
            } else {
                Progress.SetActive(false);
            }
        }

        private string GetButtonIcon() {
            if (!exp.IsExploration && S.Sys.Planets.TryGetResourceFor(
                    exp.Target, out _, out var mt)) {
                return mt?.Def?.Preview;
            }
            return IconId.CDestination;
        }

        private void UpdateStorageProgress(MatStorageComp storage) {
            Progress.SetValue(storage.FillPercent);
        }

        private string BuildTooltip() {
            var sb = Caches.StringBuilder3;
            sb.Clear();
            if (exp.Shuttle != null) {
                sb.Append(Texts.SubTitle(exp.Shuttle.Name));
            }
            var target = exp.InitialTarget ?? exp.Target;
            if (target != null) {
                sb.Append(T.NounTarget);
                sb.Append(": ");
                sb.AppendLine(Texts.LinkToStarmap(target));
            }
            var stageText = exp.CurrentStage?.TitleTooltip ?? exp.CurrentStage?.Title;
            if (stageText != null) {
                sb.AppendLine(stageText);
            }
            if (!Arrays.IsEmpty(exp.Crew)) {
                sb.AppendLine();
                sb.Append(Texts.SubTitle(T.ExpeditionCrew));
                foreach (var member in exp.Crew) {
                    sb.Append(' ');
                    sb.Append(GlyphMap.ArrowRight);
                    sb.Append(' ');
                    sb.AppendLine(Texts.LinkToBeing(member));
                }
            }
            return sb.ToString();
        }

        private void OnButtonClick() {
            if (exp.Context?.IsActionRequired == true) {
                exp.Context.ShowActionRequiredPrompt();
            } else {
                A.S.UIQuery.ToggleExpedition.Ask(exp);
            }
        }
    }
}
