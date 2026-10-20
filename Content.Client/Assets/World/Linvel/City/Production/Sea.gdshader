shader_type spatial;
render_mode diffuse_toon, cull_disabled, shadows_disabled;
uniform vec3 deep_colour : source_color = vec3(0.045, 0.31, 0.46);
uniform vec3 shallow_colour : source_color = vec3(0.17, 0.65, 0.66);
varying vec3 position_world;
void vertex() {
    position_world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}
void fragment() {
    // Bounded painted shallows avoid depth feedback on the Mobile renderer.
    vec2 bay = (position_world.xz - vec2(-20.0, 110.0)) / vec2(310.0, 105.0);
    float shallows = exp(-dot(bay, bay)) * 0.65;
    float wash = 0.003 * sin(position_world.x * 0.63 + sin(position_world.z * 0.31) + TIME * 0.22);
    ALBEDO = mix(deep_colour, shallow_colour, shallows) + vec3(wash);
    ROUGHNESS = 0.6;
    SPECULAR = 0.18;
}
