#version 410 core

// Images: an sRGB texture (sampled as linear, so converted back) times the colour.
// Text: white glyph coverage in the alpha channel times the colour. Colours are sRGB, like the rest of the picture.

in vec2 v_TexCoord;
in vec4 v_Color;

uniform sampler2D u_Texture;
uniform int u_IsText;

out vec4 o_Color;

void main()
{
    vec4 texel = texture(u_Texture, v_TexCoord);
    if (u_IsText == 1)
    {
        o_Color = vec4(v_Color.rgb, v_Color.a * texel.a);
        return;
    }

    o_Color = vec4(pow(texel.rgb, vec3(1.0 / 2.2)), texel.a) * v_Color;
}
