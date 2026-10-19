#version 450
layout(set=2,binding=0) uniform MaterialUniforms {
    vec4 _State; // @default = vec4(0.0, 1.0, 0.0, 0.0)
} u;
layout(set=2,binding=1) uniform sampler2D _MainTex;
layout(location=0) in vec4 vColor;
layout(location=1) in vec2 vUv;
layout(location=0) out vec4 outColor;

float silhouette(vec2 p) {
    vec2 uv=p*.5+.5;
    if(u._State.z<.5 || any(lessThan(uv,vec2(0))) || any(greaterThan(uv,vec2(1))))return 0.;
    return texture(_MainTex,vec2(uv.x,1.-uv.y)).a;
}
float box(vec2 p,vec2 size) {
    vec2 q=abs(p)-size;
    return length(max(q,vec2(0)))+min(max(q.x,q.y),0.);
}
void main() {
    float t=clamp(u._State.x,0.,1.);
    // Smooth start/end; continuous closure, no final scale or facing discontinuity.
    float envelope=smoothstep(0.,.12,t)*(1.-smoothstep(.82,1.,t));
    float spread=(1.-smoothstep(.15,.86,t))*.64;
    vec2 p=(vUv-.5)*4.;
    vec3 light=vec3(0);float opacity=0.;
    vec3 gold=vec3(1.,.72,.22),white=vec3(1.,.94,.68);
    float width=max(fwidth(p.x)*1.2,.008);
    for(int i=0;i<3;i++) {
        float layer=float(i)-1.;
        vec2 q=p-vec2(.80,.42)*layer*spread;
        // Slightly oblique energy screens; no permanent opaque rectangle around the item.
        vec2 screen=vec2(q.x+q.y*.16,q.y);
        float d=box(screen,vec2(.72,.90));
        float border=(1.-smoothstep(width,width*2.6,abs(d)))*.72;
        float inside=1.-smoothstep(-width,width,d);
        float scan=exp(-pow((q.y-mix(-1.,1.,t))/.06,2.))*inside;
        float a=silhouette(q);
        float edge=max(max(silhouette(q+vec2(.018,0)),silhouette(q-vec2(.018,0))),
                       max(silhouette(q+vec2(0,.018)),silhouette(q-vec2(0,.018))))-a;
        float ghost=(i==1 ? .10 : .32)*a+max(edge,0.)*.75;
        float energy=(border+ghost+scan*.6)*envelope;
        light+=mix(gold,white,scan)*energy;
        opacity+=((inside*.035+border*.4+ghost*.5+scan*.12)*envelope);
    }
    // Last brief seal travels outward instead of switching the pawn sprite off/on.
    float seal=exp(-pow((t-.84)/.065,2.))*(1.-smoothstep(.95,1.,t));
    float ring=1.-smoothstep(width,width*3.,abs(length(p)-mix(.35,1.45,smoothstep(.76,1.,t))));
    light+=white*ring*seal*.7;opacity+=ring*seal*.25;
    float peak=max(light.r,max(light.g,light.b));
    light=light/max(1.,peak);
    outColor=vec4(light,clamp(opacity,0.,.65));
}
