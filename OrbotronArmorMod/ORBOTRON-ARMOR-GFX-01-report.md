# ORBOTRON-ARMOR-GFX-01 — Отчёт: устранение ошибки ObjGraphics у брони Orbotron

**Игра:** Stardeus 0.15.11b. **Ветка:** `orbotron-armor-gfx-01` (1 коммит поверх `main` = `1bb5710`; merge/PR — после аудита).
**Изменения:** 2 файла JSON, +12 строк. Баланс, рецепты, исследования, слот — не тронуты.

## 1. Причина исходной ошибки

Диалог в игре:

```text
Possible Game Breaking Bug
Component config for ObjGraphics missing String key: Graphic.
All: Array<SerializableProperty>(Size: 2)[Shader: ObjFlat (String), SelectableScale: 0.7 (Float)]
```

Цепочка (все шаги подтверждены исходниками 0.15.11b):

1. При создании сущности предмета (крафт, спавн в мире, загрузка сейва) компоненты проходят `OnConfig()`.
2. `ObjGraphicsComp.OnConfig()` читает `graphicId = Config.GetString(PropertyIdH.Graphic)` — **без значения по умолчанию** (`Core/ReferenceSourceCode/Components/ObjGraphicsComp.cs:59`).
3. Ключа `Graphic` в конфиге нет → `ComponentConfig.NotifyMissingKey` (`Core/ReferenceSourceCode/Components/Base/ComponentConfig.cs:390-406`) показывает `WPopup.SystemWarning(T.Error, "Possible Game Breaking Bug", msg)` и пишет `D.Err`. Текст из попапа совпадает с форматом строки 398–401 дословно.
4. Конфиг без `Graphic` приходит от абстрактного шаблона `Core/Definitions/Obj/Equipment/BaseEquipment.json`: его `ObjGraphics` содержит только `Shader: ObjFlat` и `SelectableScale: 0.7` — дамп в ошибке совпадает точно.

Почему ошибка не была видна в UI-иконках: `Def.Preview`/`PreviewSprite` проверяют **`PreviewIcon` первым** и при его наличии до `ObjGraphics` не доходят (`Core/ReferenceSourceCode/Data/Def.cs:1060-1063, 1109-1112`). У брони задан `PreviewIcon: "Icons/Color/Shield"`, поэтому все потребители `Def.Preview` (иконка слота `InventoryItemSlot.SlotIcon` → `Item.Def.Preview`, `Systems/Items/InventoryItemSlot.cs:30`; очередь диспоузера `DisposerTab.cs:101` → `def?.Preview`) работали нормально. Ошибку давал только путь создания **мировой сущности** предмета, который читает `Graphic` напрямую из конфига компонента.

## 2. Исследованные исходники и конфигурации

