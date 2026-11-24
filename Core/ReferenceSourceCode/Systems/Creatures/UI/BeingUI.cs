using System;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using KL.Utils;
using Game.Input;
using System.Linq;
using Game.Systems.UIUX;
using UnityEngine;
using System.Text;
using Game.CodeGen;
using Game.Systems.Mental;
using Game.Systems.Stats;

namespace Game.Systems.Creatures {
    public sealed class BeingUI {
        private readonly BeingData data;
        private readonly GameState S;
        public BeingUI(BeingData data) {
            S = A.S;
            this.data = data;
        }
        /// <summary>
        /// For starting config
        /// </summary>
        public bool HighlightName;
        private ColorChoice colorChoice;

        public string GetFullBeingTooltip() {
            var sb = Caches.StringBuilder3;
            sb.Clear();
            var n = data.Mind.Name.FullNameOrNameless;
            sb.Append(n);
            sb.Append(GlyphMap.BRHR);
            if (!data.IsPlayer && !data.EffectiveFaction.IsInternal) {
                sb.Append(Texts.Accented(T.NounFaction));
                sb.Append(": ");
                sb.Append(Texts.Red(data.EffectiveFaction.NameT));
                sb.Append(GlyphMap.BRHR);
            }
            if (data.Being == null) {
                // must be something, is it stored, on expedition, etc?
                if (data.HasStateFlag(BeingState.Expedition)) {
                    sb.Append(Texts.Accented(T.OnExpedition));
                    sb.Append(GlyphMap.BRHR);
                } else if (data.HasStateFlag(BeingState.Stored)) {
                    sb.Append(Texts.Accented(T.ObjectInStorage));
                    sb.Append(GlyphMap.BRHR);
                }
            } else {
                if (data.HasStateFlag(BeingState.Dead)) {
                    sb.Append(Texts.Red(T.Dead));
                    sb.Append(GlyphMap.BRHR);
                } else if (data.Body.IsIncapacitated) {
                    sb.Append(Texts.Red(T.BeingStateIncapacitated));
                    sb.Append(GlyphMap.BRHR);
                } else if (data.Being.Motion.IsImmobilized) {
                    sb.Append(Texts.Red(T.InfoBeingImmobilized));
                    sb.Append(GlyphMap.BRHR);
                }
            }
            if (data.Being != null && data.HasStateFlag(BeingState.Alive) && data.IsPlayer) {
                if (data.Mind.Health != null) {
                    data.Mind.Health.Comp.AddTooltip(sb);
                    sb.Append(GlyphMap.BRHR);
                }
            }
            AddWeaponsAndArmorInfo(sb, data);
            return sb.ToString();
        }

        public static void AddWeaponsAndArmorInfo(StringBuilder sb, BeingData data) {
            string w;
            if (data.Inventory?.Equipment.TryGetValue(
                    InventorySlotIdH.Weapon, out var weapon) == true) {
                w = weapon.Item?.ItemName ?? Texts.RedMinus;
            } else {
                w = Texts.RedMinus;
            }
            sb.AppendFormat("{0}: <b>{1}</b>{2}", T.EquipmentSlotWeapon, w, GlyphMap.BRHR);

            var s = data.StatsBeing.ArmorBlunt;
            sb.AppendFormat("{0}: <b>{1}</b>\n", Stat.NameFor(s), s.Display());

            s = data.StatsBeing.ArmorSharp;
            sb.AppendFormat("{0}: <b>{1}</b>\n", Stat.NameFor(s), s.Display());

            s = data.StatsBeing.ArmorEnergy;
            sb.AppendFormat("{0}: <b>{1}</b>\n", Stat.NameFor(s), s.Display());

            s = data.StatsBeing.ArmorPiercing;
            sb.AppendFormat("{0}: <b>{1}</b>", Stat.NameFor(s), s.Display());
        }

        public void AfterRename() {
            data.Mind.Name.UpdateName();
            data.Being?.Name.SetName(data.Mind.Name.ShortName);
            data.UpdateAvatarName();
            S.Sig.BeingNameChanged.Send(data);
        }

