# DE-UI-02 — Отчёт: исправление интерфейса DisasemblerEquipment

**Основание:** принятый отчёт [DE-UI-01-report.md](DE-UI-01-report.md).
**Версия игры:** Stardeus 0.15.11b. **Ветка:** `de-ui-02` (3 коммита поверх `main`; merge в main — после аудита).

## Изменённые файлы и методы

Один файл: `DisasemblerEquipment/Code/DisasemblerEquipmentMod.cs`.

| Метод / класс | Что изменено |
|---|---|
| `RuleUI.Append` | Блок-обёртка с 3 заголовками заменена на отдельный `WContentBlock` на категорию (Weapons / Clothing and armor), заголовок один и первым в каждом блоке. Добавлены: очистка «осиротевших» черновиков, валидация и восстановление редактора по `SubPanelWidget.IsOpenFor`, передача якоря, `ForceRebuildLayoutImmediate` → `OpenEditor` → `Coroutines.OnNextFrame(RepositionWidget)`. |
| `RuleUI.SpawnList` | Новая сигнатура (`WGroup group, int gIdx`, возвращает `WBase`-якорь). Строки правил — `WContent2ButtonX` (`Button.Load` = Edit, `XButton.Load` = Remove), «Add rule» — `WContent2ButtonSimple`. Убраны `WFooterItem_DeleteButton` из контента и строка-`WIconText` (сведения о правиле теперь на самой строке-кнопке). |
| `RuleUI.OpenEditor` | Контент разделён на блоки: тип → Rule active → Any quality + слайдер → происхождение одежды. Save — `WFooterItem_ConfirmButton` в `panel.Footer.Container`, Cancel — `WFooterItem_RedButton` там же, `panel.ShowFooter(true)`. Слайдер: `SetValueTextFunc(v => Units.Percentage(v, clamp:false))`, `SetText2` убран из onChange. Открытие — `panel.Show(anchor, subId)` с уникальным `SubId`. Черновик правится через статический стор, а не замыкания. |
| `RuleUI` (новое) | `EditorDraft` (Rule/Category/EditingIndex), `Drafts` (ключ — `EntityId` диспоузера), `SubIdFor`, `RemoveDraft`. |
| `RemovePatch.Postfix` | +1 строка: `RuleUI.RemoveDraft(__instance)` — см. «Отклонение от ограничений» ниже. |

**Не тронуто (по ограничениям):** все патчи разборки (`LoadItemPatch`, `AfterTickPatch`, `BeginDisposePatch`, `OutputPatch`), `AutoScanner`, `Output.Produce` (формула материалов), `EquipmentDefs.Matches` (фильтрация), `Rules.Save/Load` (постоянное сохранение), ванильная логика.

## Commit SHA (ветка `de-ui-02`, поверх `main` = `6437354`)

| SHA | Коммит |
|---|---|
| `0504f3c` | Restructure DE rule list into per-category content blocks |
| `bff21a5` | Move DE rule editor Save/Cancel to subpanel footer, fix slider label |
| `13b36c8` | Preserve DE rule editor across tab reloads via draft and SubId |

## Причины изменений (кратко, по DE-UI-01)

1. **Наложение кнопок:** `WFooterItem_DeleteButton` — футерный префаб, в ванилле используется только в футерных контейнерах; в вертикальном контенте не компоновался. Заменён на контентные префабы; плюс устранено нарушение «1 header на блок».
2. **Cancel поверх UI / «нет» Save:** в `SubPanelWidget` есть отдельная зона `Footer` для подтверждений (BedTab/EquipmentPolicyTab); кнопки перенесены туда. Save был в коде, но перекрывался Cancel — теперь оба в футере.
3. **Потеря состояния тумблеров:** вкладка перезагружается на каждый сигнал `DisposerStateChangedB`, и `ShowTab` закрывал редактор безвозвратно. Черновик вынесен из UI-виджетов, редактор восстанавливается по SubId.
4. **Слайдер:** документированный ваниллой антипаттерн «SetText2 внутри onChange» заменён на `SetValueTextFunc` (лейбл синхронен во время drag; убран конфликт с собственным лейблом слайдера).

## Результат сборки

