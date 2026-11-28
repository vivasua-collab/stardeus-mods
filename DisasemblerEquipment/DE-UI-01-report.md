# DE-UI-01 — Технический отчёт: аудит UI DisasemblerEquipment

**Версия игры:** Stardeus 0.15.11b
**Исходники:** `Core/ReferenceSourceCode/` (снапшот в репозитории, 3184 файла .cs)
**Код мода:** `DisasemblerEquipment/Code/DisasemblerEquipmentMod.cs`
**Статус:** только исследование, код не изменялся.

Все пути ванильных файлов указаны от `Core/ReferenceSourceCode/`.

---

## 0. Сводка корневых причин

| # | Проблема | Корневая причина | Статус |
|---|---|---|---|
| 1 | Кнопки Add rule накладываются | Футерный префаб `WFooterItem_DeleteButton` используется в контентном контейнере + 3 заголовка в одном блоке | Причина misuse подтверждена; точная геометрия наложения — предположение |
| 2 | Cancel перекрывает UI | Save/Cancel спавнятся в контент блока вместо `subPanel.Footer` | Подтверждено |
| 3 | Нет кнопки Save | Кнопка спавнится, но тем же сломанным способом; вероятно скрыта под Cancel | Существование в коде подтверждено; причина невидимости — предположение |
| 4 | Тумблеры не меняют состояние | API тумблеров корректен; состояние теряется из-за перезагрузки вкладки, которая уничтожает открытый редактор | Перезагрузка подтверждена; доля вклада механизмов — предположение |
| 5 | Правила нельзя создать | Следствие 1–4 | — |

---

## 1. Используемые модом UI-классы и их ванильный статус

Код мода: `RuleUI.Append` / `SpawnList` / `OpenEditor` / `OriginToggle` (DisasemblerEquipmentMod.cs:481–574), точка входа — Harmony-постфикс `DisposerTab.LoadContent` (DisasemblerEquipmentMod.cs:471–479).

| API мода | Ванильный класс/файл | Оценка |
|---|---|---|
| `UISpawn.Block(life, group, idx)` | `Systems/UIUX/Helpers/UISpawn.cs:95-99` → `Widget<WContentBlock>` | корректно |
| `UISpawn.Widget<T>(life, prefabId, parent, idx)` | `UISpawn.cs:45-93` (`PrefabPool.Spawn` + `SetSiblingIdx` + `lifetime.Add`) | корректно |
| `WText` + `UIPrefabId.WContent2Header` | `Systems/UIUX/Blocks/WText.cs`; маппинг `UISpawnExamples.cs:43` | корректно, но нарушено правило «1 header на блок» (см. §2) |
| `WIconText` + `WContent2Text` | `Blocks/WIconText.cs`; `UISpawnExamples.cs:53` | корректно |
| `WButtonText` + `WFooterItem_DeleteButton` | `Blocks/WButtonText.cs:46-58`; `UISpawnExamples.cs:98` | **некорректно** — префаб только для футеров (см. §3.1) |
| `WDropdownWithIcon` + `WContent2Dropdown`; `Dropdown.Load(options, selected, cb)` | `Blocks/WDropdownWithIcon.cs`; `Blocks/WDropdown.cs:55-59` | корректно |
| `WToggleWithIconText2` + `WContent2Text2Toggle`; `Item.Load(icon,text,text2)`, `Toggle.Load(v,cb)` | `Blocks/WToggleWithIconText2.cs`; `Blocks/WIconText2.cs:22-30`; `Blocks/WToggle.cs:37-42` | API корректен (см. §3.4) |
| `WSliderWithIconText2` + `WContent2Text2Slider`; `Slider.Load(v,cb,from,to)` | `Blocks/WSliderWithIconText2.cs`; `Blocks/WSlider.cs:33-45` | **антипаттерн** — SetText2 внутри onChange (см. §3.4) |
| `GetEntitySubPanel.Ask()` → `SubPanelWidget` | `Systems/UIUX/UIQuery.cs:41`; `Systems/UIUX/Widgets/SubPanelWidget.cs` | корректно, но используется неполно (нет Footer, нет SubId) |
| `panel.CreateLifetime(parent)`, `SetTitle`, `Show(anchor)`, `Close` | `SubPanelWidget.cs:164-207` | частично корректно |
| `A.S.UIQuery.ReloadEntityTab.Ask(tab)` | `UIQuery.cs:167`; обработчик `Widgets/EntityPanelWidget.cs:547-553` | корректно (аналог `BaseEntityTab.ReloadTab`, `Handlers/EntityTabs/BaseEntityTab.cs:60-62`) |
| Рефлексия полей `disposer` / `lifetime` | `Handlers/EntityTabs/DisposerTab.cs:20`; `BaseEntityTab.cs:23` | корректно, имена совпадают |

