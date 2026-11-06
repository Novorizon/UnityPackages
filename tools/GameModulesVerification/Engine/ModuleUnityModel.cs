// Test-only boundary models. Native audio, JsonUtility and scene import require Unity verification.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace UnityEngine
{
    public partial class Object
    {
        public static T FindObjectOfType<T>(bool includeInactive = false)
            where T : Object => FindObjectsOfType<T>(includeInactive).FirstOrDefault();
        public static T Instantiate<T>(T original)
            where T : Object => Instantiate(original, null, false);
    }

    public struct Quaternion
    {
    }

    public partial class Transform
    {
        public Quaternion localRotation;
        public void SetPositionAndRotation(Vector3 value, Quaternion rotation)
        {
            position = value;
            localRotation = rotation;
        }
    }

    public static partial class Time
    {
        public static double timeAsDouble;
        public static double realtimeSinceStartupAsDouble;
    }

    public static partial class Application
    {
        public static string persistentDataPath = System.IO.Path.GetTempPath();
    }

    public class TextAsset : Object
    {
        public string text = "";
    }

    public class Sprite : Object
    {
    }

    public class ShaderVariantCollection : Object
    {
        public void WarmUp()
        {
        }
    }

    public static class JsonUtility
    {
        private static readonly JsonSerializerOptions options = CreateOptions();
        private static JsonSerializerOptions CreateOptions()
        {
            var resolver = new DefaultJsonTypeInfoResolver();
            resolver.Modifiers.Add(info =>
            {
                if (info.Kind == JsonTypeInfoKind.Object && info.CreateObject == null)
                {
                    info.CreateObject = () => Activator.CreateInstance(info.Type, true);
                }
            });
            return new JsonSerializerOptions
            {
                IncludeFields = true,
                TypeInfoResolver = resolver
            };
        }

        public static string ToJson(object value) => JsonSerializer.Serialize(value, options);
        public static T FromJson<T>(string json) => JsonSerializer.Deserialize<T>(json, options);
    }

    public partial class AsyncOperation
    {
        public float progress => isDone ? 1 : .5f;
    }

    public static partial class Resources
    {
        public static AsyncOperation UnloadUnusedAssets()
        {
            var operation = new AsyncOperation();
            operation.CompleteForTest();
            return operation;
        }
    }

    public class AudioClip : Object
    {
    }

    public static class AudioListener
    {
        public static bool pause;
    }

    public class AudioSource : MonoBehaviour
    {
        public AudioClip clip;
        public bool loop, playOnAwake;
        public float pitch, volume, spatialBlend;
        private bool playing;
        public bool isPlaying => playing && !AudioListener.pause;

        public void Play() => playing = true;
        public void Stop() => playing = false;
        public void Pause() => playing = false;
        public void UnPause() => playing = true;
        public void FinishForTest() => playing = false;
    }
}

namespace UnityEngine.U2D
{
    public class SpriteAtlas : UnityEngine.Object
    {
        public UnityEngine.Sprite GetSprite(string location) => new UnityEngine.Sprite();
    }

    public static class SpriteAtlasManager
    {
        public static event Action<string, Action<SpriteAtlas>> atlasRequested;
    }
}

namespace UnityEngine.SceneManagement
{
    public partial struct Scene
    {
        public string path => name;

        public bool IsValid() => handle > 0;
    }

    public static partial class SceneManager
    {
        private static readonly List<Scene> scenes = new List<Scene>();
        private static int nextId;
        public static Scene ActiveForTest;
        public static int sceneCount => scenes.Count;

        public static Scene GetSceneAt(int index) => scenes[index];
        public static Scene GetActiveScene() => ActiveForTest;
        public static void MoveGameObjectToScene(UnityEngine.GameObject value, Scene scene)
        {
        }

        public static Scene CreateForTest(string name)
        {
            var scene = new Scene
            {
                handle = ++nextId,
                name = name,
                isLoaded = true
            };
            scenes.Add(scene);
            return scene;
        }

        public static bool SetActiveScene(Scene scene)
        {
            ActiveForTest = scene;
            return scene.isLoaded;
        }

        public static UnityEngine.AsyncOperation LoadSceneAsync(string location, LoadSceneMode mode)
        {
            if (mode == LoadSceneMode.Single)
            {
                foreach (var scene in scenes.ToArray())
                {
                    UnloadSceneAsync(scene);
                }
            }

            var loaded = CreateForTest(location);
            sceneLoaded?.Invoke(loaded, mode);
            var operation = new UnityEngine.AsyncOperation();
            operation.CompleteForTest();
            return operation;
        }

        public static UnityEngine.AsyncOperation UnloadSceneAsync(Scene scene)
        {
            scenes.RemoveAll(value => value.handle == scene.handle);
            sceneUnloaded?.Invoke(scene);
            var operation = new UnityEngine.AsyncOperation();
            operation.CompleteForTest();
            return operation;
        }
    }
}
