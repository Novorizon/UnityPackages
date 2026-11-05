Shader "Custom/GpuSkinningAnimation"
{
    Properties
    {
        _MainTex ("Albedo (RGB)", 2D) = "white" { }
        _Mask ("Mask (RGB)", 2D) = "white" { }
        _Color ("Color", Color) = (1, 1, 1, 1)

        //骨骼动画
        _AnimationTex ("Animation Texture", 2D) = "white" { }
        _AnimationTexSize ("Animation Texture Size", Vector) = (0, 0, 0, 0)

        _BoneNum ("Bone Num", Int) = 0
        _FrameIndex ("Frame Index", Range(1, 1000)) = 1
        _BlendFrameIndex ("Blend Frame Index", Range(1, 1000)) = 1
        _BlendProgress ("Blend Progress", Range(0.0, 1.0)) = 0.0
        
        _IsPlayer ("IsPlayer", Int) = 1
    }
    
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 200
        Pass
        {
            CGPROGRAM

            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            
            sampler2D _MainTex;
            float4 _MainTex_ST;
            
            sampler2D _Mask;
            float4 _Mask_ST;
            fixed4 _Color;

            //  动画纹理
            int _BoneNum;
            sampler2D _AnimationTex;
            float4 _AnimationTex_ST;
            float4 _AnimationTexSize;

            int _IsPlayer;

            UNITY_INSTANCING_BUFFER_START(Props)
            UNITY_DEFINE_INSTANCED_PROP(int, _FrameIndex)
            UNITY_DEFINE_INSTANCED_PROP(int, _BlendFrameIndex)
            UNITY_DEFINE_INSTANCED_PROP(float, _BlendProgress)
            UNITY_INSTANCING_BUFFER_END(Props)

            float4x4 QuaternionToMatrix(float4 vec)
            {
                float lengthSq = dot(vec, vec);
                vec = lengthSq > 0.000000000001
                    ? vec * rsqrt(max(lengthSq, 0.000000000001))
                    : float4(0, 0, 0, 1);

                float4x4 ret;
                ret._11 = 2.0 * (vec.x * vec.x + vec.w * vec.w) - 1;
                ret._21 = 2.0 * (vec.x * vec.y + vec.z * vec.w);
                ret._31 = 2.0 * (vec.x * vec.z - vec.y * vec.w);
                ret._41 = 0.0;
                ret._12 = 2.0 * (vec.x * vec.y - vec.z * vec.w);
                ret._22 = 2.0 * (vec.y * vec.y + vec.w * vec.w) - 1;
                ret._32 = 2.0 * (vec.y * vec.z + vec.x * vec.w);
                ret._42 = 0.0;
                ret._13 = 2.0 * (vec.x * vec.z + vec.y * vec.w);
                ret._23 = 2.0 * (vec.y * vec.z - vec.x * vec.w);
                ret._33 = 2.0 * (vec.z * vec.z + vec.w * vec.w) - 1;
                ret._43 = 0.0;
                ret._14 = 0.0;
                ret._24 = 0.0;
                ret._34 = 0.0;
                ret._44 = 1.0;
                return ret;
            }

            float4x4 DualQuaternionToMatrix(float4 m_dual, float4 m_real)
            {
                float4x4 rotationMatrix = QuaternionToMatrix(float4(m_dual.x, m_dual.y, m_dual.z, m_dual.w));
                float4x4 translationMatrix;
                translationMatrix._11_12_13_14 = float4(1, 0, 0, 0);
                translationMatrix._21_22_23_24 = float4(0, 1, 0, 0);
                translationMatrix._31_32_33_34 = float4(0, 0, 1, 0);
                translationMatrix._41_42_43_44 = float4(0, 0, 0, 1);
                translationMatrix._14 = m_real.x;
                translationMatrix._24 = m_real.y;
                translationMatrix._34 = m_real.z;
                float4x4 scaleMatrix;
                scaleMatrix._11_12_13_14 = float4(1, 0, 0, 0);
                scaleMatrix._21_22_23_24 = float4(0, 1, 0, 0);
                scaleMatrix._31_32_33_34 = float4(0, 0, 1, 0);
                scaleMatrix._41_42_43_44 = float4(0, 0, 0, 1);
                scaleMatrix._11 = m_real.w;
                scaleMatrix._22 = m_real.w;
                scaleMatrix._33 = m_real.w;
                scaleMatrix._44 = 1;
                float4x4 M = mul(translationMatrix, mul(rotationMatrix, scaleMatrix));

                return M;
            }

            float ConvertColorToFloat32(float4 color)
            {
                float4 bytes = floor(color * 255.0 + 0.5);
                int integer = (int)bytes.r;

                int flag = integer / 128; // 1: negative, 0: positive
                
                integer = integer - flag * 128; // 整数部分
                float floats0 = bytes.g / 256.0;
                float floats1 = bytes.b / 65536.0;
                float floats2 = bytes.a / 16777216.0;
                
                float result = integer + floats0 + floats1 + floats2;
                result = result - 2 * flag * result;		//1: 负  0:正

                return result;
            }

            float4 ConvertColors2Halfs(float4 color1, float4 color2, float4 color3, float4 color4)
            {
                return float4(
                    ConvertColorToFloat32(color1),
                    ConvertColorToFloat32(color2),
                    ConvertColorToFloat32(color3),
                    ConvertColorToFloat32(color4)
                );
            }


            float4 indexToUV(int index)
            {
                int width = (int)_AnimationTexSize.x;
                int row = index / width;
                int col = index - row * width;
                return float4((col + 0.5) / _AnimationTexSize.x, (row + 0.5) / _AnimationTexSize.y, 0, 0);
            }
            

            struct appdata
            {
                float4 vertex: POSITION;
                float3 normal: NORMAL;
                float2 uv: TEXCOORD0;
                //float4 color : COLOR;

                float4 boneIndices: TEXCOORD1;
                float4 boneWeights: TEXCOORD2;

                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex: SV_POSITION;
                float2 uv: TEXCOORD0;
                float3 normal: TEXCOORD1;
                float3 worldPos: TEXCOORD2;
            };

            v2f vert(appdata v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                v2f o;
                //UNITY_TRANSFER_INSTANCE_ID(v, o);

                float4 boneIndices = v.boneIndices;
                float4 boneWeights = v.boneWeights;
                float4 boneUV1;
                float4 boneUV2;
                float4 boneUV3;
                float4 boneUV4;
                float4 boneUV5;
                float4 boneUV6;
                float4 boneUV7;
                float4 boneUV8;
                int frameDataPixelIndex;
                static const int DEFAULT_PER_FRAME_BONE_DATASPACE = 8;
                
                float4 a;
                float4 b ;
                float4 c ;
                float4 d ;
                float4 e ;
                float4 f ;
                float4 g ;
                float4 h ;

                int FrameIndex = UNITY_ACCESS_INSTANCED_PROP(Props, _FrameIndex);
                // 正在播放的动画
                frameDataPixelIndex = ((_BoneNum * (FrameIndex)) * DEFAULT_PER_FRAME_BONE_DATASPACE);
                // bone0
                boneUV1 = indexToUV(frameDataPixelIndex + (int)boneIndices[0] * DEFAULT_PER_FRAME_BONE_DATASPACE);
                boneUV2 = indexToUV(frameDataPixelIndex + (int)boneIndices[0] * DEFAULT_PER_FRAME_BONE_DATASPACE + 1);
                boneUV3 = indexToUV(frameDataPixelIndex + (int)boneIndices[0] * DEFAULT_PER_FRAME_BONE_DATASPACE + 2);
                boneUV4 = indexToUV(frameDataPixelIndex + (int)boneIndices[0] * DEFAULT_PER_FRAME_BONE_DATASPACE + 3);
                boneUV5 = indexToUV(frameDataPixelIndex + (int)boneIndices[0] * DEFAULT_PER_FRAME_BONE_DATASPACE + 4);
                boneUV6 = indexToUV(frameDataPixelIndex + (int)boneIndices[0] * DEFAULT_PER_FRAME_BONE_DATASPACE + 5);
                boneUV7 = indexToUV(frameDataPixelIndex + (int)boneIndices[0] * DEFAULT_PER_FRAME_BONE_DATASPACE + 6);
                boneUV8 = indexToUV(frameDataPixelIndex + (int)boneIndices[0] * DEFAULT_PER_FRAME_BONE_DATASPACE + 7);

                a = tex2Dlod(_AnimationTex, boneUV1);
                b = tex2Dlod(_AnimationTex, boneUV2);
                c = tex2Dlod(_AnimationTex, boneUV3);
                d = tex2Dlod(_AnimationTex, boneUV4);
                e = tex2Dlod(_AnimationTex, boneUV5);
                f = tex2Dlod(_AnimationTex, boneUV6);
                g = tex2Dlod(_AnimationTex, boneUV7);
                h = tex2Dlod(_AnimationTex, boneUV8);
                float4x4 bone0_matrix = DualQuaternionToMatrix(ConvertColors2Halfs(a, b, c, d), ConvertColors2Halfs(e, f, g, h));
                // bone1
                boneUV1 = indexToUV(frameDataPixelIndex + (int)boneIndices[1] * DEFAULT_PER_FRAME_BONE_DATASPACE);
                boneUV2 = indexToUV(frameDataPixelIndex + (int)boneIndices[1] * DEFAULT_PER_FRAME_BONE_DATASPACE + 1);
                boneUV3 = indexToUV(frameDataPixelIndex + (int)boneIndices[1] * DEFAULT_PER_FRAME_BONE_DATASPACE + 2);
                boneUV4 = indexToUV(frameDataPixelIndex + (int)boneIndices[1] * DEFAULT_PER_FRAME_BONE_DATASPACE + 3);
                boneUV5 = indexToUV(frameDataPixelIndex + (int)boneIndices[1] * DEFAULT_PER_FRAME_BONE_DATASPACE + 4);
                boneUV6 = indexToUV(frameDataPixelIndex + (int)boneIndices[1] * DEFAULT_PER_FRAME_BONE_DATASPACE + 5);
                boneUV7 = indexToUV(frameDataPixelIndex + (int)boneIndices[1] * DEFAULT_PER_FRAME_BONE_DATASPACE + 6);
                boneUV8 = indexToUV(frameDataPixelIndex + (int)boneIndices[1] * DEFAULT_PER_FRAME_BONE_DATASPACE + 7);
                
                a = tex2Dlod(_AnimationTex, boneUV1);
                b = tex2Dlod(_AnimationTex, boneUV2);
                c = tex2Dlod(_AnimationTex, boneUV3);
                d = tex2Dlod(_AnimationTex, boneUV4);
                e = tex2Dlod(_AnimationTex, boneUV5);
                f = tex2Dlod(_AnimationTex, boneUV6);
                g = tex2Dlod(_AnimationTex, boneUV7);
                h = tex2Dlod(_AnimationTex, boneUV8);
                float4x4 bone1_matrix = DualQuaternionToMatrix(ConvertColors2Halfs(a, b, c, d), ConvertColors2Halfs(e, f, g, h));
                
                // bone2
                boneUV1 = indexToUV(frameDataPixelIndex + (int)boneIndices[2] * DEFAULT_PER_FRAME_BONE_DATASPACE);
                boneUV2 = indexToUV(frameDataPixelIndex + (int)boneIndices[2] * DEFAULT_PER_FRAME_BONE_DATASPACE + 1);
                boneUV3 = indexToUV(frameDataPixelIndex + (int)boneIndices[2] * DEFAULT_PER_FRAME_BONE_DATASPACE + 2);
                boneUV4 = indexToUV(frameDataPixelIndex + (int)boneIndices[2] * DEFAULT_PER_FRAME_BONE_DATASPACE + 3);
                boneUV5 = indexToUV(frameDataPixelIndex + (int)boneIndices[2] * DEFAULT_PER_FRAME_BONE_DATASPACE + 4);
                boneUV6 = indexToUV(frameDataPixelIndex + (int)boneIndices[2] * DEFAULT_PER_FRAME_BONE_DATASPACE + 5);
                boneUV7 = indexToUV(frameDataPixelIndex + (int)boneIndices[2] * DEFAULT_PER_FRAME_BONE_DATASPACE + 6);
                boneUV8 = indexToUV(frameDataPixelIndex + (int)boneIndices[2] * DEFAULT_PER_FRAME_BONE_DATASPACE + 7);
                
                a = tex2Dlod(_AnimationTex, boneUV1);
                b = tex2Dlod(_AnimationTex, boneUV2);
                c = tex2Dlod(_AnimationTex, boneUV3);
                d = tex2Dlod(_AnimationTex, boneUV4);
                e = tex2Dlod(_AnimationTex, boneUV5);
                f = tex2Dlod(_AnimationTex, boneUV6);
                g = tex2Dlod(_AnimationTex, boneUV7);
                h = tex2Dlod(_AnimationTex, boneUV8);
                float4x4 bone2_matrix = DualQuaternionToMatrix(ConvertColors2Halfs(a, b, c, d), ConvertColors2Halfs(e, f, g, h));
                
                // bone3
                boneUV1 = indexToUV(frameDataPixelIndex + (int)boneIndices[3] * DEFAULT_PER_FRAME_BONE_DATASPACE);
                boneUV2 = indexToUV(frameDataPixelIndex + (int)boneIndices[3] * DEFAULT_PER_FRAME_BONE_DATASPACE + 1);
                boneUV3 = indexToUV(frameDataPixelIndex + (int)boneIndices[3] * DEFAULT_PER_FRAME_BONE_DATASPACE + 2);
                boneUV4 = indexToUV(frameDataPixelIndex + (int)boneIndices[3] * DEFAULT_PER_FRAME_BONE_DATASPACE + 3);
                boneUV5 = indexToUV(frameDataPixelIndex + (int)boneIndices[3] * DEFAULT_PER_FRAME_BONE_DATASPACE + 4);
                boneUV6 = indexToUV(frameDataPixelIndex + (int)boneIndices[3] * DEFAULT_PER_FRAME_BONE_DATASPACE + 5);
                boneUV7 = indexToUV(frameDataPixelIndex + (int)boneIndices[3] * DEFAULT_PER_FRAME_BONE_DATASPACE + 6);
                boneUV8 = indexToUV(frameDataPixelIndex + (int)boneIndices[3] * DEFAULT_PER_FRAME_BONE_DATASPACE + 7);
                
                a = tex2Dlod(_AnimationTex, boneUV1);
                b = tex2Dlod(_AnimationTex, boneUV2);
                c = tex2Dlod(_AnimationTex, boneUV3);
                d = tex2Dlod(_AnimationTex, boneUV4);
                e = tex2Dlod(_AnimationTex, boneUV5);
                f = tex2Dlod(_AnimationTex, boneUV6);
                g = tex2Dlod(_AnimationTex, boneUV7);
                h = tex2Dlod(_AnimationTex, boneUV8);
                float4x4 bone3_matrix = DualQuaternionToMatrix(ConvertColors2Halfs(a, b, c, d), ConvertColors2Halfs(e, f, g, h));
                
                float4 pos = mul(bone0_matrix, v.vertex) * boneWeights[0] +
                mul(bone1_matrix, v.vertex) * boneWeights[1] +
                mul(bone2_matrix, v.vertex) * boneWeights[2] +
                mul(bone3_matrix, v.vertex) * boneWeights[3];

                //////////////////////////

                o.vertex = UnityObjectToClipPos(pos);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.normal = normalize(o.normal);
                
                return o;
            }
            

            half4 frag(v2f i): SV_Target
            {
                //UNITY_SETUP_INSTANCE_ID(i);
                i.normal = normalize(i.normal);
                fixed4 Albedo = tex2D(_MainTex, i.uv);
                fixed4 Mask = tex2D(_Mask, i.uv);
                float r = Mask.r;
                r = r * (1 - _IsPlayer) ;
                Albedo = (1 - r) * Albedo + r * _Color;
                return Albedo;
            }
            ENDCG

        }
    }
    FallBack "Diffuse"
}
