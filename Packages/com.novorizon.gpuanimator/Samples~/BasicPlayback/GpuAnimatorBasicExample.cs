using System;
using UnityEngine;

namespace GpuAnimatorSamples
{
    /// <summary>提供GPU 动画播放器基础示例的实现。</summary>
    public sealed class GpuAnimatorBasicExample : MonoBehaviour
    {
        [SerializeField]
        private Renderer targetRenderer;
        [SerializeField]
        private TextAsset animationDataAsset;
        [SerializeField]
        private string loopClipName;
        [SerializeField]
        private string actionClipName;
        private TextAsset decodedAsset;
        private AnimationData decodedData;
        private GpuAnimator player;
        /// <summary>初始化示例。</summary>
        [ContextMenu("Initialize Sample")]
        public void InitializeSample()
        {
            if (!RequirePlayMode())
            {
                return;
            }

            ReleasePlayer();
            if (targetRenderer == null || animationDataAsset == null)
            {
                Debug.LogError("请指定烘焙模型的 Renderer 和配套的动画 .bytes 资源。", this);
                return;
            }

            if (decodedAsset != animationDataAsset || decodedData == null)
            {
                decodedAsset = null;
                decodedData = null;
                try
                {
                    decodedData = AnimationData.FromBytes(animationDataAsset.bytes);
                    decodedAsset = animationDataAsset;
                }
                catch (Exception exception)
                {
                    Debug.LogError("动画数据解码失败：" + exception.Message, this);
                    return;
                }
            }

            player = targetRenderer.GetComponent<GpuAnimator>();
            if (player == null)
            {
                player = targetRenderer.gameObject.AddComponent<GpuAnimator>();
            }

            player.AutomaticUpdate = true;
            if (!player.Initialize(targetRenderer, decodedData))
            {
                ReleasePlayer();
                Debug.LogError("播放器初始化失败，请检查配套的烘焙动画数据。", this);
            }
        }

        /// <summary>播放循环。</summary>
        [ContextMenu("Play Loop")]
        public void PlayLoop()
        {
            if (!RequirePlayer())
            {
                return;
            }

            if (string.IsNullOrEmpty(loopClipName) || !player.TryPlay(loopClipName, loop: true))
            {
                Debug.LogError("循环动画名为空、不存在或对应数据无效，请填写烘焙时的完整名称。", this);
            }
        }

        /// <summary>播放动作。</summary>
        [ContextMenu("Play Action")]
        public void PlayAction()
        {
            if (!RequirePlayer())
            {
                return;
            }

            if (string.IsNullOrEmpty(loopClipName) || string.IsNullOrEmpty(actionClipName))
            {
                Debug.LogError("请先填写循环动画和单次动作的名称。", this);
                return;
            }

            if (!player.TryPlay(actionClipName, OnActionCompleted, loop: false))
            {
                Debug.LogError("动作名称不存在或对应数据无效，请填写烘焙时的完整名称。", this);
            }
        }

        /// <summary>停止播放。</summary>
        [ContextMenu("Stop Playback")]
        public void StopPlayback()
        {
            if (RequirePlayMode() && player != null)
            {
                player.Stop();
            }
        }

        /// <summary>清空播放。</summary>
        [ContextMenu("Clear Playback")]
        public void ClearPlayback()
        {
            if (RequirePlayMode())
            {
                ReleasePlayer();
            }
        }

        /// <summary>响应动作完成。</summary>
        private void OnActionCompleted(GpuAnimator completedPlayer, object userdata)
        {
            if (this != null && completedPlayer == player && Application.isPlaying)
            {
                PlayLoop();
            }
        }

        /// <summary>检查 Player 是否满足前置条件。</summary>
        private bool RequirePlayer()
        {
            if (!RequirePlayMode())
            {
                return false;
            }

            if (player != null && player.IsInitialized)
            {
                return true;
            }

            Debug.LogWarning("请先在组件菜单中执行 Initialize Sample。", this);
            return false;
        }

        /// <summary>检查 Play Mode 是否满足前置条件。</summary>
        private bool RequirePlayMode()
        {
            if (Application.isPlaying)
            {
                return true;
            }

            Debug.LogWarning("此示例的控制菜单仅在 Play Mode 中执行。", this);
            return false;
        }

        /// <summary>释放 Player。</summary>
        private void ReleasePlayer()
        {
            if (player != null)
            {
                player.Clear();
            }

            player = null;
        }

        /// <summary>Unity 销毁对象时执行本对象的清理逻辑。</summary>
        private void OnDestroy()
        {
            ReleasePlayer();
        }
    }
}
