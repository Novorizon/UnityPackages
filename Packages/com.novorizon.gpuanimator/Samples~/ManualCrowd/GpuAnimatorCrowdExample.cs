using System;
using System.Collections.Generic;
using UnityEngine;

namespace GpuAnimatorSamples
{
    public sealed class GpuAnimatorCrowdExample : MonoBehaviour
    {
        [SerializeField] private GameObject bakedPrefab;
        [SerializeField] private TextAsset animationDataAsset;
        [SerializeField] private string clipName;
        [SerializeField, Range(1, 1000)] private int count = 100;
        [SerializeField, Min(1)] private int columns = 10;
        [SerializeField, Min(0.01f)] private float spacing = 2.0f;

        private readonly List<GameObject> spawnedObjects = new List<GameObject>();
        private readonly List<GpuAnimator> players = new List<GpuAnimator>();
        private TextAsset decodedAsset;
        private AnimationData decodedData;

        [ContextMenu("Spawn Crowd")]
        public void SpawnCrowd()
        {
            if (!RequirePlayMode())
                return;

            ClearOwnedInstances();
            if (bakedPrefab == null || animationDataAsset == null || string.IsNullOrEmpty(clipName))
            {
                Debug.LogError("请指定烘焙 prefab、配套动画 .bytes 和真实的动画名称。", this);
                return;
            }
            if (count < 1 || count > 1000 || columns < 1 || spacing <= 0 ||
                float.IsNaN(spacing) || float.IsInfinity(spacing))
            {
                Debug.LogError("Count 必须为 1..1000，Columns 至少为 1，Spacing 必须为有限正数。", this);
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

            try
            {
                for (int i = 0; i < count; i++)
                {
                    GameObject instance = Instantiate(bakedPrefab, transform, false);
                    spawnedObjects.Add(instance);
                    instance.name = bakedPrefab.name + " " + i;
                    instance.transform.localPosition = new Vector3((i % columns) * spacing, 0, (i / columns) * spacing);

                    Renderer renderer = instance.GetComponent<Renderer>();
                    if (renderer == null)
                        throw new InvalidOperationException("烘焙 prefab 的根节点必须有 Renderer。");

                    GpuAnimator player = instance.GetComponent<GpuAnimator>();
                    if (player == null)
                        player = instance.AddComponent<GpuAnimator>();
                    players.Add(player);
                    player.AutomaticUpdate = false;
                    if (!player.Initialize(renderer, decodedData) ||
                        !player.TryPlay(clipName, randomizeStart: true, loop: true))
                        throw new InvalidOperationException("播放器初始化或动画选择失败，请检查配套数据和动画名称。");
                }
            }
            catch (Exception exception)
            {
                ClearOwnedInstances();
                Debug.LogError("生成群体失败，已清理此次生成的实例：" + exception.Message, this);
            }
        }

        [ContextMenu("Clear Crowd")]
        public void ClearCrowd()
        {
            if (RequirePlayMode())
                ClearOwnedInstances();
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i] != null)
                    players[i].Tick(deltaTime);
            }
        }

        private bool RequirePlayMode()
        {
            if (Application.isPlaying)
                return true;

            Debug.LogWarning("此示例的控制菜单仅在 Play Mode 中执行。", this);
            return false;
        }

        private void ClearOwnedInstances()
        {
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i] != null)
                    players[i].Clear();
            }
            players.Clear();

            for (int i = 0; i < spawnedObjects.Count; i++)
            {
                GameObject instance = spawnedObjects[i];
                if (instance == null)
                    continue;

                instance.SetActive(false);
                if (Application.isPlaying)
                    Destroy(instance);
                else
                    DestroyImmediate(instance);
            }
            spawnedObjects.Clear();
        }

        private void OnDestroy()
        {
            ClearOwnedInstances();
        }
    }
}
