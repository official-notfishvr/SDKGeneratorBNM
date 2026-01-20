using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Mono.Cecil;

namespace SDKGeneratorBNM
{
    public static class Utils
    {
        public static readonly HashSet<string> ReservedTypeNames = new HashSet<string> { "RenderMode", "Method", "IEnumerator", "IEnumerable", "ICollection", "IList", "IDictionary", "IComparer", "IEqualityComparer", "StringComparison", "BehaviorRegisterDelegate", "BoingReactorField" };

        private static readonly Dictionary<string, string> PrimitiveTypeMappings = new Dictionary<string, string>
        {
            { "System.Void", "void" },
            { "System.Int8", "int8_t" },
            { "System.UInt8", "uint8_t" },
            { "System.Int16", "short" },
            { "System.Int32", "int" },
            { "System.Int64", "int64_t" },
            { "System.Single", "float" },
            { "System.Double", "double" },
            { "System.Boolean", "bool" },
            { "System.Char", "char" },
            { "System.UInt16", "BNM::Types::ushort" },
            { "System.UInt32", "BNM::Types::uint" },
            { "System.UInt64", "BNM::Types::ulong" },
            { "System.Decimal", "BNM::Types::decimal" },
            { "System.Byte", "BNM::Types::byte" },
            { "System.SByte", "BNM::Types::sbyte" },
            { "System.String", "BNM::Structures::Mono::String*" },
            { "System.Type", "BNM::MonoType*" },
            { "System.IntPtr", "BNM::Types::nuint" },
            { "UnityEngine.Object", "BNM::UnityEngine::Object*" },
            { "UnityEngine.MonoBehaviour", "BNM::UnityEngine::MonoBehaviour*" },
            { "UnityEngine.Vector2", "BNM::Structures::Unity::Vector2" },
            { "UnityEngine.Vector3", "BNM::Structures::Unity::Vector3" },
            { "UnityEngine.Vector4", "BNM::Structures::Unity::Vector4" },
            { "UnityEngine.Quaternion", "BNM::Structures::Unity::Quaternion" },
            { "UnityEngine.Rect", "BNM::Structures::Unity::Rect" },
            { "UnityEngine.Color", "BNM::Structures::Unity::Color" },
            { "UnityEngine.Color32", "BNM::Structures::Unity::Color32" },
            { "UnityEngine.Ray", "BNM::Structures::Unity::Ray" },
            { "UnityEngine.RaycastHit", "BNM::Structures::Unity::RaycastHit" },
        };

        private static readonly HashSet<string> CppKeywords = new HashSet<string>
        {
            "alignas",
            "alignof",
            "and",
            "and_eq",
            "asm",
            "atomic_cancel",
            "atomic_commit",
            "atomic_noexcept",
            "auto",
            "bitand",
            "bitor",
            "bool",
            "break",
            "case",
            "catch",
            "char",
            "char8_t",
            "char16_t",
            "char32_t",
            "class",
            "compl",
            "concept",
            "const",
            "consteval",
            "constexpr",
            "constinit",
            "const_cast",
            "continue",
            "contract_assert",
            "co_await",
            "co_return",
            "co_yield",
            "decltype",
            "default",
            "delete",
            "do",
            "double",
            "dynamic_cast",
            "else",
            "enum",
            "explicit",
            "export",
            "extern",
            "false",
            "float",
            "for",
            "friend",
            "goto",
            "if",
            "inline",
            "int",
            "long",
            "mutable",
            "namespace",
            "new",
            "noexcept",
            "not",
            "not_eq",
            "nullptr",
            "operator",
            "or",
            "or_eq",
            "public",
            "protected",
            "reflexpr",
            "register",
            "reinterpret_cast",
            "requires",
            "return",
            "short",
            "signed",
            "sizeof",
            "static",
            "static_assert",
            "static_cast",
            "struct",
            "switch",
            "synchronized",
            "template",
            "this",
            "thread_local",
            "throw",
            "true",
            "try",
            "typedef",
            "typeid",
            "typename",
            "union",
            "unsigned",
            "using",
            "virtual",
            "void",
            "volatile",
            "wchar_t",
            "while",
            "xor",
            "xor_eq",
            "abstract",
            "add",
            "as",
            "base",
            "byte",
            "checked",
            "decimal",
            "delegate",
            "event",
            "finally",
            "fixed",
            "foreach",
            "implicit",
            "in",
            "interface",
            "internal",
            "is",
            "lock",
            "null",
            "object",
            "out",
            "override",
            "params",
            "readonly",
            "ref",
            "remove",
            "sbyte",
            "sealed",
            "stackalloc",
            "string",
            "typeof",
            "uint",
            "ulong",
            "unchecked",
            "unsafe",
            "ushort",
            "value",
            "when",
            "where",
            "yield",
            "INT32_MAX",
            "INT32_MIN",
            "UINT32_MAX",
            "UINT16_MAX",
            "INT16_MAX",
            "UINT8_MAX",
            "INT8_MAX",
            "INT_MAX",
            "Assert",
            "NULL",
            "O",
        };

        public static string CleanTypeName(string typeName)
        {
            return typeName.Contains("`") ? typeName.Split('`')[0] : typeName;
        }

