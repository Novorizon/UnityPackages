using System;
using NUnit.Framework;
using UnityEngine;

namespace GpuAnimatorTests
{
    public sealed class GpuAnimationBakeUtilityTests
    {
        [TestCase(1f, 30f, true, 30)]
        [TestCase(1f, 30f, false, 31)]
        [TestCase(1.1f, 30f, true, 33)]
        [TestCase(1.1f, 30f, false, 34)]
        [TestCase(0.01f, 30f, true, 1)]
        [TestCase(0.01f, 30f, false, 2)]
        public void FrameCountSeparatesLoopSamplesFromOneShotEndpoint(float length, float rate, bool loop, int expected)
        {
            Assert.That(GpuAnimationBakeUtility.CalculateFrameCount(length, rate, loop), Is.EqualTo(expected));
        }

        [Test]
        public void SamplingIncludesExactOneShotEndpointWithoutDuplicatingLoopStart()
        {
            const float length = 1.02f;
            const float rate = 30f;
            int once = GpuAnimationBakeUtility.CalculateFrameCount(length, rate, false);
            int loop = GpuAnimationBakeUtility.CalculateFrameCount(length, rate, true);
            Assert.That(GpuAnimationBakeUtility.GetSampleTime(once - 1, length, rate, false), Is.EqualTo(length));
            Assert.That(GpuAnimationBakeUtility.GetSampleTime(loop - 1, length, rate, true), Is.LessThan(length));
            Assert.That(GpuAnimationBakeUtility.GetSampleTime(0, length, rate, false), Is.Zero);
        }

        [Test]
        public void SamplingRejectsNonfiniteParametersAndInvalidRates()
        {
            Assert.That(() => GpuAnimationBakeUtility.CalculateFrameCount(-1, 30, false), Throws.InstanceOf<ArgumentException>());
            Assert.That(() => GpuAnimationBakeUtility.CalculateFrameCount(0, 30, false), Throws.InstanceOf<ArgumentException>());
            Assert.That(() => GpuAnimationBakeUtility.CalculateFrameCount(float.NaN, 30, false), Throws.InstanceOf<ArgumentException>());
            Assert.That(() => GpuAnimationBakeUtility.CalculateFrameCount(1, 0, false), Throws.InstanceOf<ArgumentException>());
            Assert.That(() => GpuAnimationBakeUtility.CalculateFrameCount(1, float.PositiveInfinity, true), Throws.InstanceOf<ArgumentException>());
        }

        [Test]
        public void TextureDimensionsFitAllPixelsAndStayInsideRequestedLimit()
        {
            GpuAnimationBakeUtility.CalculateTextureSize(73, 317, 1024, out int width, out int height);
            Assert.That(width, Is.InRange(1, 1024));
            Assert.That(height, Is.InRange(1, 1024));
            Assert.That(width & (width - 1), Is.Zero);
            Assert.That(height & (height - 1), Is.Zero);
            Assert.That((long)width * height, Is.GreaterThanOrEqualTo(73L * 317 * GpuAnimationBakeUtility.PixelsPerBone));
        }

        [Test]
        public void TextureAllocationRejectsCapacityOverflowBeforeAllocating()
        {
            Assert.That(() => GpuAnimationBakeUtility.CalculateTextureSize(1, 1025, 64, out _, out _), Throws.Exception);
            Assert.That(() => GpuAnimationBakeUtility.CalculateTextureSize(int.MaxValue, int.MaxValue, 2048, out _, out _), Throws.Exception);
            Assert.That(() => GpuAnimationBakeUtility.CalculateTextureSize(0, 1, 2048, out _, out _), Throws.InstanceOf<ArgumentException>());
        }