| Файл | Что установлено |
|---|---|
| `Core/ReferenceSourceCode/Components/ObjGraphicsComp.cs` | `OnConfig` (строка 59) — единственное обязательное чтение `Graphic` без дефолта; `BuildAnimatedSpriteSet` (241–249) ищет спрайт через `RenderingService.Sprites.Find(gfxId)`; `IconFor` (347–349) — только для превью |
| `Core/ReferenceSourceCode/Components/Base/ComponentConfig.cs` | `GetString(key)` без дефолта всегда вызывает `NotifyMissingKey`; `Has(key)`; `MergeWith` (53–92, 112–114) — слияние свойств, значение потомка перебивает родителя; `CheckUsage` — предупреждение о свойстве, объявленном в JSON, но не читаемом в `OnConfig` |
| `Core/ReferenceSourceCode/Data/SerializablePropety.cs:50-68` | `MergeWith(ovrd)`: значения `ovrd` (потомка) побеждают |
| `Core/ReferenceSourceCode/Data/Def.cs:953-1139` | `PreviewIcon` → `Preview`/`PreviewSprite` до компонентного fallback; ObjGraphics-case вызывает `ObjGraphicsComp.IconFor` |
| `Core/ReferenceSourceCode/Mods/ModLoader.cs:2321-2407` | Наследование `ParentId` = `parent.Copy()` + одноимённые компоненты **сливаются по свойствам** (`cc.MergeWith(comp)`), новые компоненты добавляются в конец; `PreviewIcon` потомка перебивает родительский |
| `Core/ReferenceSourceCode/Systems/Items/InventoryItemSlot.cs:270-330` | Надетый предмет: `UpdateLooks` — если у слота нет `LookPart` (`Type == None`), рендер на теле не выполняется; иначе `LookPart.SpriteId = BeingLooks.SpriteForBodyType(bodyType, Item.SpriteId)` |
| `Core/ReferenceSourceCode/Systems/Items/InventoryItemDef.cs` | `SpriteId` (строка 39) — поле **инвентарного** конфига предмета (спрайт на теле носителя), отдельная система от `ObjGraphics.Graphic` |
| `Core/ReferenceSourceCode/Systems/Creatures/BeingLooks.cs:36-59` | `SpriteForBodyType` — подстановка `[BODY_TYPE]` в спрайт-наборы одежды; `HasSprite` через `RenderingService.Sprites.TryFind` |
| `Core/ReferenceSourceCode/Systems/Creatures/LookPartTypeDef.cs:14-18` | Стратегия `None` — «Hat, Clothing, Weapon are equipment-driven»; типы задаются в `Config/Species/LookPartTypes` |
| `Core/ReferenceSourceCode/Game.Constants/Generated/GraphicsId.cs` | Сгенерированный реестр спрайт-id игры (фактически поставляемые ресурсы): строки 532–539 — `Obj/BodyParts/Organic/Robotic*`, 3414–3421 — `Beings/Bodies/Orbotron/...`, 249 — `Icons/Color/PreviewOrbotron01`, 295 — `Icons/Color/Shield`; 297 вхождений `Beings/Clothing/...` |
| `Core/Definitions/Obj/Equipment/BaseEquipment.json` | Абстрактный шаблон: `ObjGraphics` = `[Shader: ObjFlat, SelectableScale: 0.7]` — без `Graphic` |
| `Core/Definitions/Obj/Parts/BasePartRobotic.json`, `Obj/Parts/Body/BodyOrbotron.json`, `Obj/Parts/Arm/ServoArm.json`, `Obj/Parts/Battery/MiniCell.json` | Прецедент: робо-предметы (протезы) используют `Obj/BodyParts/Robotic*` как мировой `Graphic`; `BodyOrbotron` (часть тела Орботрона) — тот же спрайт; `Scale` не задан (по умолчанию 1.0) |
| `Core/Definitions/Obj/Equipment/Clothing/CombatSuit01.json`, `Weapons/Knife01.json` | Паттерн наследников `BaseEquipment`: дочерний деф переобъявляет `ObjGraphics` с `Graphic` (+`Scale`); `Shader` не переобъявляется |
| `Core/Config/Inventory/Items/Clothing/CombatSuit01.json` | Спрайт на теле: `SpriteId: "Beings/Clothing/Human/CombatSuit01/[BODY_TYPE]/[BODY_TYPE]CombatSuit01"` + `ColorGroupId` |
| `OrbotronArmorMod/Definitions/Obj/Equipment/Orbotron/OrbotronArmor01/02.json`, `Config/Inventory/Items/Orbotron/*.json`, `Config/Inventory/SlotTypes/OrbotronArmor.json`, `Patches/AddOrbotronArmorSlot.json` | Состояние мода: `PreviewIcon` есть; `SpriteId` нет; слот `OrbotronArmor` (`Worn: true`) **без `LookPart`**; патч добавляет слот в `Config/Species/Types/Orbotron.json` |

## 3. Найденные графические ресурсы

Существование подтверждено сгенерированным реестром `GraphicsId.cs` (константы генерируются из фактического контента игры), а не по именам файлов:

