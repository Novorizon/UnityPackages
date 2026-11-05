using System;
using UnityEngine;

internal static class GpuAnimationBakeUtility
{
    internal const int PixelsPerBone = 8;
    private const double RelativeTolerance = 1e-4;
    private const double AffineTolerance = 1e-6;
    private const double FloatRelativePrecision = 1.1920928955078125e-7;

    internal static int CalculateFrameCount(float length, float frameRate, bool loop)
    {
        ValidateSampling(length, frameRate);
        // Match Unity's float sampling arithmetic without losing very short clips to underflow.
        float product = length * frameRate;
        double intervals = Math.Ceiling(product == 0 ? (double)length * frameRate : product);
        double count = loop ? Math.Max(1, intervals) : intervals + 1;
        if (double.IsInfinity(count) || count > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(length), "The clip requires more frames than an Int32 can address.");
        return checked((int)count);
    }

    internal static float GetSampleTime(int frameIndex, float length, float frameRate, bool loop)
    {
        ValidateSampling(length, frameRate);
        if (frameIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(frameIndex), "A sample index cannot be negative.");

        double time = (double)frameIndex / frameRate;
        if (!loop)
            time = Math.Min(time, length);
        if (time > float.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(frameIndex), "The sample time exceeds the finite float range.");
        return (float)time;
    }

