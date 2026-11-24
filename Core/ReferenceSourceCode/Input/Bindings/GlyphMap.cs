using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Utils;
using KL.Collections;
using KL.Utils;
using UnityEngine.InputSystem;

namespace Game.Input {
    public class GlyphMap {
        // available extra characters:
        // ←↑→↓↔↕↖↗↘↙
        // ◆◇◈◉◎●▲△▼▽▶▷◀◁◢◣◤◥
        public const string BulletUnchecked = "<size=75%>◎</size>";
        public const string BulletChecked = "<size=75%>◉</size>";
        public const string BulletPoint = "<size=75%>●</size>";
        public const string ArrowRight = "<size=75%>→</size>";
        public const string ArrowRightLarge = "<size=150%>→</size>";
        public const string ArrowLeft = "<size=75%>←</size>";
        public const string ArrowUp = "<size=75%>↑</size>";
        public const string ArrowUp2 = "<size=75%>↑↑</size>";
        public const string ArrowUp3 = "<size=75%>↑↑↑</size>";
        public const string ArrowDown = "<size=75%>↓</size>";
        public const string ArrowDown2 = "<size=75%>↓↓</size>";
        public const string ArrowDown3 = "<size=75%>↓↓↓</size>";
        public const string TriangleRight = "▶"; //"<size=75%>▶</size>";
        public const string TriangleLeft = "<size=75%>◀</size>";
        public const string TriangleUp = "<size=75%>▲</size>";
        public const string TriangleDown = "<size=75%>▼</size>";
        public const string HollowTriangleRight = "<size=75%>▷</size>";
        public const string HollowTriangleLeft = "<size=75%>◁</size>";
        public const string HollowTriangleUp = "<size=75%>△</size>";
        public const string HollowTriangleDown = "<size=75%>▽</size>";
        // private static string hr;
        // public static string HR => hr ??= Sprite("HR", "hr");
        // private static string brhr;
        [Obsolete("This looks bad, never use it")]
        public static string BRHR = "\n\n"; //=> brhr ??= $"\n{HR}\n";
        public static string SpriteUp1 = Sprite("IconsColor", "Up1");
        public static string SpriteUp2 = Sprite("IconsColor", "Up2");
        public static string SpriteUp3 = Sprite("IconsColor", "Up3");
        public static string SpriteDown1 = Sprite("IconsColor", "Down1");
        public static string SpriteDown2 = Sprite("IconsColor", "Down2");
        public static string SpriteDown3 = Sprite("IconsColor", "Down3");
        public static string SpriteHealth = Sprite("IconsColor", "Health");
        public static string SpriteSleeping = Sprite("IconsColor", "Sleeping");
        public static string SpriteAnimalPaw = Sprite("IconsColor", "AnimalPaw");
        public static string SpriteFaceHappy = Sprite("IconsColor", "Face_Happy");
        public static string SpriteFaceNormal = Sprite("IconsColor", "Face_Normal");
        public static string SpriteFaceSad = Sprite("IconsColor", "Face_Sad");
        public static string SpriteCross = Sprite("IconsColor", "Cross");
        public static string SpriteTattered = Sprite("IconsColor", "Tattered");
        public static string SpriteRecycle = Sprite("IconsColor", "Recycle");
        public static string SpriteSparkles = Sprite("IconsColor", "Sparkles");
        public static string SpriteSkull = Sprite("IconsColor", "Skull");
        public static string SpritePlusThin = Sprite("IconsColor", "PlusThin");
        public static string SpriteCrutch = Sprite("IconsColor", "Crutch");
        public static string SpriteBlood = Sprite("IconsColor", "Blood");
        public static string SpriteCombat = Sprite("IconsColor", "Combat");
        public static string SpriteFist = Sprite("IconsColor", "Fist");
        public static string SpritePoison = Sprite("IconsColor", "Poison");
        public static string SpriteBookmark = Sprite("IconsColor", "Bookmark");
        public static string SpritePoliceBadge = Sprite("IconsColor", "PoliceBadge");
        public static string SpriteCallOngoing = Sprite("IconsColor", "CallOngoing");
        public static string SpriteCallPickUp = Sprite("IconsColor", "CallPickUp");
        public static string SpriteCallDrop = Sprite("IconsColor", "CallDrop");
        public static string SpriteAttackTarget = Sprite("IconsColor", "AttackTarget");
        public static string SpriteWarning = Sprite("IconsColor", "Warning");
        public static string SpriteQuest = Sprite("IconsColor", "Quest");
        public static string SpriteQuestSide = Sprite("IconsColor", "QuestSide");
        public static string SpriteThumbsUp = Sprite("IconsColor", "ThumbsUp");
        public static string SpriteThumbsDown = Sprite("IconsColor", "ThumbsDown");
        public static string SpriteCheck = Sprite("IconsColor", "Check");
        public static string SpriteClock = Sprite("IconsColor", "Clock");
        public static string SteamIcon = Sprite("Social", "Steam");
        public static string DiscordIcon = Sprite("Social", "Discord");
        public static string XIcon = Sprite("Social", "X_White");
        public static string ExternalLink = Sprite("Social", "ExternalLink");
        public static string SpriteCheckNotDoneClock = Sprite("IconsColor", "CheckNotDoneClock");
        public static string SpriteCheckNotDone = Sprite("IconsColor", "CheckNotDone");
        public static string SpriteCheckDone = Sprite("IconsColor", "CheckDone");
        public static string SpriteCheckFail = Sprite("IconsColor", "CheckFail");

