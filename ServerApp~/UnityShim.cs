using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

// Замена тех немногих частей Unity, которыми пользуется код сервера.
// JsonUtility повторяет правила Unity, чтобы файлы данных и ответы были те же:
//  - пишутся открытые поля и поля с [SerializeField], кроме [NonSerialized], static, const и readonly;
//  - поддерживаются числа, bool, string, enum (как число), [Serializable]-классы и структуры, List<T> и T[];
//  - Dictionary, DateTime и прочее Unity не пишет - и мы не пишем;
//  - null-строка пишется как "", null-список как [], null-объект как объект со значениями по умолчанию.
namespace UnityEngine
{
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SerializeField : Attribute { }

    public static class Debug
    {
        static readonly object sync = new object();

        static void Write(string text, ConsoleColor color)
        {
            lock (sync)
            {
                ConsoleColor old = Console.ForegroundColor;
                Console.ForegroundColor = color;
                Console.WriteLine(DateTime.Now.ToString("HH:mm:ss ") + text);
                Console.ForegroundColor = old;
            }
        }

        public static void Log(object message) { Write(message?.ToString(), ConsoleColor.Gray); }
        public static void LogWarning(object message) { Write(message?.ToString(), ConsoleColor.Yellow); }
        public static void LogError(object message) { Write(message?.ToString(), ConsoleColor.Red); }
    }

    public static class Mathf
    {
        public static int Max(int a, int b) { return a > b ? a : b; }
        public static float Max(float a, float b) { return a > b ? a : b; }
        public static int Min(int a, int b) { return a < b ? a : b; }
        public static float Min(float a, float b) { return a < b ? a : b; }
        public static int Abs(int a) { return a < 0 ? -a : a; }
        public static float Abs(float a) { return a < 0 ? -a : a; }
        public static int Clamp(int v, int min, int max) { return v < min ? min : v > max ? max : v; }
        public static float Clamp(float v, float min, float max) { return v < min ? min : v > max ? max : v; }
    }

    public static class JsonUtility
    {
        const int MaxDepth = 10;   // как в Unity: дальше вложенные null-объекты не разворачиваются

        // ---------- Запись ----------

        public static string ToJson(object obj) { return ToJson(obj, false); }

        public static string ToJson(object obj, bool prettyPrint)
        {
            if (obj == null) return "";
            var sb = new StringBuilder();
            WriteObject(sb, obj, obj.GetType(), prettyPrint, 0);
            return sb.ToString();
        }

        static readonly Dictionary<Type, FieldInfo[]> fieldCache = new Dictionary<Type, FieldInfo[]>();

        static FieldInfo[] Fields(Type t)
        {
            lock (fieldCache)
            {
                if (fieldCache.TryGetValue(t, out FieldInfo[] cached)) return cached;
                var chain = new List<Type>();
                for (Type x = t; x != null && x != typeof(object) && x != typeof(ValueType); x = x.BaseType) chain.Insert(0, x);
                var list = new List<FieldInfo>();
                foreach (Type x in chain)
                    foreach (FieldInfo f in x.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        if (f.IsInitOnly || f.IsLiteral || f.IsNotSerialized) continue;
                        if (!f.IsPublic && f.GetCustomAttribute<SerializeField>() == null) continue;
                        if (!Supported(f.FieldType)) continue;
                        list.Add(f);
                    }
                return fieldCache[t] = list.ToArray();
            }
        }

        static bool IsPrimitive(Type t)
        {
            return t == typeof(string) || t.IsEnum || t == typeof(bool) || t == typeof(int) || t == typeof(float)
                || t == typeof(double) || t == typeof(long) || t == typeof(short) || t == typeof(byte)
                || t == typeof(sbyte) || t == typeof(ushort) || t == typeof(uint) || t == typeof(ulong) || t == typeof(char);
        }

        static bool IsComposite(Type t)
        {
            return !t.IsPrimitive && !t.IsEnum && t != typeof(string) && !t.IsAbstract && !t.IsGenericType
                && (t.Attributes & TypeAttributes.Serializable) != 0;
        }

