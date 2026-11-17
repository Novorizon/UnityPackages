using System;
using System.IO;
using System.Linq;
using System.Text;

namespace Game.Framework.HotUpdate
{
    // Immutable content-addressed releases. A directory rename is the commit boundary.
    /// <summary>提供发布版本缓存的实现。</summary>
    public sealed class ReleaseCache : IDisposable
    {
        public string Root { get; }
        private FileStream lease;
        /// <summary>使用调用方提供的root创建 ReleaseCache。</summary>
        public ReleaseCache(string root)
        {
            Root = Path.GetFullPath(root);
            Directory.CreateDirectory(Root);
            ReleasePolicy.Require((File.GetAttributes(Root) & FileAttributes.ReparsePoint) == 0,
                "Cache root must not be a symbolic link.");
        }

        /// <summary>取得租约。</summary>
        public void AcquireLease()
        {
            ReleasePolicy.Require(lease == null, "Cache lease is already held.");
            lease = new FileStream(Path.Combine(Root, ".lease"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }

        /// <summary>结束发布版本缓存对相关资源的持有，并执行该类型的清理流程。</summary>
        public void Dispose() { lease?.Dispose(); lease = null; }

        /// <summary>删除发布版本。</summary>
        public void DeleteRelease(string id)
        {
            string directory = ReleasePath(id);
            ReleasePolicy.Require(Directory.GetDirectories(directory).Length == 0, "Unexpected nested cache directory.");
            foreach (string path in Directory.GetFiles(directory))
            {
                ReleasePolicy.Require((File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0, "Unexpected cache link.");
                File.Delete(path);
            }
            Directory.Delete(directory);
        }

        /// <summary>释放路径。</summary>
        public string ReleasePath(string id)
        {
            ReleasePolicy.Require(ReleasePolicy.IsHash(id), "Invalid release content ID.");
            string path = Path.Combine(Root, id);
            if (Directory.Exists(path))
                ReleasePolicy.Require((File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0,
                    "Release directory must not be a symbolic link.");
            return path;
        }

        /// <summary>枚举 Releases。</summary>
        public string[] EnumerateReleases() => Directory.GetDirectories(Root)
            .Where(p => ReleasePolicy.IsHash(Path.GetFileName(p))
                && (File.GetAttributes(p) & FileAttributes.ReparsePoint) == 0).ToArray();

        /// <summary>开始暂存。</summary>
        public string BeginStage()
        {
            string path = Path.Combine(Root, ".staging-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        /// <summary>写入已验证的发布清单后，将整个暂存目录移到正式版本位置。</summary>
        public string Commit(string stage, string id, string signedEnvelope)
        {
            RequireStage(stage);
            string destination = ReleasePath(id);
            ReleasePolicy.Require(!Directory.Exists(destination), "Release already exists.");
            File.WriteAllText(Path.Combine(stage, "release.json"), signedEnvelope, new UTF8Encoding(false));
            Directory.Move(stage, destination);
            return destination;
        }

        /// <summary>调用 RequireStage 完成Discard 暂存。</summary>
        public void DiscardStage(string stage)
        {
            RequireStage(stage);
            if (!Directory.Exists(stage)) return;
            // Staging contains only files created by this updater; reject unexpected links/directories.
            ReleasePolicy.Require(Directory.GetDirectories(stage).Length == 0, "Unexpected staging directory.");
            foreach (string file in Directory.GetFiles(stage))
            {
                ReleasePolicy.Require((File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0,
                    "Unexpected staging link.");
                File.Delete(file);
            }
            Directory.Delete(stage);
        }

        /// <summary>调用 Directory.GetDirectories 完成Clean 已中断 Downloads。</summary>
        public void CleanInterruptedDownloads()
        {
            foreach (string path in Directory.GetDirectories(Root, ".staging-*")) DiscardStage(path);
            foreach (string path in Directory.GetFiles(Root, ".accepted-*"))
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0) File.Delete(path);
        }

        /// <summary>检查暂存是否满足前置条件。</summary>
        private void RequireStage(string path)
        {
            path = Path.GetFullPath(path);
            ReleasePolicy.Require(Path.GetDirectoryName(path) == Root
                && Path.GetFileName(path).StartsWith(".staging-", StringComparison.Ordinal)
                && (!Directory.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0),
                "Staging path escaped cache root.");
        }

        /// <summary>根据传入参数取得Rejection 路径。</summary>
        private string RejectionPath(string id)
        {
            ReleasePath(id);
            string directory = Path.Combine(Root, ".rejected");
            Directory.CreateDirectory(directory);
            ReleasePolicy.Require((File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0, "Invalid rejection directory.");
            return Path.Combine(directory, id);
        }

        /// <summary>返回Was 已拒绝的判断结果。</summary>
        public bool WasRejected(string id) => File.Exists(RejectionPath(id)) || File.Exists(Path.Combine(ReleasePath(id), "rejected"));
        /// <summary>判断是否为健康。</summary>
        public bool IsHealthy(string id) => File.Exists(Path.Combine(ReleasePath(id), "healthy"));

        /// <summary>检查上次启动是否停在未完成状态，供后续版本选择处理。</summary>
        public bool RecoverInterruptedBoot(string id)
        {
            string path = ReleasePath(id);
            if (!File.Exists(Path.Combine(path, "attempt"))) return WasRejected(id);
            Reject(id, "Previous process stopped before startup health acknowledgement.");
            return true;
        }

        /// <summary>开始启动。</summary>
        public void BeginBoot(string id) => File.WriteAllText(Path.Combine(ReleasePath(id), "attempt"), "starting");
        /// <summary>标记健康。</summary>
        public void MarkHealthy(string id, bool advanceAcceptedReceipt = true)
        {
            string path = ReleasePath(id);
            if (advanceAcceptedReceipt)
            {
                string temporary = Path.Combine(Root, ".accepted-" + Guid.NewGuid().ToString("N"));
                byte[] envelope = Encoding.UTF8.GetBytes(ReleasePolicy.ReadBoundedText(Path.Combine(path, "release.json")));
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { stream.Write(envelope, 0, envelope.Length); stream.Flush(true); }
                string destination = Path.Combine(Root, "accepted.json");
                if (File.Exists(destination)) File.Replace(temporary, destination, destination + ".bak");
                else File.Move(temporary, destination);
            }
            File.WriteAllText(Path.Combine(path, "healthy"), "ready");
            File.Delete(Path.Combine(path, "attempt"));
        }

        /// <summary>拒绝发布版本缓存。</summary>
        public void Reject(string id, string reason)
        {
            string path = ReleasePath(id);
            File.WriteAllText(RejectionPath(id), reason);
            File.WriteAllText(Path.Combine(path, "rejected"), reason);
            File.Delete(Path.Combine(path, "attempt"));
            // Receipts are tiny; cap them independently of payload retention.
            foreach (var old in new DirectoryInfo(Path.GetDirectoryName(RejectionPath(id))).GetFiles()
                .Where(f => ReleasePolicy.IsHash(f.Name)).OrderByDescending(f => f.LastWriteTimeUtc).Skip(256))
                if ((old.Attributes & FileAttributes.ReparsePoint) == 0) old.Delete();
        }
    }
}
