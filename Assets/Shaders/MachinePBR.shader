// Machines, lifts and props built by tools/assetgen. Built-in Render Pipeline surface shader.
//
// Texture channels are the ones docs/ART_CONTRACT.md section 6 publishes: _MainTex albedo,
// _BumpMap tangent normal, _ORM (R ambient occlusion, G roughness, B metallic) and _WearMask
// (R edge wear, G rust, B salt, A paint chipping). Every one of them is optional. A checkout
// that has never run the asset build has no textures at all, and a machine still has to be
// recognisable on the hill, so the maps that exist are flagged by _UseOrm / _UseWear and the
// rest falls back to flat paint with the smoothness and metallic the material was given.
//
// Livery is a tint on a shared texture set, never a texture of its own: one material serves a
// whole class and _LiveryColor is the only thing that differs between two machines.
Shader "AlpineSim/MachinePBR"
{
    Properties
    {
        _MainTex ("Albedo (RGB)", 2D) = "white" {}
        _BumpMap ("Normal", 2D) = "bump" {}
        _ORM ("Occlusion / Roughness / Metallic", 2D) = "white" {}
        _WearMask ("Wear (R edge, G rust, B salt, A chip)", 2D) = "black" {}
        _LiveryColor ("Livery", Color) = (1,1,1,1)
        _AccentColor ("Accent", Color) = (1,1,1,1)
        _WearAmount ("Wear (0 new, 1 worn out)", Range(0,1)) = 0
        _SoilAmount ("Soiling", Range(0,1)) = 0
        _Glossiness ("Smoothness (no ORM map)", Range(0,1)) = 0.45
        _Metallic ("Metallic (no ORM map)", Range(0,1)) = 0
        _UseOrm ("Has ORM map", Range(0,1)) = 0
        _UseWear ("Has wear map", Range(0,1)) = 0
        _AccentFromAlpha ("Accent mask from albedo alpha", Range(0,1)) = 0
        _NormalStrength ("Normal strength", Range(0,2)) = 1
        _UvScroll ("UV scroll (belts)", Vector) = (0,0,0,0)
        _PrimerColor ("Primer under the paint", Color) = (0.34,0.35,0.37,1)
        _BareMetalColor ("Rubbed-through metal", Color) = (0.58,0.59,0.60,1)
        _RustColor ("Rust", Color) = (0.34,0.15,0.07,1)
        _SaltColor ("Salt and road film", Color) = (0.74,0.74,0.71,1)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 300

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma multi_compile_instancing
        #pragma target 3.0

        sampler2D _MainTex;
        sampler2D _BumpMap;
        sampler2D _ORM;
        sampler2D _WearMask;
        fixed4 _LiveryColor, _AccentColor, _PrimerColor, _BareMetalColor, _RustColor, _SaltColor;
        half _WearAmount, _SoilAmount, _Glossiness, _Metallic, _UseOrm, _UseWear, _AccentFromAlpha, _NormalStrength;
        float4 _UvScroll;

        struct Input
        {
            float2 uv_MainTex;
        };

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float2 uv = IN.uv_MainTex + _UvScroll.xy;

            fixed4 tex = tex2D(_MainTex, uv);
            // The albedo's alpha is the accent coverage channel. Nothing writes it yet, so the mask
            // is switched off by default and a model wears its livery everywhere until it does.
            fixed accentMask = tex.a * _AccentFromAlpha;
            fixed3 paint = tex.rgb * lerp(_LiveryColor.rgb, _AccentColor.rgb, accentMask);

            // Without a wear map the four masks become uniform, so a worn machine still reads as
            // worn - just evenly, instead of along its edges.
            half4 masks = lerp(half4(0.5, 0.35, 0.6, 0.3), tex2D(_WearMask, uv), _UseWear);
            half wear = _WearAmount;
            half edge = saturate((wear - 0.10) * 1.55) * masks.r;   // paint polishes off the edges first
            half chip = saturate((wear - 0.40) * 1.90) * masks.a;   // then it chips to primer
            half rust = saturate((wear - 0.62) * 2.60) * masks.g;   // and only a tired machine rusts
            half salt = saturate(_SoilAmount) * masks.b;

            fixed3 albedo = paint;
            albedo = lerp(albedo, _PrimerColor.rgb, chip);
            albedo = lerp(albedo, _BareMetalColor.rgb, edge * 0.6);
            albedo = lerp(albedo, _RustColor.rgb, rust);
            albedo = lerp(albedo, _SaltColor.rgb, salt * 0.6);

            half3 orm = tex2D(_ORM, uv).rgb;
            half occlusion = lerp(1.0, orm.r, _UseOrm);
            half smoothness = lerp(_Glossiness, 1.0 - orm.g, _UseOrm);
            half metallic = lerp(_Metallic, orm.b, _UseOrm);
            smoothness *= (1.0 - 0.55 * salt) * (1.0 - 0.45 * rust);
            metallic = saturate(metallic + edge * 0.5 - rust * 0.3);

            half3 n = UnpackNormal(tex2D(_BumpMap, uv));
            n.xy *= _NormalStrength;

            o.Albedo = albedo;
            o.Normal = normalize(n);
            o.Metallic = metallic;
            o.Smoothness = smoothness;
            o.Occlusion = occlusion;
            o.Alpha = 1;
        }
        ENDCG
    }

    FallBack "Diffuse"
}
