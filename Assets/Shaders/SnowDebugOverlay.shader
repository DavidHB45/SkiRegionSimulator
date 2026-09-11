// Snow debug overlay (M1): false-colour ramps drawn as a second material on the terrain meshes.
// _Mode: 1 depth (0..2 m, blue->white), 2 density (100..600 kg/m3, green->red), 3 roughness (0..1, white->magenta),
//        4 PQI proxy (from depth/density/roughness, red->green), 5 groom age (0..1, green->grey), 6 surface type (categorical).
Shader "AlpineSim/SnowDebugOverlay"
{
    Properties
    {
        _SnowMap ("Snow Map", 2D) = "black" {}
        _SurfaceMap ("Surface Map", 2D) = "black" {}
        _Mode ("Mode", Int) = 1
        _Alpha ("Alpha", Range(0,1)) = 0.75
        _DisplacementScale ("Displacement Scale", Float) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        LOD 100
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Offset -1, -1
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _SnowMap;
            sampler2D _SurfaceMap;
            int _Mode;
            float _Alpha, _DisplacementScale;

            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata_full v)
            {
                v2f o;
                float4 snow = tex2Dlod(_SnowMap, float4(v.texcoord.xy, 0, 0));
                v.vertex.xyz += v.normal * (snow.r * _DisplacementScale + 0.02);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord.xy;
                return o;
            }

            float3 Ramp(float t, float3 a, float3 b, float3 c)
            {
                return t < 0.5 ? lerp(a, b, t * 2.0) : lerp(b, c, (t - 0.5) * 2.0);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float4 snow = tex2D(_SnowMap, i.uv);
                float4 surf = tex2D(_SurfaceMap, i.uv);
                float3 col = 0;
                if (_Mode == 1) col = Ramp(saturate(snow.r / 2.0), float3(0.05, 0.1, 0.5), float3(0.3, 0.6, 1.0), float3(1, 1, 1));
                else if (_Mode == 2) col = Ramp(saturate(snow.g), float3(0.2, 0.9, 0.3), float3(0.95, 0.9, 0.2), float3(0.9, 0.15, 0.1));
                else if (_Mode == 3) col = lerp(float3(1, 1, 1), float3(0.9, 0.1, 0.8), saturate(snow.b));
                else if (_Mode == 4)
                {
                    float cover = saturate(snow.r / 0.15);
                    float dens = 1.0 - abs(snow.g - 0.62) * 2.2;
                    float pqi = saturate(cover * (0.45 * saturate(dens) + 0.35 * (1.0 - snow.b) + 0.2 * (1.0 - snow.a)));
                    col = Ramp(pqi, float3(0.85, 0.1, 0.1), float3(0.95, 0.85, 0.2), float3(0.15, 0.85, 0.3));
                }
                else if (_Mode == 5) col = lerp(float3(0.2, 0.9, 0.3), float3(0.45, 0.45, 0.5), saturate(snow.a));
                else
                {
                    int id = (int)(surf.r * 255.0 + 0.5);
                    if (id == 0) col = float3(0.35, 0.35, 0.4);      // off-piste
                    else if (id == 1) col = float3(0.2, 0.5, 1.0);   // piste
                    else if (id == 2) col = float3(0.3, 0.3, 0.3);   // road
                    else if (id == 3) col = float3(0.6, 0.6, 0.2);   // lot
                    else if (id == 4) col = float3(1.0, 0.5, 0.1);   // lift ramp
                    else if (id == 5) col = float3(0.8, 0.8, 0.5);   // nordic trail
                    else if (id == 6) col = float3(0.9, 0.2, 0.9);   // park
                    else col = float3(0.9, 0.9, 0.9);                // base area
                }
                return fixed4(col, _Alpha);
            }
            ENDCG
        }
    }
}
