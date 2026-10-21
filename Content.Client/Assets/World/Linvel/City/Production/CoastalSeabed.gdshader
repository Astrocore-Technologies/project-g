shader_type spatial;
render_mode cull_back, specular_disabled;
#include "res://Assets/World/Linvel/City/Materials/painted_light.gdshaderinc"
uniform sampler2D sand_paint : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D rock_paint : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D water_paint : source_color, filter_linear_mipmap, repeat_enable;
uniform vec3 sand_tint : source_color = vec3(0.74, 0.82, 0.68);
uniform float caustic_strength : hint_range(0.0, 0.3) = 0.08;
varying vec3 world_position;
void vertex() { world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; }
void fragment() {
	vec2 p = world_position.xz;
	float patches = smoothstep(0.44, 0.68, texture(water_paint, p * 0.022).g);
	vec3 sand = texture(sand_paint, p * 0.16).rgb * sand_tint;
	vec3 rock = texture(rock_paint, p * 0.13).rgb * vec3(0.67, 0.83, 0.80);
	ALBEDO = mix(sand, rock, patches * 0.65);
	float a = sin(p.x * 1.3 + sin(p.y * 0.8 + TIME * 0.4));
	float b = cos(p.y * 1.1 + sin(p.x * 0.9 - TIME * 0.3));
	float caustic = pow(clamp(1.0 - abs(a + b) * 0.7, 0.0, 1.0), 10.0);
	EMISSION = ALBEDO * caustic * caustic_strength;
	ROUGHNESS = 1.0;
	SPECULAR = 0.0;
}
void light() { DIFFUSE_LIGHT += painted_diffuse(NORMAL, LIGHT) * LIGHT_COLOR * ATTENUATION / PI; }