        private void ChangeFavColor(bool setup) {
            if (!setup && !A.InstantBuild) {
                UISounds.PlayActionDenied();
                return;
            }

            if (colorChoice == null) {
                colorChoice = ColorChoice.ForGeneric(S).WithAction((cdef) => {
                    data.Mind.FavColorId = cdef.Id;
                });
            }
            var favCol = Palette.Get(data.Mind.FavColorId);
            // colorChoice.WithSelectedColor(favCol);
            colorChoice.Show(null);
        }

        private string BodyAgeText() {
            return Mind.AgeText(data.Body.Age);
        }
        private string MindAgeText() {
            return Mind.AgeText(data.Mind.Age);
        }

        public void ShowRenameCtx(RectTransform widget, Action afterRename) {
            if (data.IsPlayer) {
                var n = data.Mind.Name;
                string newFirst = n.FirstName;
                string newNick = n.Nickname;
                string newLast = n.LastName;

                // auto focus
                bool f1 = !string.IsNullOrEmpty(newFirst);
                bool f2 = !f1 && !string.IsNullOrEmpty(newNick);
                bool f3 = !f1 && !f2 && !string.IsNullOrEmpty(newLast);

                var choice = QuickChoice.Create();
                choice.Widget = widget;
                Func<bool> confirm = () => OnNameChangeConfirm(newFirst, newNick, newLast, afterRename);
                Action<string> submit = (v) => {
                    if (!confirm()) {
                        UISounds.PlayActionDenied();
                    }
                };
                var in1 = new QuickChoice.Input() {
                    AutoFocus = f1,
                    Value = data.Mind.Name.FirstName,
                    OnChange = (v) => newFirst = v,
                    OnSubmit = submit,
                };
                choice.AddInput(T.FirstName, in1);

                var in2 = new QuickChoice.Input() {
                    AutoFocus = f2,
                    Value = data.Mind.Name.Nickname,
                    OnChange = (v) => newNick = v,
                    OnSubmit = submit,
                };
                choice.AddInput(T.Nickname, in2);

                var in3 = new QuickChoice.Input() {
                    AutoFocus = f3,
                    Value = data.Mind.Name.LastName,
                    OnChange = (v) => newLast = v,
                    OnSubmit = submit,
                };
                choice.AddInput(T.LastName, in3);

                var btn = new QuickChoice.Button() {
                    OnClickValidate = confirm,
                    AutoClose = true,
                };
                choice.AddButton(T.Confirm, btn);
                S.UIQuery.ShowQuickChoice.Ask(choice);
            } else {
                UISounds.PlayActionDenied();
            }
        }

        private bool OnNameChangeConfirm(string f, string n, string l, Action afterRename) {
            if (string.IsNullOrWhiteSpace(f) && string.IsNullOrWhiteSpace(n)
                    && string.IsNullOrWhiteSpace(l)) {
                The.SysSig.ShowTooltipWithTimeout.Send(Texts.Red(T.EnterName), true, 1f);
                return false;
            }
            var name = data.Mind.Name;
            name.FirstName = f;
            name.LastName = l;
            name.Nickname = n;
            AfterRename();
            afterRename?.Invoke();
            S.UIQuery.CancelQuickChoice.Ask();
            return true;
        }

        public void ResetBio() {
            cachedBio = null;
            cachedBioShort = null;
        }

        private string cachedBio;
        private string cachedBioShort;

