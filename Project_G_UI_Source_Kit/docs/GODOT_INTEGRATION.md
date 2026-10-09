# Godot 4: перенос UI-starter

## Что подготовлено

`project.godot` — отдельный демонстрационный проект. `Main.tscn` объединяет фон, `NativeHud.tscn`, `InventoryPanel.tscn` и `GuildPanel.tscn`. Все панели построены на нативных контролах, а не на текстуре полноэкранного SVG.

`themes/adventurer_theme.tres` содержит `Theme` и ресурсы `StyleBoxFlat`: панели, поля, обычные/hover/pressed/disabled/focus состояния кнопок, светлые/тёмные надписи, progress bar. Вариации: `PrimaryButton`, `SecondaryButton`, `GoldButton`, `SkillButton`, `Parchment`, `DarkPanel`, `Header`, `Muted`, `OnDark`, `OnDarkHeader`.

Компоненты: `PrimaryButton.tscn`, `ParchmentPanel.tscn`, `SkillSlot.tscn`. У слота есть экспортируемые `hotkey` и `icon_texture`. Их можно менять в инспекторе.

## Запуск

Импортировать `godot/project.godot`, дождаться импорта ресурсов, открыть `Main.tscn` и запустить проект. В native demo `B` открывает инвентарь, `G` — Гильдию, `Esc` закрывает панель. Кнопки остальных разделов объясняют, где находится полный SVG/HTML-дизайн.

Нативная реализация ограничена тремя экранами и базовыми действиями. Точное соответствие браузерной версии, все вкладки, анимации и сетевые сценарии не заявляются.

**Runtime-проверка в движке не выполнена.** Godot не был доступен в среде подготовки. Проверены только структура файлов и ссылки ресурсов. Перед включением в основной проект надо проверить импорт, GDScript и метрики используемого шрифта в вашей версии редактора.

## Применение в C#-клиенте

Не требуется менять архитектуру Client/Shared/Server. Скопируйте тему и нужные сцены в соответствующий каталог клиента, адаптируя `res://`-пути. GDScript обработчика `main.gd` — пример, а не требование к языку проекта. Его можно заменить C#-контроллером.

Текущий демонстрационный корень испускает `ui_intent(action: String)`. В C# это можно подключить через `Connect`:

```csharp
using Godot;

public partial class UiAdapter : Node
{
    [Export] public NodePath UiRootPath { get; set; } = new NodePath("../UIStarter");

    public override void _Ready()
    {
        var ui = GetNodeOrNull<Control>(UiRootPath);
        if (ui == null)
        {
            GD.PushError("UI root not found: check UiRootPath.");
            return;
        }

        ui.Connect("ui_intent", Callable.From<string>(OnUiIntent));
    }

    private void OnUiIntent(string action)
    {
        // Route a known, whitelisted UI action into the client's application layer.
        // Do not award items or mutate authoritative state here.
        GD.Print($"UI intent: {action}");
    }
}
```

Пример C# не компилировался в среде подготовки; он не подключён автоматически. Для реальной работы удобнее заменить строковые действия типизированными клиентскими командами и передавать стабильные ID предметов/контрактов вместо индексов демо.

## Правильные границы ввода

Декоративные `TextureRect` и свободный корень HUD используют `mouse_filter = IGNORE`. Панели и кнопки принимают ввод. Игровой click-to-move следует обрабатывать после UI, например через `_UnhandledInput`, а не безусловно из `_Input`.

Фокусированный `LineEdit` не должен одновременно отправлять боевые команды. Большие окна здесь блокируют указатель, но это **не** ставит сетевой мир на паузу. Нативный демо-контроллер использует `_unhandled_key_input` и проверяет владельца GUI-фокуса.

## SVG: макет и ассет — разные вещи

Полноэкранные SVG предназначены для дизайна и браузерного прототипа. Не импортируйте их как единственную TextureRect-картинку игрового интерфейса. В Godot SVG импортируются как растр; текст в SVG имеет ограничения при импорте. Текст интерфейса в игре оставляйте в `Label`/`RichTextLabel`.

Иконки в `godot/icons` содержат только простые геометрические контуры, без текста и фильтров, в белом цвете для tint/modulate. Аналогичные исходники в `assets/icons` сине-серые. Шрифтовые файлы в архиве отсутствуют; `SystemFont` в starter использует системные семейства и fallback.

## Что ещё нужно до production

Проверить layout через anchors и контейнеры на целевых разрешениях; подключить штатные игровые шрифты и локализацию; согласовать масштаб UI; реализовать адаптеры состояния, ошибки и загрузку; подключить настоящий 3D-preview; перенести остальные пять экранов; добавить accessibility-настройки и ремаппинг; проверить PvP, фокус и отсутствие click-through под нагрузкой.

## Официальная документация, использованная при подготовке

* Theme / GUI skinning: https://docs.godotengine.org/en/stable/tutorials/ui/gui_skinning.html
* Control / input and mouse_filter: https://docs.godotengine.org/en/stable/classes/class_control.html
* StyleBoxFlat: https://docs.godotengine.org/en/stable/classes/class_styleboxflat.html
* Importing images / SVG: https://docs.godotengine.org/en/stable/tutorials/assets_pipeline/importing_images.html

Ссылки проверены при подготовке комплекта. Этот набор не фиксирует «последнюю стабильную» версию Godot и не меняет выбранную проектом версию движка.
