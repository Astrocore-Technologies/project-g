shader_type spatial;
render_mode cull_back;
#include "res://Assets/ArtDirection/ForestReference/Shaders/painted_lighting.gdshaderinc"
uniform sampler2D surface_paint : source_color,filter_linear_mipmap_anisotropic,repeat_enable;
uniform vec3 surface_tint : source_color=vec3(.73,.76,.68);
uniform float paint_strength : hint_range(0.0,1.0)=.65;
varying vec3 paint_position;
varying vec3 paint_normal;
void vertex(){paint_position=VERTEX;paint_normal=NORMAL;}
void fragment(){
 vec3 w=pow(abs(paint_normal),vec3(4.0));w/=max(w.x+w.y+w.z,.0001);
 vec3 p=texture(surface_paint,vec2(paint_position.z*.25,paint_position.y*.15)).rgb*w.x
  +texture(surface_paint,paint_position.xz*.25).rgb*w.y
  +texture(surface_paint,vec2(paint_position.x*.25,paint_position.y*.15)).rgb*w.z;
 ALBEDO=mix(surface_tint,surface_tint*p*1.25,paint_strength);
 ROUGHNESS=.92;SPECULAR=.05;
}
void light(){
 DIFFUSE_LIGHT+=ALBEDO*LIGHT_COLOR*painted_response(dot(NORMAL,LIGHT),ATTENUATION)/PI;
}
