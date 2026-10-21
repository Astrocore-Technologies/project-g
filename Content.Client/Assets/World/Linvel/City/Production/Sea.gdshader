shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_back;

group_uniforms Palette;
uniform vec3 shallow_colour : source_color = vec3(0.17, 0.67, 0.66);
uniform vec3 deep_colour : source_color = vec3(0.025, 0.26, 0.43);
uniform vec3 foam_colour : source_color = vec3(0.84, 0.94, 0.89);
uniform float colour_depth : hint_range(1.0, 24.0) = 6.5;
uniform float clarity : hint_range(0.1, 8.0) = 2.4;
group_uniforms Motion;
uniform vec2 wind_direction = vec2(0.8, 0.35);
uniform float wave_height : hint_range(0.0, 0.5) = 0.18;
uniform float wave_length : hint_range(8.0, 40.0) = 22.0;
uniform float wave_speed : hint_range(0.0, 3.0) = 1.1;
uniform float ripple_strength : hint_range(0.0, 0.3) = 0.075;
uniform vec4 harbor_bounds = vec4(-160.0, 105.0, 110.0, 215.0);
uniform float harbor_calm : hint_range(0.0, 1.0) = 0.8;
group_uniforms Shore;
uniform sampler2D shore_field : filter_linear, repeat_disable;
// R: bed depth / 16 m, G: distance to land / 32 m, B: land mask.
uniform vec4 field_bounds = vec4(-480.0, -240.0, 480.0, 640.0);
uniform sampler2D water_paint : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D foam_paint : source_color, filter_linear_mipmap, repeat_enable;
uniform float foam_width : hint_range(0.1, 4.0) = 2.7;
uniform float foam_strength : hint_range(0.0, 1.0) = 0.62;
uniform float contact_width : hint_range(0.02, 1.0) = 0.22;
group_uniforms Reflection;
uniform float reflection_strength : hint_range(0.0, 1.0) = 0.36;
uniform float sun_glint : hint_range(0.0, 1.0) = 0.12;
uniform float water_roughness : hint_range(0.1, 1.0) = 0.38;
group_uniforms;
uniform sampler2D depth_texture : hint_depth_texture, filter_nearest, repeat_disable;
varying vec3 water_world;
varying float shelter;

vec2 field_uv(vec2 world_xz) {
	return (world_xz - field_bounds.xy) / (field_bounds.zw - field_bounds.xy);
}
float harbor_shelter(vec2 p) {
	vec2 edge = smoothstep(harbor_bounds.xy - vec2(22.0), harbor_bounds.xy + vec2(10.0), p)
		* (1.0 - smoothstep(harbor_bounds.zw - vec2(12.0), harbor_bounds.zw + vec2(22.0), p));
	return edge.x * edge.y;
}
void vertex() {
	vec3 world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	vec2 p = world.xz;
	shelter = harbor_shelter(p);
	float shore = textureLod(shore_field, field_uv(p), 0.0).g * 32.0;
	float edge = 1.0 - smoothstep(0.88, 1.0, max(abs(p.x) / 400.0, abs(p.y - 200.0) / 320.0));
	float amplitude = wave_height * mix(1.0, 1.0 - harbor_calm, shelter) * smoothstep(0.4, 5.0, shore) * edge;
	vec2 direction = normalize(wind_direction + vec2(0.0001));
	float phase = dot(p, direction) * (6.283185 / wave_length) - TIME * wave_speed;
	float cross_phase = dot(p, vec2(-direction.y, direction.x)) * 0.43 - TIME * wave_speed * 0.73;
	VERTEX.y += amplitude * (sin(phase) + 0.28 * sin(cross_phase));
	water_world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}
