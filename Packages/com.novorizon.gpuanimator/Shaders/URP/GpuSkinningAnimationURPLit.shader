Shader "Novorizon/GpuSkinning/URP Lit"
{
    Properties
    {
        _MainTex ("Albedo", 2D) = "white" {}
        _Mask ("Team Color Mask", 2D) = "white" {}
        _Color ("Team Color", Color) = (1, 1, 1, 1)
        _IsPlayer ("Keep Original Color", Int) = 1
        _Metallic ("Metallic", Range(0, 1)) = 0
        _Smoothness ("Smoothness", Range(0, 1)) = 0.5
        [Normal] _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Strength", Range(0, 2)) = 1
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
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);
        TEXTURE2D(_Mask);
        SAMPLER(sampler_Mask);
        TEXTURE2D(_BumpMap);
        SAMPLER(sampler_BumpMap);
        TEXTURE2D(_AnimationTex);

        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            float4 _AnimationTexSize;
            half4 _Color;
            half _Metallic;
            half _Smoothness;
            half _BumpScale;
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
            float3 normalOS : NORMAL;
            float4 tangentOS : TANGENT;
            float2 uv : TEXCOORD0;
            float4 boneIndices : TEXCOORD1;
            float4 boneWeights : TEXCOORD2;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 uv : TEXCOORD0;
            float3 positionWS : TEXCOORD1;
            half3 normalWS : TEXCOORD2;
            half4 tangentWS : TEXCOORD3;
            float4 shadowCoord : TEXCOORD4;
            half4 fogAndVertexLight : TEXCOORD5;
            UNITY_VERTEX_OUTPUT_STEREO
        };

        void Skin(inout Attributes input)
        {
            int frame = UNITY_ACCESS_INSTANCED_PROP(Props, _FrameIndex);
            NovorizonSkinVertex(input.positionOS, input.normalOS, input.tangentOS,
                input.boneIndices, input.boneWeights, frame);
        }

        Varyings Vert(Attributes input)
        {
            UNITY_SETUP_INSTANCE_ID(input);
            Varyings output = (Varyings)0;
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            Skin(input);
            VertexPositionInputs position = GetVertexPositionInputs(input.positionOS);
            VertexNormalInputs normal = GetVertexNormalInputs(input.normalOS, input.tangentOS);
            output.positionCS = position.positionCS;
            output.positionWS = position.positionWS;
            output.normalWS = normal.normalWS;
            output.tangentWS = half4(normal.tangentWS, input.tangentOS.w * GetOddNegativeScale());
            output.uv = TRANSFORM_TEX(input.uv, _MainTex);
            output.shadowCoord = GetShadowCoord(position);
            output.fogAndVertexLight = half4(ComputeFogFactor(position.positionCS.z), VertexLighting(position.positionWS, normal.normalWS));
            return output;
        }

        half3 SurfaceNormal(Varyings input)
        {
            half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv), _BumpScale);
            half3 bitangent = input.tangentWS.w * cross(input.normalWS, input.tangentWS.xyz);
            return NormalizeNormalPerPixel(TransformTangentToWorld(normalTS,
                half3x3(input.tangentWS.xyz, bitangent, input.normalWS)));
        }

        half4 Frag(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            half4 albedo = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
            half mask = SAMPLE_TEXTURE2D(_Mask, sampler_Mask, input.uv).r * (1 - _IsPlayer);
            albedo = lerp(albedo, _Color, mask);
            SurfaceData surface = (SurfaceData)0;
            surface.albedo = albedo.rgb;
            surface.alpha = 1;
            surface.metallic = _Metallic;
            surface.smoothness = _Smoothness;
            surface.occlusion = 1;
            surface.normalTS = half3(0, 0, 1);
            InputData data = (InputData)0;
            data.positionWS = input.positionWS;
            data.positionCS = input.positionCS;
            data.normalWS = SurfaceNormal(input);
            data.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
            data.shadowCoord = input.shadowCoord;
            #if defined(_MAIN_LIGHT_SHADOWS_CASCADE)
                data.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
            #endif
            data.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1), input.fogAndVertexLight.x);
            data.vertexLighting = input.fogAndVertexLight.yzw;
            data.bakedGI = SampleSH(data.normalWS);
            data.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
            data.shadowMask = half4(1, 1, 1, 1);
            half4 color = UniversalFragmentPBR(data, surface);
            color.rgb = MixFog(color.rgb, data.fogCoord);
            return color;
        }

        half4 DepthFrag(Varyings input) : SV_Target
        {
            return 0;
        }

        half4 NormalsFrag(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float3 normalWS = SurfaceNormal(input);
            #if defined(_GBUFFER_NORMALS_OCT)
                float2 oct = PackNormalOctQuadEncode(normalWS);
                return half4(PackFloat2To888(saturate(oct * .5 + .5)), 0);
            #else
                return half4(normalWS, 0);
            #endif
        }

        float3 _LightDirection;
        float3 _LightPosition;
        Varyings ShadowVert(Attributes input)
        {
            UNITY_SETUP_INSTANCE_ID(input);
            Varyings output = (Varyings)0;
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            Skin(input);
            float3 positionWS = TransformObjectToWorld(input.positionOS);
            float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
            #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                float3 lightDirection = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirection = _LightDirection;
            #endif
            output.positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirection));
            #if UNITY_REVERSED_Z
                output.positionCS.z = min(output.positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                output.positionCS.z = max(output.positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
            return output;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForwardOnly" }
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _FORWARD_PLUS
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
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
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormalsOnly" }
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment NormalsFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            ENDHLSL
        }
    }
    FallBack Off
}
