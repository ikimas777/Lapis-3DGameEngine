#version 410 core

#define MAX_POINT_LIGHTS 8
#define PI 3.14159265

in vec3 v_WorldPosition;
in vec3 v_Normal;
in vec2 v_TexCoord;

// Surface (physically based, metallic / roughness). Colours are linear.
uniform vec3 u_Albedo;
uniform float u_Metallic;
uniform float u_Roughness;
uniform sampler2D u_Texture;          // unit 0: sRGB albedo texture, white when there is none

uniform vec3 u_CameraPosition;

// Directional light; u_LightDirection is the direction the light travels.
uniform vec3 u_LightDirection;
uniform vec3 u_LightColor;

// Shadow map of the directional light.
uniform sampler2DShadow u_ShadowMap;  // unit 1
uniform mat4 u_LightViewProjection;
uniform int u_ShadowsEnabled;
uniform float u_ShadowTexelSize;      // world units per shadow texel
uniform vec3 u_ShadowCenter;
uniform float u_ShadowDistance;

// Point lights; colours are premultiplied by intensity.
uniform int u_PointLightCount;
uniform vec3 u_PointLightPositions[MAX_POINT_LIGHTS];
uniform vec3 u_PointLightColors[MAX_POINT_LIGHTS];
uniform float u_PointLightRanges[MAX_POINT_LIGHTS];

// Hemisphere ambient: sky colour from above, ground colour from below.
uniform vec3 u_SkyColor;
uniform vec3 u_GroundColor;

out vec4 o_Color;

float distributionGgx(float nDotH, float roughness)
{
    float a = roughness * roughness;
    float a2 = a * a;
    float d = nDotH * nDotH * (a2 - 1.0) + 1.0;
    return a2 / (PI * d * d);
}

float geometrySmith(float nDotV, float nDotL, float roughness)
{
    float k = (roughness + 1.0) * (roughness + 1.0) / 8.0;
    float gv = nDotV / (nDotV * (1.0 - k) + k);
    float gl = nDotL / (nDotL * (1.0 - k) + k);
    return gv * gl;
}

vec3 fresnelSchlick(float cosTheta, vec3 f0)
{
    return f0 + (1.0 - f0) * pow(clamp(1.0 - cosTheta, 0.0, 1.0), 5.0);
}

// Light reflected towards the camera from one light. The incoming light is scaled by PI so that a
// white light of intensity 1 lights a white surface to full brightness, which keeps intensities intuitive.
vec3 shade(vec3 albedo, vec3 f0, vec3 n, vec3 v, vec3 l, vec3 radiance)
{
    float nDotL = max(dot(n, l), 0.0);
    if (nDotL <= 0.0)
    {
        return vec3(0.0);
    }

    vec3 h = normalize(v + l);
    float nDotV = max(dot(n, v), 1e-4);
    float roughness = clamp(u_Roughness, 0.04, 1.0);

    vec3 f = fresnelSchlick(max(dot(h, v), 0.0), f0);
    float d = distributionGgx(max(dot(n, h), 0.0), roughness);
    float g = geometrySmith(nDotV, nDotL, roughness);
    vec3 specular = d * g * f / (4.0 * nDotV * nDotL + 1e-4);

    vec3 kd = (1.0 - f) * (1.0 - u_Metallic);
    return (kd * albedo / PI + specular) * radiance * nDotL * PI;
}

float shadowFactor(vec3 normal, vec3 toLight)
{
    if (u_ShadowsEnabled == 0)
    {
        return 1.0;
    }

    // Normal offset: sample slightly above the surface to avoid self-shadowing stripes ("acne").
    float slope = 1.0 - max(dot(normal, toLight), 0.0);
    vec3 position = v_WorldPosition + normal * u_ShadowTexelSize * (1.5 + 2.0 * slope);
    vec4 clip = u_LightViewProjection * vec4(position, 1.0);
    vec3 coords = clip.xyz / clip.w;
    coords.xy = coords.xy * 0.5 + 0.5;
    if (coords.x <= 0.0 || coords.x >= 1.0 || coords.y <= 0.0 || coords.y >= 1.0 || coords.z >= 1.0)
    {
        return 1.0;
    }

    // 5 x 5 percentage-closer filtering for soft edges.
    vec2 texel = 1.0 / vec2(textureSize(u_ShadowMap, 0));
    float lit = 0.0;
    for (int x = -2; x <= 2; x++)
    {
        for (int y = -2; y <= 2; y++)
        {
            lit += texture(u_ShadowMap, vec3(coords.xy + vec2(x, y) * texel, coords.z - 0.0005));
        }
    }

    lit /= 25.0;

    // Fade shadows out near the edge of the shadowed area instead of cutting them off.
    float fade = smoothstep(u_ShadowDistance * 0.85, u_ShadowDistance, distance(v_WorldPosition, u_ShadowCenter));
    return mix(lit, 1.0, fade);
}

// ACES filmic tone mapping (Narkowicz's fit): keeps bright highlights from clipping harshly.
vec3 toneMap(vec3 color)
{
    return clamp((color * (2.51 * color + 0.03)) / (color * (2.43 * color + 0.59) + 0.14), 0.0, 1.0);
}

void main()
{
    vec3 n = normalize(v_Normal);
    if (!gl_FrontFacing)
    {
        n = -n; // light the back of thin, double-sided surfaces correctly
    }

    vec3 v = normalize(u_CameraPosition - v_WorldPosition);
    vec3 albedo = u_Albedo * texture(u_Texture, v_TexCoord).rgb;
    vec3 f0 = mix(vec3(0.04), albedo, u_Metallic);

    // Ambient: diffuse from the sky/ground hemisphere, plus a rough approximation of reflected sky.
    vec3 hemisphere = mix(u_GroundColor, u_SkyColor, n.y * 0.5 + 0.5);
    vec3 ambientFresnel = fresnelSchlick(max(dot(n, v), 0.0), f0);
    vec3 color = hemisphere * albedo * (1.0 - u_Metallic) * (1.0 - ambientFresnel)
               + hemisphere * ambientFresnel * (1.0 - u_Roughness * 0.7);

    vec3 toSun = normalize(-u_LightDirection);
    color += shade(albedo, f0, n, v, toSun, u_LightColor) * shadowFactor(n, toSun);

    for (int i = 0; i < u_PointLightCount; i++)
    {
        vec3 offset = u_PointLightPositions[i] - v_WorldPosition;
        float dist = length(offset);
        float ratio = clamp(dist / u_PointLightRanges[i], 0.0, 1.0);
        float falloff = (1.0 - ratio * ratio) * (1.0 - ratio * ratio) / (1.0 + dist * dist);
        color += shade(albedo, f0, n, v, offset / max(dist, 1e-4), u_PointLightColors[i] * falloff);
    }

    o_Color = vec4(pow(toneMap(color), vec3(1.0 / 2.2)), 1.0);
}
