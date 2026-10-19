#version 450
layout(set=2,binding=0) uniform MaterialUniforms {
    vec4 _Effect; // @default = vec4(0.0, 0.0, 1.0, 0.0)
    vec4 _Shape; // @default = vec4(1.7778, 0.0, 0.0, 0.0)
} u;
layout(set=2,binding=1) uniform sampler2D _MainTex;
layout(location=0) in vec4 vColor;
layout(location=1) in vec2 vUv;
layout(location=0) out vec4 outColor;
float hash(vec2 p) {
    uvec2 q=uvec2(ivec2(p));
    uint h=q.x*374761393u+q.y*668265263u;
    h=(h^(h>>13u))*1274126177u; h^=h>>16u;
    return float(h&0x00ffffffu)/16777215.0;
}
float noise(vec2 p) {
    vec2 i=floor(p),f=fract(p); f=f*f*(3.0-2.0*f);
    return mix(mix(hash(i),hash(i+vec2(1,0)),f.x),mix(hash(i+vec2(0,1)),hash(i+1.0),f.x),f.y);
}
float fbm(vec2 p) { return .56*noise(p)+.28*noise(p*2.03+19.2)+.12*noise(p*4.17+43.7)+.04*noise(p*8.31); }
vec4 sampleTex(vec2 p) { return texture(_MainTex,vec2(p.x,1.0-p.y)); }
void main() {
    float t=u._Effect.x,a=u._Effect.y,opacity=u._Effect.z,mode=u._Effect.w;
    vec2 uv=vUv;
    if(mode<.5) {
        outColor=sampleTex(uv); outColor.a*=opacity;
    } else if(mode<1.5) {
        vec2 p=(uv-.5)*vec2(u._Shape.x,1.0);
        vec2 drift=vec2(t*.055,-t*.07);
        vec2 curl=vec2(fbm(p*2.1+drift),fbm(p*2.1-drift+31.7))-.5;
        float n=fbm(p*3.6+drift+curl*.7);
        float detail=fbm(p*9.0+vec2(-t*.08,t*.06)+curl);
        float cloud=smoothstep(.27,.77,n*.75+detail*.25);
        // A soft central opening keeps the ship readable; the clouds retain parallax motion.
        float edge=mix(.30,1.0,smoothstep(.15,.65,length(p)));
        outColor=vec4(mix(vec3(.15,.20,.25),vec3(.74,.80,.84),n),cloud*edge*a*opacity);
    } else if(mode<2.5) {
        vec3 c=sampleTex(uv).rgb;
        float l=dot(c,vec3(.2126,.7152,.0722));
        float contrast=clamp(l*.60+.06,0.0,1.0);
        float grain=(hash(floor(uv*vec2(1600,900))+floor(t*24.0))-.5)*.009;
        float vignette=1.0-.20*smoothstep(.25,.75,length(uv-.5));
        outColor=vec4((vec3(contrast)*vec3(.96,1.0,1.03)+grain)*vignette,1.0);
    } else if(mode<3.5) {
        vec2 p=(uv-.5)*vec2(u._Shape.x,1);
        float jag=(noise(vec2(p.x*40.0,t*9.0))-.5)*.045*a;
        float dist=abs(p.y+jag);
        float span=exp(-pow(abs(p.x)/max(.06,a*.95),6.0));
        float core=exp(-dist*180.0)*span;
        float bloom=exp(-dist*22.0)*span;
        vec3 color=vec3(.65,.77,1.0)*bloom+vec3(1.0,.85,.52)*core;
        outColor=vec4(color,clamp((core+bloom*.45)*a,0.0,1.0)*opacity);
    } else if(mode<4.5) {
        outColor=vec4(sampleTex(uv).rgb,1.0);
    } else if(mode<5.5) {
        outColor=vec4(.035,.045,.08,sampleTex(uv).a*opacity);
    } else if(mode<6.5) {
        vec4 body=sampleTex(uv);
        // Relative heat signature on the actual visible RenderTree, not a temperature simulation.
        float heat=mix(.78,1.0,dot(body.rgb,vec3(.3,.5,.2)));
        outColor=vec4(vec3(heat),body.a*opacity);
    } else {
        // Horizontal FTL streak: sharp leading flare, long fading wake and fine parallel filaments.
        vec2 p=vec2(uv.x-.10,(uv.y-.5)*2.);
        float tail=exp(-max(p.x,0.)*2.6)*smoothstep(-.10,.01,p.x)*(1.-smoothstep(.84,.90,p.x));
        float core=exp(-abs(p.y)*190.)*tail;
        float glow=exp(-abs(p.y)*24.)*tail;
        float filaments=(exp(-abs(p.y-.10)*80.)+exp(-abs(p.y+.10)*80.))*tail*.15;
        float flare=exp(-length(vec2(p.x*u._Shape.x,p.y))*13.);
        float rays=exp(-abs(p.x*u._Shape.x)*100.)*exp(-abs(p.y)*6.);
        vec3 color=vec3(.42,.70,1.)*(glow+filaments)+vec3(1.,.94,.76)*(core+flare+rays*.4);
        outColor=vec4(color,clamp((core+glow*.48+filaments+flare+rays*.25)*a,0.,1.)*opacity);
    }
    outColor*=vColor;
}
