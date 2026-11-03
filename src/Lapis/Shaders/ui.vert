#version 410 core

// UI quads in pixels: (0, 0) is the top-left corner of the screen, Y goes down.

layout(location = 0) in vec2 a_Position;
layout(location = 1) in vec2 a_TexCoord;
layout(location = 2) in vec4 a_Color;

uniform vec2 u_ScreenSize;

out vec2 v_TexCoord;
out vec4 v_Color;

void main()
{
    v_TexCoord = a_TexCoord;
    v_Color = a_Color;
    vec2 ndc = a_Position / u_ScreenSize * 2.0 - 1.0;
    gl_Position = vec4(ndc.x, -ndc.y, 0.0, 1.0);
}
