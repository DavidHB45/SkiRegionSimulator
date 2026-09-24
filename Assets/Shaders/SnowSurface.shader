// Snow surface (M1). Built-in Render Pipeline surface shader with vertex displacement.
// _SnowMap    (RGBAHalf): R depth m, G density fraction (100..600 kg/m3 -> 0..1), B roughness 0..1, A groom age 0..1 (0 = just groomed)
// _SurfaceMap (RGBA32):   R surface type id / 255, G groom direction angle / 2pi, B lateral 0..1, A unused
// UV0 of the terrain meshes is world xz / _MapSize, so both maps cover the whole map.
//
// The detail maps below come from tools/assetgen (textures/snow.py) and are bound by
// SnowView when a build is present. They are tiled in metres off world position, not off
// the map UV, so the ribbing stays the right size whatever the map is. Every one defaults
// to a neutral built-in texture and _UseDetailMaps defaults to 0, so the surface renders
// exactly as it did before when no assets have been generated.
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

        [Header(Generated detail maps)]
        _UseDetailMaps ("Use Detail Maps", Range(0, 1)) = 0
        _CorduroyNormal ("Corduroy Normal", 2D) = "bump" {}
        _CrustNormal ("Crust Normal", 2D) = "bump" {}
        _PowderNormal ("Powder Normal", 2D) = "bump" {}
        _DriftMask ("Drift Mask (R deposit, G scour, B streak)", 2D) = "black" {}
        _DirtySnow ("Dirty Snow Albedo", 2D) = "white" {}
        _CorduroyTilingM ("Corduroy Tile (m)", Float) = 3.84
        _CrustTilingM ("Crust Tile (m)", Float) = 4.0
        _PowderTilingM ("Powder Tile (m)", Float) = 2.0
        _DriftTilingM ("Drift Tile (m)", Float) = 24.0
        _WindBearing ("Wind Bearing (radians)", Float) = 0
        _DetailNormalStrength ("Detail Normal Strength", Range(0, 2)) = 1
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
        sampler2D _CorduroyNormal;
        sampler2D _CrustNormal;
        sampler2D _PowderNormal;
        sampler2D _DriftMask;
        sampler2D _DirtySnow;
        float4 _SnowMap_TexelSize;
        float _MapSize, _CorduroyWavelength, _CorduroyDepth, _DisplacementScale, _BareDepth;
        float _UseDetailMaps, _CorduroyTilingM, _CrustTilingM, _PowderTilingM, _DriftTilingM;
        float _WindBearing, _DetailNormalStrength;
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

        float2 Rotate2(float2 p, float s, float c)
        {
            return float2(p.x * c - p.y * s, p.x * s + p.y * c);
        }

        // A tangent-space normal sampled in a rotated frame has to have its own xy rotated
        // back by the same angle, or the ribbing tilts the light the wrong way.
        float3 SampleRotatedNormal(sampler2D tex, float2 worldXZ, float tiling, float angle)
        {
            float s = sin(angle), c = cos(angle);
            float3 n = UnpackNormal(tex2D(tex, Rotate2(worldXZ, s, c) / max(0.05, tiling)));
            n.xy = Rotate2(n.xy, -s, c);
            return n;
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

            float3 normal = float3(0, 0, 1);
            if (_UseDetailMaps > 0.001)
            {
                float2 world = IN.worldPos.xz;
                float groom = surf.g * 6.2831853;
                // Corduroy is the freshly groomed signal: it fades out as the groom ages and
                // as the pack roughens under traffic, exactly as the analytic ribbing does.
                float groomed = saturate(1.0 - snow.a) * saturate(1.0 - snow.b * 1.5) * cover;
                float3 cord = SampleRotatedNormal(_CorduroyNormal, world, _CorduroyTilingM, groom);
                // Crust is the opposite signal: wind-hammered, broken, and strongest where the
                // surface has roughened.
                float3 crust = UnpackNormal(tex2D(_CrustNormal, world / max(0.05, _CrustTilingM)));
                float3 powder = UnpackNormal(tex2D(_PowderNormal, world / max(0.05, _PowderTilingM)));
                float crusted = saturate(snow.b) * cover;
                float powdery = saturate(1.0 - density * 1.4) * cover;

                normal = lerp(normal, cord, groomed);
                normal = lerp(normal, crust, crusted * 0.8);
                normal = lerp(normal, powder, powdery * 0.5);
                normal.xy *= _DetailNormalStrength;
                normal = normalize(normal);

                // Drift streaking runs with the wind, and dirty snow shows through where the
                // pack is thin - grit, pine litter and the edge contamination of a worked run.
                float3 drift = tex2D(_DriftMask, Rotate2(world, sin(_WindBearing), cos(_WindBearing)) / max(0.05, _DriftTilingM)).rgb;
                snowCol *= 1.0 + (drift.r - drift.g) * 0.07;
                fixed3 dirty = tex2D(_DirtySnow, world / 8.0).rgb;
                snowCol = lerp(snowCol, snowCol * dirty, saturate(1.0 - cover) * 0.65 + snow.b * 0.2);
            }

            // roads and lots read darker where the pack is thin
            float road = (surf.r * 255.0 > 1.5 && surf.r * 255.0 < 3.5) ? 1.0 : 0.0;
            fixed3 col = lerp(ground, snowCol, cover);
            col = lerp(col, col * 0.82, road * (1.0 - cover));
            o.Albedo = col;
            o.Normal = normal;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
