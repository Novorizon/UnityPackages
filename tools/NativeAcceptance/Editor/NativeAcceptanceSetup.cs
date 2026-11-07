using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;

public static class NativeAcceptanceSetup
{
    private static double deadline;

    public static void ImportTmpResources()
    {
        if (!Application.isBatchMode)
        {
            throw new InvalidOperationException("Use this setup only in the generated batch-mode acceptance project.");
        }
        if (Resources.Load<TMP_Settings>("TMP Settings") != null)
        {
            ValidateAndExit();
            return;
        }
        var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(TMP_Text).Assembly);
        if (package == null)
        {
            throw new InvalidOperationException("Install com.unity.textmeshpro before native acceptance.");
        }
        string resources = Path.Combine(package.resolvedPath, "Package Resources", "TMP Essential Resources.unitypackage");
        if (!File.Exists(resources))
        {
            throw new FileNotFoundException("TMP Essential Resources archive was not found.", resources);
        }
        deadline = EditorApplication.timeSinceStartup + 120;
        AssetDatabase.importPackageCompleted += OnImported;
        AssetDatabase.importPackageFailed += OnFailed;
        AssetDatabase.importPackageCancelled += OnCancelled;
        EditorApplication.update += CheckTimeout;
        AssetDatabase.ImportPackage(resources, false);
    }

    private static void OnImported(string packageName)
    {
        Unsubscribe();
        EditorApplication.delayCall += ValidateAndExit;
    }

    private static void OnFailed(string packageName, string message)
    {
        Fail("TMP resource import failed: " + message);
    }

    private static void OnCancelled(string packageName)
    {
        Fail("TMP resource import was cancelled.");
    }

    private static void CheckTimeout()
    {
        if (EditorApplication.timeSinceStartup > deadline)
        {
            Fail("TMP resource import timed out.");
        }
    }

    private static void ValidateAndExit()
    {
        if (Resources.Load<TMP_Settings>("TMP Settings") == null || TMP_Settings.defaultFontAsset == null)
        {
            Fail("Configure TMP Settings and its default font before native acceptance.");
            return;
        }
        Debug.Log("Native acceptance TMP resources and default font are ready.");
        EditorApplication.Exit(0);
    }

    private static void Fail(string message)
    {
        Unsubscribe();
        Debug.LogError(message);
        EditorApplication.Exit(1);
    }

    private static void Unsubscribe()
    {
        AssetDatabase.importPackageCompleted -= OnImported;
        AssetDatabase.importPackageFailed -= OnFailed;
        AssetDatabase.importPackageCancelled -= OnCancelled;
        EditorApplication.update -= CheckTimeout;
    }
}
