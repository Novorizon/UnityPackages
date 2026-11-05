using System;
using System.Linq;
using System.Reflection;

internal static class EditorVerificationEntry
{
    private static int Main()
    {
        Console.WriteLine("Portable editor source checks: production codec/workspace/sandbox in a separate assembly; Unity math substitutes, no Unity Editor GUI runner.");
        int failures = 0;
        Type[] suites = Assembly.GetExecutingAssembly().GetTypes().Where(type => type.Name.EndsWith("Tests", StringComparison.Ordinal) && type.GetMethod("Run", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null)?.ReturnType == typeof(int)).OrderBy(type => type.FullName, StringComparer.Ordinal).ToArray();
        foreach (string required in new[] { "CodecTests", "WorkspaceTests", "SandboxTests", "PreflightTests" })
            if (!suites.Any(type => type.Name == required))
            {
                failures++;
                Console.WriteLine("FAIL missing required suite: " + required);
            }
        foreach (Type suite in suites)
        {
            try { failures += (int)suite.GetMethod("Run", BindingFlags.Public | BindingFlags.Static).Invoke(null, null); }
            catch (Exception error)
            {
                failures++;
                Console.WriteLine("FAIL " + suite.Name + ": " + (error.InnerException ?? error));
            }
        }
        Console.WriteLine("Editor source verification: " + suites.Length + " suites, " + failures + " failed.");
        return failures == 0 ? 0 : 1;
    }
}
