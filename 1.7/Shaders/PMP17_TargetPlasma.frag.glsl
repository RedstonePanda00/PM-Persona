#version 450
layout(set=2,binding=0) uniform MaterialUniforms {
    vec4 _Flow; // @default = vec4(0.0, 1.0, 1.0, 1.0)
    vec4 _Clock; // @default = vec4(0.0, 1.0, 0.00390625, 2.0)
    vec4 _Surface; // @default = vec4(0.0, 0.0, 0.0, 0.0)
    float _WorldSpace; // @default = 0.0
    float _WakeLength; // @default = 0.0
    vec4 _MaskUV; // @default = vec4(1.0, 1.0, 0.0, 0.0)
} u;
layout(set=2,binding=1) uniform sampler2D _MainTex;
layout(location=0) in vec4 vColor;
layout(location=1) in vec2 vUv;
layout(location=0) out vec4 outColor;
float mask(vec2 uv) {
    uv=uv*u._MaskUV.xy+u._MaskUV.zw;
    if(any(lessThan(uv,vec2(0))) || any(greaterThan(uv,vec2(1)))) return 0.0;
    return smoothstep(.06,.4,texture(_MainTex,vec2(uv.x,1.0-uv.y)).a);
}
float hash(vec2 p) {
    uvec2 q=uvec2(ivec2(p)); uint h=q.x*374761393u+q.y*668265263u;
    h=(h^(h>>13u))*1274126177u; h^=h>>16u;
    return float(h&0x00ffffffu)/16777215.0;
}
float noise(vec2 p) {
    vec2 i=floor(p),f=fract(p); f=f*f*(3.0-2.0*f);
    return mix(mix(hash(i),hash(i+vec2(1,0)),f.x),mix(hash(i+vec2(0,1)),hash(i+1.0),f.x),f.y);
}
void main() {
    vec2 world=(vUv-.5)*(2.0*u._Clock.w);
    if(u._WorldSpace>.5) world+=u._Surface.xy;
    vec2 flow=u._Flow.xy;
    if(u._WorldSpace>.5) flow=world-u._Surface.zw;
    flow=length(flow)>.001?normalize(flow):vec2(0,1);
    vec2 side=vec2(-flow.y,flow.x);
    float t=u._Clock.x, seed=u._Clock.y, center=mask(vUv);
    float width=max(u._Clock.z*.85,.018/max(u._Clock.w,.5));
    vec2 dx=vec2(width,0),dy=vec2(0,width);
    vec2 gradient=vec2(mask(vUv+dx)-mask(vUv-dx),mask(vUv+dy)-mask(vUv-dy));
    float front=max(0.0,dot(gradient,flow)/max(length(gradient),.001));
    float rim=0.0,halo=0.0;
    for(int i=0;i<12;i++) {
        float a=float(i)*.523598776;
        vec2 d=vec2(cos(a),sin(a))*width;
        rim=max(rim,mask(vUv+d)); halo=max(halo,mask(vUv+d*3.0));
    }
    rim=max(0.0,rim-center); halo=max(0.0,halo-center);
    float turbulence=noise((vUv-flow*t*.25)*36.0+seed);
    float wake=0.0;
    // Advect captured silhouette strands downstream.
    int steps=u._MaskUV.y>1.1?40:14;
    float maxDistance=u._WorldSpace>.5?1.2/(2.0*u._Clock.w):.26;
    if(u._WakeLength>0.0) maxDistance=u._WakeLength/max(length(flow*u._MaskUV.xy),.001);
    for(int i=1;i<=40;i++) {
        if(i>steps) break;
        float distance=float(i)/float(steps)*maxDistance;
        float curl=(noise(vec2(dot(vUv,side)*26.0+seed,distance*14.0-t*2.7))-.5)*distance*.3;
        vec2 source=vUv-flow*distance+side*curl;
        float strand=noise(vec2(dot(source,side)*95.0+seed,dot(vUv,flow)*24.0-t*3.5));
        float decay=u._WakeLength>0.0?exp(-distance/maxDistance*2.8)*(1.-smoothstep(.8,1.,distance/maxDistance)):exp(-distance*9.0);
        wake=max(wake,mask(source)*decay*smoothstep(.27,.73,strand));
    }
    wake*=1.0-center;
    float strength=mix(.35,1.0,u._Flow.z)*u._Flow.w;
    vec3 light=vec3(1.0,.16,.025)*wake*1.15+vec3(1.0,.065,.012)*halo*.16;
    light+=mix(vec3(1.0,.36,.045),vec3(.57,.78,1.0),front)*rim*(.4+front*.8)*(.7+.3*turbulence);
    // Clip the filled plasma sheet to RenderTree alpha.
    vec2 stream=vec2(dot(world,side)*5.5,dot(world,flow)*3.0-t*2.3);
    // Shared world coordinates keep joined walls continuous, even when the union changes size.
    if(u._WorldSpace>.5) stream=(world-flow*t*.5)*4.0;
    float eddies=noise(stream*.65+vec2(seed,t*.35));
    float plasma=.65*noise(stream+vec2(seed,eddies*2.0))+.35*noise(stream*2.1-vec2(eddies,t*.8));
    float filaments=pow(1.0-abs(plasma*2.0-1.0),8.0);
    vec3 surfaceColor=mix(vec3(.85,.09,.012),vec3(1.0,.62,.15),smoothstep(.2,.8,plasma));
    surfaceColor+=vec3(.2,.22,.17)*filaments;
    float surfaceAlpha=center*(.48+.18*plasma)*strength;
    light*=strength;
    float peak=max(max(light.r,light.g),light.b);
    vec3 mapped=light/max(peak,1e-5)*(1.0-exp(-peak));
    float glowAlpha=clamp(max(max(mapped.r,mapped.g),mapped.b)*.72,0.0,.85);
    // Premultiplied surface compositing leaves underlying apparel/body detail visible.
    outColor=vec4(surfaceColor*surfaceAlpha+mapped*(1.0-surfaceAlpha),
                  surfaceAlpha+glowAlpha*(1.0-surfaceAlpha));
    outColor*=vec4(vColor.rgb*vColor.a,vColor.a);
}