        [Test]
        public void BoneIndicesStayExactlyRepresentableInVertexFloatChannels()
        {
            GpuAnimationBakeUtility.CalculateTextureSize(16777216, 1, 16384, out int width, out int height);
            Assert.That((long)width * height, Is.GreaterThanOrEqualTo(16777216L * 8));
            Assert.That(() => GpuAnimationBakeUtility.CalculateTextureSize(16777217, 1, 16384, out _, out _), Throws.InstanceOf<ArgumentException>());
        }

        [TestCase(0f)]
        [TestCase(0.5f)]
        [TestCase(-0.5f)]
        [TestCase(2f)]
        [TestCase(-127.5f)]
        [TestCase(127.999f)]
        public void FixedPointEncodingRoundTripsSupportedValues(float value)
        {
            Assert.That(Decode(GpuAnimationBakeUtility.EncodeFloat(value)), Is.EqualTo(value).Within(0.00001f));
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        [TestCase(128f)]
        [TestCase(-128f)]
        public void EncodingRejectsValuesThatCannotBeRepresented(float value)
        {
            Assert.That(() => GpuAnimationBakeUtility.EncodeFloat(value), Throws.InstanceOf<ArgumentException>());
        }

        [TestCase(0.5f)]
        [TestCase(2f)]
        [TestCase(-0.5f)]
        [TestCase(-2f)]
        [TestCase(0f)]
        public void SignedUniformScaleAndRotationReconstructOriginalTransform(float scale)
        {
            Matrix4x4 original = Matrix4x4.TRS(new Vector3(3, -2, 5), Quaternion.Euler(20, 60, -35), Vector3.one * scale);
            GpuAnimationBakeUtility.DecomposeMatrix(original, out Quaternion rotation, out Vector3 translation, out float actualScale);
            Matrix4x4 reconstructed = Matrix4x4.TRS(translation, rotation, Vector3.one * actualScale);
            for (int row = 0; row < 4; row++)
                for (int column = 0; column < 4; column++)
                    Assert.That(reconstructed[row, column], Is.EqualTo(original[row, column]).Within(0.0001f));
        }

        [Test]
        public void MatrixDecompositionRejectsNonuniformScaleShearAndNonAffineMatrices()
        {
            Matrix4x4 nonuniform = Matrix4x4.Scale(new Vector3(1, 2, 1));
            Matrix4x4 shear = Matrix4x4.identity;
            shear.m01 = 0.2f;
            Matrix4x4 perspective = Matrix4x4.identity;
            perspective.m30 = 0.1f;
            Assert.That(() => GpuAnimationBakeUtility.DecomposeMatrix(nonuniform, out _, out _, out _), Throws.InstanceOf<ArgumentException>());
            Assert.That(() => GpuAnimationBakeUtility.DecomposeMatrix(shear, out _, out _, out _), Throws.InstanceOf<ArgumentException>());
            Assert.That(() => GpuAnimationBakeUtility.DecomposeMatrix(perspective, out _, out _, out _), Throws.InstanceOf<ArgumentException>());
        }

        [Test]
        public void TransformedBoundsContainEveryOriginalCorner()
        {
            Bounds original = new Bounds(new Vector3(2, -3, 1), new Vector3(4, 2, 6));
            Matrix4x4 transform = Matrix4x4.TRS(new Vector3(7, 2, -4), Quaternion.Euler(25, 40, 80), new Vector3(-2, 0.5f, 3));
            Bounds transformed = GpuAnimationBakeUtility.PadBounds(GpuAnimationBakeUtility.TransformBounds(original, transform));
            for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                    for (int z = -1; z <= 1; z += 2)
                    {
                        Vector3 corner = original.center + Vector3.Scale(original.extents, new Vector3(x, y, z));
                        Assert.That(transformed.Contains(transform.MultiplyPoint3x4(corner)), Is.True);
                    }
        }

        internal static float Decode(Color32 value)
        {
            float magnitude = (value.r & 127) + value.g / 256f + value.b / 65536f + value.a / 16777216f;
            return value.r >= 128 ? -magnitude : magnitude;
        }
    }
}
