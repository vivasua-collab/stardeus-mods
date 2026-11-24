using System;
using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;
using Game.Systems.Quests;
using Game.Systems.UIUX;

namespace Game.Mods {
    public static class ModRegistry {
        [SelfInit]
        private static void Clear() {
            modTypes.Clear();
        }
        private static readonly Dictionary<Type, ModInfo> modTypes = new();

        public static void MarkRuntimeBroken(ModInfo mod, string why) {
            mod.HadWarnings = true;
            mod.MarkRuntimeBroken(why);
        }

        public static void AddModType(Type t, ModInfo mod) {
            if (!modTypes.TryAdd(t, mod)) {
                D.Err("Duplicate mod Type! {0} in {1}, already exists in {2}",
                    t.AssemblyQualifiedName, mod.Id, modTypes[t].Id);
            // } else {
            //     D.Warn("Adding Type from a mod: {0} -> {1}", mod.Id, t);
            }
        }

        public static bool IsModded(Entity entity, out ModInfo mod) {
            if (entity == null || entity.Definition == null) {
                mod = null;
                return false;
            }
            if (entity.Definition.IsCoreMod) {
                mod = null;
                return false;
            }
            if (The.ModLoader.ModInfos.TryGetValue(entity.Definition.ModId, out mod)) {
                return true;
            }
            D.Err("Entity appears to be modded, but mod if not found: {0} -> {1}",
                entity, entity.Definition.ModId);
            return false;
        }

        public static bool IsModded(IComponent comp, out ModInfo mod,
                out bool isModdedComp, out bool isModdedEntity) {
            isModdedComp = false;
            isModdedEntity = false;
            if (comp == null) {
                mod = null;
                return false;
            }
            if (IsModType(comp.GetType(), out mod)) {
                isModdedComp = true;
                return true;
            }
            if (IsModded(comp.Entity, out mod)) {
                isModdedEntity = true;
                return true;
            }
            mod = null;
            return false;
        }

        public static bool IsModType(Type t, out ModInfo mod) {
            return modTypes.TryGetValue(t, out mod);
        }
        public static void OpenModURL(ModInfo mod) {
            if (mod == null) {
                WPopup.SystemWarning(T.OperationFailed, $"{T.OpenUrl}: Mod");
                return;
            }
            if (mod.SteamWorkshopId != 0) {
                #if !DISABLESTEAMWORKS
                Files.OpenOSUrl(Platform.Steam.SteamWorkshopItem.URLFor(mod));
                #endif
            } else {
                Files.OpenOSFolder(mod.ModFolder(""));
            }
        }
        public static void ShowModItemPopup(string modId) {
            if (The.ModLoader.ModInfos.TryGetValue(modId, out var mod)) {
                WPopup.Create(true, IconId.WWrench, T.ModItem, null,
                    $"{ModLoader.ModName(mod)}{GlyphMap.BRHR}{Texts.Substring(mod.Description, 255)}")
                        .WithAction(T.Ok, null)
                        .WithAction2(Texts.ExternalLink(T.Open), () => OpenModURL(mod))
                        .Spawn();
            }
        }

        public static void ShowModdedDefErrorPopup(Def def, ModInfo mod, string message) {
            var msg = $"{message}\n\nAffected Entity: <b>{def.Id}</b>\n\nMod: <b>{mod.Name} v{mod.ModVersion}</b>\nMod ID: <b>{mod.Id}</b>";
            mod.HadWarnings = true;
            D.Err(msg);
            if (!A.IsMainThread) {
                Ready.WhenMainThread(mod, "Show Error", () => {
                    WPopup.Create(true, IconId.CWarning, $"Mod Issue: {mod.Name}", null,
                        msg)
                        .WithAction(T.Ok, null)
                        .WithAction2(Texts.ExternalLink(T.Open), () => OpenModURL(mod))
                        .Spawn();
                });
            } else {
                WPopup.Create(true, IconId.CWarning, $"Mod Issue: {mod.Name}", null,
                    msg)
                    .WithAction(T.Ok, null)
                    .WithAction2(Texts.ExternalLink(T.Open), () => OpenModURL(mod))
                    .Spawn();
            }
        }
        public static void ShowModErrorPopup(ModInfo mod, string message) {
            var msg = $"{message}{GlyphMap.BRHR}Mod: <b>{mod.Name} v{mod.ModVersion}</b>\nMod ID: <b>{mod.Id}</b>";
            mod.HadWarnings = true;
            D.Err(msg);
            WPopup.Create(true, null, "Mod Issue", mod.Name, msg)
                .WithAction(T.Ok, null)
                .WithAction2(Texts.ExternalLink(T.Open), () => OpenModURL(mod))
                .Spawn();
        }

