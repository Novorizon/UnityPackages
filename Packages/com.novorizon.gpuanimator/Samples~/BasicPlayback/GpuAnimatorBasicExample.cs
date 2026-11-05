using System;
using UnityEngine;

namespace GpuAnimatorSamples
{
    public sealed class GpuAnimatorBasicExample : MonoBehaviour
    {
        [SerializeField] private Renderer targetRenderer;
        [SerializeField] private TextAsset animationDataAsset;
        [SerializeField] private string loopClipName;
        [SerializeField] private string actionClipName;

        private TextAsset decodedAsset;
        private AnimationData decodedData;
        private GpuAnimator player;

        [ContextMenu("Initialize Sample")]
        public void InitializeSample()
        {
            if (!RequirePlayMode())
                return;

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
                player = targetRenderer.gameObject.AddComponent<GpuAnimator>();

            player.AutomaticUpdate = true;
            if (!player.Initialize(targetRenderer, decodedData))
            {
                ReleasePlayer();
                Debug.LogError("播放器初始化失败，请检查配套的烘焙动画数据。", this);
            }
        }

        [ContextMenu("Play Loop")]
        public void PlayLoop()
        {
            if (!RequirePlayer())
                return;

            if (string.IsNullOrEmpty(loopClipName) || !player.TryPlay(loopClipName, loop: true))
                Debug.LogError("循环动画名为空、不存在或对应数据无效，请填写烘焙时的完整名称。", this);
        }

        [ContextMenu("Play Action")]
        public void PlayAction()
        {
            if (!RequirePlayer())
                return;

            if (string.IsNullOrEmpty(loopClipName) || string.IsNullOrEmpty(actionClipName))
            {
                Debug.LogError("请先填写循环动画和单次动作的名称。", this);
                return;
            }

            if (!player.TryPlay(actionClipName, OnActionCompleted, loop: false))
                Debug.LogError("动作名称不存在或对应数据无效，请填写烘焙时的完整名称。", this);
        }

        [ContextMenu("Stop Playback")]
        public void StopPlayback()
        {
            if (RequirePlayMode() && player != null)
                player.Stop();
        }

        [ContextMenu("Clear Playback")]
        public void ClearPlayback()
        {
            if (RequirePlayMode())
                ReleasePlayer();
        }

        private void OnActionCompleted(GpuAnimator completedPlayer, object userdata)
        {
            if (this != null && completedPlayer == player && Application.isPlaying)
                PlayLoop();
        }

        private bool RequirePlayer()
        {
            if (!RequirePlayMode())
                return false;
            if (player != null && player.IsInitialized)
                return true;

            Debug.LogWarning("请先在组件菜单中执行 Initialize Sample。", this);
            return false;
        }

        private bool RequirePlayMode()
        {
            if (Application.isPlaying)
                return true;

            Debug.LogWarning("此示例的控制菜单仅在 Play Mode 中执行。", this);
            return false;
        }

        private void ReleasePlayer()
        {
            if (player != null)
                player.Clear();
            player = null;
        }

        private void OnDestroy()
        {
            ReleasePlayer();
        }
    }
}