    internal static void CalculateTextureSize(int boneCount, int frameCount, int maximumTextureSize, out int width, out int height)
    {
        if (boneCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(boneCount), "At least one bone is required.");
        if (boneCount > 16777216)
            throw new ArgumentOutOfRangeException(nameof(boneCount), "The bone count exceeds the exact integer range of floating-point mesh bone indices.");
        if (frameCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(frameCount), "At least one frame is required.");
        if (maximumTextureSize < 32 || maximumTextureSize > 16384 ||
            (maximumTextureSize & (maximumTextureSize - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(maximumTextureSize), "The maximum texture size must be a power of two between 32 and 16384.");

        long requiredPixels;
        try
        {
            requiredPixels = checked((long)boneCount * frameCount * PixelsPerBone);
        }
        catch (OverflowException)
        {
            throw new ArgumentOutOfRangeException(nameof(frameCount), "The combined bone and frame counts overflow the pixel count.");
        }
        if (requiredPixels > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(frameCount), "The animation exceeds the Int32 pixel-array limit.");
        if (requiredPixels > (long)maximumTextureSize * maximumTextureSize)
            throw new ArgumentException("The animation exceeds the configured maximum texture capacity; rescaling would corrupt its data.", nameof(maximumTextureSize));

        width = 32;
        height = 32;
        bool growWidth = true;
        while ((long)width * height < requiredPixels)
        {
            if (growWidth)
                width = checked(width * 2);
            else
                height = checked(height * 2);
            growWidth = !growWidth;
        }
    }

    internal static Color32 EncodeFloat(float value)
    {
        if (!IsFinite(value) || Math.Abs((double)value) >= 128)
            throw new ArgumentOutOfRangeException(nameof(value), value, "The animation codec requires a finite value strictly between -128 and 128.");

        double magnitude = Math.Abs((double)value);
        int integer = (int)magnitude;
        uint fraction = (uint)((magnitude - integer) * 16777216.0);
        // Preserve the legacy zero encoding as well as the sign/integer/fraction byte order.
        int sign = value > 0 ? 0 : 128;
        return new Color32((byte)(sign | integer), (byte)(fraction >> 16), (byte)((fraction >> 8) & 255), (byte)(fraction & 255));
    }

    internal static void DecomposeMatrix(Matrix4x4 matrix, out Quaternion rotation, out Vector3 translation, out float scale)
    {
        ValidateMatrix(matrix);
        translation = new Vector3(matrix.m03, matrix.m13, matrix.m23);

        double m00 = matrix.m00, m01 = matrix.m01, m02 = matrix.m02;
        double m10 = matrix.m10, m11 = matrix.m11, m12 = matrix.m12;
        double m20 = matrix.m20, m21 = matrix.m21, m22 = matrix.m22;
        double length0 = Math.Sqrt(m00 * m00 + m10 * m10 + m20 * m20);
        double length1 = Math.Sqrt(m01 * m01 + m11 * m11 + m21 * m21);
        double length2 = Math.Sqrt(m02 * m02 + m12 * m12 + m22 * m22);
        double largest = Math.Max(length0, Math.Max(length1, length2));
        if (largest == 0)
        {
            rotation = Quaternion.identity;
            scale = 0;
            return;
        }

        double smallest = Math.Min(length0, Math.Min(length1, length2));
        if (largest - smallest > largest * RelativeTolerance)
            throw new ArgumentException("The bone matrix has non-uniform scale, which the animation codec cannot represent.", nameof(matrix));
        if (Math.Abs(m00 * m01 + m10 * m11 + m20 * m21) > length0 * length1 * RelativeTolerance ||
            Math.Abs(m00 * m02 + m10 * m12 + m20 * m22) > length0 * length2 * RelativeTolerance ||
            Math.Abs(m01 * m02 + m11 * m12 + m21 * m22) > length1 * length2 * RelativeTolerance)
            throw new ArgumentException("The bone matrix contains shear, which the animation codec cannot represent.", nameof(matrix));

        double determinant = m00 * (m11 * m22 - m12 * m21) -
            m01 * (m10 * m22 - m12 * m20) + m02 * (m10 * m21 - m11 * m20);
        double signedScale = (length0 + length1 + length2) / 3 * (determinant < 0 ? -1 : 1);
        scale = ToFiniteFloat(signedScale, nameof(matrix));

        // Dividing by a signed scale leaves a proper rotation even for representable reflections.
        double r00 = m00 / signedScale, r01 = m01 / signedScale, r02 = m02 / signedScale;
        double r10 = m10 / signedScale, r11 = m11 / signedScale, r12 = m12 / signedScale;
        double r20 = m20 / signedScale, r21 = m21 / signedScale, r22 = m22 / signedScale;
        double x, y, z, w;
        double trace = r00 + r11 + r22;
        if (trace > 0)
        {
            double factor = Math.Sqrt(trace + 1) * 2;
            x = (r21 - r12) / factor;
            y = (r02 - r20) / factor;
            z = (r10 - r01) / factor;
            w = factor / 4;
        }
        else if (r00 > r11 && r00 > r22)
        {
            double factor = Math.Sqrt(1 + r00 - r11 - r22) * 2;
            x = factor / 4;
            y = (r01 + r10) / factor;
            z = (r02 + r20) / factor;
            w = (r21 - r12) / factor;
        }
        else if (r11 > r22)
        {
            double factor = Math.Sqrt(1 + r11 - r00 - r22) * 2;
            x = (r01 + r10) / factor;
            y = factor / 4;
            z = (r12 + r21) / factor;
            w = (r02 - r20) / factor;
        }
        else
        {
            double factor = Math.Sqrt(1 + r22 - r00 - r11) * 2;
            x = (r02 + r20) / factor;
            y = (r12 + r21) / factor;
            z = factor / 4;
            w = (r10 - r01) / factor;
        }

        double inverseLength = 1 / Math.Sqrt(x * x + y * y + z * z + w * w);
        rotation = new Quaternion((float)(x * inverseLength), (float)(y * inverseLength), (float)(z * inverseLength), (float)(w * inverseLength));
        ValidateReconstruction(matrix, rotation, scale, largest * RelativeTolerance);
    }

    internal static Bounds TransformBounds(Bounds source, Matrix4x4 matrix)
    {
        ValidateBounds(source, nameof(source));
        ValidateMatrix(matrix);
        Vector3 center = source.center;
        Vector3 extents = source.extents;
        double x = (double)matrix.m00 * center.x + (double)matrix.m01 * center.y + (double)matrix.m02 * center.z + matrix.m03;
        double y = (double)matrix.m10 * center.x + (double)matrix.m11 * center.y + (double)matrix.m12 * center.z + matrix.m13;
        double z = (double)matrix.m20 * center.x + (double)matrix.m21 * center.y + (double)matrix.m22 * center.z + matrix.m23;
        Vector3 transformedCenter = new Vector3(ToFiniteFloat(x, nameof(matrix)), ToFiniteFloat(y, nameof(matrix)), ToFiniteFloat(z, nameof(matrix)));
        double extentX = Math.Abs((double)matrix.m00) * extents.x + Math.Abs((double)matrix.m01) * extents.y + Math.Abs((double)matrix.m02) * extents.z;
        double extentY = Math.Abs((double)matrix.m10) * extents.x + Math.Abs((double)matrix.m11) * extents.y + Math.Abs((double)matrix.m12) * extents.z;
        double extentZ = Math.Abs((double)matrix.m20) * extents.x + Math.Abs((double)matrix.m21) * extents.y + Math.Abs((double)matrix.m22) * extents.z;
        Bounds result = new Bounds();
        result.center = transformedCenter;
        result.extents = new Vector3(
            RoundExtentOutward(extentX + Math.Abs(x - transformedCenter.x), nameof(matrix)),
            RoundExtentOutward(extentY + Math.Abs(y - transformedCenter.y), nameof(matrix)),
            RoundExtentOutward(extentZ + Math.Abs(z - transformedCenter.z), nameof(matrix)));
        return result;
    }

    internal static Bounds PadBounds(Bounds bounds)
    {
        ValidateBounds(bounds, nameof(bounds));
        Vector3 center = bounds.center;
        Vector3 extents = bounds.extents;
        double magnitude = Math.Max(Math.Max(Math.Abs((double)center.x), Math.Abs((double)center.y)), Math.Abs((double)center.z));
        magnitude = Math.Max(magnitude, Math.Max(extents.x, Math.Max(extents.y, extents.z)));
        double margin = 0.001 + magnitude * RelativeTolerance;
        bounds.extents = new Vector3(
            RoundExtentOutward(extents.x + margin, nameof(bounds)),
            RoundExtentOutward(extents.y + margin, nameof(bounds)),
            RoundExtentOutward(extents.z + margin, nameof(bounds)));
        return bounds;
    }

    private static void ValidateSampling(float length, float frameRate)
    {
        if (!IsFinite(length) || length <= 0)
            throw new ArgumentOutOfRangeException(nameof(length), "Clip length must be finite and positive.");
        if (!IsFinite(frameRate) || frameRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(frameRate), "Clip frame rate must be finite and positive.");
    }

    private static void ValidateMatrix(Matrix4x4 matrix)
    {
        for (int i = 0; i < 16; i++)
        {
            if (!IsFinite(matrix[i]))
                throw new ArgumentException("The matrix contains a non-finite component.", nameof(matrix));
        }
        if (Math.Abs(matrix.m30) > AffineTolerance || Math.Abs(matrix.m31) > AffineTolerance ||
            Math.Abs(matrix.m32) > AffineTolerance || Math.Abs(matrix.m33 - 1) > AffineTolerance)
            throw new ArgumentException("The matrix must be affine with a bottom row of (0, 0, 0, 1).", nameof(matrix));
    }

    private static void ValidateReconstruction(Matrix4x4 matrix, Quaternion rotation, float scale, double tolerance)
    {
        double x = rotation.x, y = rotation.y, z = rotation.z, w = rotation.w;
        double scaleValue = scale;
        if (Math.Abs(matrix.m00 - scaleValue * (1 - 2 * (y * y + z * z))) > tolerance ||
            Math.Abs(matrix.m01 - scaleValue * 2 * (x * y - z * w)) > tolerance ||
            Math.Abs(matrix.m02 - scaleValue * 2 * (x * z + y * w)) > tolerance ||
            Math.Abs(matrix.m10 - scaleValue * 2 * (x * y + z * w)) > tolerance ||
            Math.Abs(matrix.m11 - scaleValue * (1 - 2 * (x * x + z * z))) > tolerance ||
            Math.Abs(matrix.m12 - scaleValue * 2 * (y * z - x * w)) > tolerance ||
            Math.Abs(matrix.m20 - scaleValue * 2 * (x * z - y * w)) > tolerance ||
            Math.Abs(matrix.m21 - scaleValue * 2 * (y * z + x * w)) > tolerance ||
            Math.Abs(matrix.m22 - scaleValue * (1 - 2 * (x * x + y * y))) > tolerance)
            throw new ArgumentException("The matrix cannot be reconstructed as rotation and uniform signed scale within the relative tolerance of 0.0001.", nameof(matrix));
    }

    private static void ValidateBounds(Bounds bounds, string parameterName)
    {
        Vector3 center = bounds.center;
        Vector3 extents = bounds.extents;
        if (!IsFinite(center.x) || !IsFinite(center.y) || !IsFinite(center.z) ||
            !IsFinite(extents.x) || !IsFinite(extents.y) || !IsFinite(extents.z) ||
            extents.x < 0 || extents.y < 0 || extents.z < 0)
            throw new ArgumentException("Bounds must have finite center and nonnegative finite extents.", parameterName);
    }

    private static float RoundExtentOutward(double value, string parameterName)
    {
        float result = ToFiniteFloat(value, parameterName);
        if (result < value)
            result += (float)Math.Max(float.Epsilon, result * FloatRelativePrecision);
        return IsFinite(result) ? result : float.MaxValue;
    }

    private static float ToFiniteFloat(double value, string parameterName)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || Math.Abs(value) > float.MaxValue)
            throw new ArgumentException("The result exceeds the finite float range.", parameterName);
        return (float)value;
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
