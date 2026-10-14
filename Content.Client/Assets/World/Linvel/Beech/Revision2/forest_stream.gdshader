shader_type spatial;
render_mode cull_disabled, specular_disabled;
uniform float flow_speed : hint_range(0.0,2.0)=.7;
uniform sampler2D water_paint : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D foam_paint : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
varying vec3 world_position;
void vertex(){
 world_position=(MODEL_MATRIX*vec4(VERTEX,1.0)).xyz;
 VERTEX.y+=sin(world_position.z*.8+world_position.x*.4-TIME*flow_speed)*.012;
}
void fragment(){
 // Broad painted colour and long foam strokes, rather than dot noise.
 vec2 flow_uv=world_position.xz*vec2(.11,.055)-vec2(0.0,TIME*flow_speed*.018);
 vec3 paint=texture(water_paint,flow_uv).rgb;
 vec3 foam=texture(foam_paint,flow_uv*1.35+vec2(.17,.08)).rgb;
 float foam_strength=smoothstep(.18,.7,dot(foam,vec3(.333)))*.34;
 ALBEDO=mix(paint,vec3(.43,.66,.60),foam_strength);
 float edge=min(UV.x,1.0-UV.x)-.018*sin(world_position.z*1.7+world_position.x*.9);
 ALPHA=smoothstep(0.0,.07,edge);
 ALPHA_SCISSOR_THRESHOLD=.35;
 ROUGHNESS=1.0;
}
void light(){
 float shade=mix(.65,1.0,smoothstep(.05,.75,dot(NORMAL,LIGHT)));
 DIFFUSE_LIGHT+=ALBEDO*LIGHT_COLOR*ATTENUATION*shade/PI;
}
