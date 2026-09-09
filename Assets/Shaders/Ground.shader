// Bare-ground terrain shader (M0). Built-in Render Pipeline surface shader.
// Vertex colour: r = rock, g = alpine grass/scree, b = engineered flat. No textures needed.
Shader "AlpineSim/Ground"
{
    Properties
    {
        _RockColor ("Rock", Color) = (0.42, 0.40, 0.38, 1)
        _GrassColor ("Grass", Color) = (0.36, 0.42, 0.22, 1)
        _FlatColor ("Flat", Color) = (0.30, 0.30, 0.31, 1)
        _MapSize ("Map Size (m)", Float) = 2048
        _DetailScale ("Detail Scale", Float) = 0.15
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200
        CGPROGRAM
        #pragma surface surf Lambert fullforwardshadows vertex:vert
        #pragma target 3.0

        fixed4 _RockColor, _GrassColor, _FlatColor;
        float _MapSize, _DetailScale;

        struct Input
        {
            float2 uv_dummy;
            float4 color : COLOR;
            float3 worldPos;
        };

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
        }

        float hash21(float2 p)
        {
            p = frac(p * float2(123.34, 456.21));
            p += dot(p, p + 45.32);
            return frac(p.x * p.y);
        }

        void surf(Input IN, inout SurfaceOutput o)
        {
            float3 c = IN.color.r * _RockColor.rgb + IN.color.g * _GrassColor.rgb + IN.color.b * _FlatColor.rgb;
            float n = hash21(floor(IN.worldPos.xz * _DetailScale));
            c *= 0.92 + 0.16 * n;
            o.Albedo = c;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
