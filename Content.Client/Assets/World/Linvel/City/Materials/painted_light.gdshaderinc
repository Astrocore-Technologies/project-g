// Shared soft anime light. Albedo remains the painted atlas.
group_uniforms PaintedLight;
uniform vec3 shadow_tint : source_color = vec3(0.68, 0.76, 0.88);
uniform float light_softness : hint_range(0.05, 1.0) = 0.48;
uniform float wrap_light : hint_range(0.0, 0.4) = 0.12;
uniform float shade_floor : hint_range(0.0, 0.5) = 0.18;
group_uniforms;
vec3 painted_diffuse(vec3 normal, vec3 light_direction) {
	float band = smoothstep(-light_softness, 0.65, dot(normal, light_direction) + wrap_light);
	return mix(shadow_tint * shade_floor, vec3(1.0), band);
}
