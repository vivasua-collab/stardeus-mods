using System.Collections;
using System.Collections.Generic;
using Game.Commands;
using Game.Constants;
using Game.Story;
using Game.Story.Events;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;
using Game.Systems.Story;
using Game.Systems.Quests;
using Game.Systems.UIUX;
using System;

namespace Game.Data.Space {
    public sealed class SOActionRename : SOAction {
        public const string ActionId = "rename";
        [SelfInit]
        private static void Register() {
            Register(ActionId, new SOActionRename());
        }

        public override bool IsAvailable(SOActionContext ctx) {
            var so = ctx.SO;
            if (so.IsShip) {
                return so.Ship.IsPlayer;
            }
            return so.Faction?.IsPlayer == true;
        }

        public override bool AddOptions(QuickChoice qc) {
            qc.AddButton(T.Rename, BeginRename, autoClose: false);
            return true;
        }

        public void BeginRename() {
            var so = ctx.SO;

            var qc = QuickChoice.Create(so.ListName);
            string newName = so.Name;
            Func<bool> confirm = () => OnConfirm(newName);
            Action<string> submit = (v) => {
                if (confirm()) {
                    ctx.S.UIQuery.CancelQuickChoice.Ask();
                } else {
                    UISounds.PlayActionDenied();
                }
            };
            var in1 = new QuickChoice.Input() {
                AutoFocus = true,
                Value = newName,
                OnChange = (v) => newName = v,
                OnSubmit = submit,
            };
            qc.AddInput(T.EnterName, in1);
            var btn = new QuickChoice.Button() {
                OnClickValidate = confirm,
                AutoClose = true,
            };
            qc.AddButton(T.Confirm, btn);
            ctx.S.UIQuery.ShowQuickChoice.Ask(qc);
        }

        private bool OnConfirm(string name) {
            if (string.IsNullOrWhiteSpace(name)) {
                The.SysSig.ShowTooltipWithTimeout.Send(
                    Texts.Red(T.EnterName), true, 1f);
                return false;
            }
            var so = ctx.SO;
            so.UpdateName(name);
            return true;
        }

        public override string Id => ActionId;

        public override int UIPriority => 50;
    }
}
