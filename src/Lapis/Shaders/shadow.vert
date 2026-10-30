#version 410 core

layout(location = 0) in vec3 a_Position;

uniform mat4 u_Model;
uniform mat4 u_LightViewProjection;

void main()
{
    gl_Position = u_LightViewProjection * u_Model * vec4(a_Position, 1.0);
    gl_Position.z = gl_Position.z * 2.0 - gl_Position.w; // [0, 1] depth from System.Numerics to OpenGL's [-1, 1]
}
