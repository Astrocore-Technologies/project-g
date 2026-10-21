shader_type spatial;
render_mode specular_disabled, cull_disabled;
#include "res://Assets/World/Linvel/City/Materials/painted_light.gdshaderinc"

// Existing Linvel atlas shared by the first city part; source GLBs stay unchanged.
uniform sampler2D albedo_texture : source_color, filter_linear_mipmap, repeat_disable;
uniform vec4 tint : source_color = vec4(1.0);
uniform float roughness : hint_range(0.0, 1.0) = 0.85;

void fragment() {
    ALBEDO = texture(albedo_texture, UV).rgb * tint.rgb;
    ROUGHNESS = roughness;
    SPECULAR = 0.0;
}

void light() {
    DIFFUSE_LIGHT += painted_diffuse(NORMAL, LIGHT) * LIGHT_COLOR * ATTENUATION / PI;
}
