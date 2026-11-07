#ifndef NOVORIZON_GPU_SKINNING_URP_INCLUDED
#define NOVORIZON_GPU_SKINNING_URP_INCLUDED

// The existing bake format stores quaternion.xyzw and translation.xyz/uniform-scale
// in eight RGBA32 pixels per bone/frame. Texture import must remain linear and lossless.
float NovorizonDecodeFloat(float4 encoded)
{
    float4 bytes = floor(encoded * 255.0 + 0.5);
    float negative = floor(bytes.r / 128.0);
    float magnitude = bytes.r - negative * 128.0 + bytes.g / 256.0
        + bytes.b / 65536.0 + bytes.a / 16777216.0;
    return magnitude * (1.0 - 2.0 * negative);
}

float NovorizonReadFloat(int pixel)
{
    int width = (int)_AnimationTexSize.x;
    return NovorizonDecodeFloat(LOAD_TEXTURE2D_LOD(_AnimationTex, int2(pixel % width, pixel / width), 0));
}

float3 NovorizonRotate(float4 rotation, float3 direction)
{
    return direction + 2.0 * cross(rotation.xyz, cross(rotation.xyz, direction) + rotation.w * direction);
}

void NovorizonSkinVertex(inout float3 positionOS, inout float3 normalOS, inout float4 tangentOS,
    float4 boneIndices, float4 boneWeights, int frame)
{
    float3 position = 0;
    float3 column0 = 0;
    float3 column1 = 0;
    float3 column2 = 0;
    [unroll]
    for (int slot = 0; slot < 4; slot++)
    {
        int pixel = ((_BoneNum * frame) + (int)boneIndices[slot]) * 8;
        float4 rotation = float4(NovorizonReadFloat(pixel), NovorizonReadFloat(pixel + 1),
            NovorizonReadFloat(pixel + 2), NovorizonReadFloat(pixel + 3));
        float lengthSq = dot(rotation, rotation);
        rotation = lengthSq > 1e-12 ? rotation * rsqrt(max(lengthSq, 1e-12)) : float4(0, 0, 0, 1);
        float3 translation = float3(NovorizonReadFloat(pixel + 4), NovorizonReadFloat(pixel + 5), NovorizonReadFloat(pixel + 6));
        float scale = NovorizonReadFloat(pixel + 7);
        position += (NovorizonRotate(rotation, positionOS * scale) + translation) * boneWeights[slot];
        // Blend the linear transform as well as the position. The codec permits signed scale,
        // including reflections; inverse transpose and tangent handedness must account for it.
        float weightScale = scale * boneWeights[slot];
        column0 += NovorizonRotate(rotation, float3(1, 0, 0)) * weightScale;
        column1 += NovorizonRotate(rotation, float3(0, 1, 0)) * weightScale;
        column2 += NovorizonRotate(rotation, float3(0, 0, 1)) * weightScale;
    }
    positionOS = position;
    // Remove a common positive scale before cofactors, avoiding loss of normal precision
    // on very small but valid uniform scales encoded by the existing texture format.
    float linearScale = max(max(length(column0), length(column1)), length(column2));
    float inverseScale = rcp(max(linearScale, 1e-20));
    column0 *= inverseScale;
    column1 *= inverseScale;
    column2 *= inverseScale;
    float determinant = dot(column0, cross(column1, column2));
    float3 normal = cross(column1, column2) * normalOS.x
        + cross(column2, column0) * normalOS.y + cross(column0, column1) * normalOS.z;
    float3 tangent = column0 * tangentOS.x + column1 * tangentOS.y + column2 * tangentOS.z;
    // A collapsed transform has no surface normal. Keep a finite fallback until it unfolds.
    normalOS = abs(determinant) > 1e-12 ? SafeNormalize(normal * sign(determinant)) : SafeNormalize(normalOS);
    tangentOS.xyz = SafeNormalize(tangent - normalOS * dot(normalOS, tangent));
    tangentOS.w *= determinant < 0 ? -1 : 1;
}

float3 NovorizonSkinPosition(float3 positionOS, float4 boneIndices, float4 boneWeights, int frame)
{
    float3 normal = float3(0, 1, 0);
    float4 tangent = float4(1, 0, 0, 1);
    NovorizonSkinVertex(positionOS, normal, tangent, boneIndices, boneWeights, frame);
    return positionOS;
}

#endif