---

## 2. Проблема 1: наложение кнопок Add rule в основном окне

**Подтверждённые факты:**

1. Мод спавнит все кнопки (Edit/Remove/Add) префабом `UIPrefabId.WFooterItem_DeleteButton` в контентный `block.Container` (DisasemblerEquipmentMod.cs:500, 503, 506).
2. В ванилле `WFooterItem_DeleteButton` используется **исключительно в футерных контейнерах**:
   - `Systems/Creatures/Surgeries/SurgeryChoiceView.cs:72-73` — `wFooter.Container`;
   - `Systems/BodyParts/InstantBodyPartChoiceView.cs:53-54` — `wFooter.Container`.
   Других использований в 0.15.11b нет.
3. Официальные примеры кнопок для контента используют другие префабы (`UISpawnExamples.cs:441-445` — `WContent2ButtonSimple`, `:34` — `WContent2Button` и т.д.).
4. Мод кладёт **три заголовка** (`WContent2Header`: «Automatic equipment recycling», «Weapons», «Clothing and armor») в **один** `WContentBlock` (DisasemblerEquipmentMod.cs:483-488). Прямое правило из `UISpawnExamples.cs:18-21`: *«A Header can only appear once, at the very beginning of a block. If you need multiple headers, create multiple blocks.»*
5. Геометрия префабов (якоря, LayoutElement, размеры) находится в ассет-бандлах игры и **в репозитории отсутствует** — точный механизм наложения из исходников не выводится.

**Предположение (высокая уверенность):** футерные префабы рассчитаны на горизонтальный контейнер футера; в вертикальном layout контентного блока они не компоновываются ожидаемым образом (сохраняют собственное позиционирование), поэтому соседние кнопки визуально перекрываются. Косвенное подтверждение: все не-кнопочные виджеты мода (`WContent2*`) отображаются корректно, ломаются именно футерные кнопки.

**Исправление:** контентные кнопки `WContent2ButtonSimple` / `WContent2ButtonX` / `WContent2Button2`; заголовки — по одному на блок, блок на категорию (см. §6).

---

## 3. Проблемы 2 и 3: редактор — Cancel поверх UI, кнопка Save отсутствует

**Подтверждённые факты:**

1. `SubPanelWidget` имеет **две** зоны: `Container` (контент) и отдельный `Footer` (WGroup) (SubPanelWidget.cs:23-24), плюс `ShowFooter(bool)` (:40-42).
2. Ванильные confirm/cancel-кнопки в сабпанелях спавнятся **в `subPanel.Footer.Container`** с включением футера:
   - `Handlers/EntityTabs/BedTab.cs:206-215` — `WFooterItem_Toggle` в `subPanel.Footer.Container`, затем `subPanel.ShowFooter(true)`;
   - `Handlers/Policies/EquipmentPolicyTab.cs:178-183` — `WFooterItem_ConfirmButton` в `ctx.Footer.Container`, `ctx.Footer.SetActiveFast(true)`;
   - `SurgeryChoiceView.cs:45-56` — `wFooter.Container` после `wFooter.SetActiveFast(false)`.
