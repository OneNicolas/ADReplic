using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;
using ADReplic.Core.Abstractions;
using ADReplic.Core.Models;

namespace ADReplic.Core.Export
{
    /// <summary>
    /// Désérialise un fichier JSON produit par <see cref="JsonAuditExporter"/>
    /// vers un <see cref="AuditSnapshot"/>. Utilise <see cref="JavaScriptSerializer"/>
    /// du framework natif pour le parsing brut, puis mappe vers les modèles via
    /// réflexion (insensible à la casse, supporte camelCase et PascalCase).
    /// </summary>
    /// <remarks>
    /// Le mapping suit volontairement la même philosophie que l'exporter :
    /// propriétés publiques avec setter, conversions de DateTime ISO 8601, de
    /// TimeSpan via parse, et d'énumérations par nom (compatible avec l'écriture
    /// par défaut de l'exporter qui sérialise les enums comme strings).
    /// </remarks>
    public sealed class JsonAuditReader : IJsonAuditReader
    {
        public AuditSnapshot Read(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Chemin requis.", nameof(path));
            if (!File.Exists(path))
                throw new FileNotFoundException("Fichier de snapshot introuvable.", path);

            var json = File.ReadAllText(path, Encoding.UTF8);
            return ReadFromString(json);
        }

        /// <summary>Variante exposée pour les tests : parse une chaîne JSON directement.</summary>
        public AuditSnapshot ReadFromString(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new ArgumentException("JSON vide.", nameof(json));

            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var raw = serializer.DeserializeObject(json);
            var converted = Convert(raw, typeof(AuditSnapshot));
            return (AuditSnapshot)converted;
        }

        // === Cœur du mapping : object brut -> instance typée =====================

        private static object Convert(object value, Type targetType)
        {
            if (value == null) return null;

            // Nullable<T> : on déballe puis on convertit vers le type sous-jacent.
            var nullableUnderlying = Nullable.GetUnderlyingType(targetType);
            if (nullableUnderlying != null)
            {
                return Convert(value, nullableUnderlying);
            }

            // Primitives "scalaires" courantes
            if (targetType == typeof(string)) return value.ToString();
            if (targetType == typeof(bool)) return System.Convert.ToBoolean(value);

            // IMPORTANT : les enums doivent être traités AVANT IsNumeric.
            // Type.GetTypeCode(typeof(MyEnum)) retourne le TypeCode de l'underlying
            // (Int32), donc IsNumeric serait vrai et tenterait un ChangeType qui
            // échoue sur une chaîne ("Failing" -> Int32 impossible).
            if (targetType.IsEnum)
            {
                // L'exporter écrit les enums sous forme de string par défaut.
                // On accepte aussi les entiers au cas où.
                if (value is string)
                {
                    return Enum.Parse(targetType, value.ToString(), ignoreCase: true);
                }
                return Enum.ToObject(targetType, System.Convert.ToInt64(value));
            }

            if (IsNumeric(targetType))
            {
                return System.Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
            }

            if (targetType == typeof(DateTime))
            {
                return DateTime.Parse(
                    value.ToString(), CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind);
            }

            if (targetType == typeof(TimeSpan))
            {
                return TimeSpan.Parse(value.ToString(), CultureInfo.InvariantCulture);
            }

            // Listes / tableaux
            if (value is IList list && !(value is string))
            {
                return ConvertList(list, targetType);
            }

            // Objets imbriqués
            if (value is IDictionary dict)
            {
                return ConvertObject(dict, targetType);
            }

            throw new InvalidOperationException(
                $"Impossible de convertir une valeur de type {value.GetType()} vers {targetType}.");
        }

        private static object ConvertList(IList input, Type targetType)
        {
            var elementType = GetElementType(targetType);
            if (elementType == null)
            {
                throw new InvalidOperationException(
                    $"Type de collection non supporte : {targetType}.");
            }

            var listType = typeof(List<>).MakeGenericType(elementType);
            var typedList = (IList)Activator.CreateInstance(listType);

            foreach (var item in input)
            {
                var converted = Convert(item, elementType);
                typedList.Add(converted);
            }

            if (targetType.IsArray)
            {
                var array = Array.CreateInstance(elementType, typedList.Count);
                typedList.CopyTo(array, 0);
                return array;
            }

            return typedList;
        }

        private static object ConvertObject(IDictionary input, Type targetType)
        {
            // Cas particulier : si le type cible est trop générique pour qu'on l'instancie,
            // on retourne le dictionnaire brut (peu probable sur nos modèles).
            if (targetType == typeof(object))
            {
                return input;
            }

            var instance = Activator.CreateInstance(targetType);
            var propsByName = targetType
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanWrite && p.GetIndexParameters().Length == 0)
                .ToDictionary(p => p.Name, p => p, StringComparer.OrdinalIgnoreCase);

            foreach (DictionaryEntry kv in input)
            {
                var key = kv.Key?.ToString();
                if (string.IsNullOrEmpty(key)) continue;
                if (!propsByName.TryGetValue(key, out var prop)) continue;

                try
                {
                    var converted = Convert(kv.Value, prop.PropertyType);
                    if (converted == null
                        && prop.PropertyType.IsValueType
                        && Nullable.GetUnderlyingType(prop.PropertyType) == null)
                    {
                        // Type valeur non-nullable et valeur null dans le JSON : on garde le default CLR.
                        continue;
                    }
                    prop.SetValue(instance, converted, null);
                }
                catch
                {
                    // Forward-compatibility : un champ inconnu ou impossible à mapper
                    // n'arrête pas la désérialisation. On préfère un AuditSnapshot
                    // partiel à un échec complet.
                }
            }

            return instance;
        }

        // === Helpers ============================================================

        private static Type GetElementType(Type collectionType)
        {
            if (collectionType.IsArray) return collectionType.GetElementType();
            if (!collectionType.IsGenericType) return null;
            var args = collectionType.GetGenericArguments();
            return args.Length == 1 ? args[0] : null;
        }

        private static bool IsNumeric(Type type)
        {
            switch (Type.GetTypeCode(type))
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
    }
}
