#version 450
layout(set=2,binding=0) uniform MaterialUniforms {
    vec4 _State; // @default = vec4(0.0, 0.0, 0.0, 0.0)
    vec4 _Geometry; // @default = vec4(1.0, 0.0, 0.0, 0.0)
} u;
layout(set=2,binding=1) uniform sampler2D _MainTex;
layout(location=0) in vec4 vColor;
layout(location=1) in vec2 vUv;
layout(location=0) out vec4 outColor;
const vec3 gold=vec3(1.,.68,.12), hot=vec3(1.,.94,.65);
float hash(vec2 p) { return fract(sin(dot(p,vec2(127.1,311.7))+u._State.z)*43758.5453); }
float segment(vec2 p,vec2 a,vec2 b) {
    vec2 ab=b-a;float t=clamp(dot(p-a,ab)/max(dot(ab,ab),.0001),0.,1.);return length(p-a-ab*t);
}
float pulse(float d,float w) { return exp(-d*d/(w*w)); }
vec4 body(vec2 p) {
    vec2 uv=p*.5+.5;
    if(any(lessThan(uv,vec2(0)))||any(greaterThan(uv,vec2(1))))return vec4(0);
    return texture(_MainTex,vec2(uv.x,1.-uv.y));
}
vec4 over(vec4 a,vec4 b) { return a+b*(1.-a.a); }
vec4 energy(float e,vec3 color) { float a=clamp(e,0.,.95);return vec4(color*a,a); }
void main() {
    float t=clamp(u._State.y,0.,1.); int phase=int(u._State.x+.1);
    vec2 p=(vUv-.5)*4.;
    if(u._State.w>1.5) {
        vec2 q=(vUv-.5)*2.1;float r=length(q), time=u._State.x;
        float edge=1.-smoothstep(.89,1.,r);
        float wave=pulse(q.y-mix(-1.05,1.05,t),.022)*(1.-step(.999,t));
        vec2 grid=abs(fract(q*11.+.5)-.5);
        float trace=(1.-smoothstep(.012,.027,min(grid.x,grid.y)))*.055;
        float ring=pulse(r-(.92+.008*sin(time*2.)),.009)*.11;
        float veil=pulse(q.y-mix(-1.05,1.05,t),.14)*.025*(1.-step(.999,t));
        outColor=energy((wave*.18+trace+veil+ring)*edge*u._State.z,gold);return;
    }
    vec4 result=vec4(0);
    if(phase==0) {
        vec4 b=body(p);
        vec2 cell=fract(p*vec2(4.,5.)+vec2(.17,.29));
        float crack=1.-smoothstep(.012,.045,min(min(cell.x,1.-cell.x),min(cell.y,1.-cell.y)));
        float scan=pulse(p.y-mix(-1.2,1.2,t),.09);
        float onset=smoothstep(0.,.25,t)*(1.-smoothstep(.7,1.,t));
        result=energy((crack*.38*onset+scan*.55)*b.a,gold);
    } else if(phase==1) {
        // Thirty unequal rectangular shards, each sampled only from its original RenderTree region.
        for(int y=0;y<6;y++)for(int x=0;x<5;x++) {
            vec2 id=vec2(x,y);
            vec2 lo=vec2(-1.)+vec2(float(x)/5.,float(y)/6.)*2.;
            vec2 hi=vec2(-1.)+vec2(float(x+1)/5.,float(y+1)/6.)*2.;
            if(x>0)lo.x+=.06*(hash(vec2(x,0))-.5);
            if(x<4)hi.x+=.06*(hash(vec2(x+1,0))-.5);
            if(y>0)lo.y+=.06*(hash(vec2(0,y))-.5);
            if(y<5)hi.y+=.06*(hash(vec2(0,y+1))-.5);
            vec2 c=(lo+hi)*.5;
            float delay=hash(id)*.12;
            float f=smoothstep(delay,1.,t);
            float s=max(.025,1.-f*.975);
            vec2 offset=c*(sin(f*3.14159)*.65-f)+vec2(0,.12*sin(f*3.14159));
            vec2 q=(p-c-offset)/s+c;
            vec2 cut=q+vec2(.055*sin(q.y*13.+u._State.z),.04*sin(q.x*17.+u._State.z));
            if(all(greaterThanEqual(cut,lo))&&all(lessThan(cut,hi))) {
                vec4 b=body(q);float visibility=1.-smoothstep(.70,1.,f);
                float border=min(min(cut.x-lo.x,hi.x-cut.x),min(cut.y-lo.y,hi.y-cut.y));
                float light=(1.-smoothstep(.005,.025,border))*sin(t*3.14159)*.8+f*.8;
                vec3 rgb=mix(b.rgb,hot*b.a,clamp(light,0.,1.));
                result=over(vec4(rgb*visibility,b.a*visibility),result);
            }
            float mote=body(c).a*smoothstep(.2,.5,f)*(1.-smoothstep(.85,1.,f));
            vec2 at=c+offset;
            result=over(energy((pulse(length(p-at),.025)+pulse(segment(p,at,at*.80),.008)*.25)*mote,hot),result);
        }
    } else if(phase>=3) {
        // Compact armor regions: head, left/right shoulders, torso, and floating lower panels.
        for(int y=0;y<3;y++)for(int x=0;x<3;x++) {
            vec2 lo=vec2(-1.)+vec2(float(x),float(y))*(2./3.);
            vec2 hi=lo+vec2(2./3.);vec2 c=(lo+hi)*.5;
            float delay=(y==1&&x==1)?0.:.08+hash(vec2(x,y))*.15;
            float assembly=phase==4?1.:smoothstep(delay,.96,t);
            vec2 offset=c*(1.-assembly)*.75;
            vec2 q=p-offset;
            float link=body(c).a*sin(assembly*3.14159)*.14;
            result=over(energy(pulse(segment(p,vec2(0),c+offset),.009)*link,gold),result);
            if(all(greaterThanEqual(q,lo))&&all(lessThan(q,hi))) {
                vec4 b=body(q);float visible=smoothstep(0.,.30,assembly);
                float glow=(1.-assembly)*.8;
                result=over(vec4(mix(b.rgb,hot*b.a,glow)*visible,b.a*visible),result);
            }
        }
        if(phase==4) {
            float ring=pulse(length(p*vec2(1.,.85))-mix(.12,1.2,t),.025)*sin(t*3.14159);
            result=over(energy(ring*.65,hot),result);
        }
    }
    // One continuous core envelope across disassembly, hold and assembly (no stage flashes).
    float core=phase==1?smoothstep(.25,1.,t):phase==2?1.:phase==3?1.-smoothstep(0.,.65,t):0.;
    float r=length(p*u._Geometry.x-vec2(0,.02));
    float glow=pulse(r,.11)*.85+pulse(r,.27)*.12;
    float orbit=pulse(r-.18,.012)*.25;
    result=over(energy((glow+orbit)*core,hot),result);
    outColor=result;
}
