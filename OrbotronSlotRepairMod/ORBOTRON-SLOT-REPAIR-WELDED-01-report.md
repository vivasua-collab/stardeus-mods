# ORBOTRON-SLOT-REPAIR-WELDED-01 — Отчёт: операция Remove Welded Part

**Мод:** OrbotronSlotRepairMod. **Версия:** 0.1.1 → 0.1.2.
**Версия игры:** Stardeus 0.15.11b. **Ветка:** `orbotron-welded-part-removal-01` (один коммит поверх `main`; merge — после ревью и игрового теста).

## 1. Изменённые файлы и методы

Один файл кода: `Code/SlotRepairMod.cs`.

| Метод / класс | Что изменено |
|---|---|
| `SlotRepairIds` | + `RemoveWeldedPart = 9` (тип 8 не тронут), + ключ локализации `orbotron.slotrepair.removeWeldedPart`, + общий предикат `IsWeldedRemovablePart(slot)`: не удалён, `InstalledDef != null`, `State != null`, `IsWelded`, `Type.IsRemovable` |
| `SlotRepairChoicePatch.Postfix` | Рефакторинг: общий хелпер `AddFooterButton` (создание `WButtonText` → `SurgicalOperation` → `RegisterSurgicalOperation` → `SlotRepairQueueKick.TryStartNext` → закрытие окна). Ветка 1 — прежняя destroyed-проверка (поведение идентично прежнему). Ветка 2 — welded-кнопка с иконкой `CBodyRemoveBad`; postfix выполняется и после vanilla раннего return после `BodyPartWeldedWarning`, footer активируется через `SetActiveFast(true)` |
| `SlotRepairOrderPatch.Prefix` | Обрабатывает типы 8 и 9. Для 9 — строгая валидация (Orbotron ×2, `IsWeldedRemovablePart`), при отказе `FailReason` + `Failed`. Постановка — тот же `CustomSurgeryGoalFactory.TryOrder`, иконка цели `CBodyRemoveBad` (у 8 — `CBodyReplaceBad`) |
| `SlotRepairResolveOpTypePatch.Prefix` | Оба типа → `needsPart = false`, `needsBodySlot = true` |
| `SlotRepairOperationNamePatch` | Прежний Postfix (тип 8) не тронут. Добавлен Prefix для типа 9 → локализованное имя; Prefix перехватывает до vanilla `switch`, чтобы неизвестный тип не логировал `D.Err("Unhandled name...")` в очередь и на индикаторе прогресса |
| `SlotRepairFinalizePatch.Prefix` | Диспетчеризация: тип 9 → новый `RemoveWeldedProcedure`, тип 8 → прежний путь (не тронут), остальное → vanilla |
| `SlotRepairFinalizePatch.RemoveWeldedProcedure` (новое) | См. §2 |

Прочее: `Translations/English.csv`, `Translations/Russian.csv` — ключ `orbotron.slotrepair.removeWeldedPart` (+ уточнён desc исследования: теперь «операции» во множественном числе); `ModInfo.json` — 0.1.2, описание двух операций; `README.md` — обе операции, чек-лист 13 пунктов.

## 2. Механика финализации (RemoveWeldedProcedure)

Точка: Harmony Prefix на приватный `BodyModSpotComp.FinalizeProcedure(subOpType, out failReason)` — тот же патч, что у ремонта, vanilla `ProcedureOutcome` возвращается рефлексивно (InProgress/Completed/Failed).

1. Валидации: пациент Orbotron + `bodyPartSlot` задан (передаётся из цели через `AIGoalVars.BodySlotId_String`); слот существует и `IsWeldedRemovablePart`.
2. Сохраняется `weldedFlags = slot.State.Flags`; снимается **только** бит `Welded` (`Flags & ~BodyPartStateFlags.Welded`) — штатный внутренний `TryRemovePart(out …)` вызывает `CanRemovePart(true)`, который иначе отклоняет приваренную часть.
3. `slot.TryRemovePart(S, out spawnedPart, worker, canFail: false, out _, isForSwap: false, opType: 9, performerLink)` — штатная процедура Core:
   - `canFail: false` отключает бросок `CheckBodyPartOpSuccess` → никаких случайных осложнений, повторное приваривание невозможно;
   - чистка статы/способностей/нужд/атак/иерархии — vanilla; перенос разума из части — vanilla `TransferMindAfterPartRemoval`;
   - объект детали создаётся с `BodyPart = part`, `BodyPartState = state` → качество, состояние, раны и остальные флаги переносятся; `Welded` уже снят → извлечённая деталь не приварена, остальные флаги (`Crippled`/`Destroyed`/`Infected`/…) нетронуты;
   - `state.IsInfected → SetQuality(0)` и авто-утилизация при `Irreparable`/условие < 0.1 — штатные ветки Core, сохранены сознательно.
4. Неудача (не ожидается при пройденных валидациях, но защищено): флаг `Welded` восстанавливается, `failReason = T.OperationFailed`, outcome Failed — установленная деталь никогда не теряет статус приваривания незаметно.
5. Успех — зеркало vanilla Remove-ветки `PerformActionOnBodyPartSlot`: при смерти тела от извлечения — `Mind.OnDeath(T.DeathByBodyPartRemovalRobo_V2…)`, затем `EjectRemovedPart(spawnedPart)` (выходной порт, + штатный заказ утилизации для Irreparable) и `EjectOrphanedParts(slot)` (отсоединённые части). Оба — приватные методы Core через рефлексию.
6. Не тронуто: `slot.IsDestroyed`, другие слоты, состояние прочности детали, `InstalledDef` (очищается самой vanilla-процедурой, не вручную).

