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

float3 NovorizonSkinPosition(float3 positionOS, float4 boneIndices, float4 boneWeights, int frame)
{
    float3 position = 0;
    [unroll]
    for (int slot = 0; slot < 4; slot++)
    {
        int pixel = ((_BoneNum * frame) + (int)boneIndices[slot]) * 8;
        float4 rotation = float4(NovorizonReadFloat(pixel), NovorizonReadFloat(pixel + 1),
            NovorizonReadFloat(pixel + 2), NovorizonReadFloat(pixel + 3));
        float lengthSq = dot(rotation, rotation);
        rotation = lengthSq > 1e-12 ? rotation * rsqrt(max(lengthSq, 1e-12)) : float4(0, 0, 0, 1);
        float3 translation = float3(NovorizonReadFloat(pixel + 4), NovorizonReadFloat(pixel + 5), NovorizonReadFloat(pixel + 6));
        float3 scaled = positionOS * NovorizonReadFloat(pixel + 7);
        float3 rotated = scaled + 2.0 * cross(rotation.xyz, cross(rotation.xyz, scaled) + rotation.w * scaled);
        position += (rotated + translation) * boneWeights[slot];
    }

    return position;
}

#endif
