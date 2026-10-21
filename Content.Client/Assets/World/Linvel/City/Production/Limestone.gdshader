shader_type spatial;
render_mode specular_disabled, cull_disabled;
#include "res://Assets/World/Linvel/City/Materials/painted_light.gdshaderinc"
uniform sampler2D stone_paint : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform float texture_density = 0.22;
uniform vec4 tint : source_color = vec4(1.0);
varying vec3 position_world;
varying vec3 normal_world;
void vertex() {
    position_world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    normal_world = normalize(MODEL_NORMAL_MATRIX * NORMAL);
}
void fragment() {
    vec2 uv = position_world.xz;
    if (abs(normal_world.y) < 0.6) {
        uv = abs(normal_world.x) > abs(normal_world.z) ? position_world.zy : position_world.xy;
    }
    ALBEDO = texture(stone_paint, uv * texture_density).rgb * tint.rgb;
    ROUGHNESS = 0.94;
    SPECULAR = 0.0;
}

void light() {
    DIFFUSE_LIGHT += painted_diffuse(NORMAL, LIGHT) * LIGHT_COLOR * ATTENUATION / PI;
}
