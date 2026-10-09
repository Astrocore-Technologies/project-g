extends Control
## Native UI-only starter. Emits intent; never mutates a real game inventory.
## Replace demo handlers with validated client/server commands in the game.
signal ui_intent(action: String)

@onready var inventory: Control = $InventoryPanel
@onready var guild: Control = $GuildPanel
@onready var message: AcceptDialog = $Message
@onready var toast_label: Label = $Toast
var selected_item: int = 0
var selected_quest: int = 0
var accepted: bool = false
var sale_locked: bool = false
var pvp_demo: bool = false

const ITEM_NAMES: Array[String] = [
	"Дорожный клинок +3", "Клинок стражи +2", "Щит дозора", "Шлем странника",
	"Лесная трава", "Малое зелье", "Осколок памяти", "Сапоги следопыта",
	"Полевой дневник", "Свет Старого пути", "Медный знак", "Карта переправы"
]
const QUEST_NAMES: Array[String] = [
	"Лесная дорога", "Лекарства для лечебницы", "Пропавший караван", "Следы у старой мельницы"
]

func _ready() -> void:
	_bind_actions(self)
	var search := inventory.get_node("Window/Content/Search") as LineEdit
	search.text_changed.connect(_filter_inventory)
	var chat := $NativeHud/Chat/Content/Input as LineEdit
	chat.text_submitted.connect(_demo_chat)

func _bind_actions(node: Node) -> void:
	if node is BaseButton and node.has_meta("action"):
		var action := str(node.get_meta("action"))
		(node as BaseButton).pressed.connect(_on_action.bind(action))
	for child in node.get_children():
		_bind_actions(child)

func _show_panel(name: String) -> void:
	inventory.visible = name == "inventory"
	guild.visible = name == "guild"
	if inventory.visible:
		(inventory.get_node("Window/Content/Close") as Button).grab_focus()
	elif guild.visible:
		(guild.get_node("Window/Content/Close") as Button).grab_focus()

func _message(title: String, body: String) -> void:
	message.title = title
	message.dialog_text = body
	message.popup_centered(Vector2i(680, 280))

func _on_action(action: String) -> void:
	ui_intent.emit(action)
	if action.begins_with("item_"):
		_select_item(action.trim_prefix("item_").to_int())
		return
	if action.begins_with("quest_"):
		_select_quest(action.trim_prefix("quest_").to_int())
		return
	if action.begins_with("skill_") or action.begins_with("echo_"):
		toast_label.text = "Локальное UI-намерение: " + action + " — без боевой симуляции"
		return
	match action:
		"inventory":
			_show_panel("inventory")
		"guild":
			_show_panel("guild")
		"close":
			_show_panel("")
		"compare":
			_message("Сравнение", "Дорожный клинок +3: урон 148, модификатор защиты −12.\nКлинок стражи +2: урон 136, модификатор защиты +4.\n\nДемонстрационные числа. Серверная модель не подключена.")
		"equip":
			toast_label.text = "Запрошена экипировка: " + ITEM_NAMES[selected_item] + " (локальное демо)"
		"lock":
			sale_locked = not sale_locked
			(inventory.get_node("Window/Content/Lock") as Button).text = "Снять защиту от продажи" if sale_locked else "Защитить от случайной продажи"
			toast_label.text = "Защита от продажи не меняет правила PvP-дропа."
		"accept":
			if selected_quest >= 2:
				_message("Недостаточно квалификации", "Для самостоятельного договора нужен более высокий ранг.\nЭто не запрещает персонажу входить в регион.")
			else:
				_message("Пример контракта", "Выбрано поручение: " + QUEST_NAMES[selected_quest] + ".\n\nНативный starter показывает сведения. Полный диалог подтверждения реализован в HTML-прототипе; реальное принятие должен подтвердить сервер.")
		"rank":
			_message("Ранги F → S", "F → E → D → C → B → A → S.\nРанг не равен уровню, профессии или рангу в гильдии игроков.\nТочные условия аттестации ещё требуют дизайна.")
		"pvp":
			_message("Правила PvP", "Вне безопасной зоны выключенный тег не гарантирует защиту от нападения.\nС включённым тегом при PvP-смерти может выпасть нелегендарная экипировка.\nЛегендарные предметы не выпадают в PvP.\n\nИзменение тега в этом starter не выполняется.")
		_:
			_message("Дополнительный экран", "Полный дизайн этого экрана есть в SVG и HTML-прототипе.\nНативный Godot starter содержит HUD, инвентарь и Гильдию.\nДействие для интеграции: " + action)

