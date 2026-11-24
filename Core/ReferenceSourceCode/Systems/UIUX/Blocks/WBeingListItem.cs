using System;
using Game.Data;
using Game.Systems.Creatures;
using Game.Utils;
using HarmonyLib;
using KL.Pool;
using KL.UI;
using KL.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    [DisallowMultipleComponent]
    public class WBeingListItem : WIconText, ICustomPoolable {
        [SerializeField] private Button button;
        [SerializeField] private Image avatarBackground;
        [SerializeField] private string controllableColorId;
        protected BeingData being;
        public BeingData Being => being;
        private Action onClick;
        private Lifetime nameLifetime;
        private bool decorateName;
        private bool bloodBelowCapacity;

        private void Awake() {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(OnClick);
        }

        private void OnDestroy() {
            DisposeNameLifetime();
        }

        public void Load(BeingData being, Action onClick = null, bool decorateName = true) {
            DisposeNameLifetime();
            nameLifetime = Lifetime.Create($"WBeingListItem_{being.Id}");
            this.being = being;
            this.onClick = onClick;
            this.decorateName = decorateName;
            var capturedBeing = being;
            being.Avatar.Load((sprite) => {
                if (this.being == capturedBeing) {
                    image.sprite = sprite;
                }
            });

            being.Mind.Name.NameChangedB.Bind(
                nameLifetime, RefreshName, invokeImmediately: false);
            if (decorateName) {
                being.Body.StateB.Bind(
                    nameLifetime, _ => RefreshName(), notifyImmediately: false);
                being.ConditionsListChangedB.Bind(
                    nameLifetime, RefreshName, invokeImmediately: false);
                being.Being?.AIAgent.DirectControl.DirectControlChangedB.Bind(
                    nameLifetime, RefreshName, invokeImmediately: false);

                if (being.Species.IsBiological) {
                    bloodBelowCapacity = IsBloodBelowCapacity();
                    being.StatsBeing.BloodAmount.BFloat.Bind(
                        nameLifetime, OnBloodChanged, notifyImmediately: false);
                    being.StatsBeing.BloodCapacity.BFloat.Bind(
                        nameLifetime, OnBloodChanged, notifyImmediately: false);
                }
            }

            RefreshName();
            var color = controllableColorId;
            avatarBackground.color = The.UITheme.GetColor(color, this);
            UIActivator.EnsureDeselected(gameObject);
        }

        private void RefreshName() {
            if (being == null) {
                return;
            }
            SetText(decorateName
                ? BeingUtils.DecoratedBeingName(being, false)
                : being.Mind.Name.FullNameOrNameless);
        }

        private bool IsBloodBelowCapacity() {
            return being.StatsBeing.BloodAmount.Value <
                being.StatsBeing.BloodCapacity.Value;
        }

        private void OnBloodChanged(float _) {
            bool isBelowCapacity = IsBloodBelowCapacity();
            if (isBelowCapacity == bloodBelowCapacity) {
                return;
            }
            bloodBelowCapacity = isBelowCapacity;
            RefreshName();
        }

        private void DisposeNameLifetime() {
            if (nameLifetime != null) {
                nameLifetime.Dispose();
                nameLifetime = null;
            }
        }

        private void OnClick() {
            if (onClick != null) {
                onClick();
                return;
            }
            if (being.Being != null) {
                EntityUtils.SelectOrFocus(being.Being);
            } else {
                A.S.UIQuery.ShowBeingPanel.Ask(being);
            }
        }

        public override string ToString() {
            return $"WBeingListItem[{being}]";
        }

        public void PoolDeactivate() {
            DisposeNameLifetime();
            being = null;
            this.SetActiveFast(false);
        }

        public void PoolActivate() {
            this.SetActiveFast(true);
        }
    }
}
