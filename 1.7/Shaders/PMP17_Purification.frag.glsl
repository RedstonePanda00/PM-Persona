#version 450
layout(set=2,binding=0) uniform MaterialUniforms {
    vec4 _CameraLocal; // @default = vec4(0.0, 5.0, 0.0, 1.0)
    vec4 _RayDirection; // @default = vec4(0.0, -1.0, 0.0, 0.0)
    vec4 _Beam; // @default = vec4(0.0, 1.0, 1.0, 0.0)
} u;
layout(location=0) in vec3 localPosition;
layout(location=1) flat in vec3 faceNormal;
layout(location=0) out vec4 outColor;
void main() {
    vec3 rd=u._CameraLocal.w>.5 ? normalize(u._RayDirection.xyz) : normalize(localPosition-u._CameraLocal.xyz);
    if(dot(faceNormal,rd)>=0.) discard;
    vec3 ro=localPosition-rd*.002;
    vec3 inv=1./(rd+vec3(1e-7));
    vec3 a=(-vec3(1)-ro)*inv,b=(vec3(1)-ro)*inv;
    vec3 lo=min(a,b),hi=max(a,b);
    float entry=max(0.,max(max(lo.x,lo.y),lo.z)),exit=min(min(hi.x,hi.y),hi.z);
    if(exit<=entry) discard;
    float stepLength=(exit-entry)/48.,opacity=0.,t=u._Beam.x;
    vec3 emission=vec3(0.);
    for(int i=0;i<48;i++) {
        vec3 p=ro+rd*(entry+(float(i)+.5)*stepLength);
        if(p.y<1.-2.*clamp(t/(8./60.),0.,1.)) continue;
        float r=length(p.xz),angle=atan(p.z,p.x),y=p.y;
        float flow=sin(y*19.-t*13.+sin(y*6.+t*3.));
        float lobes=sin(angle*7.+y*8.-t*6.)*.5+sin(angle*11.-y*17.+t*9.)*.25;
        float shellRadius=.48+.07*flow+.065*lobes;
        float shell=exp(-pow((r-shellRadius)/.075,2.));
        float core=exp(-r*r/.095);
        float filaments=pow(max(0.,sin(angle*9.+y*14.-t*10.)),12.)*exp(-pow((r-.65)/.16,2.));
        float pulse=.83+.17*sin(y*38.-t*32.);
        float density=(core*.85+shell*.25+filaments*.45)*pulse*u._Beam.z*(1.-smoothstep(.78,.98,r));
        float alpha=1.-exp(-density*stepLength*6.);
        vec3 tint=mix(vec3(1.,.62,.045),vec3(1.,.99,.86),1.-smoothstep(.13,.52,r));
        emission+=(1.-opacity)*tint*alpha;
        opacity+=(1.-opacity)*alpha;
        if(opacity>.995)break;
    }
    if(opacity<.002)discard;
    outColor=vec4(emission/max(opacity,.001),opacity);
}
