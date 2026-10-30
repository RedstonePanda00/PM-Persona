#version 450
// Texture-free 3D radiative-transfer approximation with dimensionless density and temperature fields.
layout(set = 2, binding = 0) uniform MaterialUniforms {
    vec4 _CameraLocal; // @default = vec4(0.0, 5.0, 0.0, 1.0)
    vec4 _RayDirection; // @default = vec4(0.0, -1.0, 0.0, 0.0)
    vec4 _Evolution; // @default = vec4(0.8, 0.5, 0.125, 4300.0)
    vec4 _Geometry; // @default = vec4(0.035, 30.0, 4.0, 0.0)
    vec4 _Clock; // @default = vec4(0.0, 1.0, 1.0, 0.4)
    vec4 _Phases; // @default = vec4(1.0, 1.0, 0.2, 0.0)
    float _Exposure; // @default = 1.15
    float _Steps; // @default = 48.0
    float _Appearance; // @default = 1.0
} u;
layout(location = 0) in vec3 localPosition;
layout(location = 1) flat in vec3 faceNormal;
layout(location = 0) out vec4 outColor;
const float PI = 3.14159265359;

float hash3(vec3 p) {
    p = fract(p * 0.1031); p += dot(p, p.yzx + 33.33);
    return fract((p.x + p.y) * p.z);
}
float noise3(vec3 p) {
    vec3 i = floor(p), f = fract(p); f = f*f*(3.0-2.0*f);
    return mix(mix(mix(hash3(i),hash3(i+vec3(1,0,0)),f.x),
                   mix(hash3(i+vec3(0,1,0)),hash3(i+vec3(1,1,0)),f.x),f.y),
               mix(mix(hash3(i+vec3(0,0,1)),hash3(i+vec3(1,0,1)),f.x),
                   mix(hash3(i+vec3(0,1,1)),hash3(i+vec3(1,1,1)),f.x),f.y),f.z);
}
float turbulence(vec3 p) { return .67*noise3(p) + .33*noise3(p*2.03+vec3(13.2,5.9,8.1)); }
vec3 rotateY(vec3 p, float a) { float s=sin(a),c=cos(a); return vec3(c*p.x+s*p.z,p.y,-s*p.x+c*p.z); }

// Rigid rotation in a tilted stellar frame.
vec3 stellarFrame(vec3 p, float t) {
    vec3 axis=normalize(vec3(.82,.28,.50));
    float a=t*.006, s=sin(a), c=cos(a);
    return p*c+cross(axis,p)*s+axis*dot(axis,p)*(1.0-c);
}

// Interpolate cell generations with asynchronous spatial phases.
float evolvingCells(vec3 p, float age) {
    float generation=floor(age), blend=fract(age);
    blend=blend*blend*(3.0-2.0*blend);
    vec3 offset=vec3(17.13,43.71,9.20);
    float a=noise3(p+generation*offset), b=noise3(p+(generation+1.0)*offset);
    return mix(a,b,blend);
}

// Planck B_lambda = 2hc^2/[lambda^5 (exp(hc/lambda kT)-1)].
// Approximate RGB from three visible wavelengths, white-normalized at 6500 K.
vec3 thermal(float kelvin) {
    vec3 lambda = vec3(650e-9,550e-9,450e-9);
    vec3 x = vec3(0.01438776877) / (lambda * max(kelvin, 800.0));
    vec3 reference = vec3(0.01438776877) / (lambda * 6500.0);
    vec3 spectrum = (exp(reference)-1.0) / max(exp(min(x,vec3(70.0)))-1.0,vec3(1e-6));
    return spectrum / max(max(spectrum.r,spectrum.g),spectrum.b);
}
vec2 sphere(vec3 ro, vec3 rd, float radius) {
    float b=dot(ro,rd), d=b*b-dot(ro,ro)+radius*radius;
    if(d<0.0) return vec2(1e8,-1e8);
    float q=sqrt(d);return vec2(-b-q,-b+q);
}

vec3 photosphere(vec3 point, vec3 ray) {
    vec3 n=normalize(point);
    float t=u._Clock.x;
    vec3 p=stellarFrame(n,t);
    float phase=noise3(p*3.0+vec3(u._Clock.y))*2.0;
    float age=t*.48+phase;
    vec3 flow=vec3(evolvingCells(p*4.0+vec3(0,0,7),age*.61),
                   evolvingCells(p*4.0+vec3(3,0,0),age*.73),
                   evolvingCells(p*4.0+vec3(0,11,0),age*.67))-.5;
    // Bounded local tangential displacement.
    flow-=p*dot(flow,p);
    vec3 cellPosition=normalize(p+flow*.045)*19.0+vec3(u._Clock.y);
    float cells=.67*evolvingCells(cellPosition,age)
               +.33*evolvingCells(cellPosition*2.03+vec3(13.2,5.9,8.1),age*1.31);
    float granules=smoothstep(.18,.83,cells);
    float broad=evolvingCells(p*4.0+vec3(u._Clock.y,0,3),age*.43);
    // Procedural convective temperature variation.
    float breakout=pow(u._Phases.x,4.0)*exp(-u._Phases.y*18.0)*(1.0-u._Phases.w);
    float hot=smoothstep(.64,.83,broad)*smoothstep(.55,.8,granules);
    float temperature=2600.0+2000.0*granules+1800.0*hot+breakout*6500.0;
    float mu=max(0.0,dot(n,-ray));
    float limb=.4+.6*mu; // Linear limb darkening I(mu)/I(1)=1-u(1-mu), u=0.6.
    float flux=pow(temperature/4200.0,4.0); // Stefan-Boltzmann scaling, exposure referenced to a cool envelope.
    return thermal(temperature)*flux*limb*2.2;
}

