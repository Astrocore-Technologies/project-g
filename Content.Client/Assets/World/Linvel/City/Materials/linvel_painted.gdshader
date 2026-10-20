shader_type spatial;
render_mode diffuse_toon, specular_disabled, cull_disabled;

// Existing Linvel atlas shared by the first city part; source GLBs stay unchanged.
uniform sampler2D albedo_texture : source_color, filter_linear_mipmap, repeat_disable;
uniform vec4 tint : source_color = vec4(1.0);
uniform float roughness : hint_range(0.0, 1.0) = 0.85;

void fragment() {
    ALBEDO = texture(albedo_texture, UV).rgb * tint.rgb;
    ROUGHNESS = roughness;
}
