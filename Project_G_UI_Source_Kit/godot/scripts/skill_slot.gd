@tool
extends Button
## Editable native button. Cosmetic only; a server owns real cooldowns.

@export var hotkey: String = "Q":
	set(value):
		hotkey = value
		_refresh()

@export var icon_texture: Texture2D:
	set(value):
		icon_texture = value
		_refresh()

func _ready() -> void:
	_refresh()

func _refresh() -> void:
	if not is_inside_tree():
		return
	var key_label := get_node_or_null("Key") as Label
	var icon_view := get_node_or_null("Icon") as TextureRect
	if key_label != null:
		key_label.text = hotkey
	if icon_view != null:
		icon_view.texture = icon_texture
