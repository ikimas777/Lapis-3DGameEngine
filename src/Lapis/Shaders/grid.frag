#version 410 core

// Infinite editor grid on the ground plane (Y = 0): 1 m cells, a brighter line every 10 m, and the
// X (red) and Z (blue) axes. It fades with distance and is hidden behind objects via its depth.

in vec3 v_NearPoint;
in vec3 v_FarPoint;

uniform mat4 u_ViewProjection;
uniform vec3 u_CameraPosition;

out vec4 o_Color;

float gridLines(vec2 coord, float spacing)
{
    vec2 scaled = coord / spacing;
    vec2 width = fwidth(scaled);
    vec2 lines = abs(fract(scaled - 0.5) - 0.5) / width;
    return 1.0 - min(min(lines.x, lines.y), 1.0);
}

void main()
{
    vec3 ray = v_FarPoint - v_NearPoint;
    float t = -v_NearPoint.y / ray.y;
    if (t <= 0.0)
    {
        discard; // looking away from the ground
    }

    vec3 position = v_NearPoint + t * ray;
    vec4 clip = u_ViewProjection * vec4(position, 1.0);
    gl_FragDepth = clip.z / clip.w;

    float small = gridLines(position.xz, 1.0);
    float large = gridLines(position.xz, 10.0);
    float alpha = max(small * 0.25, large * 0.5);
    vec3 color = vec3(0.75);

    vec2 axisWidth = fwidth(position.xz) * 1.5;
    if (abs(position.z) < axisWidth.y) { color = vec3(0.9, 0.25, 0.25); alpha = 0.8; } // X axis
    if (abs(position.x) < axisWidth.x) { color = vec3(0.25, 0.45, 0.95); alpha = 0.8; } // Z axis

    // Fade out with distance and at grazing angles, where lines would shimmer.
    float distanceFade = 1.0 - smoothstep(20.0, 120.0, distance(position.xz, u_CameraPosition.xz));
    float angleFade = smoothstep(0.0, 0.15, abs(normalize(ray).y));
    alpha *= distanceFade * angleFade;
    if (alpha < 0.01)
    {
        discard;
    }

    o_Color = vec4(color, alpha);
}