        public static string GetBaseClass(TypeDefinition type)
        {
            var baseType = type.BaseType;
            if (baseType == null || baseType.FullName == "System.Object")
                return string.Empty;

            string baseTypeName = CleanTypeName(baseType.Name);

            if (baseType.Namespace != null && baseType.Namespace.StartsWith("UnityEngine"))
            {
                if (baseTypeName == "MonoBehaviour" || baseTypeName == "Object" || baseTypeName == "Component")
                {
                    return $" : BNM::UnityEngine::{baseTypeName}";
                }
                if (baseTypeName == "Behaviour")
                {
                    return " : BNM::UnityEngine::MonoBehaviour";
                }
                return " : BNM::UnityEngine::Object";
            }

            if (Program.DefinedTypes.Contains(baseTypeName))
            {
                string ns = baseType.Namespace ?? string.Empty;
                return string.IsNullOrEmpty(ns) ? $" : GlobalNamespace::{baseTypeName}" : $" : {ns.Replace(".", "::")}::{baseTypeName}";
            }

            return string.Empty;
        }

        public static string GetCppType(TypeReference typeRef, TypeDefinition context = null)
        {
            try
            {
                if (typeRef == null)
                    return "BNM::IL2CPP::Il2CppObject*";

                if (typeRef.IsPointer)
                    return "void* /*POINTER*/";

                if (typeRef is GenericInstanceType)
                    return "void* /*GENERICTYPE*/";

                if (IsNullableType(typeRef))
                    typeRef = GetNullableUnderlyingType(typeRef);

                string fullName = typeRef.FullName;

                if (PrimitiveTypeMappings.TryGetValue(fullName, out var mappedType))
                    return mappedType;

                if (typeRef.Namespace != null && typeRef.Namespace.StartsWith("UnityEngine"))
                {
                    string name = CleanTypeName(typeRef.Name);
                    if (name == "GameObject" || name == "Transform")
                        return "void* /*UNITYENGINE_TYPE*/";

                    if (name == "MonoBehaviour" || name == "Object" || name == "Component" || name == "Behaviour")
                    {
                        if (name == "Behaviour")
                            name = "MonoBehaviour";
                        return $"BNM::UnityEngine::{name}*";
                    }
                    return "BNM::IL2CPP::Il2CppObject*";
                }

                if (typeRef is ArrayType arrType)
                {
                    string elementCppType = GetCppType(arrType.ElementType, context);
                    if (elementCppType.Contains("void*"))
                        return "void* /*ARRAY*/";
                    return $"BNM::Structures::Mono::Array<{elementCppType}>*";
                }

                string customTypeName = CleanTypeName(typeRef.Name);
                if (Program.DefinedTypes.Contains(customTypeName))
                {
                    string ns = typeRef.Namespace ?? string.Empty;
                    string prefix = string.IsNullOrEmpty(ns) ? "GlobalNamespace" : ns.Replace(".", "::");
                    bool isValueType = typeRef.IsValueType;

                    if (isValueType)
                        return "void* /*VALUETYPE*/";

                    return $"{prefix}::{customTypeName}*";
                }

                return "BNM::IL2CPP::Il2CppObject*";
            }
            catch
            {
                return "void* /*ERROR*/";
            }
        }

        public static string ToPascalCase(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;
            var parts = input.Split('_');
            var result = new StringBuilder();
            foreach (var part in parts)
            {
                if (!string.IsNullOrEmpty(part))
                {
                    result.Append(char.ToUpper(part[0]));
                    if (part.Length > 1)
                        result.Append(part.Substring(1));
                }
            }
            return result.ToString();
        }

        public static string ToCamelCase(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;
            string pascalCase = ToPascalCase(input);
            return pascalCase.Length > 0 ? char.ToLower(pascalCase[0]) + pascalCase.Substring(1) : pascalCase;
        }

        public static bool IsKeyword(string str) => CppKeywords.Contains(str);

        public static string[] MakeValidParams(string[] paramNames)
        {
            var results = new List<string>();
            var seen = new Dictionary<string, int>();
            foreach (var param in paramNames)
            {
                string cparam = param;
                if (IsKeyword(cparam))
                    cparam = $"_{cparam}";
                if (seen.ContainsKey(cparam))
                {
                    seen[cparam]++;
                    cparam = $"{cparam}_{seen[cparam]}";
                }
                else
                    seen[cparam] = 0;
                results.Add(cparam);
            }
            return results.ToArray();
        }

        public static string FormatInvalidName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "_";
            string str = name.Trim().Replace("<", "$").Replace(">", "$").Replace("|", "$").Replace("-", "$").Replace("`", "$").Replace("=", "$").Replace("@", "$").Replace(".", "_").Replace(":", "_").Replace(" ", "_");
            if (char.IsDigit(str[0]))
                str = "_" + str;
            if (IsKeyword(str))
                str = "$" + str;
            return str;
        }

        public static string FormatTypeNameForStruct(string typeName, string namespaceName)
        {
            if (typeName.Contains("."))
                typeName = typeName.Split('.').Last();
            string cleanName = CleanTypeName(typeName);
            return FormatInvalidName(cleanName);
        }

        public static string GetEnumUnderlyingType(TypeDefinition enumType)
        {
            var field = enumType.Fields.FirstOrDefault(f => f.Name == "value__");
            return field != null ? GetCppType(field.FieldType) : "int";
        }

        public static bool IsNullableType(TypeReference typeRef)
        {
            return typeRef is GenericInstanceType gt && gt.FullName.Contains("System.Nullable");
        }

        public static TypeReference GetNullableUnderlyingType(TypeReference typeRef)
        {
            return (typeRef is GenericInstanceType gt && gt.GenericArguments.Count > 0) ? gt.GenericArguments[0] : typeRef;
        }
    }
}
