using System.Reflection;
using Game.Ability;

static class Program
{
    const BindingFlags Public = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
    static string TypeName(Type type)
    {
        if (type.IsByRef) return TypeName(type.GetElementType()) + "&";
        if (type.IsArray) return TypeName(type.GetElementType()) + "[]";
        if (type.IsGenericParameter) return type.Name;
        if (type.IsGenericType) return (type.GetGenericTypeDefinition().FullName ?? type.Name).Split('`')[0] + "<" + string.Join(",", type.GetGenericArguments().Select(TypeName)) + ">";
        return type.FullName ?? type.Name;
    }
    static string Parameters(MethodBase method) => string.Join(", ", method.GetParameters().Select(p =>
        (p.IsOut ? "out " : p.ParameterType.IsByRef ? "ref " : "") + TypeName(p.ParameterType) + " " + p.Name +
        (p.IsOptional ? " = " + (p.DefaultValue == null ? "null" : Convert.ToString(p.DefaultValue, System.Globalization.CultureInfo.InvariantCulture)) : "")));
    static int Main(string[] args)
    {
        string root = Directory.GetCurrentDirectory();
        while (!File.Exists(Path.Combine(root, "Packages/com.novorizon.ability/package.json"))) root = Directory.GetParent(root)?.FullName ?? throw new Exception("Run inside the Ability source checkout.");
        var assembly = typeof(AbilitySystem).Assembly;
        if (assembly.GetName().Name != "Game.Ability") throw new Exception("Expected the actual runtime assembly boundary.");
        if (assembly.GetReferencedAssemblies().Any(a => a.Name.Contains("Editor") || a.Name.Contains("Sirenix") || a.Name == "ECS")) throw new Exception("Runtime acquired an editor or ECS dependency.");
        var lines = new List<string>();
        foreach (var type in assembly.GetExportedTypes().Where(t => t.Namespace?.StartsWith("Game.Ability", StringComparison.Ordinal) == true).OrderBy(TypeName, StringComparer.Ordinal))
        {
            lines.Add("type " + TypeName(type) + " : " + (type.BaseType == null ? "" : TypeName(type.BaseType)) + " [" + string.Join(",", type.GetInterfaces().Select(TypeName).OrderBy(n => n, StringComparer.Ordinal)) + "]");
            foreach (var field in type.GetFields(Public)) lines.Add(TypeName(type) + " :: field " + (field.IsStatic ? "static " : "") + (field.IsInitOnly ? "readonly " : "") + TypeName(field.FieldType) + " " + field.Name + (field.IsLiteral ? " = " + field.GetRawConstantValue() : ""));
            foreach (var ctor in type.GetConstructors(Public)) lines.Add(TypeName(type) + " :: ctor (" + Parameters(ctor) + ")");
            foreach (var method in type.GetMethods(Public).Where(m => !m.IsSpecialName)) lines.Add(TypeName(type) + " :: " + (method.IsStatic ? "static " : "") + (method.IsAbstract ? "abstract " : method.IsVirtual ? "virtual " : "") + TypeName(method.ReturnType) + " " + method.Name + (method.IsGenericMethodDefinition ? "<" + string.Join(",", method.GetGenericArguments().Select(TypeName)) + ">" : "") + "(" + Parameters(method) + ")");
            foreach (var property in type.GetProperties(Public)) lines.Add(TypeName(type) + " :: property " + TypeName(property.PropertyType) + " " + property.Name + " {" + (property.GetMethod?.IsPublic == true ? "get;" : "") + (property.SetMethod?.IsPublic == true ? "set;" : "") + "}");
            foreach (var ev in type.GetEvents(Public)) lines.Add(TypeName(type) + " :: event " + TypeName(ev.EventHandlerType) + " " + ev.Name);
        }
        string actual = string.Join("\n", lines.OrderBy(n => n, StringComparer.Ordinal)) + "\n";
        string baseline = Path.Combine(root, "tools/AbilityVerification/ApiContract/public-api.txt");
        if (args.Contains("--write")) File.WriteAllText(baseline, actual);
        else if (!File.Exists(baseline) || File.ReadAllText(baseline).Replace("\r\n", "\n") != actual) throw new Exception("Public Ability API changed. Review compatibility and deliberately update the snapshot for a new version.");
        Console.WriteLine("PASS public API contract: " + lines.Count + " declarations; Game.Ability assembly has no Editor/Odin/ECS dependency.");
        return 0;
    }
}
