#version 410 core

#define MAX_LOCAL_LIGHTS 8
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

// Point and spot lights; colours are premultiplied by intensity. A spot light lights the cone around its
// direction: full light inside cos(inner), none outside cos(outer). Point lights use a cone that covers everything.
uniform int u_LocalLightCount;
uniform vec3 u_LocalLightPositions[MAX_LOCAL_LIGHTS];
uniform vec3 u_LocalLightColors[MAX_LOCAL_LIGHTS];
uniform float u_LocalLightRanges[MAX_LOCAL_LIGHTS];
uniform vec3 u_LocalLightDirections[MAX_LOCAL_LIGHTS];
uniform vec2 u_LocalLightCones[MAX_LOCAL_LIGHTS]; // (cos outer, cos inner)

// Shadows of point and spot lights. u_LocalLightShadows is the light's layer in the spot or point shadow maps, or -1
// for none. The maps store the distance from the light divided by its range.
#define MAX_SPOT_SHADOWS 4
uniform int u_LocalLightShadows[MAX_LOCAL_LIGHTS];
uniform sampler2DArrayShadow u_SpotShadowMaps;   // unit 2
uniform samplerCubeArrayShadow u_PointShadowMaps; // unit 3
uniform mat4 u_SpotShadowMatrices[MAX_SPOT_SHADOWS];
uniform float u_SpotShadowTexel;   // 1 / spot shadow map size
uniform float u_PointShadowTexel;  // 1 / point shadow map face size

// Hemisphere ambient: sky colour from above, ground colour from below.
uniform vec3 u_SkyColor;
uniform vec3 u_GroundColor;

// Fog: 0 = off, 1 = linear from start to end, 2 = exponential with the density. The colour is sRGB.
uniform int u_FogMode;
uniform vec3 u_FogColor;
uniform float u_FogDensity;
uniform float u_FogStart;
uniform float u_FogEnd;
uniform float u_FogHeight;  // world Y of the top of the fog

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

// How much of point or spot light i reaches this surface past the objects between them (1 = all, 0 = in shadow).
float localShadowFactor(int i, vec3 normal, vec3 toLight, float dist)
{
    int index = u_LocalLightShadows[i];
    if (index < 0)
    {
        return 1.0;
    }

    // Normal offset grows with the distance, as one shadow texel covers more of the surface further away.
    float slope = 1.0 - max(dot(normal, toLight), 0.0);
    vec3 position = v_WorldPosition + normal * (0.01 + dist * 0.015) * (0.5 + slope);
    vec3 fromLight = position - u_LocalLightPositions[i];
    float depth = length(fromLight) / u_LocalLightRanges[i] - 0.002;
    float lit = 0.0;

    if (u_LocalLightCones[i].x > -1.5) // a spot light: one view
    {
        vec4 clip = u_SpotShadowMatrices[index] * vec4(position, 1.0);
        if (clip.w <= 0.0)
        {
            return 1.0;
        }

        vec2 uv = clip.xy / clip.w * 0.5 + 0.5;
        for (int x = -1; x <= 1; x++)
        {
            for (int y = -1; y <= 1; y++)
            {
                lit += texture(u_SpotShadowMaps, vec4(uv + vec2(x, y) * u_SpotShadowTexel, float(index), depth));
            }
        }

        return lit / 9.0;
    }

    // A point light: a cube around it, sampled in the direction from the light, plus a few directions around it.
    vec3 direction = normalize(fromLight);
    vec3 side = normalize(cross(direction, abs(direction.y) < 0.99 ? vec3(0.0, 1.0, 0.0) : vec3(1.0, 0.0, 0.0)));
    vec3 other = cross(direction, side);
    float spread = 2.0 * u_PointShadowTexel;
    for (int x = -1; x <= 1; x++)
    {
        for (int y = -1; y <= 1; y++)
        {
            vec3 sampleDirection = direction + (side * float(x) + other * float(y)) * spread;
            lit += texture(u_PointShadowMaps, vec4(sampleDirection, float(index)), depth);
        }
    }

    return lit / 9.0;
}

// How much fog covers a point this far from the camera (0 = clear, 1 = only fog).
float fogAmount(float dist)
{
    if (u_FogMode == 1)
    {
        return clamp((dist - u_FogStart) / max(u_FogEnd - u_FogStart, 1e-4), 0.0, 1.0);
    }

    if (u_FogMode == 2)
    {
        float d = u_FogDensity * dist;
        return 1.0 - exp(-d * d);
    }

    return 0.0;
}

// Fog lies below u_FogHeight: surfaces below it get the full fog for their distance, surfaces above it none,
// with a soft metre-high edge in between (like mist on the floor seen from above).
float fogBelowHeight(float y)
{
    return 1.0 - smoothstep(u_FogHeight - 0.5, u_FogHeight + 0.5, y);
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

    for (int i = 0; i < u_LocalLightCount; i++)
    {
        vec3 offset = u_LocalLightPositions[i] - v_WorldPosition;
        float dist = length(offset);
        vec3 l = offset / max(dist, 1e-4);
        float ratio = clamp(dist / u_LocalLightRanges[i], 0.0, 1.0);
        float falloff = (1.0 - ratio * ratio) * (1.0 - ratio * ratio) / (1.0 + dist * dist);
        float cone = smoothstep(u_LocalLightCones[i].x, u_LocalLightCones[i].y, dot(-l, u_LocalLightDirections[i]));
        if (falloff * cone <= 0.0)
        {
            continue; // out of reach: no need to look at the shadow map
        }

        color += shade(albedo, f0, n, v, l, u_LocalLightColors[i] * falloff * cone) * localShadowFactor(i, n, l, dist);
    }

    vec3 display = pow(toneMap(color), vec3(1.0 / 2.2));
    float fog = fogAmount(distance(u_CameraPosition, v_WorldPosition)) * fogBelowHeight(v_WorldPosition.y);
    display = mix(display, u_FogColor, fog);
    o_Color = vec4(display, 1.0);
}
