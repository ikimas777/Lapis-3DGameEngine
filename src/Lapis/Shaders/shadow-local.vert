#version 410 core

// Shadow maps of point and spot lights: one view of the light, rendered with the distance from it (see .frag).

layout(location = 0) in vec3 a_Position;

uniform mat4 u_Model;
uniform mat4 u_ViewProjection;

out vec3 v_WorldPosition;

void main()
{
    vec4 world = u_Model * vec4(a_Position, 1.0);
    v_WorldPosition = world.xyz;
    gl_Position = u_ViewProjection * world;
    gl_Position.z = gl_Position.z * 2.0 - gl_Position.w; // [0, 1] depth from System.Numerics to OpenGL's [-1, 1]
}
