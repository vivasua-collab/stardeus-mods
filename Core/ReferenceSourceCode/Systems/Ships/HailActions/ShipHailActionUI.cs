#if !PRODUCTION_BUILD
// #define DEBUG_CALLS
#endif
using System;
using System.Collections.Generic;
using Game.Achievements;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Data.Space;
using Game.Input;
using Game.Systems.UIUX;
using Game.UI;
using Game.Utils;
using KL.Clock;
using KL.UI;
using KL.Utils;
using UnityEngine;
using Game.Systems.Notify;

namespace Game.Systems.Ships {
    /// <summary>
    /// Handles UI when player hails the NPC ship
    /// </summary>
    public sealed class ShipHailActionUI {
        private readonly NPCShip ship;
        public readonly GameState S;
        /// <summary>
        /// Did NPC hail the player?
        /// </summary>
        public readonly bool IsByNPC;
        public bool IsWaitingForPlayerChoice;
        public bool IsWaitingForPlayerPickUp;
        public long HailTimeout;
        public long HailStart;
        public static ShipHailActionUI For(GameState s, NPCShip ship, bool isByNPC,
                ShipHailAction forNewAction) {
            D.Warn("Getting hail UI for {0} | npc: {1} | act: {2}", ship, isByNPC, forNewAction);
            if (ship.HailUI != null) {
                if (forNewAction != null) {
                    if (ship.HailUI.action != null) {
                        D.Err("Disallowing new ship interaction ({0}) | {1}",
                            forNewAction, ship.HailUI.action, ship.HailUI);
                        if (!isByNPC) {
                            UISounds.PlayActionDenied();
                        }
                        return null;
                    }
                } else {
                    D.Warn("Trying to create new hail UI while one exists! ForAction: {0} | {1}",
                        forNewAction, ship.HailUI);
                }
                return ship.HailUI;
            }

            return ship.HailUI = new ShipHailActionUI(s, ship, isByNPC);
        }
        private ShipHailActionUI(GameState s, NPCShip ship, bool isByNPC) {
            this.ship = ship;
            S = s;
            IsByNPC = isByNPC;
        }
        public bool IsShowingHailDialog;
        private WShipHailPopup currentHailPopup;
        private WText responseWidget;
        private Lifetime hailPanelLife;
        private bool _isBuilding;
        private string responseMessage;
        #if DEBUG_CALLS
        private bool _isCallEnded;
        private bool isCallEnded {
            get => _isCallEnded;
            set => D.SetFieldWithDebug(ref _isCallEnded, value, "Call Ended");
        }
        #else
        private bool isCallEnded;
        #endif
        private NotificationHandle hailEvent;
        private ShipHailAction action;
        private IShipHailAction_N2P n2p;
        private const float commVol = 0.3f;
        private ShipChatResponseType wantedCommSound;
        private bool isSubscribed;
        private bool autoRespondWhenOperatorAvailable;
        private bool hadCommsOp;
        private WPopupData hailPopup;
        public void PlayCommSound(ShipChatResponseType type, bool ifSilent = false) {
            if (ifSilent && wantedCommSound != ShipChatResponseType.Undefined) {
                return;
            }
            wantedCommSound = type;
        }
        public void DoPlayCommSound() {
            // will play it only once
            if (wantedCommSound == ShipChatResponseType.Undefined) {
                return;
            }
            var type = wantedCommSound;
            wantedCommSound = ShipChatResponseType.Undefined;
            switch (type) {
                case ShipChatResponseType.Positive: {
                    UISounds.Play("FX/Hail_Yes_SFX", commVol);
                    break;
                }
                case ShipChatResponseType.Negative: {
                    UISounds.Play("FX/Hail_No_SFX", commVol);
                    break;
                }
                case ShipChatResponseType.End: {
                    UISounds.Play("FX/Hail_End_SFX", commVol);
                    break;
                }
                default:
                case ShipChatResponseType.Busy:
                case ShipChatResponseType.Neutral: {
                    UISounds.Play("FX/Hail_SFX", commVol);
                    break;
                }
            }
        }
        public void SetResponse(bool isLastMsg, ShipChatTopic topic, ShipChatResponseType response) {
            // D.Warn("Setting response: {0}|{1}. Last: {2} | {3}",
            //     topic, response, isLastMsg, ship);
            if (isLastMsg) {
                IsWaitingForPlayerChoice = false;
                IsWaitingForPlayerPickUp = false;
                isCallEnded = true;
                ClearAction();
            }
            var msg = ShipChatResponseDef.GetMessage(ship, topic, response);

            if (topic == ShipChatTopic.CombatEngage) {
                // to play a negative sound
                response = ShipChatResponseType.Negative;
            }
            PlayCommSound(response);
            SetResponse(msg);
            if (!IsShowingHailDialog && !IsWaitingForPlayerPickUp) {
                ShowHailPanel();
            } else if (isLastMsg && IsShowingHailDialog) {
                ShowHailPanel();
            }
        }

