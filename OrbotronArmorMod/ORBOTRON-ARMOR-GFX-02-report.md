# ORBOTRON-ARMOR-GFX-02 — Отчёт: интеграция сгенерированной графики брони

**Основание:** принятый отчёт [ORBOTRON-ARMOR-GFX-01-report.md](ORBOTRON-ARMOR-GFX-01-report.md) (PR #5, `6709ece` в main).
**Версия игры:** Stardeus 0.15.11b. **Ветка:** `orbotron-armor-gfx-02` (коммиты поверх `main`; merge — после ревью и игрового теста).
**Ассеты:** `OrbotronArmorGfxAssets.zip` — 10 PNG, канвас 128×144, RGBA (сгенерированные черновики; README архива прямо требует проверить scale/placement/sorting/four-facing в игре перед релизом).

## 1. Изменённые файлы

| Файл | Изменение |
|---|---|
| `Graphics/Obj/Equipment/Orbotron/OrbotronArmor01.png`, `…Armor02.png` | **Новые** — мировые спрайты предметов |
| `Graphics/Beings/Clothing/Orbotron/OrbotronArmor01/[N]Armor01_{D,L,U,R}.png`, `…OrbotronArmor02/[N]Armor02_{D,L,U,R}.png` | **Новые** — надетая броня, 4 направления (порядок из ТЗ D/L/U/R) |
| `Definitions/Obj/Equipment/Orbotron/OrbotronArmor01.json`, `…02.json` | `ObjGraphics.Graphic`: временный `Obj/BodyParts/Robotic` → `Obj/Equipment/Orbotron/OrbotronArmor01` / `…02`; добавлен `{ "Key": "Scale", "Float": 1.2 }` |
| `Config/Inventory/Items/Orbotron/OrbotronArmor01.json`, `…02.json` | Добавлен `"SpriteId": "Beings/Clothing/Orbotron/OrbotronArmor01/[BODY_TYPE]Armor01"` (и `…Armor02/[BODY_TYPE]Armor02`) |
| `Config/Inventory/SlotTypes/OrbotronArmor.json` | Добавлен `"LookPart": "Clothing"` |
| `Patches/AddOrbotronArmorLooks.json` | **Новый** — JSON-патч `Config/Species/Looks/Orbotron.json`: `add` `/PartDefs/-` → Clothing-часть |
| `README.md` | Переписан раздел Visual scope → Visuals (было «no custom sprite…» — устарело), чек-лист дополнен графическими пунктами |

Не тронуто: статистика/рецепты/исследования/спавн, `PreviewIcon: Icons/Color/Shield` (сохранён — UI читает его в первую очередь), Core-файлы (только через патч), структура слота.

## 2. Соответствие sprite id ↔ PNG

Загрузка графики: `ModLoader.PreloadGraphics` сканирует `Graphics/` рекурсивно; id = путь относительно `Graphics/` без расширения. Мод не объявляет атласы → `PreloadOneTexture` кладёт всё в `Default` атлас (fallback, ModLoader.cs:1867–1873) — конфиг атласа не нужен и не добавлялся.

| id (в конфигах) | Файл |
|---|---|
| `Obj/Equipment/Orbotron/OrbotronArmor01` | `Graphics/Obj/Equipment/Orbotron/OrbotronArmor01.png` |
| `Obj/Equipment/Orbotron/OrbotronArmor02` | `Graphics/Obj/Equipment/Orbotron/OrbotronArmor02.png` |
| `Beings/Clothing/Orbotron/OrbotronArmor01/[BODY_TYPE]Armor01` + `_D/_L/_U/_R` | `Graphics/Beings/Clothing/Orbotron/OrbotronArmor01/[N]Armor01_{D,L,U,R}.png` |
| `Beings/Clothing/Orbotron/OrbotronArmor02/[BODY_TYPE]Armor02` + `_D/_L/_U/_R` | `Graphics/Beings/Clothing/Orbotron/OrbotronArmor02/[N]Armor02_{D,L,U,R}.png` |

`[BODY_TYPE]` подставляется движком (`SpriteForBodyType`) в char bodyType; у Орботрона это всегда `'N'` (в `Config/Species/Types/Orbotron.json` нет `Sexes` → `RandomSex` null → `'N'`), т.е. шаблон резолвится в `[N]Armor01`. Ваниль использует тот же механизм с подпапкой (`…/CombatSuit01/[BODY_TYPE]/[BODY_TYPE]CombatSuit01`); наш лейаут без подпапки — файлы лежат прямо в `OrbotronArmor01/`, шаблон без сегмента каталога. Если движок когда-нибудь даст Орботрону другой bodyType, сработает фолбэк `'N'` (SpriteForBodyType) — а при его отсутствии `GetFacingSprite` выдаёт прозрачный спрайт с soft-логом; в текущей ванили других типов у Орботрона нет.

Направления: D/L/U/R — явные файлы; `_H`-варианты **не нужны**: ванильные горизонтальные спрайты строятся только для видов с горизонтальным набором (`RebuildSprites` вызывает `BuildSpriteSetFor(facing,false)` только при `LooksDef.HasHorizontal` — «id заканчивается на `_H``; в Core это только Cat/Crawler/Dog/Human/Pig). У Орботрона горизонтального набора нет. `_1`-альтеркадры тоже не запрашиваются: `ChangeAnimationFrame` вызывается только при `Motion.IsAnimated`, который ставится лишь при `def.IsAnimated` — в `Config/Species/Looks/` есть только у `Crawler.json`; у Орботрона нет (ванильные тела/одежда `_1`-файлов также не содержат).

## 3. Как работает надетая броня (LookPart-интеграция)

Цепочка рендера:

1. `InventoryItemSlot.UpdateLooks` (InventoryItemSlot.cs:311–330): если у слот-типа задан `LookPart` и в слоте предмет — копирует в look-часть существа `part.SpriteId = BeingLooks.SpriteForBodyType(bodyType, Item.SpriteId)`. Без `LookPart` у слот-типа (наш прежний вариант) рендер обрезается на первом шаге — потому раньше мод не влиял на тело.
2. `SlotTypes/OrbotronArmor.json` теперь имеет `"LookPart": "Clothing"` → ссылается на `LookPartTypes/Clothing` (Core): `SortOrder: BeingClothing` (слой сразу над телом -20/-19, `SpriteOrder.cs`), `Gen: None` (спрайт берётся целиком из предмета, без генерации внешности), `HideInBed: true`.
3. Существо рендерит часть только если вид (`SpeciesLooks`) объявляет её в `PartDefs` → патч `AddOrbotronArmorLooks.json` добавляет Clothing-часть в `Config/Species/Looks/Orbotron.json` (файл Core, правится только патчем — механика та же, что у рабочего `AddOrbotronArmorSlot.json`).
4. `BeingLooksComp.BuildSpriteSetFor` создаёт набор спрайтов с `GetFacingSprite` → `[N]Armor01` + `_D/L/U/R` по направлению существа; `Rescale` применяет `part.Scale`.

Значения новой части — зеркало Body-части Орботрона (`DroneHover`, `RenderSorted: true`, `Offset {0, 0.3}`, `MobilizerAffected: true`, `MobilizerAltPosFilter: ""`), кроме `Scale: 2` и `PartType: Clothing`. Человеческий прецедент подтверждает правило: у Human `Clothing`-часть имеет тот же Offset, что и его Body (оба `{0,0}`). Одинаковый Offset совмещает канвасы частей — позиционирование делает сам арт внутри канваса.

## 4. Масштаб и смещение (допущения, проверить в игре)

Замеры фактического арта (альфа-границы, скриптом по A>10):

| Спрайт | канвас | art px | art в мире при Scale (пиксельная модель: размер = px×Scale/256) |
|---|---|---|---|
| Human `[M]Body01_D` | 256×256 | 105×131 | 0.41×0.51 @1 |
| Human `[M]CombatSuit01_D` (worn) | 256×256 | 112×138 | 0.44×0.54 @1 → **1.07×1.05 от тела** |
| Orbotron `[N]Body01_D` | 256×256 | 188×214 | 0.73×0.84 @1 |
| Наша `[N]Armor01_D` (worn) | 128×144 | 110×109 | @2 → 0.86×0.85 → **1.18×0.98 от тела Орботрона** |
| Наш мир `OrbotronArmor01.png` | 128×144 | 110×115 | @1.2 → 0.52×0.54 (ванильный костюм на земле: 0.45×0.59 @0.6) |

**Выбрано: мир Scale 1.2, worn part.Scale 2, Offset {0, 0.3}.**

Обоснование: модель «мир-размер ∝ пиксели канваса» (подтверждена астероидами 768×768 @Scale 1 — многотайловые; при нормализации канваса до 256 они были бы в один тайл). Ванильная одежда на теле ≈ 1.05–1.07 от арта тела; Scale 2 даёт 0.98–1.18 от шасси Орботрона — броня накрывает корпус как ванильный костюм накрывает тело. Мир Scale 1.2 компенсирует вдвое меньший канвас → эквивалент ванильных 0.6.

**Это допущения, не факты.** PPU `Default`-атласа статически не верифицируем (RenderingService/AtlasBuilder скомпилированы; кэш `Cache/*.dat` бинарный). Если в игре броня окажется вдвое крупнее ожидаемого (значит, движок нормализует канвас) — уменьшить оба значения вдвое: мир 1.2→0.6, worn 2→1. Смещение арта внутри канвасов почти выровнено (центры X 49.6% vs 49.8%; Y-центр арта брони 52.8% канваса против 45.1% у тела → броня на ~0.09 юнита ниже центра тела @Scale 2 — в пределах настройки, при необходимости корректируется Offset части).

## 5. Выполненные проверки (статические)

- Все 6 изменённых/новых JSON парсятся (PowerShell `ConvertFrom-Json`); структура патча: `target: Config/Species/Looks/Orbotron.json`, `op: add`, `path: /PartDefs/-`, `value.PartType: Clothing`, `Scale: 2`.
- Все 10 id графики резолвятся в существующие PNG (скрипт-проверка, `missing: 0`), включая подстановку `[BODY_TYPE]`→`[N]` и суффиксы направлений.
- `PreviewIcon` не тронут; статистика/рецепты/исследования/слот-поведение не менялись (в JSON-диффе только Graphic/Scale/SpriteId/LookPart/патч-часть).
- Патч-механизм применим к `Config/Species/Looks` — `LoadObjects → DeserializeObjects → LoadPatchedJson` (ModLoader.cs:1222–1241) пропускает через патчер все загружаемые пути; формат доказан рабочим `AddOrbotronArmorSlot.json` из этого же мода.
- Слияние наследования: `Scale` добавлен в собственный ObjGraphics дефа (у `BaseEquipment` Scale нет — конфликта нет; Shader/SelectableScale родителя сохраняются — проверено в GFX-01).

## 6. Факты / выводы / предположения

**Факты (из исходников/ванили):** id = путь без расширения; Default-атлас без конфига; Graphic обязателен и без него — Game Breaking Warning (причина GFX-01); worn-рендер требует и `LookPart` у слота, и части в `PartDefs` вида; `[BODY_TYPE]`→'N' для Орботрона; 4 направления резолвятся из D/L/U/R-файлов; `_H` и `_1` Орботрону не запрашиваются; Clothing = слой BeingClothing, Gen None, HideInBed.

**Выводы (моя интерпретация данных):** выбранная пара Scale (1.2 / 2) даёт ванильные пропорции покрытия; Offset Clothing = Offset Body — правило совмещения канвасов (подтверждено Human-прецедентом).

**Предположения (проверить в игре):** PPU Default-атласа = 256 и отсутствие нормализации канваса (иначе — вдвое меньшие Scale); позиция арта брони на шасси по высоте; видимость сортировки относительно body-слоя; поведение `HideInBed` и `DroneHover`-фильтра для Орботрона.

## 7. Ручной тест-план (9 шагов, по ТЗ)

1. Мод включается, загрузка без JSON/патч-ошибок и без «Game Breaking Bug» (GFX-01-фикс сохраняется).
2. В консоли/крафте создать OrbotronArmor01 и OrbotronArmor02 — в мире предметы рисуются новыми спрайтами (не робо-деталью), размер ≈ ванильного костюма.
3. Иконка в UI — прежний щит (PreviewIcon).
4. Экипировать броню 01 на Орботрона — на шасси появляется надетая броня; проверить 4 направления (поворот орбитальной камеры): D/L/U/R рисуются, зеркалятся корректно, слой над телом.
5. Сменить на 02 — рисуется второй вариант.
6. Снять/надеть — рендер появляется/исчезает синхронно со слотом; в кровати скрыт (HideInBed).
7. Прогнать Орботрона ходьбой/задачами — в логе нет «Panic: Not found sprite»/missing-sprite ошибок (alt-кадры не запрашиваются).
8. Старое сохранение с экипированной бронёй (до GFX-02): загрузка, рендер появляется без пере-экипировки (UpdateLooks пересобирается при загрузке).
9. Сравнить масштаб с ванильным Орботроном: броня не торчать за габариты шасси и не выглядеть наклейкой; при отклонении — скорректировать Scale (см. §4) и сообщить значения.

Пункты, требующие моего сравнения по скриншотам (масштаб/позиция/слои) — после игрового теста пришлите скрины; при отклонениях правка сводится к одному числу в дефе и/или в патче.

## 8. Статус

- Ветка `orbotron-armor-gfx-02` поверх main `6709ece`; merge в main не выполнялся (по ограничениям).
- Runtime-валидация не проводилась (нет запуска игры) — всё выше помечено соответственно.
- Арт — сгенерированные черновики; замена на финальный арт не потребует правок конфигов при сохранении путей/размера канваса.