| Ресурс | Назначение |
|---|---|
| `Obj/BodyParts/Robotic` (+ `Robotic1/2/3`) | Мировые спрайты роботизированных частей тела; используются протезами-предметами (`ServoArm` → `Robotic1`, `MiniCell` → `Robotic2`) и частью тела Орботрона `BodyOrbotron` → `Robotic` |
| `Beings/Bodies/Orbotron/[N]Body01_D/L/R/U` | Тело существа Орботрона, 4 ракурса (`_D` вниз и т.д.) |
| `Beings/Bodies/Orbotron_Tracks/[N]Body01_*` | Тело Орботрона с гусеницами |
| `Beings/Shadows/Orbotron/[N]Shadow01_*` | Тень Орботрона |
| `Icons/Color/PreviewOrbotron01` | UI-иконка (используется как `PreviewIcon` дефа Орботрона) |
| `Icons/Color/Shield` | UI-иконка (используется модом как `PreviewIcon` брони); реестр иконок доступен через тот же `RenderingService.Sprites` (см. fallback `Icons/Color/Missing` в `TileGraphicsComp.cs:79,91`) |
| `Beings/Clothing/Human/<Item>/<Item>` и наборы `[M]/[F]/[N]/[BODY_TYPE]` | Одежда людей: базовый id — мировой спрайт предмета; фасетные наборы — спрайт на теле через `SpriteId` |

Специализированной графики брони Орботрона в игре **нет** — это согласуется с вводной задачи («полные комплекты специальных текстур не созданы»).

## 4. Механизм отрисовки экипировки на Orbotron

Две независимые системы:

1. **Мировой спрайт предмета** (на земле, в переноске, при разборке): `ObjGraphicsComp` → `Graphic` из `Definitions` JSON → `RenderingService.Sprites.Find`. Здесь не хватало ключа — источник попапа.
2. **Надетый предмет на теле**: `InventoryItemSlot.UpdateLooks` рисует `LookPart` только если слот-тип ссылается на `LookPart` (`Config/Inventory/SlotTypes/<Slot>.json`, у ванильных — `"LookPart": "Clothing"`), а спрайт берётся из `InventoryItemDef.SpriteId` (инвентарный конфиг) с подстановкой `[BODY_TYPE]`.

У брони мода: слот `OrbotronArmor` не имеет `LookPart`, инвентарные конфиги не задают `SpriteId` → **надетая броня сейчас не рисуется на теле вовсе** (`UpdateLooks` выходит рано). Это состояние не меняется данным исправлением: добавление `Graphic` влияет только на мировой спрайт предмета. Никакой «человеческой одежды на роботе» не возникнет — эта система вообще не задействована.

UI-иконки предмета (слот экипировки, очередь диспоузера, строки правил DE) идут через `Def.Preview` → `PreviewIcon: "Icons/Color/Shield"` — работали и до исправления.

## 5. Сравнение вариантов

| | A. Существующий ресурс Орботрона: `Obj/BodyParts/Robotic` | B. Иконка `Icons/Color/Shield` как мировой спрайт | C. Мировой спрайт человеческой брони (`Beings/Clothing/Human/CombatSuit01/CombatSuit01`) | D. Оставить без `Graphic` |
|---|---|---|---|---|
| Изменения | +1 компонент-блок (3 строки) в каждый деф | то же | то же | нет |
| Прецедент в ванилле | **Да, точный**: `BasePartRobotic`, `BodyOrbotron`, `ServoArm`, `MiniCell` — робо-предметы с этим `Graphic`; деф-наследник переобъявляет `ObjGraphics` только с `Graphic` (как `Knife01`) | Нет: ни один ванильный деф не использует `Icons/*` как мировой `Graphic` | Да (это родной мировой спрайт CombatSuit01), но для «одежды человека» | Нет: все 5 дефов без `Graphic` — только абстрактные шаблоны |
| Риски | Минимальные: тот же шейдер-пайплайн, что у всех наследников `BaseEquipment` (`Shader: ObjFlat` наследуется, как у `Knife01`) | Спрайт иконки под шейдером `ObjFlat`/масштабом 1.0 в мире не проверен ни одним прецедентом | Технически безопасно; визуально «человеческий костюм» у предмета «Orbotron Armor» — диссонанс до появления своей графики | Попап `NotifyMissingKey` **безусловен**; вариант «пустая строка» избегает недостающего ключа, но `Sprites.Find("")` и рендер пустого id не подтверждены исходниками |
| Совместимость с сейвами | Конфиги компонентов берутся из дефов, а не из сейва; предмет в сейве при загрузке пересоздаёт компоненты из обновлённого дефа | то же | то же | — |
| Результат для игрока | Предмет брони виден в мире как роботизированная деталь (как протезы); иконки UI без изменений; на теле — по-прежнему не рисуется | Предмет в мире выглядит плоской иконкой щита | Предмет в мире выглядит как человеческий боевой костюм | Ошибка остаётся при каждом крафте/загрузке |

