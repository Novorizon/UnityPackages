using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Game.Framework.Generators
{
    /// <summary>Unity 2022.3 的 Roslyn 3.8 可加载；不依赖较新的 Incremental Generator API。</summary>
    [Generator]
    public sealed class SingletonStaticsGenerator : ISourceGenerator
    {
        private const string Prefix = "__NovorizonStaticReset";
        private static readonly SymbolDisplayFormat NullableTypeFormat = SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);
        private static readonly DiagnosticDescriptor PartialRequired = Rule("NVR001", "Partial declaration required", "Singleton '{0}' owns static state; mark it and its containing types partial to generate Editor reset code");
        private static readonly DiagnosticDescriptor StaticConstructor = Rule("NVR002", "Static constructor cannot be replayed", "Singleton '{0}' has an explicit static constructor; put state initialization in declarations or Initialize");
        private static readonly DiagnosticDescriptor UnsupportedMember = Rule("NVR003", "Static declaration cannot be restored", "Static member '{0}' cannot be restored automatically: {1}");
        private static readonly DiagnosticDescriptor ReservedName = Rule("NVR004", "Reserved generated member name", "Member '{0}' uses a name reserved for Framework's generated static reset");
        private static readonly DiagnosticDescriptor CrossTypeDependency = Rule("NVR005", "Static initializer depends on another singleton", "Initializer for '{0}' reads another singleton's static state; perform dependent initialization in Initialize instead");

        public void Initialize(GeneratorInitializationContext context)
        {
        }

        public void Execute(GeneratorExecutionContext context)
        {
            if (!context.ParseOptions.PreprocessorSymbolNames.Contains("UNITY_EDITOR"))
            {
                return;
            }

            var seen = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            foreach (var tree in context.Compilation.SyntaxTrees)
            {
                var model = context.Compilation.GetSemanticModel(tree);
                foreach (var declaration in tree.GetRoot(context.CancellationToken).DescendantNodes().OfType<ClassDeclarationSyntax>())
                {
                    var type = model.GetDeclaredSymbol(declaration, context.CancellationToken);
                    if (type == null || !IsSingleton(type) || !seen.Add(type))
                    {
                        continue;
                    }

                    GenerateType(context, type);
                }
            }
        }

        private static bool IsBase(INamedTypeSymbol type)
        {
            string name = type.OriginalDefinition.ToDisplayString();
            return name == "Game.Framework.Singleton<T>" || name == "Game.Framework.MonoSingleton<T>";
        }

        private static bool IsSingleton(INamedTypeSymbol type)
        {
            for (var current = type.BaseType; current != null; current = current.BaseType)
            {
                if (IsBase(current))
                {
                    return true;
                }
            }

            return false;
        }

        private static void GenerateType(GeneratorExecutionContext context, INamedTypeSymbol type)
        {
            var parts = type.DeclaringSyntaxReferences.Select(item => (ClassDeclarationSyntax)item.GetSyntax(context.CancellationToken))
                .OrderBy(item => item.SyntaxTree.FilePath, StringComparer.Ordinal).ThenBy(item => item.SpanStart).ToArray();
            if (!parts.Any(HasState))
            {
                return;
            }

            for (var current = (TypeDeclarationSyntax)parts[0]; current != null; current = current.Parent as TypeDeclarationSyntax)
            {
                var symbol = context.Compilation.GetSemanticModel(current.SyntaxTree).GetDeclaredSymbol(current, context.CancellationToken);
                if (symbol == null || symbol.DeclaringSyntaxReferences.Any(item => !((TypeDeclarationSyntax)item.GetSyntax(context.CancellationToken)).Modifiers.Any(SyntaxKind.PartialKeyword)))
                {
                    Report(context, PartialRequired, current.GetLocation(), type.Name);
                    return;
                }
            }

            if (type.GetMembers().Any(item => item.Name.StartsWith(Prefix, StringComparison.Ordinal)))
            {
                Report(context, ReservedName, parts[0].GetLocation(), Prefix);
                return;
            }

            foreach (var constructor in parts.SelectMany(item => item.Members).OfType<ConstructorDeclarationSyntax>().Where(item => item.Modifiers.Any(SyntaxKind.StaticKeyword)))
            {
                Report(context, StaticConstructor, constructor.GetLocation(), type.Name);
                return;
            }

            bool valid = true;
            var clears = new List<string>();
            var restores = new List<string>();
            for (int index = 0; index < parts.Length; index++)
            {
                var part = parts[index];
                var model = context.Compilation.GetSemanticModel(part.SyntaxTree);
                var clear = new StringBuilder();
                var restore = new StringBuilder();
                foreach (var member in part.Members.Where(item => item.Modifiers().Any(SyntaxKind.StaticKeyword)))
                {
                    if (member is FieldDeclarationSyntax field)
                    {
                        foreach (var variable in field.Declaration.Variables)
                        {
                            var symbol = (IFieldSymbol)model.GetDeclaredSymbol(variable, context.CancellationToken);
                            if (symbol.IsConst)
                            {
                                continue;
                            }

                            if (symbol.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == "System.ThreadStaticAttribute"))
                            {
                                Reject(context, variable, symbol.Name, "ThreadStatic fields require per-thread cleanup");
                                valid = false;
                                continue;
                            }

                            valid &= EmitMember(context, model, symbol.Type, symbol.IsReadOnly, variable.Identifier.Text, variable.Initializer?.Value, variable, clear, restore, type);
                        }
                    }
                    else if (member is EventFieldDeclarationSyntax eventField)
                    {
                        foreach (var variable in eventField.Declaration.Variables)
                        {
                            var symbol = (IEventSymbol)model.GetDeclaredSymbol(variable, context.CancellationToken);
                            valid &= EmitMember(context, model, symbol.Type, false, variable.Identifier.Text, variable.Initializer?.Value, variable, clear, restore, type);
                        }
                    }
                    else if (member is EventDeclarationSyntax explicitEvent)
                    {
                        Reject(context, explicitEvent, explicitEvent.Identifier.Text, "custom event accessors have no assignable event field");
                        valid = false;
                    }
                    else if (member is PropertyDeclarationSyntax property && IsAutoProperty(property))
                    {
                        var symbol = (IPropertySymbol)model.GetDeclaredSymbol(property, context.CancellationToken);
                        valid &= EmitMember(context, model, symbol.Type, symbol.SetMethod == null, property.Identifier.Text, property.Initializer?.Value, property, clear, restore, type);
                    }
                }

                clears.Add(clear.ToString());
                restores.Add(restore.ToString());
            }

            if (!valid)
            {
                return;
            }

            for (int index = 0; index < parts.Length; index++)
            {
                var members = new StringBuilder();
                if (index == 0)
                {
                    members.AppendLine("#pragma warning disable CS0414");
                    members.Append("private static readonly bool ").Append(Prefix).Append("Registration = RegisterEditorStaticReset(").Append(Prefix).Append("Clear, ").Append(Prefix).AppendLine("Restore);");
                    members.Append("private static void ").Append(Prefix).AppendLine("Clear()\n{");
                    for (int i = 0; i < parts.Length; i++)
                    {
                        members.Append(Prefix).Append("Clear_").Append(i).AppendLine("();");
                    }

                    members.AppendLine("}");
                    members.Append("private static void ").Append(Prefix).AppendLine("Restore()\n{");
                    for (int i = 0; i < parts.Length; i++)
                    {
                        members.Append(Prefix).Append("Restore_").Append(i).AppendLine("();");
                    }

                    members.AppendLine("}");
                }

                members.Append("private static void ").Append(Prefix).Append("Clear_").Append(index).AppendLine("()\n{").Append(clears[index]).AppendLine("}");
                members.Append("private static void ").Append(Prefix).Append("Restore_").Append(index).AppendLine("()\n{").Append(restores[index]).AppendLine("}");
                string source = Wrap(parts[index], members.ToString());
                string hint = string.Join("_", type.ToDisplayString().Select(character => char.IsLetterOrDigit(character) ? character.ToString() : ((int)character).ToString("X")));
                context.AddSource(hint + "." + index + ".Statics.g.cs", SourceText.From(source, Encoding.UTF8));
            }
        }

        private static bool HasState(ClassDeclarationSyntax declaration)
        {
            return declaration.Members.Any(member => member.Modifiers().Any(SyntaxKind.StaticKeyword) &&
                (member is BaseFieldDeclarationSyntax || member is EventDeclarationSyntax || (member is PropertyDeclarationSyntax property && IsAutoProperty(property))));
        }

        private static bool IsAutoProperty(PropertyDeclarationSyntax property)
        {
            return property.AccessorList != null && property.AccessorList.Accessors.All(item => item.Body == null && item.ExpressionBody == null);
        }

        private static bool EmitMember(GeneratorExecutionContext context, SemanticModel model, ITypeSymbol type, bool readOnly, string name, ExpressionSyntax initializer, SyntaxNode node, StringBuilder clear, StringBuilder restore, INamedTypeSymbol owner)
        {
            if (initializer != null && ReadsOtherSingleton(model, initializer, owner))
            {
                Report(context, CrossTypeDependency, node.GetLocation(), name);
                return false;
            }

            if (!readOnly)
            {
                clear.Append(name).AppendLine(" = default!;");
                if (initializer != null)
                {
                    restore.Append(name).Append(" = ");
                    if (initializer is InitializerExpressionSyntax && type is IArrayTypeSymbol)
                    {
                        restore.Append("new ").Append(type.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString(NullableTypeFormat)).Append(' ');
                    }

                    restore.Append(initializer).AppendLine(";");
                }

                return true;
            }

            if (type is INamedTypeSymbol weakTable && weakTable.OriginalDefinition.ToDisplayString() == "System.Runtime.CompilerServices.ConditionalWeakTable<TKey, TValue>" &&
                initializer is BaseObjectCreationExpressionSyntax weakCreation && (weakCreation.ArgumentList == null || weakCreation.ArgumentList.Arguments.Count == 0) &&
                (weakCreation.Initializer == null || weakCreation.Initializer.Expressions.Count == 0))
            {
                clear.Append(name).AppendLine(".Clear();");
                return true;
            }

            if (IsStableReadonly(type))
            {
                if (type.SpecialType == SpecialType.System_Object &&
                    !(initializer is BaseObjectCreationExpressionSyntax objectCreation && objectCreation.Initializer == null &&
                      model.GetSymbolInfo(objectCreation).Symbol is IMethodSymbol constructor && constructor.ContainingType.SpecialType == SpecialType.System_Object))
                {
                    Reject(context, node, name, "readonly object is preserved only for an object synchronization gate initialized with new object()");
                    return false;
                }

                return true;
            }

            if (type is INamedTypeSymbol collection && collection.ContainingNamespace.ToDisplayString() == "System.Collections.Generic" &&
                (collection.Name == "List" || collection.Name == "Dictionary" || collection.Name == "HashSet") &&
                initializer is BaseObjectCreationExpressionSyntax creation && (creation.ArgumentList == null || creation.ArgumentList.Arguments.Count == 0))
            {
                clear.Append(name).AppendLine(".Clear();");
                foreach (var expression in creation.Initializer?.Expressions ?? default(SeparatedSyntaxList<ExpressionSyntax>))
                {
                    if (collection.Name == "Dictionary")
                    {
                        if (expression is InitializerExpressionSyntax pair && pair.Expressions.Count == 2)
                        {
                            restore.Append(name).Append(".Add(").Append(pair.Expressions[0]).Append(", ").Append(pair.Expressions[1]).AppendLine(");");
                        }
                        else if (expression is AssignmentExpressionSyntax assignment && assignment.Left is ImplicitElementAccessSyntax element)
                        {
                            restore.Append(name).Append(element.ArgumentList).Append(" = ").Append(assignment.Right).AppendLine(";");
                        }
                        else
                        {
                            Reject(context, node, name, "unsupported dictionary initializer");
                            return false;
                        }
                    }
                    else
                    {
                        restore.Append(name).Append(".Add(").Append(expression).AppendLine(");");
                    }
                }

                return true;
            }

            Reject(context, node, name, "readonly state must be immutable, a synchronization gate, or a List/Dictionary/HashSet initialized with new() and optional collection elements");
            return false;
        }

        private static bool IsStableReadonly(ITypeSymbol type)
        {
            if (type.TypeKind == TypeKind.Enum || (type.SpecialType != SpecialType.None && type.SpecialType != SpecialType.System_Object))
            {
                return true;
            }

            string name = type.ToDisplayString();
            return name == "System.DateTime" || name == "System.DateTimeOffset" || name == "System.TimeSpan" || name == "System.Guid" || name == "object" || name == "System.Object" || name == "System.Type" || name == "System.Threading.SemaphoreSlim" || name == "System.Threading.ReaderWriterLockSlim";
        }

        private static bool ReadsOtherSingleton(SemanticModel model, ExpressionSyntax initializer, INamedTypeSymbol owner)
        {
            foreach (var expression in initializer.DescendantNodesAndSelf().OfType<ExpressionSyntax>())
            {
                var symbol = model.GetSymbolInfo(expression).Symbol;
                if ((symbol is IFieldSymbol field && field.IsConst) || symbol == null || !symbol.IsStatic || !(symbol is IFieldSymbol || symbol is IPropertySymbol))
                {
                    continue;
                }

                var containing = symbol.ContainingType;
                if (containing != null && !SymbolEqualityComparer.Default.Equals(containing, owner) && (IsSingleton(containing) || IsBase(containing)))
                {
                    return true;
                }
            }

            return false;
        }

        private static string Wrap(ClassDeclarationSyntax declaration, string members)
        {
            var source = new StringBuilder("// <auto-generated/>\n#nullable enable\n");
            var root = (CompilationUnitSyntax)declaration.SyntaxTree.GetRoot();
            foreach (var item in root.Externs)
            {
                source.AppendLine(item.ToString());
            }

            foreach (var item in root.Usings)
            {
                source.AppendLine(item.ToString());
            }

            var containers = declaration.Ancestors().Reverse().ToArray();
            int braces = 0;
            foreach (var container in containers)
            {
                if (container is NamespaceDeclarationSyntax space)
                {
                    source.Append("namespace ").Append(space.Name).AppendLine("\n{");
                    braces++;
                    foreach (var item in space.Externs)
                    {
                        source.AppendLine(item.ToString());
                    }

                    foreach (var item in space.Usings)
                    {
                        source.AppendLine(item.ToString());
                    }
                }
                else if (container is TypeDeclarationSyntax type)
                {
                    AppendType(source, type);
                    braces++;
                }
            }

            AppendType(source, declaration);
            source.Append(members).AppendLine("}");
            for (int i = 0; i < braces; i++)
            {
                source.AppendLine("}");
            }

            var parsed = CSharpSyntaxTree.ParseText(source.ToString(), new CSharpParseOptions(LanguageVersion.CSharp9));
            return "#if UNITY_EDITOR\n" + parsed.GetRoot().NormalizeWhitespace("    ", "\n").ToFullString() + "\n#endif\n";
        }

        private static void AppendType(StringBuilder source, TypeDeclarationSyntax type)
        {
            if (type.Modifiers.Any(SyntaxKind.StaticKeyword))
            {
                source.Append("static ");
            }

            source.Append("partial ").Append(type.Keyword.Text).Append(' ').Append(type.Identifier.Text).Append(type.TypeParameterList).Append(' ')
                .Append(string.Join(" ", type.ConstraintClauses)).AppendLine("\n{");
        }

        private static DiagnosticDescriptor Rule(string id, string title, string message)
        {
            return new DiagnosticDescriptor(id, title, message, "Novorizon.StaticReset", DiagnosticSeverity.Error, true);
        }

        private static void Reject(GeneratorExecutionContext context, SyntaxNode node, string name, string reason)
        {
            Report(context, UnsupportedMember, node.GetLocation(), name, reason);
        }

        private static void Report(GeneratorExecutionContext context, DiagnosticDescriptor rule, Location location, params object[] arguments)
        {
            context.ReportDiagnostic(Diagnostic.Create(rule, location, arguments));
        }
    }

    internal static class MemberModifiers
    {
        internal static SyntaxTokenList Modifiers(this MemberDeclarationSyntax member)
        {
            switch (member)
            {
                case BaseFieldDeclarationSyntax field:
                    return field.Modifiers;
                case BaseMethodDeclarationSyntax method:
                    return method.Modifiers;
                case BasePropertyDeclarationSyntax property:
                    return property.Modifiers;
                default:
                    return default;
            }
        }
    }
}
