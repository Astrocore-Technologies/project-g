shader_type spatial;
render_mode blend_mix,depth_draw_opaque,cull_disabled,shadows_disabled;
uniform sampler2D depth_texture : hint_depth_texture,filter_nearest,repeat_disable;
uniform sampler2D water_paint : source_color,filter_linear_mipmap_anisotropic,repeat_enable;
uniform sampler2D foam_paint : source_color,filter_linear_mipmap_anisotropic,repeat_enable;
uniform vec3 shallow_colour : source_color=vec3(.27,.66,.61);
uniform vec3 deep_colour : source_color=vec3(.055,.30,.37);
uniform vec3 foam_colour : source_color=vec3(.76,.87,.78);
uniform float flow_speed : hint_range(0.0,2.0)=.65;
uniform float shore_width : hint_range(0.01,1.0)=.32;
uniform float depth_colour_distance=1.8;
uniform float minimum_opacity : hint_range(0.0,1.0)=.28;
uniform float opacity_depth=.75;
uniform float shore_fade_distance=0.0;
varying vec3 world_position;
void vertex(){
 world_position=(MODEL_MATRIX*vec4(VERTEX,1.0)).xyz;
 VERTEX.y+=.016*sin(world_position.x*.7+world_position.z*.5-TIME*1.2);
}
void fragment(){
 float scene_depth=texture(depth_texture,SCREEN_UV).r;
 vec4 pos=INV_PROJECTION_MATRIX*vec4(SCREEN_UV*2.0-1.0,scene_depth,1.0);
 vec3 floor_world=(INV_VIEW_MATRIX*vec4(pos.xyz/pos.w,1.0)).xyz;
 float depth=max(world_position.y-floor_world.y,0.0);
 if(scene_depth<.00001) depth=depth_colour_distance;
 vec2 flow=world_position.xz*vec2(.13,.075)-vec2(0.0,TIME*flow_speed*.05);
 vec3 paint=texture(water_paint,flow).rgb;
 float brush=dot(texture(foam_paint,flow*1.4).rgb,vec3(.333));
 vec3 colour=mix(shallow_colour,deep_colour,smoothstep(.0,depth_colour_distance,depth));
 colour*=mix(.94,1.08,paint.g);
 float shore=(1.0-smoothstep(.025,shore_width,depth))*smoothstep(.18,.64,brush);
 float ripples=smoothstep(.42,.72,brush)*.22;
 float crest=pow(.5+.5*sin(flow.y*18.0+sin(flow.x*9.0)*.8),18.0)*.075;
 ripples+=crest;
 ALBEDO=mix(colour,foam_colour,clamp(shore*.75+ripples,0.0,.8));
 ALPHA=mix(minimum_opacity,.92,smoothstep(0.0,opacity_depth,depth));
 if(shore_fade_distance>0.0) ALPHA*=smoothstep(0.0,shore_fade_distance,depth);
 ROUGHNESS=.32;SPECULAR=.4;
 // Broad restrained ripples avoid glitter at the actual gameplay distance.
 NORMAL_MAP=vec3(.5+.032*sin(flow.y*20.0+TIME),.5+.032*cos(flow.x*18.0-TIME*.7),1.0);
 NORMAL_MAP_DEPTH=.45;
}
void light(){
 float nl=smoothstep(-.15,.65,dot(NORMAL,LIGHT));
 DIFFUSE_LIGHT+=ALBEDO*LIGHT_COLOR*mix(.65,1.0,nl)*ATTENUATION/PI;
 float highlight=pow(max(0.0,dot(NORMAL,normalize(LIGHT+VIEW))),48.0);
 SPECULAR_LIGHT+=LIGHT_COLOR*highlight*.32*ATTENUATION;
}