3. Кнопка «Save rule» **есть в коде мода** (DisasemblerEquipmentMod.cs:557-558), но спавнится префабом `WFooterItem_DeleteButton` в `block.Container` (контент), как и «Cancel» (:559-560).
4. `panel.Show(anchor)` мода вызывает `Show(forItem, null)` (SubPanelWidget.cs:45-50) — `IsOpenFor` сбрасывается в null, панель не восстановится после перезагрузки вкладки.
5. Пример канонического редактора-сабпанели: `BedTab.ShowAssignedSlotSubPanel` (BedTab.cs:159-191) — `CreateLifetime` → `SetTitle` → блоки в `Container` (gIdx++) → виджеты в `block.Container` (idx++) → футер → `Show(forWidget, SubId)`.

**Предположение (объединяющее проблемы 2 и 3):** Save и Cancel — два `WFooterItem_DeleteButton` подряд в контенте — перекрываются друг другом по тому же механизму, что и в проблеме 1; «Cancel» визуально лежит поверх интерфейса, а «Save» скрыт под ним. Отсюда и «отсутствие» кнопки подтверждения.

**Исправление:** Save/Cancel — в `panel.Footer.Container` (`WFooterItem_ConfirmButton` для Save, `WFooterItem_RedButton` или `WFooterItem_DeleteButton` для Cancel) + `panel.ShowFooter(true)` (см. §7).

---

## 4. Проблема 4: тумблеры «не позволяют менять состояние»

**Подтверждено — API тумблеров у мода корректен:**

- Канонический пример `UISpawnExamples.cs:476-481`: `w.Item.Load(IconId.CCheck, "Feature Name", "Enabled"); w.Toggle.Load(true, isOn => { ... });` — мод делает то же самое (DisasemblerEquipmentMod.cs:534-536, 538-549, 564-573).
- `WToggle.Load(bool, Action<bool>)` регистрирует обработчик и ставит значение без уведомления (WToggle.cs:37-42); `toggle.onValueChanged` риггится в Awake (:15-18).
- Обратный вызов мода меняет поля клона `EquipmentRecycleRule` и обновляет текст — локально состояние действительно меняется.

**Подтверждено — главный механизм потери состояния:**

1. Ванильный `DisposerTab.LoadContent` привязывает перезагрузку вкладки к любому изменению состояния диспоузера: `disposer.DisposerStateChangedB.Bind(lifetime, () => ReloadTab(), false)` (DisposerTab.cs:47).
2. `DisposerStateChangedB` стреляет при **каждом добавлении предмета в очередь** и **каждом завершении разборки**: `DisposerComp.cs:157-158` (`Queue.Add(disp); UpdateUIBlock();`) и `:194-197` (`Queue.RemoveAt(0); UpdateUIBlock(); DoDispose(disp);`), где `UpdateUIBlock` → `stateChangedB?.Invoke()` (DisposerComp.cs:64-65).
3. `ReloadTab()` → `UIQuery.ReloadEntityTab` → `EntityPanelWidget.ReloadTab` → `ShowTab(tab)` (EntityPanelWidget.cs:547-553, 381-432), где **первой же строкой** `subPanel.Close(clearMemory: false)` (EntityPanelWidget.cs:383) → `DisposeContents()` (SubPanelWidget.cs:192-197) → Lifetime редактора уничтожен, все виджеты редактора деспавнены, несохранённый `rule` (локальный клон в замыканиях) потерян, панель закрыта.
4. Собственный `AutoScanner.Tick` мода каждые `Consts.TicksPerHour` ищет и заказывает разборку оборудования (DisasemblerEquipmentMod.cs:320-372) — каждый заказ = `UpdateUIBlock` = мгновенная перезагрузка вкладки. Пока диспоузер что-то разбирает или сканер пополняет очередь, редактор живёт секунды.
5. Мод не использует механизм восстановления сабпанели: открытие без `SubId` (факт 4 в §3), в `Append` нет проверки `SubPanelWidget.IsOpenFor`. Ванильный образец: BedTab.cs:95-112 — `if (SubPanelWidget.IsOpenFor == SubId && activeSlotWidget != null) { ...subPanel.Show...; Coroutines.OnNextFrame(() => sp.RepositionWidget(btn, connectorOnly: true)); }`; официально задокументировано в `UISpawnExamples.cs:687-718` (с обязательным `LayoutRebuilder.ForceRebuildLayoutImmediate` перед `Show`).

**Дополнительный подтверждённый дефект — слайдер:**

