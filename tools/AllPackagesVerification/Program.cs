using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Game.Framework.All.Editor;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using Setup = Game.Framework.HotUpdate.Editor.GameBuild;

internal static class Program
{
    private static readonly BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
    private static int passed;
    private static void Main()
    {
        Check("automatic first install submits all unpublished dependencies together", () => InProject(c =>
        {
            Drain();
            Equal(1, Client.Adds.Count);
            Equal(c.packages.Length, Client.Adds[0].Length);
            Equal(1, Setup.Calls);
        }));
        Check("matching installed versions skip add but still finish setup", () => InProject(c =>
        {
            foreach (var package in Client.Sources.Values)
            {
                Client.Installed[package.name] = package;
            }

            Drain();
            Equal(0, Client.Adds.Count);
            Equal(1, Setup.Calls);
        }));
        Check("persistent completion prevents installation on assembly reload", () => InProject(c =>
        {
            Drain();
            Reload();
            EditorApplication.TickForTest();
            Drain();
            Equal(1, Client.Adds.Count);
            Equal(1, Setup.Calls);
        }));
        Check("pending add survives lost handles without concurrent UPM calls", ReloadAdd);
        Check("pending embed survives lost handles without concurrent UPM calls", ReloadEmbed);
        Check("compilation errors stop setup and retry waits for active requests", CompileFailure);
        Check("network failure stops auto retries and explicit retry recovers", AddFailure);
        Check("embed failure stops configuration and retry recovers", EmbedFailure);
        Check("configuration failure keeps installed libraries and can retry", SetupFailure);
        Check("compiled and playing editors do not start UPM operations", Paused);
        Check("resolved version mismatch is reported instead of claiming success", VersionMismatch);
        Check("incompatible embedded package is preserved", ExistingEmbedded);
        Check("local monorepo uses portable sibling package folders", LocalWorkspace);
        Check("source install falls back to the release tag", () => InProject(c =>
        {
            Drain();
            True(Client.Adds.Single().All(s => s.StartsWith("https://github.com/") && s.Contains("#")));
        }));
        Check("aggregate version change runs the new setup once", ReleaseChange);
        Check("batch completion requires package, embed and settings evidence", BatchEvidence);
        Check("legacy editor, ECS and GPU animator packages are replaced atomically", LegacyPackages);
        Check("migration failure retains the old packages", FailedMigration);
        Check("external packages always use upstream references", ExternalReferences);
        Check("invalid dependency identities are rejected", InvalidCatalog);
        Console.WriteLine($"PASS {passed} aggregate installation scenarios. Unity/UPM are API models; native Unity was not executed.");
    }

    private static void Check(string name, Action action)
    {
        try
        {
            action();
            passed++;
            Console.WriteLine("PASS " + name);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("FAIL " + name + ": " + exception);
            Environment.Exit(1);
        }
    }

