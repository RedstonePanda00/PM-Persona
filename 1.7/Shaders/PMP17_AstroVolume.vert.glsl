#version 450
layout(push_constant) uniform Push { mat4 modelMatrix; } push;
layout(set = 0, binding = 0) uniform VertexUniforms { mat4 ViewProj; } uVertex;
layout(location = 0) in vec3 position; // @vertex Position required
layout(location = 1) in vec3 normal; // @vertex Normal required
layout(location = 0) out vec3 localPosition;
layout(location = 1) flat out vec3 faceNormal;
void main() {
    localPosition = position;
    faceNormal = normal;
    gl_Position = uVertex.ViewProj * push.modelMatrix * vec4(position, 1.0);
}
