extends Resource
class_name ForestLightingPreset
## Editable art settings for the reference; no world time or weather simulation.
@export var display_name := "День"
@export var sun_rotation_degrees := Vector3(-52,-32,0)
@export var sun_colour := Color(1,.94,.81)
@export_range(0.0,4.0) var sun_energy := 1.15
@export var ambient_colour := Color(.67,.78,.79)
@export_range(0.0,2.0) var ambient_energy := .52
@export var fog_colour := Color(.72,.81,.72)
@export_range(0.0,.02,.0001) var fog_density := .0025
