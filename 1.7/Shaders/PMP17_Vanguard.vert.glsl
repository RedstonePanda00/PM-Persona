#version 450
layout(push_constant) uniform Push { mat4 modelMatrix; } push;
layout(set=0,binding=0) uniform VertexUniforms { mat4 ViewProj; } camera;
layout(location=0) in vec3 position; // @vertex Position required
layout(location=3) in vec2 uv; // @vertex TexCoord0 required
layout(location=2) in vec4 color; // @vertex Color optional @default = vec4(1.0)
// Native GUI substitutes its own vertex stage: location 0 is color, 1 is UV.
layout(location=0) out vec4 vColor;
layout(location=1) out vec2 vUv;
void main() { vColor=color; vUv=uv; gl_Position=camera.ViewProj*push.modelMatrix*vec4(position,1.0); }
