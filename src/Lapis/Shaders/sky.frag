#version 450 core

in vec3 v_NearPoint;
in vec3 v_FarPoint;

uniform vec3 u_ZenithColor;   // linear
uniform vec3 u_HorizonColor;  // linear
uniform vec3 u_GroundColor;   // linear
uniform vec3 u_SunDirection;  // direction towards the sun
uniform vec3 u_SunColor;      // linear, zero when there is no sun

out vec4 o_Color;

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

    o_Color = vec4(pow(clamp(color, 0.0, 1.0), vec3(1.0 / 2.2)), 1.0);
}
