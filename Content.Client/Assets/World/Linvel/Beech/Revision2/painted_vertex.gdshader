shader_type spatial;
render_mode specular_disabled, cull_disabled;
uniform vec3 focus_world = vec3(-10000.0);
uniform float crown_clearance = 5.0;
uniform sampler2D leaf_paint : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D bark_paint : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D wood_paint : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D stone_paint : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D cloth_paint : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
varying vec3 world_position;
varying vec3 paint_position;
varying vec3 paint_normal;
varying float crown_visibility;
void vertex(){
 bool leaf=COLOR.a>.975&&COLOR.g>COLOR.r*1.08&&COLOR.g>COLOR.b*1.15;
 if(leaf) VERTEX.x+=sin(TIME*.85+VERTEX.y*.6+VERTEX.z*.3)*.065;
 world_position=(MODEL_MATRIX*vec4(VERTEX,1.0)).xyz;
 paint_position=VERTEX;
 paint_normal=NORMAL;
 // A combined tree shares its ground-centred pivot. Hide a whole obstructing
 // crown, rather than carving an unnatural tube through individual leaf blobs.
 vec3 centre=(MODEL_MATRIX*vec4(0.0,11.0,0.0,1.0)).xyz;
 vec3 ray=focus_world-CAMERA_POSITION_WORLD;
 float t=dot(centre-CAMERA_POSITION_WORLD,ray)/max(dot(ray,ray),1.0);
 float distance_to_ray=length(centre-(CAMERA_POSITION_WORLD+ray*clamp(t,0.0,1.0)));
 float scale=max(length(MODEL_MATRIX[0].xyz),length(MODEL_MATRIX[2].xyz));
 float radius=6.5*scale+crown_clearance;
 crown_visibility=1.0;
 if(focus_world.y>-9999.0&&t>-.25&&t<1.15)
  crown_visibility=smoothstep(radius,radius+2.0,distance_to_ray);
}
void fragment(){
 float code=floor(COLOR.a*20.0+.5)/20.0;
 bool leaf=code>.975&&COLOR.g>COLOR.r*1.08&&COLOR.g>COLOR.b*1.15;
 bool high_branch=code>.775&&code<.925&&world_position.y>focus_world.y+4.0;
 if((leaf&&world_position.y>focus_world.y+3.0)||high_branch){
  // Opaque dither keeps Mobile depth sorting and draw calls unchanged.
  float stipple=fract(dot(floor(FRAGCOORD.xy),vec2(.754877666,.569840296)));
  if(crown_visibility<=0.0||crown_visibility<stipple) discard;
 }
 float wash=.5+.25*sin(world_position.x*.75+world_position.z*.31)+.25*sin(world_position.z*.81+world_position.y*.4);
 vec3 base=COLOR.rgb*(.87+.14*wash);
 if(leaf){
  // Soft triplanar paint removes per-face projection seams on rounded crowns.
  vec3 weights=pow(abs(paint_normal),vec3(4.0));
  weights/=max(weights.x+weights.y+weights.z,.0001);
  vec3 painted_leaf=texture(leaf_paint,paint_position.yz*.07).rgb*weights.x
   +texture(leaf_paint,paint_position.xz*.07).rgb*weights.y
   +texture(leaf_paint,paint_position.xy*.07).rgb*weights.z;
  float leaf_wash=clamp(COLOR.g*2.6,.72,1.16);
  base=mix(base,painted_leaf*leaf_wash,.88);
 }
 else{
  if(code>.975) code=COLOR.r>COLOR.g*1.025&&COLOR.r>COLOR.b*1.22?.7:.55;
  vec3 paint=base;
  if(code>.775) paint=texture(bark_paint,UV).rgb;
  else if(code>.625) paint=texture(wood_paint,UV).rgb;
  else if(code>.475) paint=texture(stone_paint,UV).rgb;
  else if(code>.325) paint=texture(cloth_paint,UV).rgb;
  float vertex_wash=clamp(dot(COLOR.rgb,vec3(.333))*2.4,.7,1.18);
  base=mix(base,paint*vertex_wash,.68);
 }
 ALBEDO=base;
 ROUGHNESS=1.0;
}
void light(){float shade=mix(.48,1.0,smoothstep(.04,.72,dot(NORMAL,LIGHT)));DIFFUSE_LIGHT+=ALBEDO*LIGHT_COLOR*ATTENUATION*shade/PI;}
