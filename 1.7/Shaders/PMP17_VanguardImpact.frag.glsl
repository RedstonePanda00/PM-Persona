#version 450
layout(set=2,binding=0) uniform MaterialUniforms {
    vec4 _Params; // @default = vec4(0.0, 6.0, 1.0, 0.0)
    vec4 _Tint; // @default = vec4(1.0)
} u;
layout(location=0) in vec4 vColor;
layout(location=1) in vec2 vUv;
layout(location=0) out vec4 outColor;
float hash(vec2 p) {
    uvec2 q=uvec2(ivec2(p)); uint h=q.x*374761393u+q.y*668265263u;
    h=(h^(h>>13u))*1274126177u; h^=h>>16u;
    return float(h&0xffffffu)/16777215.0;
}
float noise(vec2 p) {
    vec2 i=floor(p), f=fract(p); f=f*f*(3.-2.*f);
    return mix(mix(hash(i),hash(i+vec2(1,0)),f.x),mix(hash(i+vec2(0,1)),hash(i+1.),f.x),f.y);
}
void main() {
    float age=u._Params.x, life=u._Params.y, seed=u._Params.z, mode=u._Params.w;
    vec2 p=vUv*2.-1.; float r=length(p);
    if(mode>5.5) {
        // Fracture compression front: start at the full scar perimeter, never at zero.
        float radius=life+age*18.;
        vec2 world=p*(radius+3.);
        float distance=length(world)-radius;
        float fade=(1.-smoothstep(.9,2.2,age))*exp(-age*.35);
        float front=exp(-pow(distance/(.16+age*.10),2.));
        float turbulence=noise(world*.65+seed+age*2.);
        float dust=exp(-pow((distance+.8+age*.3)/(.60+age*.35),2.))*(.35+.65*turbulence);
        vec3 color=mix(vec3(.45,.34,.20),vec3(1.,.89,.54),front);
        outColor=vec4(color,max(front*.85,dust*.60)*fade);
    } else if(mode>4.5) {
        float lobes=noise(p*4.)*.20+noise(p*9.)*.08;
        float puff=1.-smoothstep(.25,.95,r+lobes);
        float shade=.7+.3*noise(p*7.);
        outColor=vec4(vec3(shade),puff);
    } else if(mode>3.5) {
        float drop=1.-smoothstep(.35,.9,length(p*vec2(1.3,.75)));
        outColor=vec4(vec3(.7,.88,1.),drop*.8);
    } else if(mode<.5 || mode>2.5) {
        float n=noise(p*13.+seed), edge=r+(n-.5)*.16;
        float fade=1.-smoothstep(life*.55,life,age);
        float scar=(1.-smoothstep(.37,.72,edge))*fade;
        float flash=exp(-age*22.)*(1.-smoothstep(.1,.7,r));
        float cracks=pow(1.-abs(noise(p*20.+seed)*2.-1.),18.);
        float heat=exp(-age*1.8)*(1.-smoothstep(.12,.65,edge))*(.65+.6*cracks);
        float radius=clamp(age*2.3,0.,.95);
        float wave=exp(-pow((r-radius)/.025,2.))*exp(-age*5.)*u._Tint.w;
        vec3 color=mix(vec3(.035,.022,.016),vec3(1.,.30,.035),clamp(heat*2.,0.,1.));
        color+=vec3(1.,.92,.72)*flash*2.+vec3(.85,.45,.12)*wave;
        float alpha=max(scar*.62,max(heat,max(flash,wave*.65)));
        if(mode>2.5) { color=vec3(.63,.82,.94); alpha=max(wave*.6,flash*.5); }
        outColor=vec4(color,alpha);
    } else if(mode<1.5) {
        float along=vUv.y;
        float width=mix(.035,.28,pow(along,1.2));
        float curl=(noise(vec2(along*26.-age*5.,seed))-.5)*.035*(1.-along);
        float cross=abs(p.x+curl)/width;
        float taper=smoothstep(0.,.18,along)*(1.-smoothstep(.94,1.,along));
        float core=exp(-cross*cross*7.)*taper;
        float glow=exp(-cross*cross*.6)*taper;
        float strands=exp(-pow((abs(p.x)-width*.58)/.035,2.))*taper*.22;
        outColor=vec4(vec3(1.,.97,.91)*core+u._Tint.rgb*(glow*.65+strands),max(core,glow*.5+strands));
    } else {
        float facets=max(abs(p.x)*.85+abs(p.y)*.46,abs(p.y)*.88-abs(p.x)*.12);
        float alpha=1.-smoothstep(.58,.68,facets);
        float shade=.62+.38*step(p.y*.55,p.x);
        outColor=vec4(u._Tint.rgb*shade,alpha*u._Tint.a);
    }
    // World quads can carry zero vertex colors; only ParticleSystem submits per-particle color.
    if((mode>1.5 && mode<2.5) || (mode>3.5 && mode<5.5)) outColor*=vColor;
}