        public static void ShowModErrorPopup(Type klass, ModInfo mod, string message) {
            if (message.Length > 2000) {
                message = $"{message[..2000]}...";
            }
            var msg = $"{message}\n\nAffected C# Type: <b>{klass}</b>\n\nMod: {ModLoader.ModName(mod)}\nMod ID: <b>{mod?.Id ?? T.Unknown}</b>";
            mod.HadWarnings = true;
            D.Err(msg);
            WPopup.Create(true, null, "Mod Issue", mod.Name, msg)
                .WithAction(T.Ok, null)
                .WithAction2(Texts.ExternalLink(T.Open), () => OpenModURL(mod))
                .Spawn();
        }
        public static void ReportBrokenModSave(Type klass, ModInfo mod) {
            ShowModErrorPopup(klass, mod,
                "Mod failed while saving data. Main game save should not be affected.");
            Ready.WhenMainThread("mod", "error", () => {
                The.R?.State?.Clock?.SetSpeed(ClockSpeed.Stopped);
            });
        }
        public static void ReportBrokenSave(Type klass, string reason) {
            ShowModErrorPopup(klass, The.ModLoader.ModInfos["Core"],
                $"Failed saving data: {reason}");
            Ready.WhenMainThread("mod", "error", () => {
                The.R?.State?.Clock?.SetSpeed(ClockSpeed.Stopped);
            });
        }

        public static void ReportBrokenRepeatingModActType(Type klass, ModInfo mod, Being affectedBeing) {
            if (affectedBeing != null) {
                ShowModErrorPopup(klass, mod,
                    $"Repeating error in AI ActionExecutor. <b>{affectedBeing.Data.Mind.Name.DisplayName}</b> could get stuck due to trying to execute this task. Dropping the task.");
            } else {
                ShowModErrorPopup(klass, mod, "Repeating error in AI ActionExecutor.");
            }
        }
        public static bool WarnAboutBrokenMod(Type klass, string warning) {
            ShowModErrorPopup(klass, The.ModLoader.ModInfos["Core"], warning);
            return true;
        }
        public static bool WarnAboutBrokenMod(object target, string warning) {
            if (target == null) { return false; }
            if (IsModType(target.GetType(), out var mod)) {
                ShowModErrorPopup(target.GetType(), mod, warning);
                return true;
            }
            return false;
        }
        public static bool WarnAboutBrokenMod(Def target, string warning) {
            if (target == null) { return false; }
            // broken defs should always warn!
            // if (target.IsCoreMod) { return false; }
            if (The.ModLoader.ModInfos.TryGetValue(target.ModId, out var mod)) {
                ShowModdedDefErrorPopup(target, mod, warning);
                return true;
            }
            return false;
        }
        public static bool WarnAboutBrokenMod(Entity target, string warning) {
            if (target == null) { return false; }
            if (IsModded(target, out var mod)) {
                ShowModdedDefErrorPopup(target.Definition, mod, warning);
                return true;
            }
            return false;
        }

        public static bool WarnAboutBrokenMod(IComponent target, string warning) {
            if (target == null) { return false; }
            if (IsModded(target, out var mod, out var isMComp, out var isMEnt)) {
                if (isMComp) {
                    ShowModErrorPopup(target.GetType(), mod, warning);
                } else if (isMEnt) {
                    ShowModdedDefErrorPopup(target.Entity.Definition, mod, warning);
                } else {
                    ShowModErrorPopup(mod, warning);
                }
                return true;
            }
            return false;
        }
    }
}