        public void SetResponse(string msg, bool needsRebuild = false) {
            responseMessage = msg;
            if (responseWidget != null) {
                responseWidget.Load(msg);
            }
        }

        private bool HasCommsOperator() {
            var lastPing = S.BB.Get(BBKeyH.CommunicatorOperatorPing,
                (long) -Consts.TicksPer2Hours);
            if (lastPing == 0L) { return false; }
            return G.Ticks - lastPing <= Consts.TicksPer60Minutes;
        }

        public void ShowHailPanel() {
            D.Warn("Showing hail panel");
            if (_isBuilding) {
                S.ActQ.Enqueue(ShowHailPanel);
                return;
            }
            if (IsWaitingForPlayerPickUp) { return; }
            _isBuilding = true;
            var prevPos = currentHailPopup != null
                ? (Vector3?) currentHailPopup.Rect.position : null;
            hailPanelLife?.Dispose();
            hailPanelLife = null;
            currentHailPopup = null;
            responseWidget = null;
            IsShowingHailDialog = true;
            if (!hailEvent.IsValid) {
                hailEvent = Notification.Build(IconId.CCallOngoing, ship.NameFull,
                        0, NotificationType.Immediate)
                    .WithOnClick(ShowHailPanel)
                    .Group("ship_hail", T.IncomingTransmission)
                    .Sticky()
                    .PulseOnly()
                    .Show();
                DoPlayCommSound();
            }
            if (!isSubscribed) {
                isSubscribed = true;
                S.Clock.OnTick.AddListener(OnTickAnsweredCall);
            }
            hadCommsOp = HasCommsOperator();
            hailPanelLife = Lifetime.Create("HailPanel");
            var attitude = S.Query.FactionAttitude.Ask(ship.Faction, FactionDef.Player);
            var attText = Units.PercentageSigned(attitude);
            attText = attitude >= 0f ? Texts.Green(attText) : Texts.Red(attText);
            var factionClassAtt = $"{ship.Faction.NameT} {ship.Class.NameT} ({attText})";
            var popup = UISpawn.Widget<WShipHailPopup>(hailPanelLife, UIPrefabId.WShipHailPopup,
                UIPoolRoot.PopupContainer, -1);
            var panel = popup.Panel;
            panel.Load(HideHailPanel);
            currentHailPopup = popup;
            popup.Header.Load(ship.Class.Preview, ship.NameWithTag, factionClassAtt, () => {
                S.UIQuery.StarmapFocusOn.Ask(ship.SO, true);
            });
            Coroutines.OnNextFrame(popup.Header.AutoResize);
            popup.Header.SetFactionIcon(ship.Faction);
            UIUtils.AddTooltipDynamic(popup.Header.gameObject, () => ship.FullTooltip(true));
            if (prevPos.HasValue) {
                popup.Rect.position = prevPos.Value;
            } else {
                UIUtils.AfterShowPopup(popup.Rect, centerOfScreen: true);
            }
            int blockIdx = 0;
            var block = UISpawn.Block(hailPanelLife, panel.Content, blockIdx++);
            int idx = 0;

            if (!isCallEnded && !hadCommsOp) {
                BuildPanel_WaitingForOperator(hailPanelLife, block.Container, ref idx);
            } else {
                if (!isCallEnded && !IsByNPC && n2p == null && responseMessage == null &&
                        ship.BehaviorSector?.IsBusy(ship, out _) != true) {
                    SetResponse(isLastMsg: false,
                        ShipChatTopic.HailRequest, ShipChatResponseType.Neutral);
                }
                if (responseMessage != null) {
                    responseWidget = UISpawn.Widget<WText>(hailPanelLife, UIPrefabId.WContentTextBlock,
                        block.Container, idx++);
                    responseWidget.Load(responseMessage);
                }

                if (!isCallEnded) {
                    The.AchievementTracker.Unlock(AchievementFirstContact.AchievementId);
                    var actionsLife = hailPanelLife.CreateChild("HailActions");
                    // reuse the first block if nothing was added into it
                    bool needsNewBlock = idx > 0;
                    if (!IsByNPC) {
                        if (ship.BehaviorSector?.IsBusy(ship, out var message) == true) {
                            needsNewBlock = false;
                        }
                    }
                    WContentBlock actionsBlock = block;
                    if (needsNewBlock) {
                        actionsBlock = UISpawn.Block(actionsLife, panel.Content, blockIdx++);
                        var actionsHeader = UISpawn.Widget<WText>(actionsLife, UIPrefabId.WContent2Header, actionsBlock.Container, idx++);
                        actionsHeader.Load(T.ChooseResponse);
                    }
                    int idxBefore = idx;
                    if (IsByNPC || n2p != null) {
                        BuildPanel_N2P(actionsLife, actionsBlock.Container, ref idx);
                    } else {
                        BuildPanel_P2N(actionsLife, actionsBlock.Container, ref idx);
                    }
                    if (idx == idxBefore) {
                        actionsLife.Dispose();
                    }
                }
            }
            AddEndCallButton(hailPanelLife, panel);
            _isBuilding = false;
        }

