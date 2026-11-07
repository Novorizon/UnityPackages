using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Game.Framework.All.Editor;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

internal static class Program
{
    private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
    private static int passed;
    private static int failed;

    private static void Main()
    {
        Run("a held add reaches its deadline but retry waits for the actual UPM request", () => VerifyTimeout(0));
        Run("a held embed reaches its deadline but retry waits for the actual UPM request", () => VerifyTimeout(1));
        Run("a held list reaches its own deadline but retry waits for the actual UPM request", () => VerifyTimeout(2));
        Console.WriteLine("RESULT installer audit passed=" + passed + " failed=" + failed + "; UPM requests are boundary models.");
        Environment.ExitCode = failed == 0 ? 0 : 1;
    }

    private static void VerifyTimeout(int mode)
    {
        bool embed = mode == 1;
        bool listing = mode == 2;
        UnityEditor.PackageManager.Requests.ListRequest pendingList = null;
        string root = Path.Combine(Path.GetTempPath(), "novorizon-installer-audit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Assets"));
        string source = Path.Combine(root, "Cache/All");
        Directory.CreateDirectory(source);
        var package = new PackageDependency
        {
            name = "com.code-philosophy.hybridclr",
            version = "1.0.0",
            git = "https://github.com/example/audit.git#1",
            external = true,
            embed = true
        };
        File.WriteAllText(Path.Combine(source, "Dependencies.json"), JsonUtility.ToJson(new PackageCatalog { packages = new[] { package } }));
        Application.dataPath = Path.Combine(root, "Assets");
        PackageInfo.Owner = new PackageInfo { name = AllPackagesInstaller.PackageName, version = "audit", resolvedPath = source };
        Client.Reset();
        Client.Sources[package.git] = new PackageInfo { name = package.name, version = package.version, source = PackageSource.Git };
        if (embed)
        {
            Client.Installed[package.name] = Client.Sources[package.git];
            Client.HoldEmbed = true;
        }
        else
        {
            Client.HoldAdd = !listing;
        }
        ResetInstaller();
        try
        {
            if (listing)
            {
                EditorApplication.TickForTest();
                pendingList = (UnityEditor.PackageManager.Requests.ListRequest)typeof(AllPackagesInstaller).GetField("listing", PrivateStatic).GetValue(null);
                pendingList.IsCompleted = false;
                EditorApplication.timeSinceStartup += 301;
            }
            else
            {
                Until(() => embed ? Client.Embeds.Count == 1 : Client.Adds.Count == 1);
                var state = typeof(AllPackagesInstaller).GetField("state", PrivateStatic).GetValue(null);
                state.GetType().GetField("deadline").SetValue(state, DateTime.UtcNow.AddMinutes(-1).Ticks);
            }
            EditorApplication.TickForTest();
            Assert(Phase(root) == "Failed", "The held UPM operation ignored its persisted deadline.");
            AllPackagesInstaller.RetryInstallation();
            Assert(Phase(root) == "Failed", "Retry discarded a live UPM request and could start an overlapping operation.");
            if (listing)
            {
                pendingList.IsCompleted = true;
            }
            else if (embed)
            {
                Client.CompleteEmbed();
            }
            else
            {
                Client.CompleteAdd();
            }
            AllPackagesInstaller.RetryInstallation();
            Until(() => Phase(root) == "Complete");
            Assert(embed ? Client.Embeds.Count == 1 : Client.Adds.Count == 1, "A completed operation was duplicated on recovery.");
        }
        finally
        {
            // Release the model request even if the old implementation fails the deadline assertion.
            if (pendingList != null)
            {
                pendingList.IsCompleted = true;
            }
            if (Client.Active != null && !Client.Active.IsCompleted)
            {
                if (embed)
                {
                    Client.CompleteEmbed();
                }
                else
                {
                    Client.CompleteAdd();
                }
            }
            ResetInstaller();
            Directory.Delete(root, true);
        }
    }

    private static string Phase(string root)
    {
        string path = Path.Combine(root, "ProjectSettings/NovorizonFrameworkAll.install.json");
        if (!File.Exists(path))
        {
            return "";
        }
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty("phase").GetString();
    }

    private static void ResetInstaller()
    {
        foreach (string field in new[] { "catalog", "state", "packageRoot", "projectRoot", "release", "listing", "adding", "embedding" })
        {
            typeof(AllPackagesInstaller).GetField(field, PrivateStatic).SetValue(null, null);
        }
        foreach (string field in new[] { "blocked", "batch", "verifyAfterAdd" })
        {
            typeof(AllPackagesInstaller).GetField(field, PrivateStatic).SetValue(null, false);
        }
        typeof(AllPackagesInstaller).GetField("nextRegistrationCheck", PrivateStatic).SetValue(null, 0d);
        EditorApplication.isCompiling = EditorApplication.isUpdating = EditorApplication.isPlayingOrWillChangePlaymode = false;
        EditorApplication.timeSinceStartup = 0;
        EditorApplication.ExitCode = null;
        Debug.Errors.Clear();
    }

    private static void Until(Func<bool> condition)
    {
        for (int i = 0; i < 100 && !condition(); i++)
        {
            EditorApplication.timeSinceStartup += .5;
            EditorApplication.TickForTest();
        }
        Assert(condition(), "Installer scenario did not reach its expected boundary.");
    }

    private static void Run(string name, Action action)
    {
        try
        {
            action();
            passed++;
            Console.WriteLine("PASS " + name);
        }
        catch (Exception error)
        {
            failed++;
            Console.WriteLine("FAIL " + name + ": " + error);
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }
}