// Density follows a dipole field line r=L sin^2(theta), with localized magnetic arcs.
float magneticLoop(vec3 p,float stellarRadius) {
    float t=u._Clock.x, r=length(p), result=0.0;
    for(int i=0;i<3;i++) {
        float phase=float(i)*2.094+u._Clock.y;
        vec3 q=rotateY(stellarFrame(p,t),phase);
        vec3 axis=normalize(vec3(.6*sin(phase),1,.65*cos(phase)));
        float cosine=dot(q,axis)/max(r,1e-5);
        // Loops inflate and subside continuously; the packets move along the field.
        float activity=.5+.5*sin(t*1.3+phase);
        float fieldRadius=stellarRadius*(1.22+.47*activity)*(1.0-cosine*cosine);
        float distance=(r-fieldRadius)/max(stellarRadius*(.018+.012*activity),1e-5);
        vec3 basisX=normalize(cross(axis,vec3(0,0,1)));
        vec3 basisZ=cross(basisX,axis);
        float longitude=atan(dot(q,basisZ),dot(q,basisX));
        float azimuth=pow(max(0.0,cos(longitude-phase)),48.0);
        float packets=.28+.72*pow(.5+.5*sin(acos(clamp(cosine,-1.0,1.0))*23.0-t*5.0+phase),2.0);
        result+=exp(-distance*distance)*azimuth*packets*(.35+.65*activity);
    }
    return result*smoothstep(stellarRadius,stellarRadius*1.04,r);
}

void sampleMedium(vec3 p,out float extinction,out vec3 emission) {
    extinction=0.0; emission=vec3(0.0);
    if(u._Geometry.w>.5 && p.y*u._Geometry.y+u._Geometry.z<0.0) return;
    float r=length(p), star=u._Geometry.x;
    if(r<star) return;
    float corona=exp(-max(0.0,r/star-1.0)*9.0);
    float loops=magneticLoop(p,star);
    float coronalNumber=corona*.12+loops*.55;
    float coronaDensity=coronalNumber/max(star,.005);
    extinction+=coronaDensity*.13;
    // Optically thin thermal free-free continuum j_nu ~ rho^2 T^-1/2 exp(-h nu/kT).
    // At coronal T ~ 1e6 K the visible continuum is nearly flat; exposure is normalized for display.
    vec3 photonTemperature=vec3(22135.0,26160.0,31973.0); // h nu/k at 650, 550, 450 nm.
    emission+=exp(-photonTemperature/1e6)*corona*corona/max(star,.005)*.07;
    // Apply an illustrative H-alpha boost to dense magnetic-loop prominences.
    vec3 prominence=mix(vec3(1.0,.13,.025),thermal(10000.0),.18+.22*loops);
    emission+=prominence*loops/max(star,.005)*5.0;
    // Radial wind density ~ r^-2 with moving packets; angular activity traces the same 3D surface.
    float height=r/max(star,.005);
    float channels=pow(smoothstep(.35,.78,noise3(normalize(p)*8.0+vec3(u._Clock.y,u._Clock.x*.22,0))),3.0);
    float packets=.35+.65*pow(.5+.5*sin(height*24.0-u._Clock.x*6.0),2.0);
    float wind=channels*packets/(height*height)*exp(-(height-1.0)*5.0);
    emission+=vec3(1.0,.3,.055)*wind/max(star,.005)*1.8;

    float shock=u._Evolution.x, ejecta=u._Evolution.y;
    if(r>shock*1.08 || u._Clock.z<.001) return;
    vec3 expanding=p/max(ejecta,.035);
    vec3 direction=p/max(r,1e-5);
    float t=u._Clock.x;
    float plumes=turbulence(direction*5.0+vec3(u._Clock.y,t*.16,3));
    float clumps=turbulence(expanding*11.0+direction*(t*.85)+vec3(u._Clock.y,3,t*.24));
    float filaments=pow(clamp(clumps*1.6-.3,0.0,1.0),2.0);
    // A flat inner ejecta core and r^-7 outer envelope, advected with homologous expansion.
    float v=r/max(ejecta,.035);
    float profile=pow(max(1.0,v*(1.8-.55*plumes)),-7.0);
    float rho=min(40.0,u._Evolution.z)*profile*(.04+filaments*3.1);
    float fade=u._Clock.z*(1.0-.88*u._Phases.w);
    float envelope=rho*fade;
    extinction+=envelope*1.2;
    emission+=thermal(u._Evolution.w*(.62+.65*clumps))*envelope*(.18+filaments*1.8);

    // Strong adiabatic shock compression (gamma=5/3): density jump 4; thin shell with finite width.
    float wrinkled=shock*(.92+.13*plumes+.025*sin(plumes*32.0-t*2.0));
    float width=max(.012,shock*.045);
    float delta=(r-wrinkled)/width;
    float shocked=4.0*exp(-delta*delta)*fade*(.2+.8*smoothstep(.24,.76,clumps));
    float age=max(.02,u._Clock.w);
    float shockTemperature=1.4e7*pow(1.0+age*20.0,-1.2); // T_sh ~ v_sh^2, v_sh ~ t^-3/5.
    extinction+=shocked*.18;
    emission+=exp(-photonTemperature/shockTemperature)*shocked*shocked*sqrt(1.4e7/shockTemperature)*.012;
    // Illustrative post-shock cooling zone with display-scaled H-alpha and [O III] bands.
    float coolingDepth=max(0.0,wrinkled-r)/max(width,1e-4);
    float coolingTemperature=max(4500.0,shockTemperature*exp(-coolingDepth*2.6));
    float ions=exp(-pow(log(coolingTemperature/12000.0)/.75,2.0));
    float lineDensity=4.0*fade*(.18+filaments*2.8)*ions;
    vec3 lines=mix(vec3(1.0,.19,.025),vec3(.025,.62,.8),smoothstep(.5,.75,plumes));
    emission+=lines*lineDensity*.65;
    // Recombining cooler ejecta: H-alpha dominated filaments; line strength is an illustrative envelope.
    float recombination=exp(-pow(log(max(u._Evolution.w,1000.0)/6500.0)/.55,2.0));
    emission+=vec3(1.0,.08,.035)*envelope*filaments*recombination*.55;
}

