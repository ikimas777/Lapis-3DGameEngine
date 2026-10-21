#version 450 core

#define MAX_POINT_LIGHTS 8

in vec3 v_WorldPosition;
in vec3 v_Normal;
in vec2 v_TexCoord;

// Colours are in linear space; the result is gamma-encoded at the end.
uniform vec3 u_Albedo;
uniform sampler2D u_Texture; // sRGB texture (decoded to linear by the GPU); white when there is none
uniform float u_Shininess;

uniform vec3 u_CameraPosition;

// Directional light. u_LightDirection is the direction the light travels.
uniform vec3 u_LightDirection;
uniform vec3 u_LightColor;

// Point lights. Colours are premultiplied by intensity.
uniform int u_PointLightCount;
uniform vec3 u_PointLightPositions[MAX_POINT_LIGHTS];
uniform vec3 u_PointLightColors[MAX_POINT_LIGHTS];
uniform float u_PointLightRanges[MAX_POINT_LIGHTS];

// Hemisphere ambient: sky colour from above, ground colour from below.
uniform vec3 u_SkyColor;
uniform vec3 u_GroundColor;

out vec4 o_Color;

vec3 shade(vec3 albedo, vec3 normal, vec3 toCamera, vec3 toLight, vec3 radiance)
{
    float diffuse = max(dot(normal, toLight), 0.0);
    if (diffuse <= 0.0)
    {
        return vec3(0.0);
    }

    vec3 halfway = normalize(toLight + toCamera);
    float specular = pow(max(dot(normal, halfway), 0.0), u_Shininess);
    return radiance * (albedo * diffuse + specular * 0.25);
}

void main()
{
    vec3 normal = normalize(v_Normal);
    vec3 toCamera = normalize(u_CameraPosition - v_WorldPosition);

    vec3 albedo = u_Albedo * texture(u_Texture, v_TexCoord).rgb;
    vec3 color = albedo * mix(u_GroundColor, u_SkyColor, normal.y * 0.5 + 0.5) * 0.4;
    color += shade(albedo, normal, toCamera, normalize(-u_LightDirection), u_LightColor);

    for (int i = 0; i < u_PointLightCount; i++)
    {
        vec3 offset = u_PointLightPositions[i] - v_WorldPosition;
        float distance = length(offset);
        float ratio = clamp(distance / u_PointLightRanges[i], 0.0, 1.0);
        float falloff = (1.0 - ratio * ratio) * (1.0 - ratio * ratio) / (1.0 + distance * distance);
        color += shade(albedo, normal, toCamera, offset / max(distance, 1e-4), u_PointLightColors[i] * falloff);
    }

    o_Color = vec4(pow(color, vec3(1.0 / 2.2)), 1.0);
}