        public void HideHailPanel() {
            hailPanelLife?.Dispose();
            hailPanelLife = null;
            currentHailPopup = null;
            responseWidget = null;
            IsShowingHailDialog = false;
            if (isCallEnded) {
                PostEndCall();
            } else if (IsWaitingForPlayerChoice && S.Clock.IsForceStopped) {
                UISounds.PlayActionDenied();
                S.ActQ.Enqueue(() => {
                    if (!IsShowingHailDialog) {
                        ShowHailPanel();
                    }
                });
            }
        }

        private void BuildPanel_WaitingForOperator(Lifetime life, RectTransform container, ref int idx) {
            HailOperator();
            UISounds.Play("FX/Hail_SFX", commVol);
            var comms = S.Components.AllByType<CommunicatorComp>();
            if (!Arrays.IsEmpty(comms)) {
                foreach (CommunicatorComp comm in comms) {
                    if (comm.IsConstructed && comm.Tile.ENode.IsConnected) {
                        comm.CheckWantsOperator();
                    }
                }
            }
            var waitText = UISpawn.Widget<WIconText>(life, UIPrefabId.WContentText, container, idx++);
            waitText.Load(IconId.CTime, T.WaitingForOperator);
        }

        private void HailOperator() {
            S.BB.Set(BBKeyH.CommunicatorNeedsOperator,
                G.Ticks + Consts.TicksPer30Minutes);
        }

        private void BuildPanel_N2P(Lifetime life, RectTransform container, ref int idx) {
            n2p?.N2P_LoadPlayerChoice(this, ship, life, container, ref idx);
        }

        private void BuildPanel_P2N(Lifetime life, RectTransform container, ref int idx) {
            if (ship.BehaviorSector?.IsBusy(ship, out var message) == true) {
                if (message == null) {
                    SetResponse(isLastMsg: true, ShipChatTopic.HailRequest, ShipChatResponseType.Busy);
                }
                PlayCommSound(ShipChatResponseType.Busy, ifSilent: true);
                if (message != null) {
                    var busyText = UISpawn.Widget<WIconText>(life, UIPrefabId.WContentText, container, idx++);
                    busyText.Load(IconId.CTime, message);
                }
                if (ship.BehaviorSector is ICancellableNPCShipBehavior cancellable) {
                    cancellable.LoadCancelBlock(this, ship, life, container, ref idx);
                }
            } else {
                foreach (var act in Registries.ShipHailActions.All.Values) {
                    if (act is IShipHailAction_P2N p2n) {
                        if (p2n.P2N_IsCompatible(ship)) {
                            p2n.P2N_LoadHailNPCBlock(this, ship, life, container, ref idx);
                        }
                    }
                }
            }
        }


        public void ClearAction() {
            D.Warn("Clearing hail action for {0}", ship);
            action = null;
            n2p = null;
        }

        private void AddEndCallButton(Lifetime life, WPanel panel) {
            panel.ShowFooter(true);
            var btn = UISpawn.Widget<WButtonText>(life, UIPrefabId.WPanelButton_Red,
                panel.Footer.Rect, -1);
            btn.Load(T.VerbEndCall, () => {
                isCallEnded = true;
                S.BB.Set(BBKeyH.CommunicatorNeedsOperator, G.Ticks - 1);
                HideHailPanel();
            });
        }

