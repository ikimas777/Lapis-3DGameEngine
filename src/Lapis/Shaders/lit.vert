#version 410 core

layout(location = 0) in vec3 a_Position;
layout(location = 1) in vec3 a_Normal;
layout(location = 2) in vec2 a_TexCoord;

// Animated models: each vertex follows up to four bones (see BoneBuffer). u_Skinned is 0 for meshes that do not move.
#define MAX_BONES 128
layout(location = 3) in vec4 a_Bones;
layout(location = 4) in vec4 a_Weights;
layout(std140) uniform Bones
{
    mat4 u_Bones[MAX_BONES];
};
uniform int u_Skinned;

mat4 skinMatrix()
{
    if (u_Skinned == 0)
    {
        return mat4(1.0);
    }

    return u_Bones[int(a_Bones.x)] * a_Weights.x + u_Bones[int(a_Bones.y)] * a_Weights.y
         + u_Bones[int(a_Bones.z)] * a_Weights.z + u_Bones[int(a_Bones.w)] * a_Weights.w;
}

uniform mat4 u_Model;
uniform mat4 u_View;
uniform mat4 u_Projection;

out vec3 v_WorldPosition;
out vec3 v_Normal;
out vec2 v_TexCoord;

void main()
{
    mat4 model = u_Model * skinMatrix();
    vec4 worldPosition = model * vec4(a_Position, 1.0);

    v_WorldPosition = worldPosition.xyz;
    v_Normal = mat3(transpose(inverse(model))) * a_Normal;
    v_TexCoord = a_TexCoord;

    gl_Position = u_Projection * u_View * worldPosition;
    gl_Position.z = gl_Position.z * 2.0 - gl_Position.w; // [0, 1] depth from System.Numerics to OpenGL's [-1, 1]
}
