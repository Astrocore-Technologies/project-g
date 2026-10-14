shader_type spatial;
render_mode cull_disabled,specular_disabled;
#include "res://Assets/ArtDirection/ForestReference/Shaders/painted_lighting.gdshaderinc"
uniform vec3 root_colour : source_color=vec3(.24,.36,.12);
uniform vec3 tip_colour : source_color=vec3(.47,.61,.22);
uniform vec2 wind_direction=vec2(.8,.35);
uniform float wind_strength : hint_range(0.0,0.5)=.10;
uniform vec3 focus_world=vec3(-10000.0);
varying float blade_height;
varying float variation;
void vertex(){
 blade_height=UV.y;
 vec3 world=(MODEL_MATRIX*vec4(VERTEX,1.0)).xyz;
 variation=.5+.5*sin(world.x*.65+world.z*.48);
 float gust=sin(TIME*1.05+world.x*.12+world.z*.09)+.22*sin(TIME*2.2+world.z*.55);
 VERTEX.xz+=wind_direction*gust*wind_strength*blade_height*blade_height;
 // Upward-biased normals unite adjacent blades under high-angle lighting.
 NORMAL=normalize(mix(NORMAL,vec3(0.0,1.0,0.0),.72));
}
void fragment(){
 ALBEDO=mix(root_colour,tip_colour,smoothstep(.0,1.0,blade_height))*mix(.92,1.05,variation);
 ROUGHNESS=1.0;
}
void light(){
 float back=pow(max(0.0,dot(-LIGHT,VIEW)),3.0)*.10*ATTENUATION;
 DIFFUSE_LIGHT+=ALBEDO*LIGHT_COLOR*(painted_response(dot(NORMAL,LIGHT),ATTENUATION)+back)/PI;
}