- Документированный антипаттерн (`UISpawnExamples.cs:239-258`, блоки «BAD»/«GOOD»): *«Do NOT set any text inside the Slider.Load onChange callback. Instead, always use Slider.SetValueTextFunc»* — текст синхронизируется только на commit, не во время drag. Мод делает именно «BAD» (DisasemblerEquipmentMod.cs:543, 548).
- `WSlider` при `Load(value, cb, 0f, 3f)` помечает `isPercentage = false` (WSlider.cs:47-51) и собственный лейбл слайдера рисует `v.ToString("0.##")` (WSlider.cs:74-89) — рядом с текстом мода «Maximum quality: 150%» отображается сырой «1.5». Оба лейбла существуют одновременно.

**Предположение (средняя уверенность):** часть наблюдений «тумблеры не работают» может быть визуальным следствием слайдера (мгновенный «отскок» лейбла) и/или перезагрузок в момент клика; вклад каждого механизма подтвердится только в игре.

---

## 5. Жизненный цикл UI (подтверждённая схема)

```
EntityPanelWidget.ShowTab(tab)                      (EntityPanelWidget.cs:381-432)
  ├─ subPanel.Close(clearMemory: false)             — содержимое редактора уничтожается,
  │                                                   IsOpenFor сохраняется
  ├─ currentTab?.Deactivate()                       — Dispose lifetime вкладки → деспавн всех виджетов
  ├─ tab.CreateLifetime(lifetime)                   — новый Lifetime "EntityTab_{Id}" (BaseEntityTab.cs:103-109)
  ├─ tab.LoadTopControls / LoadContent(wItems) / LoadFooter(wControlsBottom)
  │     └─ Harmony-постфикс мода добавляет блок правил
  └─ при исключении в LoadContent: вкладка деактивируется, панель закрывается
                                        (EntityPanelWidget.cs:405-421) — исключение в постфиксе мода
                                          валит всю панель

Lifetime.Dispose                                    (Utils/Lifetime.cs:114-168)
  └─ дети → items (WBase.Dispose → PrefabPool.Despawn) → onDispose

ReloadTab                                           (BaseEntityTab.cs:60-62 → EntityPanelWidget.cs:547-553)
  └─ = ShowTab: полный пересбор контента; привязан к DisposerStateChangedB (см. §4)

Повторное открытие вкладки — тот же ShowTab; сабпанель восстанавливается только
вручную через SubPanelWidget.IsOpenFor + ForceRebuildLayoutImmediate + OnNextFrame.
```

Виджеты пулятся (`PrefabPool.Spawn/Despawn`, UISpawn.cs:64); `WToggle` сбрасывает состояние в `PoolDeactivate` (WToggle.cs:60-64) — «залипших» состояний пула не ожидается.

---

## 6. Рекомендуемая структура основного окна

Постфикс `DisposerTab.LoadContent` оставить (он корректен). Изменения внутри `RuleUI`:

```
wItems (WGroup, ванильная зона контента)
├─ [ванилла] settingsBlock (idx 0), queueBlock (idx 1) — не трогаем
└─ [мод] на КАЖДУЮ категорию — отдельный блок:
   ├─ Block(life, group, idx++)                                  ← блок №1: Weapons
   │  ├─ WText WContent2Header           «Weapons»               ← header первым и единственным
   │  ├─ на каждое правило: WContentButtonWithX (WContent2ButtonX)
   │  │     Button.Load(IconId, Rules.Summary(rule), () => OpenEditor(...))   ← клик по строке = Edit
   │  │     XButton.Load(() => { Rules.Remove(d, index); ReloadEntityTab })   ← X = Remove
   │  └─ WButtonText WContent2ButtonSimple  «Add rule»            ← контентная кнопка, низ блока
   └─ Block(life, group, idx++)                                  ← блок №2: Clothing — то же самое
```

