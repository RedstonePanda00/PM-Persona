#version 450
layout(set=2,binding=0) uniform MaterialUniforms {
    vec4 _Clock; // @default = vec4(0.0, 0.0, 1.0, 1.7778)
    vec4 _Jet; // @default = vec4(0.2, 0.1, 0.84, 0.54)
    vec4 _Planet; // @default = vec4(0.0)
} u;
layout(set=2,binding=1) uniform sampler2D _MainTex;
layout(location=0) in vec4 vColor;
layout(location=1) in vec2 vUv;
layout(location=0) out vec4 outColor;
float hash(vec2 p) {
    uvec2 q=uvec2(ivec2(p));
    uint h=q.x*374761393u+q.y*668265263u;
    h=(h^(h>>13u))*1274126177u; h^=h>>16u;
    return float(h&0x00ffffffu)/16777215.;
}
float noise(vec2 p) { vec2 i=floor(p),f=fract(p);f=f*f*(3.-2.*f);return mix(mix(hash(i),hash(i+vec2(1,0)),f.x),mix(hash(i+vec2(0,1)),hash(i+1.),f.x),f.y); }
float fbm(vec2 p) {return .58*noise(p)+.27*noise(p*2.1+17.)+.15*noise(p*4.3+43.);}
void main() {
    // GUI geometry uses a top-left origin; native interpolated UVs use bottom-left.
    vec2 uv=vec2(vUv.x,1.-vUv.y);
    float t=u._Clock.x;
    vec3 color=vec3(0.);float alpha=u._Clock.z;
    if(u._Clock.y<.5) {
        vec2 p=(uv-u._Jet.xy)*vec2(u._Clock.w,1.);
        float r=max(length(p),.012),theta=atan(p.y,p.x);
        float dust=pow(fbm(p*2.5+vec2(t*.04,0.)),3.);
        color=vec3(.005,.008,.020)+dust*vec3(.08,.065,.19);
        for(int layer=0;layer<3;layer++) {
            float count=137.+float(layer)*67.;
            float sector=floor((theta+3.14159)/6.28318*count);
            float random=hash(vec2(sector,float(layer)+41.));
            float angle=(sector+.15+random*.70)/count*6.28318-3.14159;
            float da=abs(atan(sin(theta-angle),cos(theta-angle)))*r;
            float head=exp(fract(random+t*(.40+float(layer)*.13))*4.-2.3);
            // Finite, moving streaks instead of continuous spokes from the vanishing point.
            float trailLength=.018+head*.075;
            float trail=smoothstep(head-trailLength,head-trailLength*.65,r)*(1.-smoothstep(head,head+.006,r));
            float star=exp(-da*da/.0000012)*trail*step(.48,random);
            color+=vec3(.56,.73,1.)*star*(.65+float(layer)*.25);
        }
        // Reveal the actual world camera through its projected spherical silhouette.
        // No replacement planet texture, shader sphere or extra world capture is involved.
        float planetDistance=length((uv-u._Planet.xy)*vec2(u._Clock.w,1.));
        float outside=smoothstep(u._Planet.z-.001,u._Planet.z+.002,planetDistance);
        alpha*=mix(1.,outside,u._Planet.w);
    } else {
        vec2 delta=(uv-u._Jet.xy)*vec2(u._Clock.w,1.);
        vec2 projected=normalize(u._Jet.zw);
        float along=dot(delta,projected),across=abs(dot(delta,vec2(-projected.y,projected.x)));
        // Ray-march an inclined 3D jet. Shock cells and toroidal Mach rings are volume
        // densities, not painted rings or a stretched flame texture.
        if(along<-.035 || along>.36 || across>.065) discard;
        vec3 axis=normalize(vec3(projected,.42));
        float opacity=0.;vec3 emission=vec3(0.);
        const float ds=.00625;
        for(int i=0;i<48;i++) {
            vec3 samplePoint=vec3(delta,-.07+(float(i)+.5)*ds);
            float axial=dot(samplePoint,axis);
            if(axial<0. || axial>.36) continue;
            float radial=length(samplePoint-axis*axial);
            float cell=axial/.047;
            float shock=abs(fract(cell)-.36)*.047;
            float radius=(.014+axial*.025)*(.78+.22*cos(cell*6.28318));
            float core=exp(-pow(radial/(radius*.62),2.));
            float envelope=exp(-pow(radial/radius,4.));
            float ring=exp(-pow((radial-radius*.84)/.0028,2.)-pow(shock/.0035,2.));
            float compression=exp(-pow(shock/.0045,2.))*core;
            float flow=.87+.13*sin(axial*230.-t*38.+radial*150.);
            float fade=exp(-axial*4.)*(1.-smoothstep(.27,.36,axial));
            float density=(envelope*30.+core*40.+ring*85.+compression*50.)*fade*flow;
            float a=1.-exp(-density*ds);
            vec3 tint=mix(vec3(1.,.43,.035),vec3(1.,.94,.58),clamp(core*.3+ring+compression*.7,0.,1.));
            emission+=(1.-opacity)*tint*a;opacity+=(1.-opacity)*a;
        }
        color=emission/max(opacity,.001)*1.3;alpha*=opacity;
    }
    outColor=vec4(color,alpha)*vColor;
}
