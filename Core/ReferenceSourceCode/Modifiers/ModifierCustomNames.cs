using System;
using System.Collections.Generic;
using System.Linq;
using Game.Constants;
using Game.Data;
using Game.Story.Events;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using KL.Clock;
using KL.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System.IO;
using Game.Systems.Naming;
using Game.Systems.UIUX;

namespace Game.Modifiers {
    public sealed class ModifierCustomNames : Modifier {
        public const string ModifierId = "CustomNames";
        public const string CustomNamesFile = "CustomNames.txt";
        public override int SortOrder => 74;
        public override string GroupName => T.StatCategoryColony;
        public override string Id => ModifierId;
        public override bool CanChangeInGame => true;
        public override bool PreventsAchievements => false;
        public static ModifierDisableStoryEvents Get =>
            (ModifierDisableStoryEvents) ById[ModifierId];

        [SelfInit]
        private static void Register() {
            Register(new ModifierCustomNames());
        }
        public override bool IsActiveByDefault => false;

        public override void Reset(GameSetupData data) {
            IsActive = false;
        }

        public override void Apply(GameState s, bool isActive, bool isLoading) {
        }

        public override void CreateInGameControls(Lifetime lifetime, int idx, RectTransform parent) {
            CreateNewGameControls(lifetime, idx, parent);
        }
        public override void CreateNewGameControls(Lifetime lifetime, int idx, RectTransform parent) {
            var btn = UISpawn.Widget<WButtonText>(lifetime, UIPrefabId.WTabPanelItem_Button, parent, idx);
            btn.Load(DecoratedLabelT, OnClickCustomNames);
            AddTip(lifetime, btn, DescT);
        }
        private void OnClickCustomNames() {
            // Re-enable name source because player may change the list
            Registries.NameSources.Get(CustomBeingNameSource.SourceId).IsDisabled = false;
            WPopup.Create(true, IconId.CInfo, T.ModifierCustomnames,
                T.ModifierCustomnamesTip, $"{T.Open} {Texts.Accented(Persist.PersistentPath(CustomNamesFile), false)}?")
                    .WithAction2(T.Cancel, null)
                    .WithAction(Texts.ExternalLink(T.Open), OpenFile)
                    .Spawn(UIPoolRoot.SystemPopupContainer);
        }

        private void OpenFile() {
            var file = Persist.PersistentPath(CustomNamesFile);
            if (!File.Exists(file)) {
                if (!EnsureCustomNamesFile(true)) {
                    return;
                }
            }
            Files.OpenOSFile(file);
        }

        public static bool EnsureCustomNamesFile(bool showError) {
            var file = Persist.PersistentPath(CustomNamesFile);
            if (!File.Exists(file)) {
                var res = Persist.Write(file, $"// {T.ModifierCustomnamesTip}", compress: false);
                if (!res.IsSuccess) {
                    D.Err("Could not create file: {0}: {1}", file, res.Exception);
                    if (showError) {
                        WPopup.Create(true, IconId.CWarning, T.Error,
                            $"Could not create file: {file}\n\n{res.Exception}")
                                .Spawn(UIPoolRoot.SystemPopupContainer);
                    }
                    return false;
                }
            }
            return true;
        }
    }
}
