Shader "GuerraDasNacoes/RuinsPresentation"
{
    Properties { _MainTex ("Scene", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex, _CameraDepthNormalsTexture, _OcclusionTex, _BloomTex;
        float4 _MainTex_TexelSize, _ProjectionScale, _BlurDirection;
        void ReadSurface(float2 uv, out float depth, out float3 normal, out float3 position)
        {
            DecodeDepthNormal(tex2D(_CameraDepthNormalsTexture, uv), depth, normal);
            depth *= _ProjectionScale.z;
            position = float3((uv * 2 - 1) * _ProjectionScale.xy * depth, -depth);
        }
        half4 Occlusion(v2f_img input) : SV_Target
        {
            float depth; float3 normal, position;
            ReadSurface(input.uv, depth, normal, position);
            if (depth > _ProjectionScale.z * 0.95) return 1;
            float obscurance = 0;
            float2 radius = float2(0.48 / max(depth, 0.2) / _ProjectionScale.x, 0.48 / max(depth, 0.2) / _ProjectionScale.y) * 0.5;
            [unroll] for (int index = 0; index < 12; index++)
            {
                float angle = index * 2.399963;
                float2 offset = float2(cos(angle), sin(angle)) * radius * (0.3 + index / 16.0);
                float otherDepth; float3 otherNormal, otherPosition;
                ReadSurface(saturate(input.uv + offset), otherDepth, otherNormal, otherPosition);
                float3 delta = otherPosition - position;
                float distance = length(delta);
                float facing = max(0, dot(normal, delta / max(distance, 0.001)) - 0.13);
                obscurance += facing * (1 - smoothstep(0.10, 0.75, distance));
            }
            return 1 - saturate(obscurance / 12 * 1.65);
        }
        half4 SoftOcclusion(v2f_img input) : SV_Target
        {
            float centerDepth; float3 normal, position;
            ReadSurface(input.uv, centerDepth, normal, position);
            float total = 0, weight = 0;
            [unroll] for (int x = -1; x <= 1; x++)
            [unroll] for (int y = -1; y <= 1; y++)
            {
                float2 uv = input.uv + float2(x, y) * _MainTex_TexelSize.xy;
                float depth; float3 n, p;
                ReadSurface(uv, depth, n, p);
                float w = exp(-abs(depth - centerDepth) * 12) * (x == 0 && y == 0 ? 2 : 1);
                total += tex2D(_MainTex, uv).r * w;
                weight += w;
            }
            return total / max(weight, 0.001);
        }
        half4 Bright(v2f_img input) : SV_Target
        {
            half3 color = tex2D(_MainTex, input.uv).rgb;
            half peak = max(color.r, max(color.g, color.b));
            return half4(color * max(peak - 0.85, 0) / max(peak, 0.001), 1);
        }
        half4 Blur(v2f_img input) : SV_Target
        {
            float2 step = _BlurDirection.xy * _MainTex_TexelSize.xy * 1.6;
            half3 color = tex2D(_MainTex, input.uv).rgb * 0.227027;
            color += (tex2D(_MainTex, input.uv + step * 1.384615).rgb + tex2D(_MainTex, input.uv - step * 1.384615).rgb) * 0.316216;
            color += (tex2D(_MainTex, input.uv + step * 3.230769).rgb + tex2D(_MainTex, input.uv - step * 3.230769).rgb) * 0.070270;
            return half4(color, 1);
        }
        half4 Compose(v2f_img input) : SV_Target
        {
            half3 color = tex2D(_MainTex, input.uv).rgb;
            color *= lerp(0.62, 1, tex2D(_OcclusionTex, input.uv).r);
            color += tex2D(_BloomTex, input.uv).rgb * 0.085;
            color *= 0.80;
            color = saturate((color * (2.51 * color + 0.03)) / (color * (2.43 * color + 0.59) + 0.14));
            float2 vignette = input.uv * (1 - input.uv.yx);
            color *= lerp(0.94, 1, saturate(pow(vignette.x * vignette.y * 16, 0.25)));
            return half4(color, 1);
        }
        ENDCG
        Pass { CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment Occlusion
            #pragma target 3.0
            ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment SoftOcclusion
            ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment Bright
            ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment Blur
            ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment Compose
            ENDCG }
    }
    Fallback Off
}
