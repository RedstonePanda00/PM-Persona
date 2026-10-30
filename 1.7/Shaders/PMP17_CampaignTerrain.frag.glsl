#version 450
layout(set=2,binding=0) uniform MaterialUniforms { vec4 _Scene; vec4 _Eye; vec4 _Forward; vec4 _Right; vec4 _Up; } u;
layout(set=2,binding=1) uniform sampler2D _MainTex;
layout(location=0) in vec4 vColor;
layout(location=1) in vec2 vUv;
layout(location=0) out vec4 outColor;
vec2 terrainUv(vec2 world){return world/(2.*u._Scene.z)+.5;}
float relief(vec2 world){return texture(_MainTex,terrainUv(world)).r*.24;}
void main(){
 vec2 p=(vUv-vec2(u._Eye.w,1.-u._Forward.w))*vec2(u._Scene.y,1.);
 vec3 ro=u._Eye.xyz,fw=u._Forward.xyz,rt=u._Right.xyz,up=u._Up.xyz;
 vec3 rd=normalize(fw+p.x*rt+p.y*up);
 vec3 a=(vec3(-u._Scene.z,0.,-u._Scene.z)-ro)/rd,b=(vec3(u._Scene.z,.24,u._Scene.z)-ro)/rd;
 vec3 nearV=min(a,b),farV=max(a,b);
 float first=max(max(nearV.x,nearV.y),nearV.z),last=min(min(farV.x,farV.y),farV.z);
 vec3 color=vec3(.007,.015,.025);bool found=false;vec3 hit=vec3(0);
 if(first<=last&&last>0.){
  float previous=max(first,0.);float stride=(last-previous)/64.;
  for(int i=0;i<=64;i++){
   float d=max(first,0.)+float(i)*stride;vec3 q=ro+rd*d;
   if(q.y<=relief(q.xz)){
    float low=previous,high=d;
    for(int j=0;j<7;j++){float mid=(low+high)*.5;vec3 test=ro+rd*mid;if(test.y>relief(test.xz))low=mid;else high=mid;}
    hit=ro+rd*high;found=true;break;
   }
   previous=d;
  }
 }
 if(found){
  vec2 uv=hit.xz+.5;vec4 data=texture(_MainTex,terrainUv(hit.xz));float e=2.*u._Scene.z/u._Scene.w;
  vec3 n=normalize(vec3(relief(hit.xz-vec2(e,0))-relief(hit.xz+vec2(e,0)),2.*e,relief(hit.xz-vec2(0,e))-relief(hit.xz+vec2(0,e))));
  float light=.25+.75*max(0.,dot(n,normalize(vec3(-.4,.8,-.2))));
  float contour=1.-smoothstep(.025,.11,abs(fract(hit.y*135.)-.5));
  vec2 grid=abs(fract(uv*24.)-.5);float gridLine=smoothstep(.486,.5,max(grid.x,grid.y));
  float sweep=exp(-pow((fract(uv.y*.25-u._Scene.x*.045)-.5)*65.,2.));
  color=mix(vec3(.015,.055,.10),vec3(.08,.39,.44),data.r)*light;
  color+=vec3(.04,.39,.52)*(contour*.36+data.g*.85+gridLine*.10+sweep*.34);
  color*=.93+.07*sin(gl_FragCoord.y*1.7+u._Scene.x*2.);
 }
 outColor=vec4(color,1.);
}