    private static void True(bool value)
    {
        if (!value)
        {
            throw new Exception("Assertion failed");
        }
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new Exception($"Expected {expected}, got {actual}");
        }
    }

    private static void Throws<T>(Action action)
        where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }

        throw new Exception("Expected " + typeof(T).Name);
    }

    private static string Root => Directory.GetParent(Application.dataPath).FullName;

    private static string Phase()
    {
        string path = Path.Combine(Root, "ProjectSettings/NovorizonFrameworkAll.install.json");
        if (!File.Exists(path))
        {
            return "";
        }

        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.GetProperty("phase").GetString();
    }

    private static void Reload()
    {
        foreach (string name in new[]
        {
            "catalog",
            "state",
            "packageRoot",
            "projectRoot",
            "release",
            "listing",
            "adding",
            "embedding"
        }

        )
        {
            typeof(AllPackagesInstaller).GetField(name, PrivateStatic).SetValue(null, null);
        }

        foreach (string name in new[]
        {
            "blocked",
            "batch",
            "verifyAfterAdd"
        }

        )
        {
            typeof(AllPackagesInstaller).GetField(name, PrivateStatic).SetValue(null, false);
        }
    }

    private static void InProject(Action<PackageCatalog> action)
    {
        string temp = Path.Combine(Path.GetTempPath(), "novorizon-all-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(temp, "Assets"));
        string packageRoot = Path.Combine(temp, "Cache/All");
        Directory.CreateDirectory(packageRoot);
        var catalog = JsonUtility.FromJson<PackageCatalog>(File.ReadAllText("Packages/com.novorizon.framework.all/Dependencies.json"));
        File.WriteAllText(Path.Combine(packageRoot, "Dependencies.json"), JsonUtility.ToJson(catalog));
        Application.dataPath = Path.Combine(temp, "Assets");
        PackageInfo.Owner = new PackageInfo
        {
            name = AllPackagesInstaller.PackageName,
            version = "0.1.0-preview.3",
            resolvedPath = packageRoot
        };
        Client.Reset();
        Debug.Errors.Clear();
        EditorApplication.ExitCode = null;
        Setup.Calls = 0;
        Setup.Fail = false;
        Reload();
        foreach (var package in catalog.packages)
        {
            Client.Sources[package.git] = new PackageInfo
            {
                name = package.name,
                version = package.version,
                source = PackageSource.Git
            };
        }

        try
        {
            action(catalog);
        }
        finally
        {
            Reload();
            Directory.Delete(temp, true);
            EditorApplication.isCompiling = EditorApplication.isPlayingOrWillChangePlaymode = false;
        }
    }

    private static void Drain()
    {
        for (int i = 0; i < 60 && Phase() != "Complete" && Phase() != "Failed"; i++)
        {
            EditorApplication.TickForTest();
        }

        True(Phase() == "Complete" || Phase() == "Failed");
    }

    private static void Until(Func<bool> condition)
    {
        for (int i = 0; i < 50 && !condition(); i++)
        {
            EditorApplication.TickForTest();
        }

        True(condition());
    }

    private static void ReloadAdd() => InProject(c =>
    {
        Client.HoldAdd = true;
        Until(() => Client.Adds.Count > 0);
        Reload();
        for (int i = 0; i < 4; i++)
        {
            EditorApplication.TickForTest();
        }

        Equal(1, Client.Adds.Count);
        Equal("Install", Phase());
        True(Debug.Errors.Count == 0);
        Client.CompleteAdd();
        Drain();
        Equal("Complete", Phase());
        Equal(1, Client.Adds.Count);
    });
    private static void ReloadEmbed() => InProject(c =>
    {
        Client.HoldEmbed = true;
        Until(() => Client.Embeds.Count > 0);
        Reload();
        for (int i = 0; i < 4; i++)
        {
            EditorApplication.TickForTest();
        }

        Equal(0, Setup.Calls);
        Equal("Embed", Phase());
        True(Debug.Errors.Count == 0);
        Client.CompleteEmbed();
        Drain();
        Equal("Complete", Phase());
        Equal(1, Client.Embeds.Count);
    });
    private static void CompileFailure() => InProject(c =>
    {
        Client.HoldAdd = true;
        Until(() => Client.Adds.Count > 0);
        UnityEditor.Compilation.CompilationPipeline.FailForTest();
        Equal("Failed", Phase());
        Equal(0, Setup.Calls);
        AllPackagesInstaller.RetryInstallation();
        Equal("Failed", Phase());
        Equal(1, Client.Adds.Count);
        Client.CompleteAdd();
        AllPackagesInstaller.RetryInstallation();
        Drain();
        Equal("Complete", Phase());
    });
    private static void AddFailure() => InProject(c =>
    {
        Client.FailAdd = true;
        Drain();
        Equal("Failed", Phase());
        Equal(0, Setup.Calls);
        for (int i = 0; i < 5; i++)
        {
            EditorApplication.TickForTest();
        }

        Equal(1, Client.Adds.Count);
        Client.FailAdd = false;
        AllPackagesInstaller.RetryInstallation();
        Drain();
        Equal("Complete", Phase());
    });
    private static void EmbedFailure() => InProject(c =>
    {
        Client.FailEmbed = true;
        Drain();
        Equal("Failed", Phase());
        Equal(0, Setup.Calls);
        Equal(c.packages.Length, Client.Installed.Count);
        Client.FailEmbed = false;
        AllPackagesInstaller.RetryInstallation();
        Drain();
        Equal("Complete", Phase());
        Equal(1, Client.Adds.Count);
    });
    private static void SetupFailure() => InProject(c =>
    {
        Setup.Fail = true;
        Drain();
        Equal("Failed", Phase());
        Equal(c.packages.Length, Client.Installed.Count);
        Setup.Fail = false;
        AllPackagesInstaller.RetryInstallation();
        Drain();
        Equal("Complete", Phase());
        Equal(1, Client.Adds.Count);
    });
    private static void Paused() => InProject(c =>
    {
        EditorApplication.isCompiling = true;
        for (int i = 0; i < 3; i++)
        {
            EditorApplication.TickForTest();
        }

        Equal(0, Client.Adds.Count);
        EditorApplication.isCompiling = false;
        EditorApplication.isPlayingOrWillChangePlaymode = true;
        EditorApplication.TickForTest();
        Equal(0, Client.Adds.Count);
        EditorApplication.isPlayingOrWillChangePlaymode = false;
        Drain();
        Equal("Complete", Phase());
    });
    private static void VersionMismatch() => InProject(c =>
    {
        Client.Sources[c.packages[0].git].version = "0.0.1";
        Drain();
        Equal("Failed", Phase());
        Equal(0, Setup.Calls);
        Equal(1, Client.Adds.Count);
    });
    private static void ExistingEmbedded() => InProject(c =>
    {
        string name = c.packages[0].name;
        var existing = new PackageInfo
        {
            name = name,
            version = "9.0.0",
            source = PackageSource.Embedded
        };
        Client.Installed[name] = existing;
        Drain();
        Equal("Failed", Phase());
        Equal(0, Client.Adds.Count);
        True(ReferenceEquals(existing, Client.Installed[name]));
    });
    private static void LocalWorkspace() => InProject(c =>
    {
        foreach (var package in c.packages.Where(p => !p.external))
        {
            string sibling = Path.Combine(Path.GetDirectoryName(PackageInfo.Owner.resolvedPath), package.name);
            Directory.CreateDirectory(sibling);
            File.WriteAllText(Path.Combine(sibling, "package.json"), "{\"name\":\"" + package.name + "\",\"version\":\"" + package.version + "\"}");
            string source = "file:" + Path.GetRelativePath(Path.Combine(Root, "Packages"), sibling).Replace('\\', '/');
            Client.Sources[source] = Client.Sources[package.git];
        }

        Drain();
        Equal("Complete", Phase());
        True(Client.Adds.Single().Count(source => source.StartsWith("https://github.com/")) == c.packages.Count(p => p.external));
        foreach (string source in Client.Adds.Single().Where(s => s.StartsWith("file:")))
        {
            True(File.Exists(Path.Combine(Root, "Packages", source.Substring(5), "package.json")));
        }
    });
    private static void ReleaseChange() => InProject(c =>
    {
        Drain();
        PackageInfo.Owner.version = "0.1.0-preview.4";
        Reload();
        Until(() => Setup.Calls == 2);
        Equal(1, Client.Adds.Count);
        Equal("Complete", Phase());
    });
    private static void BatchEvidence() => InProject(c =>
    {
        AllPackagesInstaller.InstallBatch();
        Drain();
        EditorApplication.TickForTest();
        Equal(0, EditorApplication.ExitCode);
        string report = Path.Combine(Root, "Library/NovorizonFrameworkAll/verification.json");
        True(File.Exists(report));
        string json = File.ReadAllText(report);
        True(json.Contains("\"hybridClrEmbedded\":true") && json.Contains("\"hotUpdateProjectConfigured\":true"));
    });
    private static void LegacyPackages() => InProject(c =>
    {
        foreach (string name in c.obsoletePackages)
        {
            Client.Installed[name] = new PackageInfo
            {
                name = name,
                version = "old"
            };
        }

        Drain();
        Equal("Complete", Phase());
        True(c.obsoletePackages.All(name => !Client.Installed.ContainsKey(name)));
        True(Client.Removes.Single().OrderBy(n => n).SequenceEqual(c.obsoletePackages.OrderBy(n => n)));
    });
    private static void FailedMigration() => InProject(c =>
    {
        foreach (string name in c.obsoletePackages)
        {
            Client.Installed[name] = new PackageInfo
            {
                name = name,
                version = "old"
            };
        }

        Client.FailAdd = true;
        Drain();
        Equal("Failed", Phase());
        True(c.obsoletePackages.All(name => Client.Installed.ContainsKey(name)));
    });
    private static void ExternalReferences() => InProject(c =>
    {
        foreach (var package in c.packages.Where(p => p.external))
        {
            string sibling = Path.Combine(Path.GetDirectoryName(PackageInfo.Owner.resolvedPath), package.name);
            Directory.CreateDirectory(sibling);
            File.WriteAllText(Path.Combine(sibling, "package.json"), "{\"name\":\"" + package.name + "\",\"version\":\"" + package.version + "\"}");
            Equal(package.git, package.PrepareSource(PackageInfo.Owner.resolvedPath, Root, "test"));
        }

        Drain();
        Equal("Complete", Phase());
        True(!Directory.Exists(Path.Combine(PackageInfo.Owner.resolvedPath, "Payload~")));
    });
    private static void InvalidCatalog()
    {
        var c = new PackageCatalog
        {
            packages = Array.Empty<PackageDependency>()
        };
        Throws<InvalidDataException>(c.Validate);
        c.packages = new[]
        {
            new PackageDependency
            {
                name = "a",
                version = "1.0.0"
            }
        };
        Throws<InvalidDataException>(c.Validate);
        var valid = new PackageDependency
        {
            name = "a",
            version = "1.0.0",
            git = "https://github.com/a/b#commit"
        };
        c.packages = new[]
        {
            valid,
            valid
        };
        Throws<InvalidDataException>(c.Validate);
    }
}
