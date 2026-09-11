// Instanced flat-colour shader for guest markers and lift carriers (Built-in RP).
// Colour comes from a per-material _Color; per-instance transforms via Graphics.DrawMeshInstanced.
Shader "AlpineSim/Instanced"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _Glossiness ("Smoothness", Range(0,1)) = 0.2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 150
        CGPROGRAM
        #pragma surface surf Lambert addshadow
        #pragma multi_compile_instancing
        #pragma target 3.0
        fixed4 _Color;
        struct Input
        {
            float4 color : COLOR;
        };
        UNITY_INSTANCING_BUFFER_START(Props)
        UNITY_INSTANCING_BUFFER_END(Props)
        void surf(Input IN, inout SurfaceOutput o)
        {
            o.Albedo = _Color.rgb * IN.color.rgb;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
