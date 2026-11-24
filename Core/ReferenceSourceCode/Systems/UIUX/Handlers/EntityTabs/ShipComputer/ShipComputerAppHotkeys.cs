using System;
using System.Collections.Generic;
using Game.Data;
using Game.Input;
using Game.UI;

namespace Game.Systems.UIUX {
    // Every Ship Computer app without an ActionType shortcut gets a named binding, listed
    // under Shortcuts in the controls settings, so any app can be given a key. Named
    // bindings need no ActionType entries and no BindingsVersion bump (which would wipe
    // every player's custom bindings), and apps added by mods get one too. A binding only
    // works while its app is visible and available: a minigame's key stops working when
    // its cartridge is removed.
    public static class ShipComputerAppHotkeys {
        public const string UIGroup = "shortcuts";

        public static string BindingId(ShipComputerApp app) {
            return $"app.{app.Id}";
        }

        // Runs after mods are loaded and before the saved overrides are applied
        public static void Bind(Bindings bindings) {
            var apps = new List<ShipComputerApp>();
            foreach (var app in Registries.ShipComputerApps.All.Values) {
                if (app.Shortcut == ActionType.None) {
                    apps.Add(app);
                }
            }
            // The registry is a dictionary, settings rows need a stable order
            apps.Sort((a, b) => string.Compare(a.Id, b.Id, StringComparison.Ordinal));
            foreach (var app in apps) {
                var bindingId = BindingId(app);
                if (bindings.ById.ContainsKey(bindingId)) {
                    continue;
                }
                var owner = app;
                var binding = MultiBinding.CreateNamed(bindingId, () => owner.Name)
                    .InLayer(InputLayer.World)
                    .WithUIGroup(UIGroup)
                    .WithAvailability(() => owner.IsVisible && owner.IsAvailable)
                    .WithHandler(() => {
                        owner.OnClick();
                        UISounds.PlayButtonClick();
                    });
                if (app.HotkeyDefault != null) {
                    binding.Bind(app.HotkeyDefault);
                }
                bindings.Add(binding);
            }
        }

        public static string GlyphFor(ShipComputerApp app) {
            if (!The.Bindings.ById.TryGetValue(BindingId(app), out var binding)) {
                return null;
            }
            return The.Bindings.GetGlyphFor(binding);
        }
    }
}