        public static string KB(string key) {
            if (keyboardRemap.TryGetValue(key, out var remap)) {
                return Sprite("Keyboard", remap);
            }
            return Sprite("Keyboard", key);
        }
        private static readonly Dictionary<string, string> keyboardPredefined = new() {
            // Example
            { "keyName", "[KeyName]" },
        };

        private static readonly Dictionary<string, string> predefined = new() {
            { "<Keyboard>/rightCtrl", Sprite("Keyboard", "leftCtrl") },
            { "<Keyboard>/rightAlt", Sprite("Keyboard", "leftAlt") },
            { "<Keyboard>/rightShift", Sprite("Keyboard", "leftShift") },
            { "<Keyboard>/ctrl", Sprite("Keyboard", "leftCtrl") },
            { "<Keyboard>/shift", Sprite("Keyboard", "leftShift") },
            { "<Keyboard>/alt", Sprite("Keyboard", "leftAlt") },
            // Surrogate dpad
            { "dpad", null },
            // Surrogate axis
            { "axis", null},
        };
        private static readonly Dictionary<string, string> keyboardRemap = new() {
            { "rightCtrl", "leftCtrl" },
            { "rightAlt", "leftAlt" },
            { "rightShift", "leftShift" },
            { "ctrl", "leftCtrl" },
            { "shift", "leftShift" },
            { "alt", "leftAlt" },
            { "equals", "plus" },
            #if UNITY_STANDALONE_OSX
            { "rightMeta", "metaOSX" },
            { "leftMeta", "metaOSX" },
            #else
            { "rightMeta", "meta" },
            { "leftMeta", "meta" },
            #endif
            { "numpad0", "0" },
            { "numpad1", "1" },
            { "numpad2", "2" },
            { "numpad3", "3" },
            { "numpad4", "4" },
            { "numpad5", "5" },
            { "numpad6", "6" },
            { "numpad7", "7" },
            { "numpad8", "8" },
            { "numpad9", "9" },
            { "numpadMinus", "minus" },
        };
        private readonly Dictionary<string, string> keyboardMappingCache = new();
        private readonly Dictionary<string, string> mouseMappingCache = new();
        private readonly Dictionary<string, string> gamepadMappingCache = new();
        private string lastGamepadFlavor;
        private readonly string mouseFailGlyph = Sprite("Keyboard", "mouse");

        public string GetGlyph(string mapping, out string failGlyph, string gamepadFlavor="Xbox") {
            failGlyph = null;
            if (predefined.TryGetValue(mapping, out var pre)) {
                return pre;
            }
            if (mapping.StartsWith("<G")) {
                return GetGamepadGlyph(mapping, gamepadFlavor);
            } else if (mapping.StartsWith("<M")) {
                return GetMouseGlyph(mapping, out failGlyph);
            } else if (mapping.StartsWith("<K")) {
                return GetKeyboardGlyph(mapping);
            } else {
                D.Err("Unhandled mapping: {0}", mapping);
                return null;
            }
        }

        public string GlyphForIcon(string iconId) {
            // D.Warn("Getting glyph icon for iconId: {0}", iconId);
            // TODO implement, if there is no glyph available, return warning
            return SpriteWarning;
        }

        public string GetMouseGlyph(string mapping, out string failGlyph) {
            failGlyph = null;
            if (mouseMappingCache.TryGetValue(mapping, out var glyph)) {
                if (glyph == null) {
                    failGlyph = mouseFailGlyph;
                }
                return glyph;
            }
            var m = mapping;
            if (m.StartsWith("<")) {
                m = mapping.Replace("<Mouse>/", string.Empty);
            }
            glyph = m switch {
                "leftButton" => Sprite("Keyboard", "mouse0"),
                "middleButton" => Sprite("Keyboard", "mouse1"),
                "rightButton" => Sprite("Keyboard", "mouse2"),
                "scroll/y" => $"{Sprite("Keyboard", "mouse1")}{T.VerbScroll}",
                "y" => Sprite("Keyboard", "mouse1"),
                _ => null,
            };
            mouseMappingCache.Add(mapping, glyph);
            if (glyph == null) {
                // D.Warn("No mouse glyph for: {0}", mapping);
                failGlyph = mouseFailGlyph;
            }
            return glyph;
        }