Обоснования:
- `WContent2ButtonX` = `WContentButtonWithX` (Blocks/WContentButtonWithX.cs): `Button` — `WButtonIconText.Load(string icon, string text, Action onClick)` (WButtonIconText.cs:32), `XButton` — `WButton.Load(Action)` (WButton.cs:22); образец использования — `UISpawnExamples.cs:316-326` («XButton cancels/removes it»).
- «Add rule» — `WContent2ButtonSimple` (`UISpawnExamples.cs:441-445`).
- Правило «не смешивать светлые WContent* и тёмные WContent2* в блоке» (`UISpawnExamples.cs:17`) — мод уже целиком на WContent2*, сохраняем.
- Глобальный заголовок «Automatic equipment recycling» либо убрать (название секций говорит само), либо вынести в `WContent2Header` первого блока — не третьим header-ом.

---

## 7. Рекомендуемая структура редактора правила

```
SubPanelWidget
├─ SetTitle("Add/Edit recycling rule")
├─ Container (gIdx++ / idx++ как в BedTab.cs:159-191):
│  ├─ Block 0: WDropdownWithIcon  WContent2Dropdown    «Тип» (как сейчас)
│  ├─ Block 1: WToggleWithIconText2 WContent2Text2Toggle «Rule active» (как сейчас)
│  ├─ Block 2: WToggleWithIconText2 «Any quality» + WSliderWithIconText2 «Max quality»
│  │            slider.Slider.SetValueTextFunc(v => Units.Percentage(v, clamp: false))
│  │            SetText2 из onChange убрать
│  └─ Block 3: origin-тумблеры (Clothing) — как сейчас
├─ Footer (subPanel.Footer.Container, затем subPanel.ShowFooter(true)):
│  ├─ WButtonText WFooterItem_ConfirmButton «Save rule»   → Rules.Add(...) + Close + Reload
│  └─ WButtonText WFooterItem_RedButton    «Cancel»       → Close
└─ Show(anchor, SubId)                                     ← ОБЯЗАТЕЛЬНО с SubId
```

Обоснования: BedTab.cs:206-215 (футер сабпанели), EquipmentPolicyTab.cs:178-183 (`WFooterItem_ConfirmButton`), `UISpawnExamples.cs:499-503` (confirm-кнопка), `:506-510` (red-кнопка), SubPanelWidget.cs:23-24/40-42 (Footer + ShowFooter).

---

## 8. Конкретный план исправления

Всё — в `RuleUI` (DisasemblerEquipmentMod.cs:481-574); Harmony-патчи логики разборки не затрагиваются.

1. **`SpawnList` (490-508):** заменить `WFooterItem_DeleteButton` → `WContentButtonWithX` для строк правил (Edit = клик по строке, Remove = XButton) и `WContent2ButtonSimple` для «Add rule».
2. **`Append` (482-489):** убрать общий блок-обёртку; один `UISpawn.Block` на категорию; header первым в каждом блоке; глобальный header убрать.
3. **`OpenEditor` (510-562):**
   - Save (557-558): `WFooterItem_ConfirmButton` в `panel.Footer.Container`, затем `panel.ShowFooter(true)`.
   - Cancel (559-560): `WFooterItem_RedButton` туда же.
   - Слайдер (540-543): после `Load(...)` вызвать `slider.Slider.SetValueTextFunc(v => Units.Percentage(v, clamp: false))`; из onChange убрать `SetText2` (оставить только `rule.MaxQuality = Mathf.Clamp(v, 0f, 3f)`); диапазон 0..3 сохранить (пороги >100% — осознанная фича, коммит `1ca4260`).
4. **Переживать перезагрузку вкладки (ключевое для проблемы 4):**
   - статическое поле мода: `static readonly Dictionary<int, (EquipmentRecycleRule draft, int index, int category, string anchorKey)> EditorDrafts` (ключ — `d.EntityId`); заполнение при `OpenEditor`, чтение при восстановлении, очистка при Close/Save.
   - `SubId` = например `$"DEQ.RuleEditor:{d.EntityId}:{category}:{editingIndex}"`; открытие `panel.Show(anchor, SubId)`.
   - в конце `SpawnList`/`Append`: если `SubPanelWidget.IsOpenFor == SubId` и якорный виджет заспавнен — восстановить редактор по паттерну BedTab.cs:95-112 / UISpawnExamples.cs:705-718: `LayoutRebuilder.ForceRebuildLayoutImmediate(group.Container)` → Show → `Coroutines.OnNextFrame(RepositionWidget)`.
   - якорем при восстановлении служит виджет строки правила (спавнить и хранить ссылку, как `activeSlotWidget` в BedTab).
