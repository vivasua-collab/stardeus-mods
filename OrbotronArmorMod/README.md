# Orbotron Armor Test Mod

Test scaffold for **Stardeus 0.15.11**. The mod is intentionally JSON-only: no C#, Harmony patches or direct changes to Core files (Core JSON is modified only through JSON patches).

## Included

- Adds the `OrbotronArmor` inventory slot to the Orbotron species with a JSON patch. The existing `Weapon` slot is preserved.
- Adds two craftable armor items with a shield preview icon in the equipment UI.
- Adds custom armor sprites: a world sprite shown for the dropped/held item and four-directional worn sprites (Down/Left/Up/Right) rendered on the Orbotron through a `Clothing` look part added with a JSON patch.
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

## Visuals

Both items use the existing `Icons/Color/Shield` preview icon for the equipment UI (`PreviewIcon` takes priority over custom sprites there).

Custom sprites live under `Graphics/` (10 PNG, 128×144, currently generated draft art — replaceable later without any config changes as long as paths stay the same):

| Files | Sprite id | Used as |
|---|---|---|
| `Obj/Equipment/Orbotron/OrbotronArmor01.png`, `…Armor02.png` | `Obj/Equipment/Orbotron/OrbotronArmor01` / `…02` | World sprite (`ObjGraphics.Graphic`, `Scale 1.2`) |
| `Beings/Clothing/Orbotron/OrbotronArmor01/[N]Armor01_{D,L,U,R}.png`, `…Armor02/…` | `Beings/Clothing/Orbotron/OrbotronArmor01/[BODY_TYPE]Armor01` / `…02` | Worn sprite on the Orbotron (`SpriteId` in the item configs) |

Worn rendering is wired by adding `"LookPart": "Clothing"` to the `OrbotronArmor` slot type and appending a `Clothing` part to the Orbotron's species looks (`Patches/AddOrbotronArmorLooks.json`). The part mirrors the Orbotron `Body` part (`DroneHover` position filter, `RenderSorted`, same offset, `MobilizerAffected`) so the armor sits over the chassis and sorts with it, with `Scale 2` compensating the smaller 128×144 canvas relative to the vanilla 256×256 body sprites.

Scale values are assumptions derived from vanilla sprite measurements (see ORBOTRON-ARMOR-GFX-02-report.md); they may need a one-number adjustment after the first in-game look. No `_H` horizontal variants are needed — the Orbotron looks definition has no horizontal sprite set.

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
- [ ] The world sprite renders for the armor item when dropped or carried, at a sensible size.
- [ ] Worn armor renders over the Orbotron chassis in all four facings (Down/Left/Up/Right), sorted with the body layer.
- [ ] No "missing sprite" errors appear in the log while an Orbotron wearing armor walks.
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