## 3. Commit SHA

| SHA | Коммит |
|---|---|
| см. git log ветки `orbotron-welded-part-removal-01` | Add Remove Welded Part operation (Orbotron Slot Repair 0.1.2) |

## 4. Результат сборки

`dotnet build .vscode/mod.csproj -p:StardeusManagedPath=D:\Games\Stardeus v0.15.11b\Stardeus_Data\Managed` — **0 ошибок, 5 предупреждений**. Предупреждения — те же известные MSB3277 (конфликты версий Microsoft.CodeAnalysis / Game.dll цепочки), не новые. DLL: `OrbotronSlotRepairMod/Libraries/OrbotronSlotRepairMod.dll` (не коммитится).

## 5. Проверки из ТЗ §6 (статически возможные)

Выполнено (статически, без запуска игры):

1. ✅ Мод собирается против локальных Managed DLL целевой версии (0 ошибок).
2–5, 6–12. ⏳ Требуют запуска игры — см. чек-лист README (13 пунктов). Статически подтверждены только механизмы, от которых зависят эти проверки:
   - UI: vanilla `LoadPopupContent` для welded-части делает ранний `return` после `BodyPartWeldedWarning` (SurgeryChoiceView.cs:64–69) → кнопка добавляется Postfix'ом и активирует footer — тот же механизм, что у проверенной кнопки ремонта.
   - Очередь: `SurgicalOperation.Validate` не отклоняет кастомный тип 9 (проверки Install/Swap/Remove не задействованы); `TryNextSurgery` не фильтрует welded; `ValidateFacilities` требует Assembly Hub — общий для обеих операций.
   - Транспорт: `ResolveOpType` → `needsPart=false, needsBodySlot=true` — путь `Surgery_GoTo`/`CarryPatient` (проверен в игре для операции 8; для 9 — идентичен).
   - Извлечение: `canFail:false` доходит до `CheckBodyPartOpSuccess` как прямое условие (BodyPartSlot.cs:806) — случайный бросок исключён; `CanRemovePart` вызывается внутренним `TryRemovePart` только после снятия `Welded`.
   - Совместимость: все существующие патчи сохраняют прежние ветки для типа 8 без изменений; новая ветка добавлена диспетчеризацией.

Не выполнено (нет запуска игры): все in-game проверки 2–12 из ТЗ, включая перенос разума и сохранность повреждений — отмечены в README-чек-листе для ручного теста.

## 6. Архитектурные решения (для ревью)

1. **Снятие Welded «на время вызова»** — по ТЗ §4.4. Альтернатива (обход `CanRemovePart` вызовом внутреннего 3-аргументного `TryRemovePart` напрямую) отвергнута: ТЗ прямо требует штатную проверку, и она дешевле/безопаснее — для disposal-режима в vanilla есть ветки, которых лучше не касаться.
2. **Воспроизведение vanilla Remove-хвоста (смерть → eject → orphans)** — `PerformActionOnBodyPartSlot` вызвать нельзя: он жёстко использует `canFail: true`. Дублировано 3 шага (≈15 строк) с точным соответствием vanilla (те же T-ключи сообщений).
3. **Prefix вместо Postfix в NameFor для типа 9** — vanilla `NameFor` для неизвестного типа пишет `D.Err("Unhandled name...")` в лог при каждом вызове; Prefix для 9 предотвращает мусор. Прежний Postfix для 8 оставлен как есть (существующее поведение не менялось сознательно).
4. **`Type.IsRemovable`** — публичное свойство `BodyPartType` (та же проверка, что в `CanRemovePart` и `AffectsSlot`), вместо прямых битовых операций.
5. **Reflexive `S`** — поле `BaseComponent.S` через `AccessTools.Field(__instance.GetType(), "S")` — соответствует vanilla (`PerformActionOnBodyPartSlot` использует `S` компонента); позиция спавна детали — позиция тела, как в vanilla.
6. **Одна фабрика целей, одна точка финализации** — обе операции различаются только типом/валидацией/иконкой; новых AI-систем и обходов проверки Assembly Hub нет (ТЗ §4.3, §5).

## 7. Известные риски / что проверить в игре

- Название кнопки/операции локализуется через `.T()` по модовому CSV-ключу — механизм тот же, что у `NameKey` исследования (работает в игре с 0.1.1), но вызов `.T()` из C# мода стоит подтвердить глазами в UI (RU/EN).
- Удаление Core-части (HoldsMind) у живого Орботрона: vanilla-путь переноса разума в деталь сохранён, ветка смерти воспроизведена — проверить поведение в игре (пункт 12 чек-листа).
- `performerLink` в `TryRemovePart` передан неявно (vanilla передаёт `Tile.NameLink`): при `canFail: false` параметр используется только в путях осложнений, которые исключены — рисков нет, но отмечено для ревью.
- Если у части, кроме `Welded`, есть `Infected`/`Irreparable` — vanilla утилизирует деталь после eject; это штатное поведение, сохранено сознательно (ТЗ §3).
