using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace Game.Ability.Configuration
{
    /// <summary>
    /// Strict JSON reader using the DTO's public fields as its single schema. Unknown/duplicate
    /// keys and invalid types are errors. Constructing DTOs preserves explicit authoring defaults
    /// for omitted fields without relying on Unity serializer initialization behavior.
    /// </summary>
    public static class AbilityJsonSchemaValidator
    {
        public static bool TryValidate(string json, Type documentType, out string error)
        {
            return TryRead(json, documentType, out _, out error);
        }

        public static bool TryDeserialize<T>(string json, out T document, out string error) where T : class
        {
            bool success = TryRead(json, typeof(T), out object value, out error);
            document = success ? (T)value : null;
            return success;
        }

        private static bool TryRead(string json, Type documentType, out object document, out string error)
        {
            try
            {
                if (json == null) throw new FormatException("Document is null.");
                document = new Reader(json).Document(documentType);
                error = null;
                return true;
            }
            catch (FormatException exception)
            {
                document = null;
                error = exception.Message;
                return false;
            }
        }

        private sealed class Reader
        {
            private readonly string source;
            private int position;
            private int depth;

            public Reader(string source) { this.source = source; }

            public object Document(Type type)
            {
                White();
                if (Peek() != '{') Fail("Document must be an object.");
                object result = Value(type, "$", false);
                White();
                if (position != source.Length) Fail("Unexpected trailing content.");
                return result;
            }

            private object Value(Type type, string path, bool nullable = true)
            {
                White();
                if (++depth > 64) Fail("Maximum nesting depth exceeded.");
                object result;
                if (Peek() == 'n')
                {
                    Literal("null");
                    if (!nullable || type.IsValueType) Fail(path + " cannot be null.");
                    result = null;
                }
                else if (type == typeof(string)) result = String();
                else if (type == typeof(bool))
                {
                    if (Peek() == 't') { Literal("true"); result = true; }
                    else if (Peek() == 'f') { Literal("false"); result = false; }
                    else { Fail(path + " must be a boolean."); result = null; }
                }
                else if (type == typeof(int) || type == typeof(float)) result = Number(type, path);
                else if (type.IsArray) result = Array(type.GetElementType(), path);
                else result = Object(type, path);
                depth--;
                return result;
            }

            private static readonly Dictionary<Type, Dictionary<string, FieldInfo>> schemas = new Dictionary<Type, Dictionary<string, FieldInfo>>();

            private static Dictionary<string, FieldInfo> Fields(Type type)
            {
                lock (schemas)
                {
                    if (schemas.TryGetValue(type, out Dictionary<string, FieldInfo> cached)) return cached;
                    Dictionary<string, FieldInfo> fields = new Dictionary<string, FieldInfo>(StringComparer.Ordinal);
                    foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance)) fields.Add(field.Name, field);
                    schemas.Add(type, fields);
                    return fields;
                }
            }

            private object Object(Type type, string path)
            {
                Expect('{');
                object result = Activator.CreateInstance(type);
                Dictionary<string, FieldInfo> fields = Fields(type);
                HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
                White();
                if (Take('}')) return result;
                while (true)
                {
                    White();
                    string name = String();
                    if (!fields.TryGetValue(name, out FieldInfo field)) Fail("Unknown field " + path + "." + name + ".");
                    if (!seen.Add(name)) Fail("Duplicate field " + path + "." + name + ".");
                    White(); Expect(':');
                    field.SetValue(result, Value(field.FieldType, path + "." + name));
                    White();
                    if (Take('}')) return result;
                    Expect(',');
                }
            }

            private object Array(Type elementType, string path)
            {
                Expect('['); White();
                List<object> values = new List<object>();
                if (!Take(']'))
                {
                    while (true)
                    {
                        values.Add(Value(elementType, path + "[" + values.Count + "]")); White();
                        if (Take(']')) break;
                        Expect(',');
                    }
                }
                System.Array result = System.Array.CreateInstance(elementType, values.Count);
                for (int i = 0; i < values.Count; i++) result.SetValue(values[i], i);
                return result;
            }

            private object Number(Type type, string path)
            {
                int start = position;
                Take('-');
                if (!Take('0'))
                {
                    if (Peek() < '1' || Peek() > '9') Fail(path + " must be a number.");
                    Digits();
                }
                if (Take('.'))
                {
                    if (!Digit(Peek())) Fail("Expected digit after decimal point.");
                    Digits();
                }
                if (Take('e') || Take('E'))
                {
                    if (!Take('+')) Take('-');
                    if (!Digit(Peek())) Fail("Expected exponent digits.");
                    Digits();
                }
                string token = source.Substring(start, position - start);
                if (type == typeof(int))
                {
                    if (!int.TryParse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int integer)) Fail(path + " must be a 32-bit integer.");
                    return integer;
                }
                if (!float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) || float.IsNaN(value) || float.IsInfinity(value)) Fail(path + " must be a finite float.");
                return value;
            }

            private string String()
            {
                Expect('"');
                StringBuilder result = new StringBuilder();
                while (position < source.Length)
                {
                    char current = source[position++];
                    if (current == '"') return result.ToString();
                    if (current < 0x20) Fail("Unescaped control character in string.");
                    if (current == '\\')
                    {
                        if (position >= source.Length) Fail("Incomplete string escape.");
                        current = source[position++];
                        switch (current)
                        {
                            case '"': case '\\': case '/': result.Append(current); break;
                            case 'b': result.Append('\b'); break;
                            case 'f': result.Append('\f'); break;
                            case 'n': result.Append('\n'); break;
                            case 'r': result.Append('\r'); break;
                            case 't': result.Append('\t'); break;
                            case 'u': result.Append(Unicode()); break;
                            default: Fail("Invalid string escape."); break;
                        }
                    }
                    else result.Append(current);
                }
                Fail("Unterminated string.");
                return null;
            }

            private char Unicode()
            {
                int value = 0;
                for (int i = 0; i < 4; i++)
                {
                    if (position >= source.Length) Fail("Incomplete Unicode escape.");
                    char c = source[position++];
                    int digit = c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 : c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1;
                    if (digit < 0) Fail("Invalid Unicode escape.");
                    value = value * 16 + digit;
                }
                return (char)value;
            }

            private void Literal(string text)
            {
                for (int i = 0; i < text.Length; i++) Expect(text[i]);
            }

            private void Digits() { while (Digit(Peek())) position++; }
            private static bool Digit(char c) => c >= '0' && c <= '9';
            private char Peek() => position < source.Length ? source[position] : '\0';
            private bool Take(char c) { if (Peek() != c) return false; position++; return true; }
            private void Expect(char c) { if (!Take(c)) Fail("Expected '" + c + "'."); }
            private void White() { while (Peek() == ' ' || Peek() == '\t' || Peek() == '\r' || Peek() == '\n') position++; }
            private void Fail(string message) { throw new FormatException(message + " (offset " + position + ")"); }
        }
    }
}
