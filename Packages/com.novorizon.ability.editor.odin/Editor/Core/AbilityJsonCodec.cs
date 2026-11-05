using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Game.Ability.Configuration;

namespace Game.Ability.Editor.Core
{
    /// <summary>
    /// Uses the production reader and writes only the public authoring DTO schema. Null fields
    /// remain absent; an explicit empty level value remains an object, including for duration.
    /// Runtime definitions, editor state and Odin metadata never enter the saved document.
    /// </summary>
    public static class AbilityJsonCodec
    {
        private const int MaximumDepth = 64;
        private static readonly Dictionary<Type, FieldInfo[]> FieldsByType = new Dictionary<Type, FieldInfo[]>();
        private static readonly HashSet<Type> DocumentTypes = new HashSet<Type>
        {
            typeof(JsonAbilityDocument), typeof(JsonAbility), typeof(JsonModifier), typeof(JsonProjectile),
            typeof(JsonAction), typeof(JsonLevelValue), typeof(JsonCharge), typeof(JsonSpecialValue),
            typeof(JsonModifierProperty), typeof(JsonModifierLevelProperty)
        };

        public static bool TryRead(string json, out JsonAbilityDocument document, out string error)
        {
            if (!AbilityJsonSchemaValidator.TryDeserialize(json, out document, out error)) return false;
            if (document.schemaVersion == 1) return true;
            error = "Unsupported $.schemaVersion " + document.schemaVersion + "; this editor supports version 1. Open the original text without overwriting it.";
            document = null;
            return false;
        }

        public static string Write(JsonAbilityDocument document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (document.schemaVersion != 1) throw new ArgumentException("Only schemaVersion 1 can be exported by this editor.", nameof(document));
            StringBuilder output = new StringBuilder();
            WriteValue(output, document, typeof(JsonAbilityDocument), 0, "$");
            output.Append('\n');
            return output.ToString();
        }

        public static string Hash(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            using (SHA256 algorithm = SHA256.Create())
            {
                byte[] bytes = algorithm.ComputeHash(new UTF8Encoding(false, true).GetBytes(text));
                StringBuilder hash = new StringBuilder(bytes.Length * 2);
                for (int i = 0; i < bytes.Length; i++) hash.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
                return hash.ToString();
            }
        }

        public static JsonAbilityDocument Clone(JsonAbilityDocument document)
        {
            if (!TryRead(Write(document), out JsonAbilityDocument copy, out string error))
                throw new InvalidOperationException("An exported authoring document could not be read: " + error);
            return copy;
        }

        private static void WriteValue(StringBuilder output, object value, Type type, int depth, string path)
        {
            if (depth > MaximumDepth) throw new ArgumentException("Maximum JSON nesting depth exceeded at " + path + ".");
            if (value == null) { output.Append("null"); return; }
            if (type == typeof(string)) { WriteString(output, (string)value); return; }
            if (type == typeof(bool)) { output.Append((bool)value ? "true" : "false"); return; }
            if (type == typeof(int)) { output.Append(((int)value).ToString(CultureInfo.InvariantCulture)); return; }
            if (type == typeof(float))
            {
                float number = (float)value;
                if (float.IsNaN(number) || float.IsInfinity(number)) throw new ArgumentException("A finite number is required at " + path + ".");
                output.Append(number.ToString("R", CultureInfo.InvariantCulture));
                return;
            }
            if (type.IsArray)
            {
                WriteArray(output, (Array)value, type.GetElementType(), depth, path);
                return;
            }
            if (!DocumentTypes.Contains(type)) throw new ArgumentException("Unsupported authoring schema type " + type.FullName + " at " + path + ".");
            FieldInfo[] fields = Fields(type);
            output.Append('{');
            bool first = true;
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo field = fields[i];
                object fieldValue = field.GetValue(value);
                if (fieldValue == null) continue;
                output.Append(first ? "\n" : ",\n");
                first = false;
                Indent(output, depth + 1);
                WriteString(output, field.Name);
                output.Append(": ");
                WriteValue(output, fieldValue, field.FieldType, depth + 1, path + "." + field.Name);
            }
            if (!first) { output.Append('\n'); Indent(output, depth); }
            output.Append('}');
        }

        private static void WriteArray(StringBuilder output, Array values, Type elementType, int depth, string path)
        {
            output.Append('[');
            bool inline = elementType == typeof(float) || elementType == typeof(int) || elementType == typeof(bool) || elementType == typeof(string);
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0) output.Append(inline ? ", " : ",\n");
                else if (!inline) output.Append('\n');
                if (!inline) Indent(output, depth + 1);
                WriteValue(output, values.GetValue(i), elementType, depth + 1, path + "[" + i + "]");
            }
            if (values.Length > 0 && !inline) { output.Append('\n'); Indent(output, depth); }
            output.Append(']');
        }

        private static FieldInfo[] Fields(Type type)
        {
            lock (FieldsByType)
            {
                if (FieldsByType.TryGetValue(type, out FieldInfo[] cached)) return cached;
                FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
                // Metadata declaration order gives schemaVersion/name/identity precedence while
                // remaining independent of reflection enumeration and current culture.
                Array.Sort(fields, (left, right) => left.MetadataToken.CompareTo(right.MetadataToken));
                FieldsByType.Add(type, fields);
                return fields;
            }
        }

        private static void Indent(StringBuilder output, int depth)
        {
            output.Append(' ', depth * 2);
        }

        private static void WriteString(StringBuilder output, string value)
        {
            output.Append('"');
            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                switch (character)
                {
                    case '"': output.Append("\\\""); break;
                    case '\\': output.Append("\\\\"); break;
                    case '\b': output.Append("\\b"); break;
                    case '\f': output.Append("\\f"); break;
                    case '\n': output.Append("\\n"); break;
                    case '\r': output.Append("\\r"); break;
                    case '\t': output.Append("\\t"); break;
                    default:
                        // Escape UTF-16 surrogate units too, preserving pairs and preventing an
                        // unpaired edit from being silently replaced by the UTF-8 file encoder.
                        if (character < 0x20 || char.IsSurrogate(character) || character == '\u2028' || character == '\u2029')
                            output.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                        else output.Append(character);
                        break;
                }
            }
            output.Append('"');
        }
    }
}