5. **Опционально (уменьшение частоты перезагрузок под редактором):** не трогать ванильный `DisposerStateChangedB`-байндинг; перезагрузка безопасна, если пп. 3-4 реализованы. Глушить байндинг Harmony-патчем — не рекомендуется (касается ванильной логики вкладки).
6. **`OriginToggle`/тумблеры:** изменений API не требуется; убедиться, что черновик правила живёт в `EditorDrafts`, а не только в замыкании (п. 4) — тогда «потеря состояния» исчезает.

---

## 9. Риски и неизвестные

| Риск | Тип |
|---|---|
| Геометрия префабов (якоря/размеры `WFooterItem_*`, `WContent2*`) — в ассет-бандлах, в снапшоте отсутствует; точная механика наложения подтверждается только в игре | Неизвестное |
| Поведение `WContent2ButtonX` в узком сабпанеле (ширина, перенос строк) не задокументировано; при проблемах — запасной вариант `WContent2Button2` (Edit) + отдельная маленькая X-кнопка | Риск вёрстки |
| Восстановление редактора по `IsOpenFor` требует, чтобы индекс правила из SubId оставался валидным на момент восстановления; между открытием и reload состав правил не меняется (меняется только очередь разборки) — коллизия маловероятна, но при Save другого правила индексы сдвигаются | Риск, требующий проверки в игре |
| `SubPanelWidget.IsOpenFor` — статик, общий на все открытые панели; сбрасывается только `Close(clearMemory: true)`/новым Show с SubId | Подтверждено (SubPanelWidget.cs:28-29, 168-180) |
| Исключение в постфиксе мода закрывает всю панель сущности (EntityPanelWidget.cs:405-421) — после рефакторинга обязательно проверять логи | Подтверждено |
| Ванильный байндинг `DisposerStateChangedB → ReloadTab` будет дёргать вкладку при активной разборке; с восстановлением по SubId редактор выживает, но видимые «мигания» останутся | Подтверждено, приемлемо |
| `T.Yes`/`T.No` — англоязычные литералы переводов (T.cs:2478-2480); для UI-текстов правил ванилла использует прямые строки — мод продолжает в том же стиле | Низкий риск |

**Отдельно зафиксировано:** правило задачи «не предполагать существование методов без подтверждения» — все сигнатуры в плане §8 проверены по исходникам: `WButtonIconText.Load(icon, text, Action)` (WButtonIconText.cs:32), `WButton.Load(Action)` (WButton.cs:22), `WSlider.SetValueTextFunc` (WSlider.cs:53-59), `SubPanelWidget.Show(WBase, string)` (SubPanelWidget.cs:52), `ShowFooter(bool)` (:40-42), `Footer.Container` (:24), `Coroutines.OnNextFrame` (используется в EntityPanelWidget.cs:431, BedTab.cs:106-111).

---

## 10. Опорные ванильные образцы (сводка)

1. **Динамический список правил в контенте:** `Handlers/Policies/EquipmentPolicyTab.cs:120-126` (блок + секции) и `UISpawnExamples.cs:334-341` (официальный шаблон block/idx).
2. **Сабпанель-редактор целиком:** `Handlers/EntityTabs/BedTab.cs:159-215` (включая футер).
3. **Восстановление сабпанели после reload:** `BedTab.cs:95-112` + `UISpawnExamples.cs:687-718`.
4. **Строки с X-удалением:** `UISpawnExamples.cs:316-326` (`WContent2ButtonX`).
5. **Тумблер с иконкой и состоянием:** `UISpawnExamples.cs:476-481`.
6. **Слайдер с текстом значения:** `UISpawnExamples.cs:250-258` (`SetValueTextFunc` — «GOOD»).
7. **Футерные кнопки:** `EquipmentPolicyTab.cs:158-183`, `SurgeryChoiceView.cs:45-56`, `InstantBodyPartChoiceView.cs:44-57`.

*Конец отчёта DE-UI-01.*
