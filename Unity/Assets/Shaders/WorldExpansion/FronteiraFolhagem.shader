Shader "Guerra das Nações/Folhagem da fronteira"
{
    Properties
    {
        _MainTex ("Coordenadas das folhas", 2D) = "white" {}
        _Color ("Cor", Color) = (0.19,0.26,0.12,1)
        _Wind ("Vento", Range(0,0.3)) = 0.07
        _LeafShape ("Folha larga", Range(0,1)) = 1
        _Cutoff ("Recorte das folhas", Range(0,1)) = .42
    }
    SubShader
    {
        Tags { "Queue"="AlphaTest" "RenderType"="TransparentCutout" "IgnoreProjector"="True" }
        LOD 160
        Cull Off
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert addshadow alphatest:_Cutoff
        #pragma target 3.0
        #pragma multi_compile_instancing
        fixed4 _Color;
        float _Wind, _LeafShape;
        struct Input { float2 uv_MainTex; float3 worldPos; float facing : VFACE; };
        void vert(inout appdata_full v)
        {
            float3 world = mul(unity_ObjectToWorld, v.vertex).xyz;
            float sway = sin(_Time.y * 1.35 + world.x * .61 + world.z * .33);
            v.vertex.x += sway * _Wind * v.texcoord.y;
            v.vertex.z += cos(_Time.y * .82 + world.z * .43) * _Wind * .5 * v.texcoord.y;
        }
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float2 uv = IN.uv_MainTex;
            float edge = pow(saturate(sin(uv.y * 3.14159265)), lerp(.33,.70,_LeafShape));
            float silhouette = edge - abs(uv.x * 2 - 1);
            float vein = 1 - smoothstep(.02,.065,abs(uv.x-.5));
            float sideVein = pow(saturate(cos((uv.y-abs(uv.x-.5)*.6)*78)),12)*.06;
            float grain = frac(sin(dot(IN.worldPos.xz,float2(12.9898,78.233)))*43758.5453);
            o.Albedo = _Color.rgb * lerp(.66,1.18,uv.y) * (.94 + grain*.08) + (vein*.022+sideVein)*_LeafShape;
            o.Metallic = 0; o.Smoothness = .12;
            o.Occlusion = lerp(.65,1,uv.y);
            // A leaf is visible from either side. Flip the tangent-space normal on its
            // reverse and retain a restrained diffuse transmission through the thin blade.
            o.Normal = float3(0, 0, IN.facing >= 0 ? 1 : -1);
            o.Emission = o.Albedo * .08;
            // The orbit camera can pass through a canopy. Nearby leaves fade through the
            // same cutout so a branch never fills the complete view during combat.
            float cameraFade = saturate((distance(IN.worldPos, _WorldSpaceCameraPos) - .7) / 2.2);
            o.Alpha = saturate(silhouette * 30) * cameraFade;
        }
        ENDCG
    }
    Fallback "Transparent/Cutout/Diffuse"
}
