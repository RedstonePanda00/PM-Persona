#version 450
layout(set=2,binding=0) uniform MaterialUniforms {
    vec4 _State; // @default = vec4(0.0)
    vec4 _Layout; // @default = vec4(1.0)
    vec4 _Tint; // @default = vec4(1.0, 0.77, 0.18, 1.0)
} u;
layout(set=2,binding=1) uniform sampler2D _MainTex;
layout(location=0) in vec4 vColor;
layout(location=1) in vec2 vUv;
layout(location=0) out vec4 outColor;
float line(float d,float width) { return 1.-smoothstep(width,width+1.,abs(d)); }
float segment(vec2 p,vec2 a,vec2 b) { vec2 v=b-a; return length(p-a-v*clamp(dot(p-a,v)/dot(v,v),0.,1.)); }
void main() {
    vec2 px=vUv*u._Layout.xy;
    float a=0.; vec3 c=u._Tint.rgb;
    if(u._State.y<.5) {
        // Solar-yellow energy glass with a transparent interior.
        vec2 p=abs(px-u._Layout.xy*.5), b=u._Layout.xy*.5-2.;
        float d=max(max(p.x-b.x,p.y-b.y),(p.x+p.y-b.x-b.y+10.)*.7071);
        float inside=1.-smoothstep(-1.,0.,d);
        float edge=line(d,1.1);
        float scan=.5+.5*sin(px.y*1.5-u._State.x*7.);
        vec2 grid=abs(fract((px+vec2(u._State.x*2.,0.))/22.)-.5);
        float lattice=smoothstep(.46,.5,max(grid.x,grid.y));
        a=inside*(.035+.012*scan+.014*lattice)+edge*mix(.20,.55,u._State.z);
        float rail=line(px.x-7.,1.2)*step(12.,px.y)*step(px.y,u._Layout.y-12.);
        a=max(a,rail*u._State.z*.75);
        float pulse=exp(-pow((px.x-mod(u._State.x*95.,u._Layout.x+100.)+50.)/22.,2.));
        a+=edge*pulse*.14;
    } else if(u._State.y<1.5) {
        // Wrap-safe bearing tape driven by heading.
        float degrees=(px.x-u._Layout.x*.5)/4.+u._State.w;
        float tick=abs(fract(degrees/5.+.5)-.5)*20.;
        float major=step(abs(fract(degrees/15.+.5)-.5)*15.,.5);
        a=line(tick, .55)*step(12.,px.y)*step(px.y,20.+major*9.);
        a*=smoothstep(0.,35.,px.x)*smoothstep(0.,35.,u._Layout.x-px.x)*.65;
        a=max(a,line(px.x-u._Layout.x*.5,1.)*step(2.,px.y)*step(px.y,33.));
    } else if(u._State.y<2.5) {
        vec2 p=px-u._Layout.xy*.5;
        float ring=line(length(p)-10.,.65);
        float horizontal=line(p.y,.5)*step(28.,abs(p.x))*step(abs(p.x),155.);
        float vertical=line(p.x,.5)*step(28.,abs(p.y))*step(abs(p.y),56.);
        a=max(ring,max(horizontal,vertical))*.85;
    } else if(u._State.y<3.5) {
        float filled=step(vUv.x,u._Layout.z);
        a=(.16+filled*.64)*(1.-smoothstep(.22,.50,abs(vUv.y-.5)));
    } else {
        // Five discrete energy reserve indicators.
        vec2 p=vec2((fract(vUv.x*5.)-.5)*u._Layout.x/5.,u._Layout.y-px.y);
        float d=min(segment(p,vec2(0.,2.),vec2(-3.,8.)),segment(p,vec2(0.,2.),vec2(3.,8.)));
        d=min(d,segment(p,vec2(-3.,8.),vec2(-3.,27.)));
        d=min(d,segment(p,vec2(3.,8.),vec2(3.,27.)));
        d=min(d,segment(p,vec2(-3.,27.),vec2(3.,27.)));
        d=min(d,segment(p,vec2(0.,8.),vec2(0.,25.)));
        a=(1.-smoothstep(.5,1.4,d))*mix(.28,.95,step(.999,u._Layout.z));
    }
    outColor=vec4(c,clamp(a*u._Tint.a,0.,1.))*vColor;
}