void main() {
    vec3 rd = u._CameraLocal.w>.5 ? normalize(u._RayDirection.xyz) : normalize(localPosition-u._CameraLocal.xyz);
    // Rasterize volume exit faces for near-plane coverage.
    if(dot(faceNormal,rd)<=0.0) discard;
    vec3 ro = u._CameraLocal.w>.5 ? localPosition-rd*4.0 : u._CameraLocal.xyz;
    float activeRadius=u._Clock.z>.001 ? max(u._Evolution.x*1.08,u._Geometry.x*1.8) : u._Geometry.x*1.8;
    vec2 interval=sphere(ro,rd,min(1.0,activeRadius));
    if(interval.x>=interval.y) discard;
    float nearT=max(0.0,interval.x),farT=interval.y;
    vec2 surface=sphere(ro,rd,u._Geometry.x);
    bool hitStar=surface.x>=nearT && surface.x<farT;
    if(hitStar) farT=surface.x;
    int count=int(clamp(u._Steps,16.0,64.0));
    float ds=(farT-nearT)/float(count);
    vec2 coronalRange=sphere(ro,rd,u._Geometry.x*1.8);
    coronalRange=clamp(coronalRange,nearT,farT);
    bool resolveCorona=coronalRange.y>coronalRange.x;
    if(resolveCorona) count=64;
    vec3 radiance=vec3(0.0);float transmittance=1.0;
    for(int step=0;step<64;step++) {
        if(step>=count || transmittance<.008) break;
        // Subpixel quadrature for thin-shell sampling.
        float jitter=.25+.5*hash3(vec3(gl_FragCoord.xy,float(step)));
        float sampleT=nearT+(float(step)+jitter)*ds;
        // Increase sampling density in the stellar atmosphere.
        if(resolveCorona) {
            int region=step<16?0:step<48?1:2;
            int index=region==0?step:region==1?step-16:step-48;
            float samples=region==1?32.0:16.0;
            float lo=region==0?nearT:region==1?coronalRange.x:coronalRange.y;
            float hi=region==0?coronalRange.x:region==1?coronalRange.y:farT;
            ds=(hi-lo)/samples; sampleT=lo+(float(index)+(region==1?.5:jitter))*ds;
        }
        vec3 p=ro+rd*sampleT;
        float sigma;vec3 source;sampleMedium(p,sigma,source);
        // Analytic homogeneous-segment integration of dI/ds=j-sigma I (Beer-Lambert).
        float attenuation=exp(-sigma*ds);
        float integral=sigma>1e-4 ? (1.0-attenuation)/sigma : ds;
        radiance+=transmittance*source*integral;
        transmittance*=attenuation;
    }
    if(hitStar) { radiance+=transmittance*photosphere(ro+rd*surface.x,rd); transmittance=0.0; }
    float opacity=1.0-transmittance;
    // Compress emissive intensity while preserving spectral ratios.
    float peak=max(max(radiance.r,radiance.g),radiance.b);
    vec3 mapped=radiance/max(peak,1e-5)*(1.0-exp(-peak*u._Exposure));
    opacity=max(opacity,max(max(mapped.r,mapped.g),mapped.b)*.75);
    if(opacity<.001) discard;
    outColor=vec4(mapped,opacity)*u._Appearance;
}
