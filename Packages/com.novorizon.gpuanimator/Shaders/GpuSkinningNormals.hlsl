#ifndef NOVORIZON_GPU_SKINNING_NORMALS_INCLUDED
#define NOVORIZON_GPU_SKINNING_NORMALS_INCLUDED

// LBS blends rotations/scales into a general linear transform. Transforming the
// normal by that same matrix is incorrect once the blended axes are not orthogonal.
float3 NovorizonSkinNormal(float3 normalOS, float3x3 skin)
{
    float largestAxis = max(max(length(skin[0]), length(skin[1])), length(skin[2]));
    skin *= rcp(max(largestAxis, 1e-20));
    float3 cofactor0 = cross(skin[1], skin[2]);
    float3 cofactor1 = cross(skin[2], skin[0]);
    float3 cofactor2 = cross(skin[0], skin[1]);
    float determinant = dot(skin[0], cofactor0);
    float3 normal = abs(determinant) > 1e-12
        ? mul(float3x3(cofactor0, cofactor1, cofactor2), normalOS) * sign(determinant)
        : normalOS;
    // A collapsed transform has no normal; retain a finite source direction.
    // Normalize after removing a common scale so small/large valid normals stay finite.
    float largest = max(max(abs(normal.x), abs(normal.y)), abs(normal.z));
    normal *= rcp(max(largest, 1e-20));
    return normal * rsqrt(max(dot(normal, normal), 1e-20));
}

#endif