`dotnet build ... -p:StardeusManagedPath=D:\Games\Stardeus v0.15.11b\Stardeus_Data\Managed` — **0 ошибок, 5 предупреждений** на каждом коммите. Предупреждения — те же известные из README (не новые): `MSB3245` (System.Runtime не резолвится) и `MSB3277` (конфликты версий `System.Collections.Immutable`, `System.Reflection.Metadata`, `System.Threading.Tasks.Extensions`, `System.Text.Encoding.CodePages`). DLL: `DisasemblerEquipment/Libraries/DisasemblerEquipment.dll` (не коммитится, как и раньше).

## Отклонение от ограничений (осознанное, одно)

В `RemovePatch` (Harmony-постфикс `DisposerComp.OnRemove`) добавлен вызов `RuleUI.RemoveDraft`. Обоснование: задача требует очистки временных данных при удалении Disposer (§5); `RemovePatch` — собственный патч жизненного цикла мода, где уже чистятся `Rules`/`QueueData`/`AutoScanner`; черновик — временное состояние того же класса. Патчи механики разборки не затронуты. Если это считать нарушением — строка удаляется одним revert, но тогда черновик будет жить до следующего `LoadContent` (не восстанавливается и на игру не влияет).

## Механика переживания ReloadTab (для ревью)

- Открытие: `OpenEditor` создаёт черновик в `Drafts[EntityId]`, открывает панель `Show(anchor, "DEQ.RuleEditor:{entityId}:{category}:{index}")`.
- Перезагрузка вкладки: `ShowTab` → `subPanel.Close(clearMemory:false)` → `IsOpenFor` сохраняется → в `Append` черновик валидируется (индекс существует, категория совпадает) → `ForceRebuildLayoutImmediate` → `OpenEditor` переиспользует черновик (значения тумблеров/типа/качества живы) → `Show` → на следующий кадр `RepositionWidget(connectorOnly:false)`.
- Закрытие пользователем (Save/Cancel/заголовок панели/ESC): `Close(clearMemory:true)` обнуляет `IsOpenFor` → черновик не переиспользуется (в `OpenEditor` — проверка `IsOpenFor == subId`) и выметается при ближайшем `LoadContent`. Восстановление закрытого редактора исключено.
- Удаление диспоузера: `RemoveDraft` из `OnRemove`-патча. Смена/уничтожение объекта: SubId привязан к EntityId, чужой объект черновик не поднимает; осиротевшие черновики чистятся в `Append`.

## Непроверенные сценарии (нужна игровая проверка)

Всё, что требует запуска игры (сборка и статические проверки выполнены, в-game валидация — за вами):

1. Создание нового правила (Weapons и Clothing) через Add rule → Save.
2. Редактирование существующего правила, в т.ч. сохранение.
3. Cancel: правило не меняется, редактор закрывается.
4. Переключение всех тумблеров: Rule active, Any quality, три Origin-тумблера.
5. Выбор конкретного типа в дропдауне (и «All types»).
6. Слайдер: drag с живым лейблом 0–300 %, блокировка при Any quality.
7. X-удаление строки правила.
8. Сохранение черновика при автообновлении очереди (DisposerStateChangedB во время редактирования) — редактор должен оставаться открытым со значениями.
9. Закрытие/повторное открытие вкладки сущности — редактор восстанавливается по SubId; закрытие редактора вручную — не восстанавливается.
10. Отсутствие ошибок/исключений в логе (особенно при перезагрузках вкладки и удалении диспоузера с открытым редактором).
11. Визуал: отсутствие наложений; футер с двумя кнопками; вёрстка `WContent2ButtonX` при длинных описаниях правил.

**Скриншоты:** с моей стороны недоступны (нет запуска игры) — пришлите скрины нового окна и редактора в чат после теста; сравню с ванильными образцами и при необходимости заведу follow-up задачу.

## Известные оставшиеся риски

- Геометрия префабов в ассет-бандлах — точную вёрстку подтверждает только игра.
- Индекс правила в SubId: если во время редактирования правило с меньшим индексом удалено (X-кнопка под панелью — маловероятно кликабельна), восстановление может указать на соседнее правило той же категории. Защита по категории стоит, полный защитой будет привязка черновика к id правила.
- Редкий «мусор»: черновик диспоузера, удалённого между `Append`-ами, лежит в словаре до ближайшей перезагрузки вкладки диспоузера (на поведение не влияет).
- Артефакты сборки (`Libraries/`, `.vscode/obj/`) не в .gitignore — при желании можно добавить репозиторный `.gitignore` (решение за вами).