func _select_item(index: int) -> void:
	if index < 0 or index >= ITEM_NAMES.size():
		return
	selected_item = index
	(inventory.get_node("Window/Content/ItemName") as Label).text = ITEM_NAMES[index]
	var risk := inventory.get_node("Window/Content/RiskInfo") as Label
	var stats := inventory.get_node("Window/Content/Stats") as Label
	if index == 9:
		risk.text = "Легендарный предмет: не выпадает в PvP.\nЭто не означает неразрушимость при улучшении."
		stats.text = "Урон                              142\nМодификатор защиты                 0\nПрочность                         80 / 80"
	elif index == 0:
		risk.text = "Нелегендарная экипировка может выпасть\nпри PvP-смерти с включённым тегом."
		stats.text = "Урон                              148\nМодификатор защиты                −12\nПрочность                         68 / 80"
	elif index == 1:
		risk.text = "Нелегендарная экипировка может выпасть\nпри PvP-смерти с включённым тегом."
		stats.text = "Урон                              136\nМодификатор защиты                 4\nПрочность                         73 / 90"
	else:
		risk.text = "Подробные свойства не реализованы в нативном demo.\nРасширенный пример смотрите в HTML-прототипе."
		stats.text = "Демонстрационный предмет\nДанные ожидаются от клиентской модели."

func _select_quest(index: int) -> void:
	if index < 0 or index >= QUEST_NAMES.size():
		return
	selected_quest = index
	(guild.get_node("Window/Content/QuestTitle") as Label).text = QUEST_NAMES[index]
	(guild.get_node("Window/Content/InfoStatus") as Label).text = "Ранг " + ("F" if index < 2 else "E" if index == 2 else "D") + " · Сведения предварительные"
	if index == 0:
		(guild.get_node("Window/Content/Body") as Label).text = "Доставьте материалы смотрителю дороги\nи выясните, почему больше нет отчётов.\n\nТочное местонахождение не подтверждено.\n\nОтчёт о новой опасности — полезный результат."
		(guild.get_node("Window/Content/Reward") as Label).text = "Награда: 180 монет и запись в послужном списке."
	else:
		(guild.get_node("Window/Content/Body") as Label).text = "Демонстрационное поручение.\nПодробная карточка и подтверждение есть\nв HTML-прототипе.\n\nПри интеграции сведения приходят от сервера."
		(guild.get_node("Window/Content/Reward") as Label).text = "Награда определяется условиями договора."

func _filter_inventory(query: String) -> void:
	for index in range(ITEM_NAMES.size()):
		var item := inventory.get_node("Window/Content/Item" + str(index)) as Button
		item.visible = query.is_empty() or ITEM_NAMES[index].to_lower().contains(query.to_lower())

func _demo_chat(text: String) -> void:
	if not text.strip_edges().is_empty():
		toast_label.text = "Вы (не отправлено): " + text
	($NativeHud/Chat/Content/Input as LineEdit).clear()

func _unhandled_key_input(event: InputEvent) -> void:
	# A focused text input or button gets its events first. Do not route gameplay
	# input from _input() behind an interactive panel.
	var key_event := event as InputEventKey
	if key_event == null or not key_event.pressed or key_event.echo:
		return
	if message.visible:
		return
	var focus := get_viewport().gui_get_focus_owner()
	if focus is LineEdit or focus is TextEdit:
		return
	match key_event.physical_keycode:
		KEY_ESCAPE:
			_show_panel("")
			get_viewport().set_input_as_handled()
		KEY_B:
			_show_panel("" if inventory.visible else "inventory")
			get_viewport().set_input_as_handled()
		KEY_G:
			_show_panel("" if guild.visible else "guild")
			get_viewport().set_input_as_handled()
