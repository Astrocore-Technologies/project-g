shader_type canvas_item;
uniform sampler2D mask_texture : filter_linear;

void fragment() {
    vec4 portrait = texture(TEXTURE, UV);
    portrait.a *= texture(mask_texture, UV).a;
    COLOR = portrait;
}