Вывод `Sprites.Find` при отсутствии id не входит в reference-снапшот (`RenderingService` — скомпилированная сборка), поэтому варианты, опирающиеся на «несуществующий/пустой» id, отвергнуты как неподтверждённые.

## 6. Выбранное решение

**Вариант A** — в `OrbotronArmor01.json` и `OrbotronArmor02.json` добавлен компонент:

```json
{
  "Component": "ObjGraphics",
  "Properties": [
    { "Key": "Graphic", "String": "Obj/BodyParts/Robotic" }
  ]
}
```

Обоснование:

- Устраняет точную причину: ключ `Graphic` теперь присутствует в конфиге, `OnConfig` проходит без `NotifyMissingKey`.
- После слияния наследования (`ModLoader.ProcessDefsWithParents` → `cc.MergeWith`) итоговый конфиг: `Shader: ObjFlat`, `SelectableScale: 0.7`, `Graphic: Obj/BodyParts/Robotic` — свойства родителя сохраняются, параметры не дублируются.
- Точный ванильный прецедент «робо-предмет в мире» (`BasePartRobotic` и его наследники) и «графика Орботрона» (`BodyOrbotron`) одновременно; масштаб по умолчанию 1.0, как у протезов.
- Оба дефа получают одинаковый спрайт — консервативный выбор; различить тиры можно позже (в т.ч. собственной графикой), не меняя механику.
- Баланс/рецепты/исследования/слот не затронуты: изменения только в `Definitions` JSON, свойства защиты живут в `Config/Inventory` и не менялись.
- `CheckUsage` чист: `Graphic` читается в `OnConfig`, никаких «объявлено, но не используется».

## 7. Изменённые файлы

| Файл | Изменение |
|---|---|
| `OrbotronArmorMod/Definitions/Obj/Equipment/Orbotron/OrbotronArmor01.json` | +блок `ObjGraphics` с `Graphic: Obj/BodyParts/Robotic` (+6 строк) |
| `OrbotronArmorMod/Definitions/Obj/Equipment/Orbotron/OrbotronArmor02.json` | то же (+6 строк) |

Коммит: `d000f78` («Give Orbotron armor defs an ObjGraphics Graphic»), ветка `orbotron-armor-gfx-01` от `main` (`1bb5710`). Остальные файлы мода не менялись.

## 8. Автоматические проверки

- JSON-валидность обоих файлов — PowerShell `ConvertFrom-Json`: OK.
- Спрайт `Obj/BodyParts/Robotic` присутствует в реестре `GraphicsId.cs:536` (константа `ObjBodyPartsRobotic`).
- `git diff main`: ровно 2 файла, +12 строк, иных изменений нет.
- Формат ванияльного паттерна сверен с `Knife01.json`/`CombatSuit01.json` (наследник `BaseEquipment` переобъявляет `ObjGraphics` с `Graphic`); слияние свойств проверено по `ComponentConfig.MergeWith`/`SerializableProperty.MergeWith` — потомок перебивает родителя, родительские `Shader`/`SelectableScale` сохраняются.
- Полнотекстовая проверка «дефы с `ObjGraphics` без `Graphic`» по `Core/Definitions`: только 5 абстрактных шаблонов (`BaseEquipment`, `BaseMaterial`, `BaseMeal`, `BasePlant`, `BaseUpgrade`) — ни один конкретный предмет ваниллы не живёт без `Graphic`.

