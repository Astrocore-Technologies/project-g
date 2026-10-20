shader_type spatial;
render_mode specular_disabled, cull_disabled;

uniform sampler2D paving_texture : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform float tiles_per_metre = 0.25;
varying vec3 world_position;
varying float top_face;

void vertex() {
	world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	top_face = abs(NORMAL.y);
}

void fragment() {
	ALBEDO = top_face > 0.5 ? texture(paving_texture, world_position.xz * tiles_per_metre + vec2(0.5)).rgb : vec3(0.56, 0.57, 0.52);
	ROUGHNESS = 1.0;
}

void light() {
	float shade = mix(0.45, 1.0, smoothstep(0.05, 0.7, dot(NORMAL, LIGHT)));
	DIFFUSE_LIGHT += ALBEDO * LIGHT_COLOR * ATTENUATION * shade / PI;
}
