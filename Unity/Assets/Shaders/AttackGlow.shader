Shader "GuerraDasNacoes/AttackGlow"
{
    Properties
    {
        _Color ("Tint", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha One
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct Attributes
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
            };
            struct Varyings
            {
                float4 position : SV_POSITION;
                fixed4 color : COLOR;
            };
            half4 _Color;
            Varyings vert(Attributes input)
            {
                Varyings output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.color = input.color * _Color;
                return output;
            }
            half4 frag(Varyings input) : SV_Target
            {
                return input.color;
            }
            ENDCG
        }
    }
}
