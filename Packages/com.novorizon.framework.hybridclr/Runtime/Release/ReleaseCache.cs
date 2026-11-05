using System;
using System.IO;
using System.Linq;
using System.Text;

namespace Game.Framework.HotUpdate
{
    // Immutable content-addressed releases. A directory rename is the commit boundary.
    public sealed class ReleaseCache : IDisposable
    {
        public string Root { get; }
        private FileStream lease;
        public ReleaseCache(string root)
        {
            Root = Path.GetFullPath(root);
            Directory.CreateDirectory(Root);
            ReleasePolicy.Require((File.GetAttributes(Root) & FileAttributes.ReparsePoint) == 0,
                "Cache root must not be a symbolic link.");
        }

        public void AcquireLease()
        {
            ReleasePolicy.Require(lease == null, "Cache lease is already held.");
            lease = new FileStream(Path.Combine(Root, ".lease"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }

        public void Dispose() { lease?.Dispose(); lease = null; }

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

        public string ReleasePath(string id)
        {
            ReleasePolicy.Require(ReleasePolicy.IsHash(id), "Invalid release content ID.");
            string path = Path.Combine(Root, id);
            if (Directory.Exists(path))
                ReleasePolicy.Require((File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0,
                    "Release directory must not be a symbolic link.");
            return path;
        }

        public string[] EnumerateReleases() => Directory.GetDirectories(Root)
            .Where(p => ReleasePolicy.IsHash(Path.GetFileName(p))
                && (File.GetAttributes(p) & FileAttributes.ReparsePoint) == 0).ToArray();

        public string BeginStage()
        {
            string path = Path.Combine(Root, ".staging-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        public string Commit(string stage, string id, string signedEnvelope)
        {
            RequireStage(stage);
            string destination = ReleasePath(id);
            ReleasePolicy.Require(!Directory.Exists(destination), "Release already exists.");
            File.WriteAllText(Path.Combine(stage, "release.json"), signedEnvelope, new UTF8Encoding(false));
            Directory.Move(stage, destination);
            return destination;
        }

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

        public void CleanInterruptedDownloads()
        {
            foreach (string path in Directory.GetDirectories(Root, ".staging-*")) DiscardStage(path);
            foreach (string path in Directory.GetFiles(Root, ".accepted-*"))
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0) File.Delete(path);
        }

        private void RequireStage(string path)
        {
            path = Path.GetFullPath(path);
            ReleasePolicy.Require(Path.GetDirectoryName(path) == Root
                && Path.GetFileName(path).StartsWith(".staging-", StringComparison.Ordinal)
                && (!Directory.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0),
                "Staging path escaped cache root.");
        }

        private string RejectionPath(string id)
        {
            ReleasePath(id);
            string directory = Path.Combine(Root, ".rejected");
            Directory.CreateDirectory(directory);
            ReleasePolicy.Require((File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0, "Invalid rejection directory.");
            return Path.Combine(directory, id);
        }

        public bool WasRejected(string id) => File.Exists(RejectionPath(id)) || File.Exists(Path.Combine(ReleasePath(id), "rejected"));
        public bool IsHealthy(string id) => File.Exists(Path.Combine(ReleasePath(id), "healthy"));

        public bool RecoverInterruptedBoot(string id)
        {
            string path = ReleasePath(id);
            if (!File.Exists(Path.Combine(path, "attempt"))) return WasRejected(id);
            Reject(id, "Previous process stopped before startup health acknowledgement.");
            return true;
        }

        public void BeginBoot(string id) => File.WriteAllText(Path.Combine(ReleasePath(id), "attempt"), "starting");
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
