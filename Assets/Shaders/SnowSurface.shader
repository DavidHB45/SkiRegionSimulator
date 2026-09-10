// Snow surface (M1). Built-in Render Pipeline surface shader with vertex displacement.
// _SnowMap    (RGBAHalf): R depth m, G density fraction (100..600 kg/m3 -> 0..1), B roughness 0..1, A groom age 0..1 (0 = just groomed)
// _SurfaceMap (RGBA32):   R surface type id / 255, G groom direction angle / 2pi, B lateral 0..1, A unused
// UV0 of the terrain meshes is world xz / _MapSize, so both maps cover the whole map.
Shader "AlpineSim/SnowSurface"
{
    Properties
    {
        _SnowMap ("Snow Map", 2D) = "black" {}
        _SurfaceMap ("Surface Map", 2D) = "black" {}
        _MapSize ("Map Size (m)", Float) = 2048
        _RockColor ("Rock", Color) = (0.42, 0.40, 0.38, 1)
        _GrassColor ("Grass", Color) = (0.36, 0.42, 0.22, 1)
        _FlatColor ("Flat", Color) = (0.30, 0.30, 0.31, 1)
        _SnowColor ("Snow", Color) = (0.93, 0.95, 0.99, 1)
        _IceColor ("Ice", Color) = (0.72, 0.80, 0.90, 1)
        _CorduroyWavelength ("Corduroy Wavelength (m)", Float) = 0.12
        _CorduroyDepth ("Corduroy Depth (m)", Float) = 0.03
        _DisplacementScale ("Displacement Scale", Float) = 1
        _BareDepth ("Bare Ground Depth (m)", Float) = 0.02
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 250
        CGPROGRAM
        #pragma surface surf Lambert fullforwardshadows vertex:vert addshadow
        #pragma target 3.0

        sampler2D _SnowMap;
        sampler2D _SurfaceMap;
        float4 _SnowMap_TexelSize;
        float _MapSize, _CorduroyWavelength, _CorduroyDepth, _DisplacementScale, _BareDepth;
        fixed4 _RockColor, _GrassColor, _FlatColor, _SnowColor, _IceColor;

        struct Input
        {
            float2 uv_SnowMap;
            float4 color : COLOR;
            float3 worldPos;
            float corduroy;
        };

        // signed corduroy relief at a world position for a groom direction (radians CCW from +X east)
        float Corduroy(float2 mapPos, float dirAngle, float age, float roughness)
        {
            float2 across = float2(-sin(dirAngle), cos(dirAngle));   // perpendicular to the groom direction
            float phase = dot(mapPos, across) / max(0.02, _CorduroyWavelength) * 6.2831853;
            float fade = saturate(1.0 - age) * saturate(1.0 - roughness * 1.5);
            return sin(phase) * fade;
        }

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            float2 uv = v.texcoord.xy;
            float4 snow = tex2Dlod(_SnowMap, float4(uv, 0, 0));
            float4 surf = tex2Dlod(_SurfaceMap, float4(uv, 0, 0));
            float2 mapPos = uv * _MapSize;
            float cord = Corduroy(mapPos, surf.g * 6.2831853, snow.a, snow.b);
            float displacement = snow.r * _DisplacementScale + cord * _CorduroyDepth;
            v.vertex.xyz += v.normal * displacement;
            o.corduroy = cord;
        }

        void surf(Input IN, inout SurfaceOutput o)
        {
            float4 snow = tex2D(_SnowMap, IN.uv_SnowMap);
            float4 surf = tex2D(_SurfaceMap, IN.uv_SnowMap);
            // ground colour from the terrain's vertex colour (r rock, g grass, b flat)
            fixed3 ground = _RockColor.rgb * IN.color.r + _GrassColor.rgb * IN.color.g + _FlatColor.rgb * IN.color.b;
            float cover = saturate(snow.r / max(0.005, _BareDepth));
            // density: powder is bright and slightly warm; ice is darker and blue
            float density = saturate(snow.g);
            fixed3 snowCol = lerp(_SnowColor.rgb, _IceColor.rgb, smoothstep(0.55, 1.0, density));
            // roughness (chop) is matte and a touch grey; corduroy lines are visible shading ridges
            snowCol *= 1.0 - snow.b * 0.12;
            snowCol *= 1.0 + IN.corduroy * 0.06 * saturate(1.0 - snow.a);
            // roads and lots read darker where the pack is thin
            float road = (surf.r * 255.0 > 1.5 && surf.r * 255.0 < 3.5) ? 1.0 : 0.0;
            fixed3 col = lerp(ground, snowCol, cover);
            col = lerp(col, col * 0.82, road * (1.0 - cover));
            o.Albedo = col;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
