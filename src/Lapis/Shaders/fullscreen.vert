#version 450 core

// One triangle that covers the whole screen; no vertex buffer needed.
// Outputs the world-space view ray for each pixel.

uniform mat4 u_InverseViewProjection;

out vec3 v_NearPoint;
out vec3 v_FarPoint;

vec3 unproject(vec2 ndc, float depth)
{
    vec4 world = u_InverseViewProjection * vec4(ndc, depth, 1.0);
    return world.xyz / world.w;
}

void main()
{
    vec2 position = vec2((gl_VertexID << 1) & 2, gl_VertexID & 2) * 2.0 - 1.0;
    v_NearPoint = unproject(position, 0.0);
    v_FarPoint = unproject(position, 1.0);
    gl_Position = vec4(position, 1.0, 1.0); // depth 1: behind everything already drawn
}
