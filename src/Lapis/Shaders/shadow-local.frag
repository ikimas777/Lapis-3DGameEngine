#version 410 core

// Stores the distance from the light divided by its range, the same for every view of a point light's cube.

in vec3 v_WorldPosition;

uniform vec3 u_LightPosition;
uniform float u_Range;

void main()
{
    gl_FragDepth = clamp(length(v_WorldPosition - u_LightPosition) / u_Range, 0.0, 1.0);
}
