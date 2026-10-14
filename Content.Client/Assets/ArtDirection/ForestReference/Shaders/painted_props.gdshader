shader_type spatial;
render_mode cull_disabled;
#include "res://Assets/ArtDirection/ForestReference/Shaders/painted_lighting.gdshaderinc"
uniform sampler2D bark_paint : source_color,filter_linear_mipmap_anisotropic,repeat_enable;
uniform sampler2D wood_paint : source_color,filter_linear_mipmap_anisotropic,repeat_enable;
uniform sampler2D stone_paint : source_color,filter_linear_mipmap_anisotropic,repeat_enable;
uniform sampler2D cloth_paint : source_color,filter_linear_mipmap_anisotropic,repeat_enable;
void fragment(){
 float family=floor(COLOR.a*20.0+.5)/20.0;
 vec3 paint=texture(wood_paint,UV).rgb;
 if(family>.775) paint=texture(bark_paint,UV).rgb;
 else if(family>.475&&family<.625) paint=texture(stone_paint,UV).rgb;
 else if(family>.325&&family<.475) paint=texture(cloth_paint,UV).rgb;
 vec3 tint=mix(vec3(.72),clamp(COLOR.rgb*2.0,vec3(.6),vec3(1.2)),.22);
 ALBEDO=paint*tint*1.35;
 ROUGHNESS=.9;SPECULAR=.05;
}
void light(){
 DIFFUSE_LIGHT+=ALBEDO*LIGHT_COLOR*painted_response(dot(NORMAL,LIGHT),ATTENUATION)/PI;
}