        public string GetControlGlyph(InputAction ia, InputControl ctrl,
                out string deviceName, out string controlName) {
            var dev = ctrl.device;
            string b = null;
            deviceName = dev.displayName;
            var bIdx = ia.GetBindingIndexForControl(ctrl);
            if (bIdx < 0) {
                D.Err("Incorrect binding index for input act {0} -> control: {1}", ia, ctrl);
            }
            controlName = ia.GetBindingDisplayString(bIdx);
            if (dev is Keyboard) {
                b = GetKeyboardGlyph(ctrl.name, false);
            } else if (dev is Mouse) {
                var glyph = GetMouseGlyph(ctrl.name, out var fail);
                b = glyph ?? $"{fail} {controlName}";
            } else if (dev is Gamepad g) {
                var flavor = VirtualMouseInput.GamepadFlavor;
                b = GetGamepadGlyph(ctrl.name, flavor, false, ctrl: ctrl);
            }
            return b ?? controlName;
        }

        public static string Sprite(string asset, string name) {
            return $"<sprite=\"Glyphs_{asset}\" name=\"{name}\">";
        }

        public string GetKeyboardGlyph(string mapping, bool replacePrefix = true) {
            var layout = Keyboard.current?.keyboardLayout;
            if (layout != lastKeyboardLayout) {
                lastKeyboardLayout = layout;
                keyboardMappingCache.Clear();
            }
            if (!keyboardMappingCache.TryGetValue(mapping, out var sprite)) {
                var m = replacePrefix ? mapping.Replace("<Keyboard>/", "") : mapping;

                // For keys like pause and scroll lock
                if (keyboardPredefined.TryGetValue(m, out var spr)) {
                    keyboardMappingCache.Add(mapping, spr);
                    return spr;
                }

                if (keyboardRemap.TryGetValue(m, out var remapping)) {
                    m = remapping;
                }
                m = LayoutKeyName(m);
                sprite = Sprite("Keyboard", m);
                keyboardMappingCache.Add(mapping, sprite);
            }
            return sprite;
        }

        private string lastKeyboardLayout;

        // Keyboard binding paths name physical keys by their US position: "<Keyboard>/q" is
        // the key labelled A on an AZERTY keyboard. That is why WASD still "works" there (it
        // lands on ZQSD), but the glyph printed Q for a key the player sees as A. Letters and
        // digits are shown by what the current layout prints on that key instead.
        private static string LayoutKeyName(string physicalName) {
            // Letters only: Enum.TryParse would read "1" as a numeric Key value, and the glyph
            // atlas only has the Latin alphabet, so a Cyrillic key name keeps the US glyph
            if (physicalName == null || physicalName.Length != 1
                    || !IsAsciiLetter(physicalName[0])) {
                return physicalName;
            }
            var kb = Keyboard.current;
            if (kb == null) { return physicalName; }
            if (!Enum.TryParse<Key>(physicalName, true, out var key)) { return physicalName; }
            var display = kb[key]?.displayName;
            if (display == null || display.Length != 1 || !IsAsciiLetter(display[0])) {
                return physicalName;
            }
            return char.ToLowerInvariant(display[0]).ToString();
        }

        private static bool IsAsciiLetter(char c) {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
        }

        public string GetGamepadGlyph(string mapping, string flavor="Xbox",
                bool replacePath = true, InputControl ctrl = null) {
            if (flavor != lastGamepadFlavor) {
                lastGamepadFlavor = flavor;
                gamepadMappingCache.Clear();
            }
            if (!gamepadMappingCache.TryGetValue(mapping, out var sprite)) {
                string suffix = null;
                if (mapping == "left" || mapping == "right" || mapping == "up" || mapping == "down") {
                    replacePath = false;
                    suffix = Sprite("IconsWhite", $"Arrow_{mapping}");
                    if (ctrl != null) {
                        var parts = ctrl.path.Split('/');
                        if (parts.Length > 2) {
                            mapping = parts[parts.Length - 2];
                        } else {
                            D.Err("Parts length unexpected: {0}", ctrl.path);
                            // FIXME: guess
                            mapping = "leftStick";
                        }
                    } else {
                        // FIXME: guess
                        D.Err("No controls but trying to get gampad glyph for {0}", mapping);
                        mapping = "leftStick";
                    }
                }
                var m = replacePath ? mapping.Replace("<Gamepad>/", "") : mapping;
                if (m.Contains("/")) {
                    if (m.EndsWith("/left")) {
                        m = m.Replace("/left", string.Empty);
                        suffix = Sprite("IconsWhite", "Arrow_left");
                    } else if (m.EndsWith("/right")) {
                        m = m.Replace("/right", string.Empty);
                        suffix = Sprite("IconsWhite", "Arrow_right");
                    } else if (m.EndsWith("/up")) {
                        m = m.Replace("/up", string.Empty);
                        suffix = Sprite("IconsWhite", "Arrow_up");
                    } else if (m.EndsWith("/down")) {
                        m = m.Replace("/down", string.Empty);
                        suffix = Sprite("IconsWhite", "Arrow_down");
                    }
                }
                sprite = Sprite(flavor, m);
                if (!string.IsNullOrEmpty(suffix)) {
                    sprite += suffix;
                }

                gamepadMappingCache[mapping] = sprite;
            }
            return sprite;
        }
    }
}
