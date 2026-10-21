#version 450 core

in vec3 v_WorldPosition;
in vec3 v_Normal;
in vec2 v_TexCoord;

// Colours are in linear space; the result is gamma-encoded at the end.
uniform vec3 u_Albedo;
uniform float u_Shininess;

uniform vec3 u_CameraPosition;

// Direction the light travels (from the light towards the scene).
uniform vec3 u_LightDirection;
uniform vec3 u_LightColor;

// Hemisphere ambient: sky colour from above, ground colour from below.
uniform vec3 u_SkyColor;
uniform vec3 u_GroundColor;

out vec4 o_Color;

void main()
{
    vec3 normal = normalize(v_Normal);
    vec3 toLight = normalize(-u_LightDirection);
    vec3 toCamera = normalize(u_CameraPosition - v_WorldPosition);
    vec3 halfway = normalize(toLight + toCamera);

    float diffuse = max(dot(normal, toLight), 0.0);
    float specular = diffuse > 0.0 ? pow(max(dot(normal, halfway), 0.0), u_Shininess) : 0.0;
    vec3 ambient = mix(u_GroundColor, u_SkyColor, normal.y * 0.5 + 0.5);

    vec3 color = u_Albedo * (ambient + u_LightColor * diffuse) + u_LightColor * specular * 0.25;

    o_Color = vec4(pow(color, vec3(1.0 / 2.2)), 1.0);
}
