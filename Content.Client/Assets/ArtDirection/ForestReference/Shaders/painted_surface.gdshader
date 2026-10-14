shader_type spatial;
render_mode cull_back;
#include "res://Assets/ArtDirection/ForestReference/Shaders/painted_lighting.gdshaderinc"
uniform sampler2D surface_paint : source_color,filter_linear_mipmap_anisotropic,repeat_enable;
uniform vec3 surface_tint : source_color = vec3(0.86,0.90,0.80);
uniform float paint_strength : hint_range(0.0,1.0) = 0.72;
uniform float roughness : hint_range(0.0,1.0) = 0.92;
uniform float specular_strength : hint_range(0.0,1.0) = 0.05;
uniform bool use_vertex_colour = false;
void fragment(){
 vec3 p=texture(surface_paint,UV).rgb;
 ALBEDO=mix(surface_tint,surface_tint*p*1.25,paint_strength);
 if(use_vertex_colour) ALBEDO*=mix(vec3(1.0),COLOR.rgb,.2);
 ROUGHNESS=roughness;SPECULAR=specular_strength;
}
void light(){
 DIFFUSE_LIGHT+=ALBEDO*LIGHT_COLOR*painted_response(dot(NORMAL,LIGHT),ATTENUATION)/PI;
 float highlight=pow(max(0.0,dot(NORMAL,normalize(LIGHT+VIEW))),mix(64.0,8.0,roughness));
 SPECULAR_LIGHT+=LIGHT_COLOR*ATTENUATION*highlight*specular_strength;
}
