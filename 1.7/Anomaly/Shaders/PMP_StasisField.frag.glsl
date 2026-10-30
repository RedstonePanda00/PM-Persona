#version 450
layout(set=2,binding=0) uniform MaterialUniforms { vec4 _State; } u;
layout(location=0) in vec4 vColor;
layout(location=1) in vec2 vUv;
layout(location=0) out vec4 outColor;

// Orthographic rays through five planes. The bottom is the building sprite;
// four walls and a roof form continuous luminous sheets around the entity.
const float halfWidth=1.29;
const float height=halfWidth*2.;
// Vertical edges have no horizontal shear. Side walls are edge-on to the ray.
const vec2 lift=vec2(0.,.62);

float planeDistance(float numerator,float direction) {
 return abs(direction)>.00001 ? numerator/direction : -1.e20;
}

float lattice(vec2 cell) {
 return fract(sin(dot(cell,vec2(41.73,289.19)))*15327.379);
}
float noise2(vec2 p) {
 vec2 cell=floor(p),f=fract(p);
 f=f*f*(3.-2.*f);
 return mix(mix(lattice(cell),lattice(cell+vec2(1.,0.)),f.x),
            mix(lattice(cell+vec2(0.,1.)),lattice(cell+vec2(1.,1.)),f.x),f.y);
}
float flow(vec2 p) {
 return noise2(p)*.67+noise2(p*2.03+vec2(13.4,7.2))*.33;
}

vec4 curtain(vec3 hit, int face, vec3 ray) {
 vec3 normal=face==4 ? vec3(0.,1.,0.) : (face<2 ? vec3(1.,0.,0.) : vec3(0.,0.,1.));
 vec2 local=face==4 ? hit.xz/halfWidth : vec2((face<2 ? hit.z : hit.x)/halfWidth,hit.y/height*2.-1.);
 float edge=1.-max(abs(local.x),abs(local.y));
 float aa=max(fwidth(edge),.002);
 float coverage=smoothstep(-aa,aa,edge);
 // Screen-visible glow extends outside the plane, independently of bloom.
 float rim=1.-smoothstep(.008,.018+aa,abs(edge));
 float halo=exp(-abs(edge)*15.);
 float grazing=pow(1.-abs(dot(normal,normalize(ray))),1.6);
 float time=u._State.x;
 // Opposing flow layers bend each other's coordinates. Pin the deformation
 // near the seams so the five curtains keep forming a closed cage.
 vec2 drift=vec2(time*.18,-time*.31);
 vec2 warp=vec2(flow(local*1.7+drift),flow(local*1.7-drift+vec2(9.,4.)))-.5;
 vec2 bent=local+warp*.42*smoothstep(0.,.3,edge);
 float turbulence=flow(bent*3.1+vec2(-time*.24,time*.16));
 float phase=bent.y*11.+bent.x*2.5+turbulence*7.-time*2.7;
 float ribbons=pow(.5+.5*sin(phase),7.);
 float swell=.5+.5*sin(bent.x*4.-bent.y*3.+time*1.2+turbulence*5.);
 float alpha=(.16+grazing*.06+turbulence*.06+ribbons*.10+swell*.035)*coverage;
 if(face==4)alpha+=.035*coverage;
 vec3 tint=mix(vec3(1.,.65,.12),vec3(1.,.92,.53),turbulence*.65);
 vec3 radiance=tint*alpha*(face==4 ? 1.45 : 1.25);
 radiance+=vec3(1.,.82,.34)*ribbons*.13*coverage;
 float pulse=.88+.12*sin(time*2.2+hit.y*4.+hit.x*3.);
 radiance+=vec3(1.,.92,.64)*(rim*.85+halo*.25)*pulse;
 return vec4(radiance,clamp(alpha+rim*.24+halo*.08,0.,.85));
}

void main() {
 // Undo the draw plane offset so the lower corners sit on the base.
 vec2 p=(vUv-.5)*5.4+vec2(0.,.825);
 vec3 origin=vec3(p.x-lift.x*4.,4.,p.y-lift.y*4.);
 vec3 ray=vec3(lift.x,-1.,lift.y);
 float distances[5];
 distances[0]=planeDistance(-halfWidth-origin.x,ray.x);
 distances[1]=planeDistance( halfWidth-origin.x,ray.x);
 distances[2]=planeDistance(-halfWidth-origin.z,ray.z);
 distances[3]=planeDistance( halfWidth-origin.z,ray.z);
 distances[4]=(height-origin.y)/ray.y;
 int faces[5]=int[5](0,1,2,3,4);
 // Back-to-front alpha composition includes the translucent far walls.
 for(int i=0;i<4;i++)for(int j=i+1;j<5;j++)if(distances[i]<distances[j]){
  float t=distances[i];distances[i]=distances[j];distances[j]=t;
  int f=faces[i];faces[i]=faces[j];faces[j]=f;
 }
 vec4 result=vec4(0.);
 for(int i=0;i<5;i++){
  if(distances[i]<0.||distances[i]>1.e10)continue;
  vec3 hit=origin+ray*distances[i];
  vec4 sheet=curtain(hit,faces[i],ray);
  result=sheet+result*(1.-sheet.a);
 }
 outColor=result;
}
