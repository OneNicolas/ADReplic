using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using ADReplic.Core.Abstractions;
using ADReplic.Core.Models;

namespace ADReplic.Core.Export
{
    /// <summary>
    /// Exporte le snapshot en JSON UTF-8 indenté.
    /// Implémentation maison pour éviter une dépendance NuGet sur la clé USB.
    /// Suffit largement pour nos modèles (POCO, primitives, DateTime, énums, IEnumerable).
    /// </summary>
    public sealed class JsonAuditExporter : IAuditExporter
    {
        public string Format => "JSON";
        public string DefaultFileExtension => ".json";

        public void Export(AuditSnapshot snapshot, string targetPath)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (string.IsNullOrWhiteSpace(targetPath)) throw new ArgumentException("Chemin requis.", nameof(targetPath));

            var sb = new StringBuilder(8192);
            WriteValue(sb, snapshot, indent: 0);
            File.WriteAllText(targetPath, sb.ToString(), new UTF8Encoding(false));
        }

        private static void WriteValue(StringBuilder sb, object value, int indent)
        {
            if (value == null) { sb.Append("null"); return; }

            switch (value)
            {
                case string s:           WriteString(sb, s); return;
                case bool b:             sb.Append(b ? "true" : "false"); return;
                case DateTime dt:        WriteString(sb, dt.ToString("o", CultureInfo.InvariantCulture)); return;
                case TimeSpan ts:        WriteString(sb, ts.ToString()); return;
                case Enum e:             WriteString(sb, e.ToString()); return;
                case IFormattable f when IsNumeric(value):
                                         sb.Append(f.ToString(null, CultureInfo.InvariantCulture)); return;
            }

            if (value is IEnumerable list && !(value is string))
            {
                WriteArray(sb, list, indent);
                return;
            }

            WriteObject(sb, value, indent);
        }

        private static void WriteArray(StringBuilder sb, IEnumerable items, int indent)
        {
            var first = true;
            sb.Append('[');
            foreach (var item in items)
            {
                if (!first) sb.Append(',');
                AppendNewline(sb, indent + 1);
                WriteValue(sb, item, indent + 1);
                first = false;
            }
            if (!first) AppendNewline(sb, indent);
            sb.Append(']');
        }

        private static void WriteObject(StringBuilder sb, object obj, int indent)
        {
            sb.Append('{');
            var props = obj.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
                .ToArray();

            for (int i = 0; i < props.Length; i++)
            {
                if (i > 0) sb.Append(',');
                AppendNewline(sb, indent + 1);
                WriteString(sb, ToCamelCase(props[i].Name));
                sb.Append(": ");
                object propValue;
                try { propValue = props[i].GetValue(obj, null); }
                catch { propValue = null; }
                WriteValue(sb, propValue, indent + 1);
            }

            if (props.Length > 0) AppendNewline(sb, indent);
            sb.Append('}');
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"':  sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.AppendFormat("\\u{0:x4}", (int)c);
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        private static void AppendNewline(StringBuilder sb, int indent)
        {
            sb.Append('\n');
            for (int i = 0; i < indent; i++) sb.Append("  ");
        }

        private static bool IsNumeric(object o)
        {
            switch (Type.GetTypeCode(o.GetType()))
            {
                case TypeCode.Byte:
                case TypeCode.SByte:
                case TypeCode.Int16:
                case TypeCode.UInt16:
                case TypeCode.Int32:
                case TypeCode.UInt32:
                case TypeCode.Int64:
                case TypeCode.UInt64:
                case TypeCode.Single:
                case TypeCode.Double:
                case TypeCode.Decimal:
                    return true;
                default:
                    return false;
            }
        }

        private static string ToCamelCase(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            if (char.IsLower(s[0])) return s;
            return char.ToLowerInvariant(s[0]) + s.Substring(1);
        }
    }
}
