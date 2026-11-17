using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;

// 动画clip信息
[Serializable]
public class AnimationClipData
{
    public string name;// 名称
    public int startFrame;// 起始帧
    public int endFrame;// 结束帧
    public bool loop;
    public float frameRate;

    /// <summary>使用调用方提供的name、startFrame、endFrame创建 AnimationClipData。</summary>
    public AnimationClipData(string name, int startFrame, int endFrame, bool loop, float frameRate)
    {
        this.name = name;
        this.startFrame = startFrame;
        this.endFrame = endFrame;
        this.loop = loop;
        this.frameRate = frameRate;
    }

    /// <summary>取得长度。</summary>
    public int Length()
    {
        return endFrame - startFrame + 1;
    }
}

/// <summary>提供动画数据的实现。</summary>
[Serializable]
public class AnimationData
{
    public int texWidth;
    public int texHeight;
    public byte[] texBytes;

    public AnimationClipData[] clips;

    public int totalFrame;
    public int totalBoneNum;

    /// <summary>
    /// Decodes bytes produced by the baking tool. Loading and caching the source
    /// asset remain the caller's responsibility; share the decoded data across instances.
    /// </summary>
    /// <remarks>按提供的字节数据构建返回值。</remarks>
    public static AnimationData FromBytes(byte[] bytes)
    {
        if (bytes == null)
        {
            throw new ArgumentNullException(nameof(bytes));
        }

        using (MemoryStream stream = new MemoryStream(bytes, false))
        {
            BinaryFormatter formatter = new BinaryFormatter
            {
                Binder = new AnimationDataBinder()
            };
            AnimationData data = formatter.Deserialize(stream) as AnimationData;
            if (data == null)
            {
                throw new SerializationException("The asset does not contain GPU animation data.");
            }

            return data;
        }
    }

    /// <summary>表示动画数据中的动画数据 Binder。</summary>
    private sealed class AnimationDataBinder : SerializationBinder
    {
        /// <summary>绑定 To 类型。</summary>
        public override Type BindToType(string assemblyName, string typeName)
        {
            // Older baked assets use the original public type names in the same binary layout.
            switch (typeName)
            {
                case "GpuSkinningAnimData":
                case "AnimationData":
                    return typeof(AnimationData);
                case "GpuSkinningAnimClip":
                case "AnimationClipData":
                    return typeof(AnimationClipData);
                case "GpuSkinningAnimClip[]":
                case "AnimationClipData[]":
                    return typeof(AnimationClipData[]);
                case "System.Byte[]":
                    return typeof(byte[]);
                default:
                    // The legacy format consists only of these records, strings and
                    // primitive fields. Never instantiate an arbitrary loaded application
                    // type before discovering it is not animation data.
                    throw new SerializationException("Unsupported GPU animation metadata type: " + typeName);
            }
        }
    }
}