        static Type ElementType(Type t)
        {
            if (t.IsArray && t.GetArrayRank() == 1) return t.GetElementType();
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>)) return t.GetGenericArguments()[0];
            return null;
        }

        static bool Supported(Type t)
        {
            Type e = ElementType(t);
            if (e != null) return ElementType(e) == null && (IsPrimitive(e) || IsComposite(e));   // список списков Unity не пишет
            return IsPrimitive(t) || IsComposite(t);
        }

        static void Indent(StringBuilder sb, bool pretty, int level)
        {
            if (!pretty) return;
            sb.Append('\n');
            sb.Append(' ', level * 4);
        }

        static void WriteObject(StringBuilder sb, object obj, Type t, bool pretty, int depth)
        {
            if (obj == null) obj = depth <= MaxDepth ? Create(t) : null;
            sb.Append('{');
            bool first = true;
            if (obj != null)
                foreach (FieldInfo f in Fields(t))
                {
                    if (depth >= MaxDepth && IsComposite(f.FieldType) && f.GetValue(obj) == null) continue;
                    if (!first) sb.Append(',');
                    first = false;
                    Indent(sb, pretty, depth + 1);
                    WriteString(sb, f.Name);
                    sb.Append(pretty ? ": " : ":");
                    WriteValue(sb, f.GetValue(obj), f.FieldType, pretty, depth + 1);
                }
            if (!first) Indent(sb, pretty, depth);
            sb.Append('}');
        }

        static void WriteValue(StringBuilder sb, object v, Type t, bool pretty, int depth)
        {
            Type e = ElementType(t);
            if (e != null)
            {
                sb.Append('[');
                bool first = true;
                if (v != null)
                    foreach (object item in (IEnumerable)v)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        Indent(sb, pretty, depth + 1);
                        WriteValue(sb, item, e, pretty, depth + 1);
                    }
                if (!first) Indent(sb, pretty, depth);
                sb.Append(']');
                return;
            }
            if (t == typeof(string)) { WriteString(sb, (string)v ?? ""); return; }
            if (t == typeof(bool)) { sb.Append((bool)v ? "true" : "false"); return; }
            if (t.IsEnum) { sb.Append(Convert.ToInt64(v).ToString(CultureInfo.InvariantCulture)); return; }
            if (t == typeof(float)) { sb.Append(Number((float)v)); return; }
            if (t == typeof(double)) { sb.Append(Number((double)v)); return; }
            if (t == typeof(char)) { sb.Append(((int)(char)v).ToString(CultureInfo.InvariantCulture)); return; }
            if (IsPrimitive(t)) { sb.Append(Convert.ToString(v, CultureInfo.InvariantCulture)); return; }
            WriteObject(sb, v, t, pretty, depth);
        }

        static string Number(double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) return "0";
            if (d == Math.Floor(d) && Math.Abs(d) < 1e15) return ((long)d).ToString(CultureInfo.InvariantCulture) + ".0";
            return d.ToString("R", CultureInfo.InvariantCulture);
        }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            sb.Append('"');
        }

        static object Create(Type t)
        {
            return t.IsValueType ? Activator.CreateInstance(t) : Activator.CreateInstance(t, true);
        }

        // ---------- Чтение ----------

        public static T FromJson<T>(string json) { return (T)FromJson(json, typeof(T)); }

        public static object FromJson(string json, Type type)
        {
            if (string.IsNullOrEmpty(json)) return null;
            var p = new Parser(json);
            p.Skip();
            if (p.Peek() != '{') throw new ArgumentException("JSON parse error: объект не найден");
            object o = Create(type);
            o = p.ReadInto(o, type);
            return o;
        }

        sealed class Parser
        {
            readonly string s;
            int i;

            public Parser(string s) { this.s = s; }

            public char Peek() { return i < s.Length ? s[i] : '\0'; }

            public void Skip() { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

            void Expect(char c)
            {
                Skip();
                if (Peek() != c) throw new ArgumentException($"JSON parse error: ждали '{c}' в позиции {i}");
                i++;
            }

            public object ReadInto(object obj, Type t)
            {
                Expect('{');
                FieldInfo[] fields = Fields(t);
                Skip();
                if (Peek() == '}') { i++; return obj; }
                while (true)
                {
                    Skip();
                    string name = ReadString();
                    Expect(':');
                    FieldInfo f = Array.Find(fields, x => x.Name == name);
                    if (f == null) SkipValue();
                    else f.SetValue(obj, ReadValue(f.FieldType, f.GetValue(obj)));
                    Skip();
                    if (Peek() == ',') { i++; continue; }
                    Expect('}');
                    return obj;
                }
            }

            object ReadValue(Type t, object current)
            {
                Skip();
                char c = Peek();
                if (c == 'n' && s.Length - i >= 4 && string.CompareOrdinal(s, i, "null", 0, 4) == 0)
                {
                    i += 4;
                    return t.IsValueType ? Activator.CreateInstance(t) : (t == typeof(string) ? null : current);
                }
                Type e = ElementType(t);
                if (e != null)
                {
                    var items = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(e));
                    Expect('[');
                    Skip();
                    if (Peek() == ']') i++;
                    else
                        while (true)
                        {
                            items.Add(ReadValue(e, null));
                            Skip();
                            if (Peek() == ',') { i++; continue; }
                            Expect(']');
                            break;
                        }
                    if (!t.IsArray) return items;
                    Array arr = Array.CreateInstance(e, items.Count);
                    items.CopyTo(arr, 0);
                    return arr;
                }
                if (t == typeof(string)) return ReadString();
                if (t == typeof(bool))
                {
                    if (string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
                    if (string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
                    return Convert.ToDouble(ReadNumber(), CultureInfo.InvariantCulture) != 0;
                }
                if (IsPrimitive(t))
                {
                    string n = c == '"' ? ReadString() : ReadNumber();
                    double d = double.TryParse(n, NumberStyles.Float, CultureInfo.InvariantCulture, out double x) ? x : 0;
                    if (t == typeof(float)) return (float)d;
                    if (t == typeof(double)) return d;
                    if (t.IsEnum) return Enum.ToObject(t, (long)d);
                    if (t == typeof(char)) return (char)(long)d;
                    if (long.TryParse(n, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l)) return Convert.ChangeType(l, t, CultureInfo.InvariantCulture);
                    return Convert.ChangeType((long)d, t, CultureInfo.InvariantCulture);
                }
                return ReadInto(Create(t), t);
            }

            string ReadNumber()
            {
                Skip();
                int start = i;
                while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
                if (i == start) throw new ArgumentException($"JSON parse error: ждали число в позиции {i}");
                return s.Substring(start, i - start);
            }

            string ReadString()
            {
                Expect('"');
                var sb = new StringBuilder();
                while (true)
                {
                    if (i >= s.Length) throw new ArgumentException("JSON parse error: строка не закрыта");
                    char c = s[i++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    char x = s[i++];
                    switch (x)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u': sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber)); i += 4; break;
                        default: sb.Append(x); break;
                    }
                }
            }

            void SkipValue()
            {
                Skip();
                char c = Peek();
                if (c == '"') { ReadString(); return; }
                if (c == '{' || c == '[')
                {
                    char close = c == '{' ? '}' : ']';
                    i++;
                    Skip();
                    if (Peek() == close) { i++; return; }
                    while (true)
                    {
                        if (c == '{') { Skip(); ReadString(); Expect(':'); }
                        SkipValue();
                        Skip();
                        if (Peek() == ',') { i++; continue; }
                        Expect(close);
                        return;
                    }
                }
                while (i < s.Length && ",}] \t\r\n".IndexOf(s[i]) < 0) i++;
            }
        }
    }
}