void fragment() {
	vec2 p = water_world.xz;
	vec2 uv_field = field_uv(p);
	bool in_field = all(greaterThanEqual(uv_field, vec2(0.0))) && all(lessThanEqual(uv_field, vec2(1.0)));
	vec3 field = texture(shore_field, clamp(uv_field, vec2(0.0), vec2(1.0))).rgb;
	float shore = in_field ? field.g * 32.0 : 32.0;
	float bed_depth = in_field ? field.r * 16.0 : 8.0;
	float raw_depth = textureLod(depth_texture, SCREEN_UV, 0.0).r;
	vec4 floor_view = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, raw_depth, 1.0);
	vec3 floor_world = (INV_VIEW_MATRIX * vec4(floor_view.xyz / floor_view.w, 1.0)).xyz;
	float depth = raw_depth > 0.000001 ? max(water_world.y - floor_world.y, 0.0) : 80.0;
	// Bed distance is stable across views. Screen depth adds contact with piles.
	float tint_depth = min(depth, max(bed_depth, 0.05));
	vec2 direction = normalize(wind_direction + vec2(0.0001));
	vec2 flow = p * 0.055 - direction * TIME * wave_speed * 0.014;
	float paint = texture(water_paint, flow).g;
	float brush = texture(foam_paint, p * 0.13 + direction * TIME * 0.022).g;
	float distance_to_camera = length(CAMERA_POSITION_WORLD - water_world);
	float detail = 1.0 - smoothstep(70.0, 300.0, distance_to_camera);
	float ripple_a = dot(p, direction) * 1.75 - TIME * wave_speed * 1.2;
	float ripple_b = dot(p, vec2(-direction.y, direction.x)) * 1.22 + TIME * 0.65;
	float broad_phase = dot(p, direction) * (6.283185 / wave_length) - TIME * wave_speed;
	float calm = mix(1.0, 1.0 - harbor_calm, shelter);
	vec2 slope = direction * cos(broad_phase) * wave_height * 0.7 * calm;
	slope += vec2(sin(ripple_a + paint * 1.8), cos(ripple_b + paint)) * ripple_strength * detail * mix(1.0, 0.55, shelter);
	vec3 normal_world = normalize(vec3(-slope.x, 1.0, -slope.y));
	NORMAL = normalize((VIEW_MATRIX * vec4(normal_world, 0.0)).xyz);
	float depth_blend = 1.0 - exp(-tint_depth / colour_depth);
	vec3 colour = mix(shallow_colour, deep_colour, clamp(depth_blend * 1.28, 0.0, 1.0));
	colour *= mix(0.96, 1.04, paint);
	float foam_distance = shore + (brush - 0.5) * 0.7;
	float wash = 0.5 + 0.5 * sin(shore * 2.8 - TIME * 1.25 + paint * 3.0);
	float shore_foam = (1.0 - smoothstep(0.12, foam_width, foam_distance)) * mix(0.35, 1.0, wash);
	float surf_line = (1.0 - smoothstep(0.12, 0.42, abs(shore - (0.7 + 0.6 * sin(TIME * 0.65))))) * (1.0 - shelter * 0.6);
	float contact = (1.0 - smoothstep(0.025, contact_width, depth));
	float foam = max(shore_foam, max(surf_line * 0.45, contact)) * smoothstep(0.15, 0.65, brush) * foam_strength;
	float crest = smoothstep(0.94, 1.0, sin(ripple_a * 0.4 + paint * 3.0)) * smoothstep(0.48, 0.8, brush) * 0.07 * detail;
	ALBEDO = mix(colour, foam_colour, clamp(foam + crest, 0.0, 0.85));
	float fresnel = pow(1.0 - max(dot(NORMAL, VIEW), 0.0), 4.0);
	ROUGHNESS = clamp(water_roughness + (1.0 - detail) * 0.24 + foam * 0.35, 0.0, 1.0);
	SPECULAR = reflection_strength;
	float opacity = 1.0 - exp(-depth / clarity);
	ALPHA = clamp(mix(0.12, 1.0, opacity) + fresnel * 0.28 + foam * 0.4, 0.0, 1.0);
}
void light() {
	float facing = smoothstep(-0.15, 0.75, dot(NORMAL, LIGHT));
	DIFFUSE_LIGHT += mix(0.38, 0.85, facing) * LIGHT_COLOR * ATTENUATION / PI;
	float highlight = pow(max(dot(NORMAL, normalize(LIGHT + VIEW)), 0.0), 72.0);
	SPECULAR_LIGHT += LIGHT_COLOR * smoothstep(0.12, 0.85, highlight) * sun_glint * ATTENUATION;
}
