#version 450
layout(set=2,binding=0) uniform MaterialUniforms { float _Pulse; } u;
layout(set=2,binding=1) uniform sampler2D _MainTex;
layout(location=0) in vec4 vColor;
layout(location=1) in vec2 vUv;
layout(location=0) out vec4 outColor;
void main(){vec4 t=texture(_MainTex,vUv);float mask=smoothstep(.12,.28,t.r-t.b)*smoothstep(.68,.8,t.g)*t.a;outColor=vec4(1.,.76,.12,mask*u._Pulse);}
