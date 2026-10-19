#version 450
layout(set=2,binding=0) uniform MaterialUniforms {
    vec4 _Effect; // @default = vec4(0.0, 0.0, 1.0, 0.0)
} u;
layout(location=0) in vec4 vColor;
layout(location=1) in vec2 vUv;
layout(location=0) out vec4 outColor;
void main() {
    vec2 p=vUv*2.-1.;float r=length(p),a=atan(p.y,p.x),t=u._Effect.x;
    float surge=sin(a*9.+t*3.)*.035+sin(a*17.-t*5.)*.023;
    float core=exp(-r*r/.075);
    float rim=exp(-pow((r-.47-surge)/.065,2.));
    float tongues=pow(max(0.,sin(a*13.+sin(t*4.+a*3.))),8.)*exp(-pow((r-.62)/.13,2.));
    float wave=exp(-pow((r-fract(t*.75)*.85)/.025,2.))*.13;
    float alpha=(core*.9+rim*.6+tongues*.5+wave)*(1.-smoothstep(.75,.98,r))*u._Effect.z;
    if(alpha<.002)discard;
    outColor=vec4(mix(vec3(1.,.48,.025),vec3(1.,.99,.80),core),clamp(alpha,0.,.95));
}