        public void EndCall() {
            isCallEnded = true;
            if (hailPopup != null) {
                hailPopup.Despawn();
                hailPopup = null;
            }
            HideHailPanel();
            ship.HailUI = null;
        }

        private void NPCEndCall() {
            if (hailPopup != null) {
                hailPopup.Despawn();
                hailPopup = null;
            }
            isCallEnded = true;
            HideHailPanel();
        }

        public void HailPlayer(ShipHailAction action) {
            D.Warn("Hailing player: {0}", action);
            if (hailPopup != null) {
                D.Err("Refusing to hail player again! Popup still up. New action: {0} | {1}",
                    action, this);
                return;
            }
            if (this.action != null && action != this.action) {
                D.Err("Refusing to hail player with a different action, already hailing: {0} | {1}",
                    action, this);
                return;
            }
            this.action = action;
            if (action is not IShipHailAction_N2P n2p) {
                D.Err("Can't HailPlayer if ShipHailAction doesn't implement IShipHailAction_N2P: {0}: {1}",
                    action.Id, action);
                return;
            }

            IsWaitingForPlayerPickUp = true;
            this.n2p = n2p;
            n2p.N2P_OnHailPlayer(this, ship);

            var timeout = ship.PlayerHailTimeoutTicks;
            HailTimeout = G.Ticks + timeout;
            HailStart = G.Ticks;
            S.BB.Set(BBKeyH.CommunicatorNeedsOperator, HailTimeout + 10);

            if (!S.Sys.Ship.HasCommunicator()) {
                autoRespondWhenOperatorAvailable = true;
                var hp = WPopup.Create(false, IconId.CCallOngoing,
                    Texts.Red(T.CommunicatorMissing),
                    T.ShipHailingYou_V1.F(ship.NameLinked),
                    T.CommunicatorMissingInfo);
                hp.WithAction($"{GlyphMap.SpriteCallDrop} {T.Ignore}", () => {
                    hp.OnTimeoutAct = null;
                    OnHailPlayerReject();
                }).WithTimeout(ship.PlayerHailTimeoutTicks, () => {
                    hailPopup = null;
                    n2p.N2P_OnPlayerChoiceTimeout(ship);
                }, OnTickNoComms);
                hp.Spawn(center: true);
                hailPopup = hp;
                S.Sig.OnNPCShipHailPlayer.Send(this, n2p, false);
                return;
            }
            ship.Memory.LastN2PInteractionTick = G.Ticks;

            var data = WPopup.Create(false, IconId.CCallOngoing,
                T.IncomingTransmission,
                T.ShipHailingYou_V1.F(ship.NameLinked))
                    .WithAction($"{GlyphMap.SpriteCallPickUp} {T.VerbRespond}", OnHailPlayerAccept)
                    .WithAction2($"{GlyphMap.SpriteCallDrop} {T.VerbIgnore}", OnHailPlayerReject);
            if (data != null) {
                hailPopup = data;
                data.WithValidateAction(() => CheckComms(data))
                    .WithTimeout(ship.PlayerHailTimeoutTicks, () => {
                        autoRespondWhenOperatorAvailable = false;
                        S.BB.Set(BBKeyH.CommunicatorNeedsOperator, G.Ticks + 10);
                        // n2p.N2P_OnPlayerChoiceTimeout(ship);
                        OnHailPlayerReject();
                    }, () => OnTickHailTimeout(data));
                data.Spawn(true);
            } else {
                D.Err("Failed to spawn UIPopupChoiceWidget for hail action: {0}: {1}",
                    action.Id, action);
            }
            S.Sig.OnNPCShipHailPlayer.Send(this, n2p, true);
        }

        private void OnTickNoComms() {
            if (!S.Sys.Ship.HasCommunicator()) { return; }
            if (!hailPopup.IsSpawned) { return; }
            RespawnWidget();
        }