        // Human Female, 37y (Mind: Human Male, 26y)
        // Attracted to: Male, Female
        // Monk, Homeless, Unemployed,
        // Fast Metabolism, Short Sleeper, Carnivore
        public string GetBio(bool forStarting, bool shortVersion) {
            if (!shortVersion && cachedBio != null && !forStarting) {
                #if !DEBUG_UDB
                return cachedBio;
                #endif
            }
            if (shortVersion && cachedBioShort != null) {
                #if !DEBUG_UDB
                return cachedBioShort;
                #endif
            }
            var sb = Caches.StringBuilder4;
            sb.Clear();

            var mind = data.Mind;

            var ageText = BodyAgeText();
            bool mindMatches = mind.MatchesBody(out bool ageOnly);
            var spName = data.Species.Def.NameT;
            if (!mindMatches && ageOnly) {
                var mindAge = Texts.GreyParentheses(
                    $"{T.NounMind}: {MindAgeText()}");
                ageText = $"{ageText} {mindAge}";
            }
            AppendPersona(sb, mind, spName, data.Body.Sex, ageText);
            if (!mindMatches && !ageOnly) {
                // need to check if mind age and sex is the same as body age / sex
                string mindBio;
                bool ageMatches = Mind.AgeYears(mind.Age) == Mind.AgeYears(data.Body.Age);
                bool sexMatches = data.Body.Sex?.TypeChar == mind.Sex?.TypeChar;
                if ((ageMatches && sexMatches) || (ageMatches && mind.Sex == null)) {
                    mindBio = mind.Species.Def.CodexLink;
                } else if (ageMatches && mind.Sex != null) {
                    // sex doesn't match
                    mindBio = $"{mind.Species.Def.CodexLink}, {mind.Sex.NameT}";
                } else if (sexMatches) {
                    // age doesn't match
                    var mindAge = MindAgeText();
                    mindBio = $"{mind.Species.Def.CodexLink}, {mindAge}";
                } else {
                    // nothing matches
                    var mindAge = MindAgeText();
                    if (mind.Sex != null) {
                        mindBio = T.PersonaBio_V3.F(mind.Species.Def.CodexLink,
                            data.Mind.Sex.NameT, mindAge);
                    } else {
                        mindBio = T.PersonaBioSexless_V2.F(
                            mind.Species.Def.CodexLink, mindAge);
                    }
                }
                sb.AppendFormat(" ({0}: {1})", T.NounMind, mindBio);
            }
            AppendOrientationAndBackground(sb, mind, data.Body.MaxLibido, shortVersion, forStarting);
            if (forStarting) {
                return sb.ToString();
            }
            if (shortVersion) {
                cachedBioShort = sb.ToString();
                return cachedBioShort;
            } else {
                cachedBio = sb.ToString();
                return cachedBio;
            }
        }

        /// <summary>
        /// Bio for a mind with no BeingData (e.g. brain/core removed from the body).
        /// Same shape as GetBio, minus the body-vs-mind mismatch clause and death info.
        /// </summary>
        public static string GetMindBio(Mind mind) {
            var sb = Caches.StringBuilder4;
            sb.Clear();
            AppendPersona(sb, mind, mind.Species?.Def?.NameT ?? T.Unknown,
                mind.Sex, Mind.AgeText(mind.Age));
            AppendOrientationAndBackground(sb, mind, null, false, false);
            return sb.ToString();
        }

        private static void AppendPersona(StringBuilder sb, Mind mind, string speciesName,
                BeingSex sex, string ageText) {
            if (!mind.Faction.IsPlayer && !mind.Faction.IsInternal) {
                sb.Append(Texts.LinkToFactionTooltip(mind.Faction));
                sb.Append(' ');
            }
            if (sex != null) {
                sb.Append(T.PersonaBio_V3.F(speciesName, sex.NameT, ageText));
            } else {
                sb.Append(T.PersonaBioSexless_V2.F(speciesName, ageText));
            }
        }

        private static void AppendOrientationAndBackground(StringBuilder sb, Mind mind,
                float? libido, bool shortVersion, bool forStarting) {
            var orientation = SexualOrientation.ResolveName(mind.Sex, mind.AttractedToSex, out var isStraight);
            if (!isStraight && orientation != null) {
                if (!Arrays.IsEmpty(mind.AttractedToSex)) {
                    var attrs = string.Join(", ", mind.AttractedToSex.Select(v => Texts.Accented(v.NameT)));
                    // Don't get fancy with colors, the tooltip will be displayed broken!
                    var tip = libido.HasValue
                        ? $"{T.NounLibido}: {Units.PercentageColored(libido.Value, clamp: false)}<br>{T.AttractedTo}: {attrs}"
                        : $"{T.AttractedTo}: {attrs}";
                    orientation = Texts.LinkToTooltip(orientation, tip);
                }
                sb.AppendFormat(", {0}", orientation);
            }
            // sb.AppendLine();
            if (!shortVersion) {
                bool hadExperience = mind.AddExperience(sb, forStarting);
                mind.AddTraits(sb, hadExperience, forStarting);
            }
        }

        private string newNameFirst;
        private string newNameLast;
        private string newNameNick;


        public void Cleanup() {
            HighlightName = false;
            colorChoice = null;
            cachedBio = null;
        }
    }
}
