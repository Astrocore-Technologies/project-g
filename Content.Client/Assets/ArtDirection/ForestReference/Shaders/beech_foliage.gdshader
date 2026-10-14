shader_type spatial;
render_mode cull_disabled,specular_disabled;
#include "res://Assets/ArtDirection/ForestReference/Shaders/painted_lighting.gdshaderinc"
uniform sampler2D leaf_paint : source_color,filter_linear_mipmap_anisotropic,repeat_enable;
uniform vec3 leaf_tint : source_color = vec3(0.46,0.65,0.29);
uniform vec3 base_tint : source_color = vec3(0.22,0.37,0.12);
uniform float paint_strength : hint_range(0.0,1.0) = 0.55;
uniform float crown_height = 12.0;
uniform float crown_floor = 4.0;
uniform vec3 crown_centre = vec3(0.0,8.7,0.0);
uniform float volume_normals : hint_range(0.0,1.0) = 0.58;
uniform vec2 wind_direction = vec2(0.8,0.35);
uniform float wind_strength : hint_range(0.0,0.5) = 0.12;
uniform float translucency : hint_range(0.0,0.5) = 0.10;
varying vec3 paint_position;
varying vec3 paint_normal;
varying float crown_top;
void vertex(){
 paint_position=VERTEX;
 paint_normal=NORMAL;
 crown_top=clamp((VERTEX.y-crown_floor)/max(crown_height-crown_floor,1.0),0.0,1.0);
 vec3 world=(MODEL_MATRIX*vec4(VERTEX,1.0)).xyz;
 float gust=sin(TIME*1.05+world.x*.12+world.z*.09)+.22*sin(TIME*2.2+world.z*.55);
 VERTEX.xz+=wind_direction*gust*wind_strength*COLOR.r;
 // Artist-controlled crown normals emphasise the whole canopy volume.
 vec3 volume=normalize((paint_position-crown_centre)*vec3(.55,.9,.55));
 NORMAL=normalize(mix(NORMAL,volume,volume_normals));
}
void fragment(){
 vec3 w=pow(abs(paint_normal),vec3(3.0));w/=max(w.x+w.y+w.z,.0001);
 vec3 paint=texture(leaf_paint,paint_position.yz*.18).rgb*w.x+texture(leaf_paint,paint_position.xz*.18).rgb*w.y+texture(leaf_paint,paint_position.xy*.18).rgb*w.z;
 vec3 base=mix(base_tint,leaf_tint,smoothstep(.05,.95,crown_top));
 // Texture supplies restrained leaf detail; vertical colour shapes the volume.
 ALBEDO=base*mix(vec3(.88),clamp(paint*4.0,vec3(.40),vec3(1.45)),paint_strength)*mix(.91,1.03,COLOR.g);
 ROUGHNESS=1.0;
}
void light(){
 float nl=dot(NORMAL,LIGHT);
 vec3 response=painted_response(nl,ATTENUATION);
 float back=pow(max(0.0,dot(-LIGHT,VIEW)),4.0)*translucency*ATTENUATION;
 DIFFUSE_LIGHT+=ALBEDO*LIGHT_COLOR*(response+vec3(.82,1.0,.48)*back)/PI;
}