## 9. Что осталось непроверенным в игре

- Фактический рендер: как `Obj/BodyParts/Robotic` выглядит как мировой спрайт именно у брони (у протезов он проверен игрой, у нового предмета — нет; шейдер/масштаб те же, но визуальную приемку даёт только игра).
- Что попап исчезает в существующем сейве, где броня уже лежит на складе/надета (загрузка сейва должна пройти без `NotifyMissingKey` — по механике компонентов конфиг берётся из дефа, но запуск не выполнялся).
- Поведение при разборке брони диспоузером (иконка в очереди из `PreviewIcon` — работала и раньше; строка мирового спрайта не задействована).
- Рендер Орботрона с надетой бронёй — без изменений и без участия `Graphic` (нет `LookPart`/`SpriteId`); отдельно не проверялся.

## 10. Пошаговый сценарий ручного тестирования

1. Собрать/обновить мод в игре (JSON-мод, DLL не требует), загрузить существующий сейв, где броня уже создана, — попапа «Possible Game Breaking Bug» быть не должно; в логе (`output_log`/Player.log) нет `Component config for ObjGraphics missing String key: Graphic`.
2. Скрафтить OrbotronArmor01 и OrbotronArmor02 по рецептам — ошибки нет; предмет в мире выглядит как роботизированная деталь (как у протезов).
3. Перенести предмет, положить на землю, забрать — спрайт отображается и на земле, и в переноске, ничего не «плавает».
4. Экипировать на Орботрона — характеристики защиты в панели брони прежние (Sharp/Piercing/Blunt 4.5, Energy 6.0, WeakPointCover 0.10 для 01-тира); на теле Орботрона визуально ничего не добавляется (как и до фикса).
5. Снять броню, положить в склад, дать приказ на разборку диспоузером — иконка в очереди разборки прежняя (щит), ошибки нет.
6. Проверить рецепты/исследования OrbotronArmor/OrbotronArmorAdvanced — доступность не изменилась.
7. Прогресс сейва: сохранить/загрузить после всех операций — ошибок нет.

## Факты / выводы / предположения

**Подтверждённые факты (исходники и реестры):**
- Триггер попапа: `ObjGraphicsComp.OnConfig` → `GetString(Graphic)` без дефолта → `NotifyMissingKey` (точные файлы и строки в §1, §2).
- `PreviewIcon` перехватывает `Def.Preview`/`PreviewSprite` до компонентного fallback.
- Наследование дефов — `parent.Copy()` + слияние одноимённых компонентов по свойствам, потомок побеждает.
- `Obj/BodyParts/Robotic*` существуют в реестре спрайтов и используются ванильными робо-предметами и `BodyOrbotron`.
- Все конкретные ванильные предметы с `ObjGraphics` имеют `Graphic`; без него — только 5 абстрактных шаблонов.
- Слот `OrbotronArmor` без `LookPart`, инвентарные конфиги без `SpriteId` → надетая броня не рисуется на теле.

**Выводы из анализа исходников:**
- Попап ловился при создании сущности предмета (крафт/спавн/загрузка сейва), а не в UI.
- Добавление `Graphic` не влияет на рендер на теле и на UI-иконки — только на мировой спрайт.
- Конфиги компонентов не хранятся в сейве, а восстанавливаются из дефов — изменение JSON совместимо с существующими сохранениями.

**Предположения, требующие проверки в игре (см. §9):**
- Визуальная приемка мирового спрайта брони.
- Отсутствие попапа при загрузке старого сейва с уже созданными предметами.
- Отсутствие побочных сообщений в логе при всех операциях с предметом.