        private void OnTickHailTimeout(WPopupData widget) {
            if (!S.Sys.Ship.HasCommunicator()) {
                if (hailPopup != null && hailPopup.IsSpawned) {
                    RespawnWidget();
                    return;
                }
            }

            if (ship.IsDerelict || ship.IsInHyperspace || A.IsInHyperspace) {
                if (widget != hailPopup && hailPopup != null) {
                    if (widget.IsSpawned) {
                        D.Err("Hail timeout called on wrong widget: {0} != {1}",
                            widget, hailPopup);
                        widget.Despawn();
                    }
                }
                NPCEndCall();
                return;
            }
            if (autoRespondWhenOperatorAvailable && HasCommsOperator()) {
                if (widget.IsSpawned && IsWaitingForPlayerPickUp) {
                    widget.Widget.Button1.Click();
                }
            }
        }

        private void RespawnWidget() {
            if (hailPopup != null) {
                hailPopup.Despawn();
                hailPopup = null;
            }
            HailPlayer(action);
            hailPopup.Widget.UpdateTimeout(HailStart, HailTimeout);
        }

        private void SetButtonToWaiting(WPopupData widget) {
            if (widget?.Widget == null) { return; }
            widget.Widget.Button1.SetText($"{GlyphMap.SpriteCallPickUp} {Texts.Green(T.Waiting)}");
            widget.Widget.SetCountdownSilent(true);
        }

        private bool CheckComms(WPopupData widget) {
            if (!HasCommsOperator()) {
                SetButtonToWaiting(widget);
                autoRespondWhenOperatorAvailable = true;
                S.Sig.ShowErrorMessage.Send(T.WaitingForOperator);
                return false;
            }
            return true;
        }

        private void OnHailPlayerAccept() {
            hailPopup = null;
            HailTimeout = 0L;
            HailStart = 0L;
            if (!S.Sys.Ship.HasCommunicator()) {
                S.Sig.ShowErrorMessage.Send($"{T.OperationFailed}: {T.CommunicatorMissing}");
                autoRespondWhenOperatorAvailable = true;
                return;
            }
            IsWaitingForPlayerPickUp = false;
            n2p.N2P_OnHailPlayer(this, ship);
            ShowHailPanel();
        }

        private void OnHailPlayerReject() {
            if (hailPopup != null) {
                hailPopup.OnTimeoutAct = null;
            }
            hailPopup = null;
            HailTimeout = 0L;
            HailStart = 0L;
            autoRespondWhenOperatorAvailable = false;
            IsWaitingForPlayerPickUp = false;
            IsWaitingForPlayerChoice = false;
            isCallEnded = true;
            PlayCommSound(ShipChatResponseType.Negative);
            n2p?.N2P_OnPlayerChoiceTimeout(ship);
            DoPlayCommSound();
            // We haven't displayed the hail ui yet, we will want to unset it
            // from the ship
            ship.HailUI = null;
        }

        private void PostEndCall() {
            responseMessage = null;
            if (hailEvent.IsValid) {
                Notification.Remove(ref hailEvent);
            }
            if (IsWaitingForPlayerPickUp || IsWaitingForPlayerChoice) {
                OnHailPlayerReject();
            } else {
                PlayCommSound(ShipChatResponseType.End);
            }
            DoPlayCommSound();
            if (isSubscribed) {
                isSubscribed = false;
                S.Clock.OnTick.RemoveListener(OnTickAnsweredCall);
            }
            ship.HailUI = null;
        }

        private void OnTickAnsweredCall(long ticks) {
            if (ship.IsDerelict || ship.IsInHyperspace || ship.Sector.SO != A.Player.Parent ||
                    A.Player.Ship.IsInHyperspace || !S.Sys.Ship.HasCommunicator()) {
                isCallEnded = true;
                NPCEndCall();
                if (ship.IsActive && !ship.IsDerelict && !ship.Class.IsSpaceStation
                        && !ship.IsInHyperspace) {
                    ship.ChangeBehavior(NPCShipBehaviorScope.Sector, false);
                }
                return;
            }

            var hasComms = HasCommsOperator();
            if (hadCommsOp != hasComms) {
                if (hasComms) {
                    if (!IsWaitingForPlayerPickUp) {
                        PlayCommSound(ShipChatResponseType.Neutral);
                        DoPlayCommSound();
                        ShowHailPanel();
                    }
                }
            }
            hadCommsOp = hasComms;
            HailOperator();
        }

        public override string ToString() {
            return $"ShipHailActionUI[Act:{action},CallEnded:{isCallEnded},WaitingChoice:{IsWaitingForPlayerChoice},Showing:{IsShowingHailDialog},Ship:{ship}]";
        }
    }
}
