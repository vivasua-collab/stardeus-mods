using System;
using Game.Systems.Creatures;
using KL.Utils;
using UnityEngine;

namespace Game.Data {
    public class DialogData {
        public bool IsSkippable;
        public int TextTemplateHash;
        public string Text;
        public string Icon;
        public BeingData Being;
        public string RightButtonText;
        public string LeftButtonText;
        public string SideText;
        public Action OnShow;
        public Action RightButtonAct;
        public Action LeftButtonAct;
        public bool CanCloseWithEsc;
        public Func<bool> ShouldBeRemoved;
        public int OverrideSortOrder;

        public override string ToString() {
            return $"DialogData[{Text}][{SideText}[{LeftButtonText}][{RightButtonText}]";
        }
    }
}
