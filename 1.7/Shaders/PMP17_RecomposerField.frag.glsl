#version 450
layout(set=2,binding=0) uniform MaterialUniforms {
    vec4 _CameraLocal; // @default = vec4(0.0, 5.0, 0.0, 1.0)
    vec4 _RayDirection; // @default = vec4(0.0, -1.0, 0.0, 0.0)
    vec4 _Beam; // @default = vec4(0.0, 0.0, 0.0, 0.0)
} u;
layout(location=0) in vec3 localPosition;
layout(location=1) flat in vec3 faceNormal;
layout(location=0) out vec4 outColor;
void main() {
    vec3 rd=u._CameraLocal.w>.5?normalize(u._RayDirection.xyz):normalize(localPosition-u._CameraLocal.xyz);
    if(dot(faceNormal,rd)>=0.)discard;
    vec3 ro=localPosition-rd*.002, inv=1./(rd+vec3(1e-7));
    vec3 a=(-vec3(1)-ro)*inv,b=(vec3(1)-ro)*inv;
    vec3 lo=min(a,b),hi=max(a,b);
    float entry=max(0.,max(max(lo.x,lo.y),lo.z)),exit=min(min(hi.x,hi.y),hi.z);
    if(exit<=entry)discard;
    float ds=(exit-entry)/32.,alpha=0.;vec3 light=vec3(0);
    for(int i=0;i<32;i++) {
        vec3 p=ro+rd*(entry+(float(i)+.5)*ds);
        float edge=1.-smoothstep(.85,1.,length(p.xz));
        float front=mix(1.,-1.,clamp(u._Beam.x,0.,1.));
        float descending=exp(-pow((p.y-front)/.055,2.))*(1.-smoothstep(.92,1.,u._Beam.x));
        float sweep=exp(-pow((p.z-mix(-1.1,1.1,u._Beam.y))/.045,2.));
        sweep*=smoothstep(.8,1.,u._Beam.x)*(1.-u._Beam.w);
        float stripes=pow(max(0.,sin(p.x*72.+sin(p.z*19.))),18.);
        float density=edge*(descending*.12+sweep*.055+stripes*.003)*u._Beam.z;
        float opacity=1.-exp(-density*ds*2.);
        light+=(1.-alpha)*vec3(1.,.72,.22)*opacity;alpha+=(1.-alpha)*opacity;
    }
    outColor=vec4(light,alpha);
}
