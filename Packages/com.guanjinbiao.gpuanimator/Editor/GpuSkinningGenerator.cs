using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Validates and samples a model without changing source objects or writing assets.
/// The writer owns persistence and the caller owns the returned result.
/// </summary>
internal static class GpuSkinningGenerator
{
    private const double WeightSumTolerance = 0.0001;

    internal static GpuAnimationBakeResult Bake(GpuAnimationBakeRequest request, GpuAnimationBakeProgress progress = null)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        if (request.Model == null)
            throw new ArgumentException("A source model is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("A nonempty output name is required.", nameof(request));
        if (request.MainTexture == null)
            throw new ArgumentException("A main texture is required.", nameof(request));

        var result = new GpuAnimationBakeResult();
        Texture2D texture = null;
        Scene previewScene = default(Scene);
        bool previewCreated = false;
        try
        {
            ReportProgress(progress, 0, "Validating " + request.Name);
            if (string.IsNullOrEmpty(request.ShaderName) || Shader.Find(request.ShaderName) == null)
                throw new ArgumentException("GPU animation shader '" + request.ShaderName + "' was not found.");
            SourceModel source = ValidateModel(request.Model, result.Warnings);
            BakedClip[] clips = PrepareClips(request, result.Warnings, out int totalFrames);
            GpuAnimationBakeUtility.CalculateTextureSize(source.Bones.Length, totalFrames,
                request.MaximumTextureSize, out int width, out int height);
            int hardwareLimit = SystemInfo.maxTextureSize;
            if (hardwareLimit > 0 && Math.Max(width, height) > hardwareLimit)
                throw new ArgumentException("Animation texture " + width + "x" + height +
                    " exceeds this Editor graphics device's maximum texture size of " + hardwareLimit +
                    ". Reduce the bone or frame count before baking.");

            result.Data = new AnimationData
            {
                texWidth = width,
                texHeight = height,
                totalBoneNum = source.Bones.Length,
                totalFrame = totalFrames,
                clips = new AnimationClipData[clips.Length]
            };
            for (int i = 0; i < clips.Length; i++)
                result.Data.clips[i] = clips[i].Data;

            ReportProgress(progress, 0.02f, "Preparing mesh and animation texture");
            result.Mesh = UnityEngine.Object.Instantiate(source.Mesh);
            result.Mesh.name = request.Name + "_Mesh";
            result.Mesh.SetUVs(1, source.Indices);
            result.Mesh.SetUVs(2, source.Weights);
            var pixels = new Color32[checked(width * height)];
            int pixelIndex = 0;
            int sampledFrames = 0;
            Bounds animationBounds = default(Bounds);
            bool hasBounds = false;

            previewScene = EditorSceneManager.NewPreviewScene();
            previewCreated = true;
            for (int clipIndex = 0; clipIndex < clips.Length; clipIndex++)
            {
                BakedClip clip = clips[clipIndex];
                GameObject clone = null;
                try
                {
                    // A fresh clone also isolates unbound Animator/muscle state between clips.
                    if (EditorUtility.IsPersistent(request.Model) && PrefabUtility.IsPartOfPrefabAsset(request.Model))
                    {
                        clone = PrefabUtility.InstantiatePrefab(request.Model, previewScene) as GameObject;
                        if (clone == null)
                            throw new InvalidOperationException("Could not instantiate the source model in the preview scene.");
                    }
                    else
                    {
                        // Supports caller-created test models without changing the globally active scene.
                        clone = UnityEngine.Object.Instantiate(request.Model);
                        SceneManager.MoveGameObjectToScene(clone, previewScene);
                    }
                    clone.name = request.Model.name;
                    clone.hideFlags = HideFlags.HideAndDontSave;
                    SkinnedMeshRenderer renderer = clone.GetComponentsInChildren<SkinnedMeshRenderer>(true)[0];
                    Transform[] bones = renderer.bones;
                    if (bones.Length != source.Bones.Length)
                        throw new InvalidOperationException("The cloned bone layout differs from the source model.");
                    var baseline = new TransformSnapshot(clone);

                    for (int frameIndex = 0; frameIndex < clip.Data.Length(); frameIndex++)
                    {
                        float time = GpuAnimationBakeUtility.GetSampleTime(frameIndex,
                            clip.Source.length, clip.Source.frameRate, clip.Data.loop);
                        string sampleContext = string.Format(CultureInfo.InvariantCulture,
                            "Clip '{0}', frame {1}, time {2:R}s", clip.Source.name, frameIndex, time);
                        ReportProgress(progress, 0.02f + 0.93f * sampledFrames / totalFrames,
                            request.Name + ": " + sampleContext);
                        try
                        {
                            baseline.Restore();
                            clip.Source.SampleAnimation(clone, time);
                        }
                        catch (Exception exception)
                        {
                            throw new InvalidOperationException(sampleContext + ": animation sampling failed.", exception);
                        }

                        for (int boneIndex = 0; boneIndex < bones.Length; boneIndex++)
                        {
                            try
                            {
                                // Bake into the identity output object's space, including the source hierarchy.
                                Matrix4x4 matrix = bones[boneIndex].localToWorldMatrix * source.BindPoses[boneIndex];
                                GpuAnimationBakeUtility.DecomposeMatrix(matrix, out Quaternion rotation,
                                    out Vector3 translation, out float scale);
                                pixels[pixelIndex++] = GpuAnimationBakeUtility.EncodeFloat(rotation.x);
                                pixels[pixelIndex++] = GpuAnimationBakeUtility.EncodeFloat(rotation.y);
                                pixels[pixelIndex++] = GpuAnimationBakeUtility.EncodeFloat(rotation.z);
                                pixels[pixelIndex++] = GpuAnimationBakeUtility.EncodeFloat(rotation.w);
                                pixels[pixelIndex++] = GpuAnimationBakeUtility.EncodeFloat(translation.x);
                                pixels[pixelIndex++] = GpuAnimationBakeUtility.EncodeFloat(translation.y);
                                pixels[pixelIndex++] = GpuAnimationBakeUtility.EncodeFloat(translation.z);
                                pixels[pixelIndex++] = GpuAnimationBakeUtility.EncodeFloat(scale);

                                if (source.UsedBones[boneIndex])
                                {
                                    Matrix4x4 exportedMatrix = Matrix4x4.TRS(translation, rotation, Vector3.one * scale);
                                    Bounds boneBounds = GpuAnimationBakeUtility.TransformBounds(
                                        source.BoneBounds[boneIndex], exportedMatrix);
                                    if (!hasBounds)
                                    {
                                        animationBounds = boneBounds;
                                        hasBounds = true;
                                    }
                                    else
                                    {
                                        animationBounds.Encapsulate(boneBounds);
                                    }
                                }
                            }
                            catch (Exception exception)
                            {
                                throw new InvalidOperationException(sampleContext + ", bone '" + source.BoneNames[boneIndex] +
                                    "' [" + boneIndex + "]: " + exception.Message, exception);
                            }
                        }
                        sampledFrames++;
                    }
                }
                finally
                {
                    if (clone != null)
                        UnityEngine.Object.DestroyImmediate(clone);
                }
            }

            if (!hasBounds)
                throw new InvalidOperationException("No weighted vertices produced animation bounds.");
            result.Mesh.bounds = GpuAnimationBakeUtility.PadBounds(animationBounds);
            ReportProgress(progress, 0.97f, "Encoding animation PNG");
            texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.SetPixels32(pixels);
            result.AnimationTexturePng = texture.EncodeToPNG();
            if (result.AnimationTexturePng == null || result.AnimationTexturePng.Length == 0)
                throw new InvalidOperationException("Unity could not encode the animation texture as PNG.");
            ReportProgress(progress, 1, "Finished sampling " + request.Name);
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
        finally
        {
            try
            {
                if (texture != null)
                    UnityEngine.Object.DestroyImmediate(texture);
            }
            finally
            {
                if (previewCreated)
                    EditorSceneManager.ClosePreviewScene(previewScene);
            }
        }
    }

    private static SourceModel ValidateModel(GameObject model, List<string> warnings)
    {
        SkinnedMeshRenderer[] renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        if (renderers.Length != 1)
            throw new ArgumentException("Model '" + model.name + "' must contain exactly one SkinnedMeshRenderer; found " + renderers.Length + ".");
        SkinnedMeshRenderer renderer = renderers[0];
        Mesh mesh = renderer.sharedMesh;
        if (mesh == null || mesh.vertexCount == 0)
            throw new ArgumentException("The SkinnedMeshRenderer must reference a nonempty mesh.");
        if (mesh.subMeshCount != 1 || mesh.GetTopology(0) != MeshTopology.Triangles)
            throw new ArgumentException("Mesh '" + mesh.name + "' must have exactly one triangle submesh.");
        var indexCount = mesh.GetIndexCount(0);
        if (indexCount < 3 || indexCount % 3 != 0)
            throw new ArgumentException("Mesh '" + mesh.name + "' must contain complete triangles in its submesh.");
        for (int i = 0; i < mesh.blendShapeCount; i++)
        {
            float weight = renderer.GetBlendShapeWeight(i);
            if (!IsFinite(weight) || weight != 0)
                throw new ArgumentException("Default blend shape [" + i + "] has weight " +
                    weight.ToString("R", CultureInfo.InvariantCulture) +
                    "; only zero default blend shape weights are supported by bone-texture baking.");
        }
        if (renderer.rootBone == null || !IsInModel(renderer.rootBone, model.transform))
            throw new ArgumentException("The renderer needs a rootBone inside the source model hierarchy.");
        Transform[] bones = renderer.bones;
        if (bones == null || bones.Length == 0)
            throw new ArgumentException("The renderer must have a nonempty bone array.");
        for (int i = 0; i < bones.Length; i++)
        {
            if (bones[i] == null || !IsInModel(bones[i], model.transform))
                throw new ArgumentException("Bone [" + i + "] is missing or outside the source model hierarchy.");
        }

        Vector3[] vertices;
        Vector3[] normals;
        Matrix4x4[] bindPoses;
        BoneWeight[] weights;
        var uv = new List<Vector4>();
        try
        {
            vertices = mesh.vertices;
            normals = mesh.normals;
            bindPoses = mesh.bindposes;
            mesh.GetUVs(0, uv);
            var influences = mesh.GetBonesPerVertex();
            try
            {
                if (influences.Length != 0 && influences.Length != mesh.vertexCount)
                    throw new ArgumentException("The per-vertex influence count does not match the vertex count.");
                for (int i = 0; i < influences.Length; i++)
                {
                    if (influences[i] > 4)
                        throw new ArgumentException("Vertex [" + i + "] has " + influences[i] +
                            " bone influences; this format supports at most four. Adjust import weights or the source mesh.");
                }
            }
            finally
            {
                if (influences.IsCreated)
                    influences.Dispose();
            }
            weights = mesh.boneWeights;
        }
        catch (ArgumentException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ArgumentException("Could not read valid skinning data from mesh '" + mesh.name +
                "': " + exception.Message + " Ensure its vertex data is readable in the Editor.", exception);
        }
        if (vertices.Length != mesh.vertexCount || normals.Length != mesh.vertexCount || uv.Count != mesh.vertexCount)
            throw new ArgumentException("Mesh '" + mesh.name + "' requires a position, normal and UV0 value for every vertex.");
        if (weights.Length != mesh.vertexCount)
            throw new ArgumentException("Mesh '" + mesh.name + "' requires bone weights for every vertex.");
        if (bindPoses.Length != bones.Length)
            throw new ArgumentException("The bindpose count must equal the renderer's bone count.");
        for (int i = 0; i < bindPoses.Length; i++)
        {
            for (int element = 0; element < 16; element++)
            {
                if (!IsFinite(bindPoses[i][element]))
                    throw new ArgumentException("Bindpose [" + i + "] contains a nonfinite value.");
            }
            float determinant = bindPoses[i].determinant;
            if (!IsFinite(determinant) || determinant == 0)
                throw new ArgumentException("Bindpose [" + i + "] is singular or has a nonfinite determinant.");
        }

        var source = new SourceModel
        {
            Mesh = mesh,
            Bones = bones,
            BoneNames = new string[bones.Length],
            BindPoses = bindPoses,
            Indices = new List<Vector4>(vertices.Length),
            Weights = new List<Vector4>(vertices.Length),
            BoneBounds = new Bounds[bones.Length],
            UsedBones = new bool[bones.Length]
        };
        for (int i = 0; i < bones.Length; i++)
            source.BoneNames[i] = bones[i].name;
        int normalizedCount = 0;
        for (int vertexIndex = 0; vertexIndex < vertices.Length; vertexIndex++)
        {
            float normalLengthSquared = normals[vertexIndex].sqrMagnitude;
            if (!IsFinite(vertices[vertexIndex]) || !IsFinite(normals[vertexIndex]) ||
                !IsFinite(normalLengthSquared) || normalLengthSquared <= 0 || !IsFinite(uv[vertexIndex]))
                throw new ArgumentException("Vertex [" + vertexIndex + "] needs finite position/UV0 values and a finite, nonzero normal.");

            BoneWeight weight = weights[vertexIndex];
            var indices = new Vector4(weight.boneIndex0, weight.boneIndex1, weight.boneIndex2, weight.boneIndex3);
            var values = new Vector4(weight.weight0, weight.weight1, weight.weight2, weight.weight3);
            double sum = 0;
            for (int influence = 0; influence < 4; influence++)
            {
                float value = values[influence];
                if (!IsFinite(value) || value < 0)
                    throw new ArgumentException("Vertex [" + vertexIndex + "] has a negative or nonfinite bone weight.");
                if (value == 0)
                {
                    // The shader fetches every slot, so canonicalize unused indices to a valid bone.
                    indices[influence] = 0;
                    continue;
                }
                int boneIndex = (int)indices[influence];
                if (boneIndex < 0 || boneIndex >= bones.Length)
                    throw new ArgumentException("Vertex [" + vertexIndex + "] has invalid bone index " + boneIndex + ".");
                sum += value;
            }
            if (sum <= 0 || Math.Abs(sum - 1) > WeightSumTolerance)
                throw new ArgumentException("Vertex [" + vertexIndex + "] has bone weight sum " +
                    sum.ToString("R", CultureInfo.InvariantCulture) + "; expected 1 within 0.0001.");
            if (sum != 1)
                normalizedCount++;
            for (int influence = 0; influence < 4; influence++)
            {
                values[influence] = (float)(values[influence] / sum);
                if (values[influence] > 0)
                    IncludeBoneVertex(source, (int)indices[influence], vertices[vertexIndex]);
            }
            source.Indices.Add(indices);
            source.Weights.Add(values);
        }
        if (normalizedCount > 0)
            warnings.Add("Normalized rounding error in bone weight sums for " + normalizedCount + " vertices (within 0.0001).");
        return source;
    }

    private static BakedClip[] PrepareClips(GpuAnimationBakeRequest request, List<string> warnings, out int totalFrames)
    {
        if (request.Clips == null || request.Clips.Length == 0)
            throw new ArgumentException("At least one animation clip is required.");
        var clips = (AnimationClip[])request.Clips.Clone();
        for (int i = 0; i < clips.Length; i++)
        {
            if (clips[i] == null || string.IsNullOrWhiteSpace(clips[i].name))
                throw new ArgumentException("Each animation clip must have a nonempty name.");
        }
        Array.Sort(clips, (left, right) => StringComparer.Ordinal.Compare(left.name, right.name));
        var bakedClips = new BakedClip[clips.Length];
        totalFrames = 0;
        for (int i = 0; i < clips.Length; i++)
        {
            AnimationClip clip = clips[i];
            if (i > 0 && string.Equals(clips[i - 1].name, clip.name, StringComparison.Ordinal))
                throw new ArgumentException("Duplicate animation clip name '" + clip.name + "'.");
            try
            {
                ValidateClipBindings(request.Model, clip, warnings);
                bool loop = AnimationUtility.GetAnimationClipSettings(clip).loopTime;
                int count = GpuAnimationBakeUtility.CalculateFrameCount(clip.length, clip.frameRate, loop);
                int nextFrame = checked(totalFrames + count);
                bakedClips[i] = new BakedClip
                {
                    Source = clip,
                    Data = new AnimationClipData(clip.name, totalFrames, nextFrame - 1, loop, clip.frameRate)
                };
                totalFrames = nextFrame;
            }
            catch (Exception exception)
            {
                throw new ArgumentException("Clip '" + clip.name + "': " + exception.Message, exception);
            }
        }
        return bakedClips;
    }

    private static void ValidateClipBindings(GameObject model, AnimationClip clip, List<string> warnings)
    {
        if (AnimationUtility.GetObjectReferenceCurveBindings(clip).Length != 0)
            throw new ArgumentException("Object-reference animation curves are not supported by bone-texture baking.");
        bool hasAnimatorCurves = false;
        foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
        {
            if (binding.type != null && typeof(Transform).IsAssignableFrom(binding.type))
            {
                Transform target = string.IsNullOrEmpty(binding.path) ? model.transform : model.transform.Find(binding.path);
                if (target == null)
                    throw new ArgumentException("Transform animation path '" + binding.path + "' does not exist in the source model.");
            }
            else if (binding.type == typeof(Animator))
            {
                hasAnimatorCurves = true;
            }
            else
            {
                string component = binding.type != null ? binding.type.Name : "unknown component";
                throw new ArgumentException("Animation of " + component + "." + binding.propertyName +
                    " is unsupported; this bake format stores bone transforms, not blend shapes, materials or component state.");
            }
        }
        if (hasAnimatorCurves)
            warnings.Add("Clip '" + clip.name + "' has Animator/muscle curves. Humanoid retargeting and IK require validation in the target Unity project.");
    }

    private static void IncludeBoneVertex(SourceModel source, int boneIndex, Vector3 vertex)
    {
        if (!source.UsedBones[boneIndex])
        {
            source.BoneBounds[boneIndex] = new Bounds(vertex, Vector3.zero);
            source.UsedBones[boneIndex] = true;
        }
        else
        {
            Bounds bounds = source.BoneBounds[boneIndex];
            bounds.Encapsulate(vertex);
            source.BoneBounds[boneIndex] = bounds;
        }
    }

    private static bool IsInModel(Transform bone, Transform modelRoot)
    {
        return bone == modelRoot || bone.IsChildOf(modelRoot);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static bool IsFinite(Vector3 value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    }

    private static bool IsFinite(Vector4 value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w);
    }

    private static void ReportProgress(GpuAnimationBakeProgress progress, float value, string message)
    {
        if (progress != null && progress(value, message))
            throw new OperationCanceledException("GPU animation baking was canceled.");
    }

    private sealed class SourceModel
    {
        internal Mesh Mesh;
        internal Transform[] Bones;
        internal string[] BoneNames;
        internal Matrix4x4[] BindPoses;
        internal List<Vector4> Indices;
        internal List<Vector4> Weights;
        internal Bounds[] BoneBounds;
        internal bool[] UsedBones;
    }

    private sealed class BakedClip
    {
        internal AnimationClip Source;
        internal AnimationClipData Data;
    }

    private sealed class TransformSnapshot
    {
        private readonly Transform[] transforms;
        private readonly Vector3[] positions;
        private readonly Quaternion[] rotations;
        private readonly Vector3[] scales;

        internal TransformSnapshot(GameObject model)
        {
            transforms = model.GetComponentsInChildren<Transform>(true);
            positions = new Vector3[transforms.Length];
            rotations = new Quaternion[transforms.Length];
            scales = new Vector3[transforms.Length];
            for (int i = 0; i < transforms.Length; i++)
            {
                positions[i] = transforms[i].localPosition;
                rotations[i] = transforms[i].localRotation;
                scales[i] = transforms[i].localScale;
            }
        }

        internal void Restore()
        {
            for (int i = 0; i < transforms.Length; i++)
            {
                transforms[i].localPosition = positions[i];
                transforms[i].localRotation = rotations[i];
                transforms[i].localScale = scales[i];
            }
        }
    }
}
