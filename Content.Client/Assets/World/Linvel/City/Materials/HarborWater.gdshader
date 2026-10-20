shader_type spatial;
render_mode depth_draw_opaque, cull_disabled, shadows_disabled;

// Opaque painted bay colour keeps the seabed from looking like muddy water.
// Existing Linvel paint textures are reused; depth adds a narrow shoreline wash.
uniform sampler2D depth_texture : hint_depth_texture, filter_nearest, repeat_disable;
uniform sampler2D water_paint : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D foam_paint : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform vec3 shallow_colour : source_color = vec3(0.20, 0.59, 0.65);
uniform vec3 deep_colour : source_color = vec3(0.045, 0.25, 0.39);
uniform vec3 foam_colour : source_color = vec3(0.78, 0.91, 0.87);
uniform float flow_speed = 0.22;
uniform float depth_colour_distance = 2.0;
uniform float shore_width = 0.18;
varying vec3 world_position;

void vertex() {
	world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	VERTEX.y += 0.016 * sin(world_position.x * 0.7 + world_position.z * 0.5 - TIME * 1.2);
}

void fragment() {
	float scene_depth = texture(depth_texture, SCREEN_UV).r;
	vec4 view_floor = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, scene_depth, 1.0);
	vec3 floor_world = (INV_VIEW_MATRIX * vec4(view_floor.xyz / view_floor.w, 1.0)).xyz;
	float depth = max(world_position.y - floor_world.y, 0.0);
	if (scene_depth < 0.00001) depth = depth_colour_distance;
	vec2 flow = world_position.xz * vec2(0.13, 0.075) - vec2(0.0, TIME * flow_speed * 0.05);
	float paint = texture(water_paint, flow).g;
	float brush = dot(texture(foam_paint, flow * 1.4).rgb, vec3(0.333));
	vec3 colour = mix(shallow_colour, deep_colour, smoothstep(0.0, depth_colour_distance, depth));
	colour *= mix(0.94, 1.08, paint);
	float shore = (1.0 - smoothstep(0.025, shore_width, depth)) * smoothstep(0.18, 0.64, brush);
	float ripples = smoothstep(0.42, 0.72, brush) * 0.12;
	float crest = pow(0.5 + 0.5 * sin(flow.y * 18.0 + sin(flow.x * 9.0) * 0.8), 18.0) * 0.055;
	ALBEDO = mix(colour, foam_colour, clamp(shore * 0.65 + ripples + crest, 0.0, 0.7));
	ROUGHNESS = 0.65;
	SPECULAR = 0.2;
}

void light() {
	float nl = smoothstep(-0.15, 0.65, dot(NORMAL, LIGHT));
	DIFFUSE_LIGHT += ALBEDO * LIGHT_COLOR * mix(0.65, 1.0, nl) * ATTENUATION / PI;
}
