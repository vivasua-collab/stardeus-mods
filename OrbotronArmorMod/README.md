# Orbotron Armor Test Mod

Test scaffold for **Stardeus 0.15.11**. The mod is intentionally JSON-only: no C#, Harmony patches, body-overlay graphics or changes to Core files.

## Included

- Adds the `OrbotronArmor` inventory slot to the Orbotron species with a JSON patch. The existing `Weapon` slot is preserved.
- Adds two craftable armor items with a shield preview icon in the equipment UI.
- Adds a two-step Robotics research branch after `Research/Robotics/Orbotron`.
- Makes both recipes available to a Fabricator through the existing `Robotics3` craftable type.
- Gives both items positive rarity and late-game spawn requirements so Orbotrons may appear equipped in more difficult situations.

## Initial test balance

The item modifiers intentionally use half of the corresponding human armor modifiers found in the current Core snapshot:

| Modifier | Basic Orbotron armor | Advanced Orbotron armor |
|---|---:|---:|
| ArmorSharp | 4.5 | 6.5 |
| ArmorPiercing | 4.5 | 6.0 |
| ArmorBlunt | 4.5 | 6.5 |
| ArmorEnergy | 6.0 | 7.0 |
| WeakPointCover | 0.10 | 0.175 |
| CombatStrengthBonus | 1.5 | 2.0 |

The Orbotron's built-in body armor remains in place and stacks with these modifiers. These values are implementation targets from the requested half-strength rule, not in-game balance results.

Recipe material counts, production times, research costs, rarity and spawn thresholds are **provisional test values**. They should be tuned after the first in-game pass. The basic recipe uses common high-tech robotic manufacturing materials; the advanced recipe adds higher-end components.

## Research progression

```text
Research/Robotics/Orbotron (Core, Tier 5)
  └─ Research/Robotics/OrbotronArmor (mod, Tier 5)
       └─ Research/Robotics/OrbotronArmorAdvanced (mod, Tier 6)
```

The advanced node uses the next existing research tier; this does not introduce a new global tier. As with any added research node, the Tier 5 node count changes the percentage-based gate for Tier 6 slightly.

## Visual scope

The inventory slot has no `LookPart`, so it does not alter the Orbotron's body rendering. The item definitions use the existing `Icons/Color/Shield` preview icon, which is what the equipment UI reads for the equipped item. No custom sprite or body overlay is included. A dedicated ground-world sprite may be added later if needed.

## Install and test

1. Copy `OrbotronArmorMod` into the game's user `Mods` directory (not `StreamingAssets/Mods`).
2. Enable **Orbotron Armor Test** under Main Menu → Mods → Installed Mods.
3. Restart the game.
4. Test with a new save or a copy of an existing save.

### Test checklist

- [ ] Mod loads with no JSON or patch errors.
- [ ] A newly spawned Orbotron has both Weapon and Orbotron Armor slots.
- [ ] The empty custom slot appears in Equipment.
- [ ] The item preview icon appears after equipping armor.
- [ ] Armor modifiers are added on equip and removed on unequip.
- [ ] The basic research becomes available after Orbotron research.
- [ ] The advanced research becomes available after the basic armor research.
- [ ] Both recipes appear at a Fabricator after their associated research.
- [ ] Orbotrons can automatically equip accessible armor.
- [ ] Enemy Orbotron gear generation is observed in late-game / high-difficulty situations.
- [ ] An old save loads and existing Weapon equipment is preserved.

The first old-save load may log a slot-count diagnostic because existing Orbotrons were saved before the custom slot existed. This compatibility path must be checked in-game; it has not been runtime-tested here.

## Validation notes

This repository contains the Core snapshot used for source inspection, but not the local game installation, Player.log or the author's workspace tools. The JSON files can be syntax-checked here; final schema, patch-simulation and runtime validation must be run against the user's installed 0.15.11 build.

The upstream [Stardeus Modding with AI workspace](https://github.com/kodolinija/stardeus-modding-with-ai) documents `tools/stardeus validate OrbotronArmorMod`, local game snapshots, patch simulation and Player.log inspection. Its README currently says it was last checked against 0.15.0-rc329, so the installed 0.15.11 Core files remain authoritative.

## Source references

- [Stardeus Modding with AI](https://github.com/kodolinija/stardeus-modding-with-ai)
- [Stardeus Mod Template](https://github.com/kodolinija/stardeus-mod-template)
- [Stardeus Mod Examples](https://github.com/kodolinija/stardeus-mod-examples)
