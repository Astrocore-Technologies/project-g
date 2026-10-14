// Shared artistic response; tune these controls on the reference scene.
uniform vec3 shadow_tint : source_color = vec3(0.62,0.72,0.78);
uniform float shade_floor : hint_range(0.0,1.0) = 0.32;
uniform float light_softness : hint_range(0.01,1.0) = 0.42;
uniform float wrap_light : hint_range(0.0,0.8) = 0.15;
vec3 painted_response(float ndotl, float visibility) {
 float tone=smoothstep(0.05-light_softness,0.45+light_softness,ndotl+wrap_light);
 // Shadow colour is explicit; shadows retain painted detail without glowing.
 float exposure=mix(shade_floor,1.0,tone)*mix(0.36,1.0,visibility);
 return mix(shadow_tint,vec3(1.0),tone*visibility)*exposure;
}
