#version 410 core

// Shadow maps of point and spot lights: one view of the light, rendered with the distance from it (see .frag).

layout(location = 0) in vec3 a_Position;

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
uniform mat4 u_ViewProjection;

out vec3 v_WorldPosition;

void main()
{
    vec4 world = u_Model * skinMatrix() * vec4(a_Position, 1.0);
    v_WorldPosition = world.xyz;
    gl_Position = u_ViewProjection * world;
    gl_Position.z = gl_Position.z * 2.0 - gl_Position.w; // [0, 1] depth from System.Numerics to OpenGL's [-1, 1]
}
