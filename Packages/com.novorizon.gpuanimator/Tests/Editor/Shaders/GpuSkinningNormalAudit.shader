Shader "Hidden/Novorizon/GpuSkinningNormalAudit"
{
    SubShader
    {
        Pass
        {
            Cull Off
            ZTest Always
            ZWrite Off
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"
            #include "Packages/com.novorizon.gpuanimator/Shaders/GpuSkinningNormals.hlsl"
            float4 _SkinRow0;
            float4 _SkinRow1;
            float4 _SkinRow2;
            float4 _SourceNormal;
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normal : TEXCOORD0;
            };
            Varyings Vert(float4 position : POSITION)
            {
                Varyings output;
                output.positionCS = position;
                output.normal = NovorizonSkinNormal(_SourceNormal.xyz,
                    float3x3(_SkinRow0.xyz, _SkinRow1.xyz, _SkinRow2.xyz));
                return output;
            }
            float4 Frag(Varyings input) : SV_Target
            {
                return float4(input.normal * .5 + .5, 1);
            }
            ENDCG
        }
    }
    FallBack Off
}
