#version 410 core

in vec3 v_NearPoint;
in vec3 v_FarPoint;

uniform vec3 u_ZenithColor;   // linear
uniform vec3 u_HorizonColor;  // linear
uniform vec3 u_GroundColor;   // linear
uniform vec3 u_SunDirection;  // direction towards the sun
uniform vec3 u_SunColor;      // linear, zero when there is no sun

// Fog, as in lit.frag: 0 = off, 1 = linear, 2 = exponential. The colour is sRGB.
uniform int u_FogMode;
uniform vec3 u_FogColor;
uniform float u_FogDensity;
uniform float u_FogStart;
uniform float u_FogEnd;
uniform float u_FogHeight;    // world Y of the top of the fog
uniform float u_CameraHeight; // world Y of the camera

out vec4 o_Color;

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

// How far a view ray travels through the fog before it reaches the sky. The fog is a layer up to u_FogHeight:
// from inside it, rays near the horizon cross a lot of fog and steep ones little, so a higher fog climbs further
// up the sky. Rays to the horizon and below stay in the fog for ever, and from above the fog only those do.
float fogPath(float height)
{
    float depth = u_FogHeight - u_CameraHeight;
    if (height <= 0.0)
    {
        return 1e6;
    }

    return depth <= 0.0 ? 0.0 : depth / height;
}

void main()
{
    vec3 direction = normalize(v_FarPoint - v_NearPoint);
    float height = direction.y;

    vec3 sky = mix(u_HorizonColor, u_ZenithColor, pow(clamp(height, 0.0, 1.0), 0.6));
    vec3 ground = mix(u_HorizonColor, u_GroundColor, clamp(-height * 4.0, 0.0, 1.0));
    vec3 color = height >= 0.0 ? sky : ground;

    // Sun disk with a soft glow around it.
    float sun = max(dot(direction, u_SunDirection), 0.0);
    color += u_SunColor * (pow(sun, 2000.0) * 8.0 + pow(sun, 40.0) * 0.25) * step(0.0, height + 0.02);

    vec3 display = pow(clamp(color, 0.0, 1.0), vec3(1.0 / 2.2));
    display = mix(display, u_FogColor, fogAmount(fogPath(height)));
    o_Color = vec4(display, 1.0);
}
