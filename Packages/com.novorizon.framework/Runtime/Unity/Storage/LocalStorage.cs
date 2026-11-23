using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace Game.Framework.Storage
{
    /// <summary>小型本地文件存储。JSON 使用 Unity JsonUtility 的字段序列化规则。</summary>
    public static class LocalStorage
    {
        /// <summary>JSON对应的记录。记录一次操作及其相关数据。</summary>
        [Serializable]
        private sealed class JsonRecord
        {
            public int version;
            public string json;
        }

#if UNITY_EDITOR
        static LocalStorage()
        {
            EditorStaticReset.Register(Reset);
        }
#endif

        private static readonly object gate = new object();
        private static string root;
        public static string RootDirectory => root;

        /// <summary>解除当前目录配置，文件仍保存在原目录。下一次可初始化其他目录。</summary>
        public static void Reset()
        {
            lock (gate)
            {
                root = null;
            }
        }

        /// <summary>配置本轮使用的存储目录。</summary>
        public static void Initialize(string directory = null)
        {
            lock (gate)
            {
                string next = Path.GetFullPath(directory ?? Path.Combine(Application.persistentDataPath, "Storage"));
                if (root != null && !string.Equals(root, next, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Storage is already initialized with another directory.");
                }

                Directory.CreateDirectory(next);
                root = next;
            }
        }

        /// <summary>保存字节数据。</summary>
        public static void SaveBytes(string key, byte[] bytes)
        {
            if (bytes == null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }

            lock (gate)
            {
                string path = Resolve(key);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
                string backup = path + ".bak";
                try
                {
                    using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        stream.Write(bytes, 0, bytes.Length);
                        stream.Flush(true);
                    }

                    if (File.Exists(path))
                    {
                        try
                        {
                            File.Replace(temporary, path, backup);
                        }
                        catch (PlatformNotSupportedException)
                        {
                            // 不支持 Replace 的平台使用备份再重命名；读操作可从中断后的备份恢复。
                            File.Delete(backup);
                            File.Move(path, backup);
                            File.Move(temporary, path);
                        }
                    }
                    else
                    {
                        File.Move(temporary, path);
                    }
                }
                finally
                {
                    if (File.Exists(temporary))
                    {
                        File.Delete(temporary);
                    }
                }
            }
        }

        /// <summary>加载字节数据。</summary>
        public static byte[] LoadBytes(string key)
        {
            lock (gate)
            {
                string path = Resolve(key);
                // 主文件缺失时才恢复备份；损坏的主文件应明确报错，不能悄悄覆盖用户数据。
                return File.ReadAllBytes(File.Exists(path) ? path : path + ".bak");
            }
        }

        /// <summary>返回Exists的判断结果。</summary>
        public static bool Exists(string key)
        {
            lock (gate)
            {
                string path = Resolve(key);
                return File.Exists(path) || File.Exists(path + ".bak");
            }
        }

        /// <summary>删除本地 Storage 管理器。</summary>
        public static void Delete(string key)
        {
            lock (gate)
            {
                string path = Resolve(key);
                // 先删备份，避免退出中断后读操作让已删除的数据重新出现。
                File.Delete(path + ".bak");
                File.Delete(path);
            }
        }

        /// <summary>保存文本。</summary>
        public static void SaveText(string key, string text)
        {
            SaveBytes(key, new UTF8Encoding(false, true).GetBytes(text ?? throw new ArgumentNullException(nameof(text))));
        }

        /// <summary>加载文本。</summary>
        public static string LoadText(string key) => new UTF8Encoding(false, true).GetString(LoadBytes(key));
        /// <summary>保存 JSON。</summary>
        public static void SaveJson<T>(string key, T value, int version = 1)
            where T : class
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            if (version < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(version));
            }

            SaveText(key, JsonUtility.ToJson(new JsonRecord { version = version, json = JsonUtility.ToJson(value) }));
        }

        /// <summary>migrate 接收旧版本号与原始 JSON，返回当前版本 JSON；迁移结果不会自动覆盖原文件。</summary>
        public static T LoadJson<T>(string key, int version = 1, Func<int, string, string> migrate = null)
            where T : class
        {
            if (version < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(version));
            }

            var record = JsonUtility.FromJson<JsonRecord>(LoadText(key));
            if (record == null || record.version < 1 || string.IsNullOrWhiteSpace(record.json))
            {
                throw new InvalidDataException("Invalid storage record: " + key);
            }

            string json = record.json;
            if (record.version != version)
            {
                if (record.version > version || migrate == null)
                {
                    throw new InvalidDataException("Unsupported storage version: " + record.version);
                }

                json = migrate(record.version, json);
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                throw new InvalidDataException("Migration returned empty JSON: " + key);
            }

            return JsonUtility.FromJson<T>(json) ?? throw new InvalidDataException("Invalid JSON payload: " + key);
        }

        /// <summary>解析本地 Storage 管理器。</summary>
        private static string Resolve(string key)
        {
            if (root == null)
            {
                throw new InvalidOperationException("Initialize LocalStorage first.");
            }

            if (string.IsNullOrWhiteSpace(key) || Path.IsPathRooted(key))
            {
                throw new ArgumentException("A relative storage key is required.", nameof(key));
            }

            string[] parts = key.Replace('\\', '/').Split('/');
            foreach (string part in parts)
            {
                // 保留备份和临时文件后缀，避免一个键覆盖另一个键的写入事务。
                if (part.Length == 0 || part == "." || part == ".." || part.EndsWith(".", StringComparison.Ordinal) || part.EndsWith(" ", StringComparison.Ordinal) || part.IndexOfAny(new[] { ':', '\0', '*', '?', '"', '<', '>', '|' }) >= 0 || part.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) || part.Contains(".tmp-"))
                {
                    throw new ArgumentException("Invalid storage key: " + key, nameof(key));
                }
            }

            return Path.Combine(root, Path.Combine(parts));
        }
    }
}
