using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems.Creatures;
using Game.Systems.Drama;
using Game.Systems.Mental;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class WRelationshipPanel : WBase, ICloseableWidget {
        [SerializeField] private WButton btnClose;
        [SerializeField] private WButtonText btnShowInteractions;
        [SerializeField] private WEntityHeader headerA;
        [SerializeField] private WEntityHeader headerB;
        [SerializeField] private WText txtInteractions;
        [SerializeField] private WGroup contentA;
        [SerializeField] private WGroup contentB;

        [ClearOnReload]
        private static Lifetime life;
        /// <summary>
        /// Interactions lifetime
        /// </summary>
        [ClearOnReload]
        private static Lifetime iLife;

        private const int PageSize = 100;

        public static void SpawnFor(BeingOpinion a, BeingOpinion b) {
            if (life != null) {
                life.Dispose();
            }
            life = Lifetime.Create("WRelationshipPanel");
            var w = UISpawn.Widget<WRelationshipPanel>(life, UIPoolRoot.PopupContainer);
            w.Rect.SetAsLastSibling();
            w.Load(a, b);
        }

        private void Load(BeingOpinion a, BeingOpinion b) {
            btnClose.Load(Close);
            LoadHeader(headerA, a.OwnerMind);
            LoadHeader(headerB, b.OwnerMind);
            LoadOpinion(contentA, a, b, true);
            LoadOpinion(contentB, b, a, false);
            btnShowInteractions.Load(T.ViewSocialInteractions,
                () => ShowInteractionsFor(life, a.OwnerMind, b.OwnerMind));
            RebuildLayout();
            UIUtils.AfterShowPopup(Rect, Screen.width > 1400, randShift: false);
        }

        private void LoadOpinion(WGroup group, BeingOpinion op, BeingOpinion op2, bool isFirst) {
            int idx = 0;
            var other = op2.OwnerMind;
            var header = UISpawn.Widget<WText>(life,
                UIPrefabId.WContentHeader, group.Container, idx++);
            header.Load(T.OpinionAbout_V1.F(other.Name.ShortName));


            // Social affinity - hidden unless in sandbox mode
            WIconText2Progress wAff = null;
            WIconText2Progress wAttr = null;
            if (A.InstantBuild) {
                wAff = UISpawn.Widget<WIconText2Progress>(life,
                    UIPrefabId.WContentProgress, group.Container, idx++);
                wAff.Load(IconId.CScale, T.NounSocialAffinity, 0f, null);
                UIUtils.AddTip(life, wAff, T.NounSocialAffinityDesc);

                // Attraction
                if (op.Love > 0f || op.SexualAttraction > 0f) {
                    wAttr = UISpawn.Widget<WIconText2Progress>(life,
                        UIPrefabId.WContentProgress, group.Container, idx++);
                    wAttr.Load(IconId.CRelationships, T.NounAttraction, 0f, null);
                }
            }

            // Friendship
            bool areFriends = op.AreFriendsWith(op2);
            if (areFriends) {
                var w = UISpawn.Widget<WIconText>(life, UIPrefabId.WContentText,
                    group.Container, idx++);
                w.Load(IconId.CThumbsUp, T.StatusFriends);
            }

            // Friendship development
            WIconText2Progress wFr = null;
            if (areFriends || op.Friendship > 0.05f) {
                var name = areFriends ? T.StrengthFriendship : T.DevelopingFriendship;
                wFr = UISpawn.Widget<WIconText2Progress>(life, UIPrefabId.WContentProgress,
                    group.Container, idx++);
                wFr.Load(IconId.CScale, name, 0f, null);
            }

            // Nemesis
            bool isNemesis = op.Flags.HasFlag(BeingOpinionFlags.Nemesis);
            if (isNemesis) {
                var w = UISpawn.Widget<WIconText>(life, UIPrefabId.WContentText,
                    group.Container, idx++);
                w.Load(IconId.CSkull, T.StatusNemesis);
            }

            // Hate development
            WIconText2Progress wHate = null;
            if (isNemesis || op.Hate > 0.05f) {
                var name = isNemesis ? T.StrengthHate : T.DevelopingHate;
                wHate = UISpawn.Widget<WIconText2Progress>(life, UIPrefabId.WContentProgress,
                    group.Container, idx++);
                wHate.Load(IconId.CFist, name, 0f, null);
            }

            // Lovers / Crush
            bool areLovers = op.AreLoversWith(op2);
            bool hasCrush = op.Flags.HasFlag(BeingOpinionFlags.Crush);
            if (areLovers || hasCrush) {
                var w = UISpawn.Widget<WIconText>(life, UIPrefabId.WContentText,
                    group.Container, idx++);
                w.Load(IconId.CHeart, areLovers ? T.StatusLovers : T.StatusCrush);
            }

            // Love development
            WIconText2Progress wLove = null;
            if (areLovers || op.Love > 0.05f) {
                var name = areLovers ? T.StrengthLove : T.DevelopingLove;
                wLove = UISpawn.Widget<WIconText2Progress>(life, UIPrefabId.WContentProgress,
                    group.Container, idx++);
                wLove.Load(IconId.CHeart, name, 0f, null);
            }

            // Married
            bool areMarried = op.AreMarried(op2);
            if (areMarried) {
                var w = UISpawn.Widget<WIconText>(life, UIPrefabId.WContentText,
                    group.Container, idx++);
                w.Load(IconId.CRings, T.StatusSpouse);
            }

            // Master / Pet
            if (op.OwnerMind.Species.IsHumanoid && other.Species.IsPet) {
                bool isMasterOf = op.Flags.HasFlag(BeingOpinionFlags.Pet);
                if (isMasterOf) {
                    var w = UISpawn.Widget<WIconText>(life, UIPrefabId.WContentText,
                        group.Container, idx++);
                    w.Load(IconId.CChainLink, T.FlagPet);
                }
            } else if (op.OwnerMind.Species.IsPet && other.Species.IsHumanoid) {
                bool isPetOf = op.Flags.HasFlag(BeingOpinionFlags.Master);
                if (isPetOf) {
                    var w = UISpawn.Widget<WIconText>(life, UIPrefabId.WContentText,
                        group.Container, idx++);
                    w.Load(IconId.CChainLink, T.StatusMaster);
                }
            }

            if (idx < 2) { // only header
                var w = UISpawn.Widget<WIconText>(life, UIPrefabId.WContentText,
                    group.Container, idx++);
                w.Load(IconId.CMoodNeutral, T.NoOpinion);
            }

            op.Changed.Bind(life, () => {
                // Social affinity
                if (wAff != null) {
                    wAff.SetPercent(op.SocialAffinity, false);
                    wAff.Item.SetText2(Units.PercentageColoredSigned(op.SocialAffinity));
                }
                if (wAttr != null) {
                    wAttr.SetPercent(op.SexualAttraction, false);
                    wAttr.Item.SetText2(Units.PercentageColored(op.SexualAttraction));
                }

                // Friendship
                if (wFr != null) {
                    wFr.SetPercent(op.Friendship, false);
                    var str = Texts.RelationshipStrength(op.Friendship, areFriends);
                    wFr.Item.SetText2(str);
                }

                if (wHate != null) {
                    wHate.SetPercent(op.Hate, false);
                    wHate.Item.SetText2(Texts.RelationshipStrength(op.Hate, isNemesis));
                }

                if (wLove != null) {
                    wLove.SetPercent(op.Love, false);
                    wLove.Item.SetText2(Texts.RelationshipStrength(op.Love, areLovers));
                }

                // Both sides bind this, so always describe the pair from the first side
                var first = isFirst ? op : op2;
                var second = isFirst ? op2 : op;
                LoadInteractions(first, second);
            });
        }

        // A pair that never met (a sleeper that was never awake) has nothing to report,
        // and "Spent 0s together, with 0 positive and 0 negative" is noise
        private void LoadInteractions(BeingOpinion a, BeingOpinion b) {
            bool hasHistory = HasHistory(a) || HasHistory(b);
            txtInteractions.gameObject.SetActiveFast(hasHistory);
            if (!hasHistory) { return; }
            txtInteractions.Load(
                T.BeingSharedInteractions_V3.F(
                    Texts.Accented(Units.TicksToTime(a.TicksInProximity, true)),
                    Texts.Bold(Texts.Green(a.GoodInteractions)),
                    Texts.Bold(Texts.Red(a.BadInteractions))));
        }

        private static bool HasHistory(BeingOpinion op) {
            return op.TicksInProximity > 0 || op.GoodInteractions > 0 || op.BadInteractions > 0;
        }

        private void LoadHeader(WEntityHeader h, Mind mind) {
            var b = mind.Owner;
            A.S.Query.GetMindAvatar.Ask(mind)?.Load(h.Image);
            if (mind.Name.DisplayName == null) {
                mind.Name.UpdateName();
            }
            h.SetText(mind.Name.DisplayName);
            h.SetText2(b != null ? b.UI.GetBio(false, false) : BeingUI.GetMindBio(mind));
            // former body (brain/core removed) holds a blank mind now - only good for focus
            var focusTarget = b ?? (mind.FormerOwnerId != 0
                ? A.S.Query.FindBeingData.Ask(mind.FormerOwnerId) : null);
            h.SetOnClick(() => FocusOnBeing(focusTarget));
            Coroutines.OnNextFrame(() => {
                h.AutoResize();
            });
            h.SetFactionIcon(b?.EffectiveFaction ?? mind.Faction);
        }

        private void FocusOnBeing(BeingData b) {
            if (b == null) { return; }
            if (b.Being != null) {
                EntityUtils.SelectOrFocus(b.Being);
            } else if (b.Mind?.IsBlank == false) {
                // Off the map (in stasis, on an expedition): there is nothing to focus on,
                // but the profile can still be shown. A blank mind is a former body, which
                // has no profile worth opening.
                BeingUtils.Select(b, false);
            }
        }

        public bool TryCloseWidget() {
            if (!gameObject.activeSelf) {
                return false;
            }
            Close();
            return true;
        }
        public static void ShowInteractionsFor(Lifetime life, Mind a, Mind b) {
            if (iLife != null) {
                iLife.Dispose();
            }
            iLife = life.CreateChild("Being.Interactions");
            var choiceWidget = UISpawn.Widget<WPanel>(
                    iLife, UIPrefabId.WListChoicePopup,
                    UIPoolRoot.PopupContainer, -1);
            choiceWidget.ShowFooter(false);
            var winTitle = a.Name.ShortName;
            if (b == null) {
                winTitle = $"{T.NounSocialInteractions}: {winTitle}";
            } else {
                winTitle = $"{winTitle} {Texts.Grey("×")} {b.Name.ShortName}";
            }
            choiceWidget.Load(winTitle, iLife.Dispose);
            UIUtils.AfterShowPopup(choiceWidget.Rect);

            var block = UISpawn.Block(iLife, choiceWidget.Content);

            var (interactions, newInteractionAdded) = A.S.UIQuery.GetSocialInteractions.Ask();
            WIconText emptyRow = null;
            var popupLife = iLife;
            newInteractionAdded.Bind(iLife, (soc) => {
                if (b == null) {
                    if (!soc.InvolvedMinds.Contains(a)) {
                        return;
                    }
                } else {
                    if (!soc.InvolvedMinds.Contains(a) || !soc.InvolvedMinds.Contains(b)) {
                        return;
                    }
                }
                if (soc.Icon == null || soc.Title == null) { return; }
                // Added on top, in place: rebuilding the whole popup moved it to the mouse,
                // reset its scroll and dropped the pages already loaded with "+N"
                if (emptyRow != null) {
                    emptyRow.Dispose();
                    emptyRow = null;
                }
                SpawnInteractionItem(popupLife, block.Container, soc, 0);
            }, false);

            var filtered = new List<SocialInteraction>();
            for (int i = interactions.Count - 1; i >= 0; i--) {
                SocialInteraction soc = interactions[i];
                if (b == null) {
                    if (!soc.InvolvedMinds.Contains(a)) { continue; }
                } else {
                    if (!soc.InvolvedMinds.Contains(a) || !soc.InvolvedMinds.Contains(b)) { continue; }
                }
                if (soc.Icon == null || soc.Title == null) { continue; }
                filtered.Add(soc);
            }

            int idx = 0;
            if (filtered.Count == 0) {
                var w = UISpawn.Widget<WIconText>(iLife,
                    UIPrefabId.WContentText, block.Container, idx++);
                w.Load(IconId.CInfo, T.Empty);
                emptyRow = w;
            } else {
                int loadCount = Math.Min(PageSize, filtered.Count);
                for (int i = 0; i < loadCount; i++) {
                    SpawnInteractionItem(iLife, block.Container, filtered[i], idx++);
                }
                if (filtered.Count > PageSize) {
                    var scroll = block.Container.GetComponentInParent<ScrollRect>();
                    var btn = UISpawn.Widget<WButtonText>(iLife,
                        UIPrefabId.WContent2ButtonSimple, block.Container, idx);
                    LoadMoreInteractions(btn, iLife, block.Container, filtered, loadCount, scroll);
                }
            }
        }

        private static void SpawnInteractionItem(Lifetime life, RectTransform container,
                SocialInteraction soc, int siblingIdx) {
            var w = UISpawn.Widget<WIconText2>(life, UIPrefabId.WContentText2, container, siblingIdx);
            w.Load(soc.Icon, soc.Title);
            A.S.Clock.TickB.Bind(life, (ticks) => {
                var time = Units.TicksAgo((int)(ticks - soc.CreatedAt));
                w.SetText2(Texts.Grey(time));
            });
            UIUtils.AddTipFunc(life, w, () => {
                var sb = Caches.StringBuilder;
                sb.Clear();
                sb.Append(Texts.SubTitle(soc.Title));
                soc.AppendTooltip(sb, false);
                return sb.ToString();
            });
        }

        private static void LoadMoreInteractions(WButtonText btn, Lifetime life,
                RectTransform container, List<SocialInteraction> entries,
                int loadedCount, ScrollRect scroll) {
            int remaining = entries.Count - loadedCount;
            btn.Load(Texts.Italic($"+{remaining}"), () => {
                int sibIdx = btn.transform.GetSiblingIndex();
                int batch = Math.Min(PageSize, remaining);
                for (int i = 0; i < batch; i++) {
                    SpawnInteractionItem(life, container, entries[loadedCount + i], sibIdx + i);
                }
                int newLoaded = loadedCount + batch;
                if (newLoaded < entries.Count) {
                    btn.transform.SetSiblingIndex(sibIdx + batch);
                    LoadMoreInteractions(btn, life, container, entries, newLoaded, scroll);
                } else {
                    btn.gameObject.SetActiveFast(false);
                }
                if (scroll != null) {
                    UIUtils.RebuildScroll(scroll, false);
                }
            });
        }

        private void Close() {
            life?.Dispose();
            life = null;
        }
    }
}
