Shader "Novorizon/GpuSkinning/URP Unlit"
{
    Properties
    {
        _MainTex ("Albedo", 2D) = "white" {}
        _Mask ("Team Color Mask", 2D) = "white" {}
        _Color ("Team Color", Color) = (1, 1, 1, 1)
        _IsPlayer ("Keep Original Color", Int) = 1
        _AnimationTex ("Animation Texture", 2D) = "white" {}
        _AnimationTexSize ("Animation Texture Size", Vector) = (0, 0, 0, 0)
        _BoneNum ("Bone Count", Int) = 0
        _FrameIndex ("Frame", Int) = 0
        _BlendFrameIndex ("Reserved Blend Frame", Int) = 0
        _BlendProgress ("Reserved Blend Progress", Float) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);
        TEXTURE2D(_Mask);
        SAMPLER(sampler_Mask);
        TEXTURE2D(_AnimationTex);

        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            float4 _AnimationTexSize;
            half4 _Color;
            int _BoneNum;
            int _IsPlayer;
        CBUFFER_END

        UNITY_INSTANCING_BUFFER_START(Props)
            UNITY_DEFINE_INSTANCED_PROP(int, _FrameIndex)
            // Runtime property blocks also publish the previous frame. Keep reserved
            // blend properties instanced so their presence does not prevent batching.
            UNITY_DEFINE_INSTANCED_PROP(int, _BlendFrameIndex)
            UNITY_DEFINE_INSTANCED_PROP(float, _BlendProgress)
        UNITY_INSTANCING_BUFFER_END(Props)

        #include "GpuSkinningURP.hlsl"

        struct Attributes
        {
            float3 positionOS : POSITION;
            float2 uv : TEXCOORD0;
            float4 boneIndices : TEXCOORD1;
            float4 boneWeights : TEXCOORD2;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 uv : TEXCOORD0;
            half fog : TEXCOORD1;
            UNITY_VERTEX_OUTPUT_STEREO
        };

        Varyings Vert(Attributes input)
        {
            UNITY_SETUP_INSTANCE_ID(input);
            Varyings output;
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            int frame = UNITY_ACCESS_INSTANCED_PROP(Props, _FrameIndex);
            float3 positionOS = NovorizonSkinPosition(input.positionOS, input.boneIndices, input.boneWeights, frame);
            output.positionCS = TransformObjectToHClip(positionOS);
            output.uv = TRANSFORM_TEX(input.uv, _MainTex);
            output.fog = ComputeFogFactor(output.positionCS.z);
            return output;
        }

        half4 Frag(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
            half mask = SAMPLE_TEXTURE2D(_Mask, sampler_Mask, input.uv).r * (1 - _IsPlayer);
            color = lerp(color, _Color, mask);
            color.rgb = MixFog(color.rgb, input.fog);
            return color;
        }

        half4 DepthFrag(Varyings input) : SV_Target
        {
            return 0;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask 0
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            ENDHLSL
        }
    }
    FallBack Off
}
