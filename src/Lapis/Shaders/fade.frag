#version 410 core

// Screen.FadeTo: the whole picture blended towards one colour (sRGB, alpha = how much).

uniform vec4 u_Color;

out vec4 o_Color;

void main()
{
    o_Color = u_Color;
}
