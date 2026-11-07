using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using NUnit.Framework;

namespace GpuAnimatorTests
{
    public sealed class AnimationDataDecodeTests
    {
        [Test]
        public void DecodePreservesCurrentAndLegacyMetadataNames()
        {
            var source = new AnimationData
            {
                texWidth = 32,
                texHeight = 32,
                totalBoneNum = 1,
                totalFrame = 2,
                texBytes = new byte[] { 1, 2, 3 },
                clips = new[] { new AnimationClipData("Move", 0, 1, true, 10) }
            };
            foreach (SerializationBinder binder in new SerializationBinder[] { null, new LegacyNames() })
            {
                AnimationData restored = AnimationData.FromBytes(Encode(source, binder));
                Assert.That(restored.totalFrame, Is.EqualTo(2));
                Assert.That(restored.texBytes, Is.EqualTo(source.texBytes));
                Assert.That(restored.clips, Has.Length.EqualTo(1));
                Assert.That(restored.clips[0].name, Is.EqualTo("Move"));
                Assert.That(restored.clips[0].loop, Is.True);
                Assert.That(restored.clips[0].frameRate, Is.EqualTo(10));
            }
        }

        [Test]
        public void DecodeRejectsUnexpectedTypesBeforeDeserializationCallbacks()
        {
            UnexpectedPayload.CallbackRan = false;
            byte[] bytes = Encode(new UnexpectedPayload());
            Assert.Throws<SerializationException>(() => AnimationData.FromBytes(bytes));
            Assert.That(UnexpectedPayload.CallbackRan, Is.False);
        }

        private static byte[] Encode(object source, SerializationBinder binder = null)
        {
            using (var stream = new MemoryStream())
            {
                new BinaryFormatter { Binder = binder }.Serialize(stream, source);
                return stream.ToArray();
            }
        }

        private sealed class LegacyNames : SerializationBinder
        {
            public override Type BindToType(string assemblyName, string typeName)
            {
                throw new NotSupportedException();
            }

            public override void BindToName(Type type, out string assemblyName, out string typeName)
            {
                assemblyName = type.Assembly.FullName;
                typeName = type == typeof(AnimationData) ? "GpuSkinningAnimData" :
                    type == typeof(AnimationClipData) ? "GpuSkinningAnimClip" :
                    type == typeof(AnimationClipData[]) ? "GpuSkinningAnimClip[]" : type.FullName;
            }
        }

        [Serializable]
        private sealed class UnexpectedPayload
        {
            internal static bool CallbackRan;

            [OnDeserialized]
            private void OnDecoded(StreamingContext context)
            {
                CallbackRan = true;
            }
        }
    }
}
