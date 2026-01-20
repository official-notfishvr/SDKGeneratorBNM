using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Mono.Cecil;

namespace BNMGameStructureGenerator
{
    internal class Program
    {
        public static HashSet<string> DefinedTypes = new HashSet<string>();
        private const string DllPath = "./Files/Assembly-CSharp.dll";
        private const string OutputDir = "BNMResolves";
        private const string OutputExtension = ".hpp";

        private static readonly string[] ExcludedTypePatterns = { "System.Collections", "IEnumerator", "IEnumerable", "ICollection", "IList", "IDictionary" };

        static void Main(string[] args)
        {
            bool singleFileMode = args.Contains("--single-file") || args.Contains("-s");
            Directory.CreateDirectory("./Files");
            Directory.CreateDirectory($"./{OutputDir}");

            if (!File.Exists(DllPath))
            {
                Console.WriteLine($"Error: {DllPath} not found.");
                WaitForExit();
                return;
            }
            try
            {
                var types = LoadTypes();
                if (types.Count == 0)
                {
                    WaitForExit();
                    return;
                }
                ProcessTypes(types, singleFileMode);
            }
            catch (Exception ex)
            {
                LogError(ex);
            }
            WaitForExit();
        }

        private static List<TypeDefinition> LoadTypes()
        {
            var allTypes = new List<TypeDefinition>();
            try
            {
                var module = ModuleDefinition.ReadModule(DllPath);
                var types = module.Types.ToList();
                foreach (var type in types)
                    allTypes.AddRange(type.NestedTypes);
                allTypes.AddRange(types);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                return allTypes;
            }
            return allTypes.Where(t => t != null && IsValidType(t)).Distinct(new TypeDefComparer()).ToList();
        }

        private static bool IsValidType(TypeDefinition type)
        {
            if (type.IsInterface)
                return false;
            string fullName = type.FullName;
            string typeName = type.Name;
            return !ExcludedTypePatterns.Any(p => fullName.Contains(p) || typeName.Contains(p));
        }

        private static void ProcessTypes(List<TypeDefinition> types, bool singleFileMode)
        {
            if (!singleFileMode && Directory.Exists(OutputDir))
                Directory.Delete(OutputDir, true);
            Directory.CreateDirectory(OutputDir);

            foreach (var type in types)
                DefinedTypes.Add(Utils.CleanTypeName(type.Name));

            var grouped = types.GroupBy(t => string.IsNullOrEmpty(t.Namespace) ? "GlobalNamespace" : t.Namespace).OrderBy(g => g.Key).ToArray();

            var warnings = new List<string>();

            if (singleFileMode)
                GenerateSingleFile(types, grouped, warnings);
            else
                GenerateMultipleFiles(grouped, warnings);
        }

        private static void GenerateSingleFile(List<TypeDefinition> types, IGrouping<string, TypeDefinition>[] grouped, List<string> warnings)
        {
            var sb = new StringBuilder();
            sb.AppendLine("#pragma once");
            sb.AppendLine("#include <BNMIncludes.hpp>");
            sb.AppendLine();

            var generatedEnums = new HashSet<string>();
            var generatedTypes = new HashSet<string>();

            foreach (var group in grouped)
                WriteForwardDeclarations(sb, group, generatedEnums, generatedTypes);

            generatedEnums.Clear();
            generatedTypes.Clear();

            foreach (var group in grouped)
            {
                bool isGlobal = group.Key == "GlobalNamespace";
                sb.AppendLine(isGlobal ? "namespace GlobalNamespace {" : $"namespace {group.Key.Replace(".", "::")} {{");
                foreach (var type in group.OrderBy(t => t.Name))
                {
                    string cleanName = Utils.CleanTypeName(type.Name);
                    if (Utils.ReservedTypeNames.Contains(cleanName) || generatedTypes.Contains(cleanName))
                        continue;
                    generatedTypes.Add(cleanName);
                    if (type.IsEnum)
                    {
                        string enumName = Utils.FormatTypeNameForStruct(type.Name, type.Namespace);
                        if (!generatedEnums.Contains(enumName))
                        {
                            generatedEnums.Add(enumName);
                            GenerateEnum(type, sb, isGlobal, warnings);
                        }
                    }
                    else
                        GenerateClass(type, sb, isGlobal, warnings);
                }
                sb.AppendLine("}");
                sb.AppendLine();
            }
            File.WriteAllText(Path.Combine(OutputDir, $"BNMResolves{OutputExtension}"), sb.ToString().Replace("StringComparison", "int"));
        }

        private static void GenerateMultipleFiles(IGrouping<string, TypeDefinition>[] grouped, List<string> warnings)
        {
            var generatedTypes = new HashSet<string>();

            foreach (var group in grouped)
            {
                string nsDir = group.Key == "GlobalNamespace" ? Path.Combine(OutputDir, "GlobalNamespace") : Path.Combine(OutputDir, group.Key.Replace(".", "/"));
                Directory.CreateDirectory(nsDir);
                var generatedEnums = new HashSet<string>();

                foreach (var type in group.OrderBy(t => t.Name))
                {
                    string cleanName = Utils.CleanTypeName(type.Name);
                    if (Utils.ReservedTypeNames.Contains(cleanName) || generatedTypes.Contains(cleanName))
                        continue;
                    generatedTypes.Add(cleanName);
                    string typeNamespace = string.IsNullOrEmpty(type.Namespace) ? "GlobalNamespace" : type.Namespace;
                    var requiredIncludes = new HashSet<string>();

                    if (type.BaseType != null && type.BaseType.FullName != "System.Object")
                    {
                        string baseTypeName = Utils.CleanTypeName(type.BaseType.Name);
                        if (Program.DefinedTypes.Contains(baseTypeName))
                        {
                            string baseNs = string.IsNullOrEmpty(type.BaseType.Namespace) ? "GlobalNamespace" : type.BaseType.Namespace;
                            string baseName = Utils.FormatTypeNameForStruct(type.BaseType.Name, baseNs);

                            int depth = typeNamespace == "GlobalNamespace" ? 1 : typeNamespace.Split('.').Length;
                            string prefix = "";
                            for (int i = 0; i < depth; i++)
                                prefix += "../";

                            string baseInclude = baseNs == "GlobalNamespace"
                                ? $"#include \"{prefix}GlobalNamespace/{baseName}{OutputExtension}\""
                                : $"#include \"{prefix}{baseNs.Replace(".", "/")}/{baseName}{OutputExtension}\"";

                            requiredIncludes.Add(baseInclude);
                        }
                        else if (IsGlobalNamespace(type.BaseType))
                        {
                            string inc = GetRelativeIncludePath(typeNamespace, type.BaseType);
                            if (inc != null)
                                requiredIncludes.Add(inc);
                        }
                    }

                    foreach (var field in type.Fields)
                    {
                        if (IsGlobalNamespace(field.FieldType))
                        {
                            string inc = GetRelativeIncludePath(typeNamespace, field.FieldType);
                            if (inc != null)
                                requiredIncludes.Add(inc);
                        }
                    }
                    foreach (var method in type.Methods)
                    {
                        if (IsGlobalNamespace(method.ReturnType))
                        {
                            string retInc = GetRelativeIncludePath(typeNamespace, method.ReturnType);
                            if (retInc != null)
                                requiredIncludes.Add(retInc);
                        }
                        foreach (var param in method.Parameters)
                        {
                            if (IsGlobalNamespace(param.ParameterType))
                            {
                                string paramInc = GetRelativeIncludePath(typeNamespace, param.ParameterType);
                                if (paramInc != null)
                                    requiredIncludes.Add(paramInc);
                            }
                        }
                    }

                    var sb = new StringBuilder();
                    sb.AppendLine("#pragma once");
                    sb.AppendLine("#include <BNMIncludes.hpp>");
                    sb.AppendLine("#include \"../ForwardDeclarations.hpp\"");
                    foreach (var include in requiredIncludes.Distinct())
                        sb.AppendLine(include);
                    sb.AppendLine();

                    bool isGlobal = typeNamespace == "GlobalNamespace";
                    sb.AppendLine(isGlobal ? "namespace GlobalNamespace {" : $"namespace {typeNamespace.Replace(".", "::")} {{");
                    sb.AppendLine();

                    if (type.IsEnum)
                        GenerateEnum(type, sb, isGlobal, warnings);
                    else
                        GenerateClass(type, sb, isGlobal, warnings);

                    sb.AppendLine("}");
                    string className = Utils.FormatTypeNameForStruct(type.Name, typeNamespace);
                    File.WriteAllText(Path.Combine(isGlobal ? Path.Combine(OutputDir, "GlobalNamespace") : nsDir, $"{className}{OutputExtension}"), sb.ToString().Replace("StringComparison", "int"));
                }
            }

            GenerateForwardDeclarationsFile(grouped);
        }

        private static void GenerateForwardDeclarationsFile(IGrouping<string, TypeDefinition>[] grouped)
        {
            var sb = new StringBuilder();
            sb.AppendLine("#pragma once");
            sb.AppendLine("#include <BNMIncludes.hpp>");
            sb.AppendLine();

            var generatedEnums = new HashSet<string>();
            var generatedTypes = new HashSet<string>();

            foreach (var group in grouped)
            {
                bool isGlobal = group.Key == "GlobalNamespace";
                sb.AppendLine(isGlobal ? "namespace GlobalNamespace {" : $"namespace {group.Key.Replace(".", "::")} {{");

                foreach (var type in group.OrderBy(t => t.Name))
                {
                    string cleanName = Utils.CleanTypeName(type.Name);
                    if (Utils.ReservedTypeNames.Contains(cleanName) || generatedTypes.Contains(cleanName))
                        continue;
                    generatedTypes.Add(cleanName);

                    string formattedName = Utils.FormatTypeNameForStruct(type.Name, type.Namespace);
                    if (type.IsEnum)
                    {
                        if (generatedEnums.Contains(formattedName))
                            continue;
                        generatedEnums.Add(formattedName);
                        sb.AppendLine($"enum class {formattedName} : {Utils.GetEnumUnderlyingType(type)};");
                    }
                    else
                        sb.AppendLine($"struct {formattedName};");
                }

                sb.AppendLine("}");
                sb.AppendLine();
            }

            File.WriteAllText(Path.Combine(OutputDir, "ForwardDeclarations.hpp"), sb.ToString());
        }

        private static bool IsGlobalNamespace(TypeReference type)
        {
            if (type == null)
                return false;
            TypeReference element = type;
            while (element.IsArray || element.IsByReference || element.IsPointer)
                element = element.GetElementType();
            string ns = element.Namespace;
            return string.IsNullOrEmpty(ns) || ns == "GlobalNamespace";
        }

        private static string GetRelativeIncludePath(string currentNs, TypeReference targetType)
        {
            if (targetType == null || targetType.IsPrimitive || targetType.FullName == "System.Void")
                return null;
            TypeReference element = targetType;
            while (element.IsArray || element.IsByReference || element.IsPointer)
                element = element.GetElementType();
            if (element.IsPrimitive || element.FullName == "System.Void")
                return null;
            string targetNs = string.IsNullOrEmpty(element.Namespace) ? "GlobalNamespace" : element.Namespace;
            if (targetNs != "GlobalNamespace")
                return null;
            string targetName = Utils.FormatTypeNameForStruct(element.Name, targetNs);
            if (targetNs == currentNs && targetName == Utils.CleanTypeName(element.Name))
                return null;
            int depth = currentNs == "GlobalNamespace" ? 1 : currentNs.Split('.').Length;
            string prefix = "";
            for (int i = 0; i < depth; i++)
                prefix += "../";
            return $"#include \"{prefix}GlobalNamespace/{targetName}{OutputExtension}\"";
        }

        private static void WriteForwardDeclarations(StringBuilder sb, IGrouping<string, TypeDefinition> group, HashSet<string> generatedEnums, HashSet<string> generatedTypes)
        {
            bool isGlobal = group.Key == "GlobalNamespace";
            sb.AppendLine(isGlobal ? "namespace GlobalNamespace {" : $"namespace {group.Key.Replace(".", "::")} {{");
            foreach (var type in group.OrderBy(t => t.Name))
            {
                string cleanName = Utils.CleanTypeName(type.Name);
                if (Utils.ReservedTypeNames.Contains(cleanName) || generatedTypes.Contains(cleanName))
                    continue;
                generatedTypes.Add(cleanName);
                string formattedName = Utils.FormatTypeNameForStruct(type.Name, type.Namespace);
                if (type.IsEnum)
                {
                    if (generatedEnums.Contains(formattedName))
                        continue;
                    generatedEnums.Add(formattedName);
                    sb.AppendLine($"enum class {formattedName} : {Utils.GetEnumUnderlyingType(type)};");
                }
                else
                    sb.AppendLine($"struct {formattedName};");
            }
            sb.AppendLine("}");
            sb.AppendLine();
        }

        private static void GenerateClass(TypeDefinition type, StringBuilder sb, bool isGlobal, List<string> warnings)
        {
            try
            {
                string className = Utils.FormatTypeNameForStruct(type.Name, type.Namespace);
                string indent = isGlobal ? "" : "    ";
                if (ExcludedTypePatterns.Any(p => type.FullName.Contains(p) || type.Name.Contains(p)))
                    return;
                string baseClass = Utils.GetBaseClass(type);
                if (baseClass.Contains("__REMOVE__"))
                    return;
                if (!string.IsNullOrEmpty(baseClass))
                    sb.AppendLine($"{indent}struct {className}{baseClass} {{");
                else
                    sb.AppendLine($"{indent}struct {className} : BNM::UnityEngine::MonoBehaviour {{");
                sb.AppendLine($"{indent}public:");
                var generatedNames = new HashSet<string> { "GetClass", "GetType", "ToString", "Equals", "GetHashCode", "MemberwiseClone", "Finalize" };
                sb.AppendLine($"{indent}    static Class GetClass() {{");
                sb.AppendLine($"{indent}        static BNM::Class clazz = Class(O(\"{type.Namespace ?? ""}\"), O(\"{type.Name}\"), Image(O(\"Assembly-CSharp.dll\")));");
                sb.AppendLine($"{indent}        return clazz;");
                sb.AppendLine($"{indent}    }}");
                sb.AppendLine();
                sb.AppendLine($"{indent}    static MonoType* GetType() {{");
                sb.AppendLine($"{indent}        return GetClass().GetMonoType();");
                sb.AppendLine($"{indent}    }}");
                sb.AppendLine();
                GenerateSingletonMethods(type, sb, generatedNames, indent);
                var fields = type.Fields.Where(f => !f.IsLiteral && !f.Name.Contains("<")).OrderBy(f => f.Name).ToArray();
                foreach (var field in fields)
                {
                    string getterName = $"Get{Utils.ToPascalCase(Utils.FormatInvalidName(field.Name))}";
                    if (generatedNames.Add(getterName))
                        GenerateFieldGetter(field, sb, type, indent, warnings);
                }
                foreach (var field in fields)
                {
                    string setterName = $"Set{Utils.ToPascalCase(Utils.FormatInvalidName(field.Name))}";
                    if (generatedNames.Add(setterName))
                        GenerateFieldSetter(field, sb, type, indent, warnings);
                }
                GeneratePropertyMethods(type, sb, generatedNames, indent, warnings);
                GenerateMethodDeclarations(type, sb, generatedNames, indent, warnings);
                sb.AppendLine($"{indent}}};");
                sb.AppendLine();
            }
            catch { }
        }

        private static void GenerateEnum(TypeDefinition type, StringBuilder sb, bool isGlobal, List<string> warnings)
        {
            try
            {
                string enumName = Utils.FormatTypeNameForStruct(type.Name, type.Namespace);
                string underlyingType = Utils.GetEnumUnderlyingType(type);
                string indent = isGlobal ? "" : "    ";
                sb.AppendLine($"{indent}enum class {enumName} : {underlyingType} {{");
                var usedNames = new HashSet<string>();
                foreach (var field in type.Fields.Where(f => f.IsStatic && f.IsLiteral))
                {
                    string name = field.Name;
                    string uniqueName = name;
                    int suffix = 1;
                    while (usedNames.Contains(uniqueName))
                        uniqueName = $"{name}_{suffix++}";
                    usedNames.Add(uniqueName);
                    string valueStr = field.Constant != null ? Convert.ToInt64(field.Constant).ToString() : "0";
                    string formattedName = Utils.FormatInvalidName(uniqueName);
                    if (formattedName.ToLower() != "delete")
                        sb.AppendLine($"{indent}    {formattedName} = {valueStr},");
                }
                sb.AppendLine($"{indent}}};");
                sb.AppendLine();
            }
            catch { }
        }

        private static void GenerateSingletonMethods(TypeDefinition type, StringBuilder sb, HashSet<string> generatedNames, string indent)
        {
            var instanceProp = type.Properties.FirstOrDefault(p => p.Name == "Instance" && p.GetMethod != null);
            var instanceField = type.Fields.FirstOrDefault(f => f.Name == "_instance" && f.IsStatic);
            string className = Utils.FormatTypeNameForStruct(type.Name, type.Namespace);
            if (instanceProp != null && generatedNames.Add("get_Instance"))
            {
                sb.AppendLine($"{indent}    static {className}* get_Instance() {{");
                sb.AppendLine($"{indent}        static Method<{className}*> method = GetClass().GetMethod(O(\"get_Instance\"));");
                sb.AppendLine($"{indent}        return method();");
                sb.AppendLine($"{indent}    }}");
            }
            if (instanceField != null && generatedNames.Add("GetInstance"))
            {
                sb.AppendLine($"{indent}    static {className}* GetInstance() {{");
                sb.AppendLine($"{indent}        static Field<{className}*> field = GetClass().GetField(O(\"_instance\"));");
                sb.AppendLine($"{indent}        return field();");
                sb.AppendLine($"{indent}    }}");
            }
        }

        private static void GenerateFieldGetter(FieldDefinition field, StringBuilder sb, TypeDefinition currentClass, string indent, List<string> warnings)
        {
            string cppType = Utils.GetCppType(field.FieldType, currentClass);
            if (cppType.Contains("void*"))
                return;
            string methodName = $"Get{Utils.ToPascalCase(Utils.FormatInvalidName(field.Name))}";
            string varName = Utils.FormatInvalidName(Utils.ToCamelCase(Utils.FormatInvalidName(field.Name)));
            sb.AppendLine($"{indent}    {cppType} {methodName}() {{");
            sb.AppendLine($"{indent}        static Field<{cppType}> {varName} = GetClass().GetField(O(\"{field.Name}\"));");
            if (!field.IsStatic)
                sb.AppendLine($"{indent}        {varName}.SetInstance((BNM::IL2CPP::Il2CppObject*)this);");
            sb.AppendLine($"{indent}        return {varName}();");
            sb.AppendLine($"{indent}    }}");
        }

        private static void GenerateFieldSetter(FieldDefinition field, StringBuilder sb, TypeDefinition currentClass, string indent, List<string> warnings)
        {
            if (field.IsInitOnly)
                return;
            string cppType = Utils.GetCppType(field.FieldType, currentClass);
            if (cppType.Contains("void*"))
                return;
            string methodName = $"Set{Utils.ToPascalCase(Utils.FormatInvalidName(field.Name))}";
            string varName = Utils.FormatInvalidName(Utils.ToCamelCase(Utils.FormatInvalidName(field.Name)));
            sb.AppendLine($"{indent}    void {methodName}({cppType} value) {{");
            sb.AppendLine($"{indent}        static Field<{cppType}> {varName} = GetClass().GetField(O(\"{field.Name}\"));");
            if (!field.IsStatic)
                sb.AppendLine($"{indent}        {varName}.SetInstance((BNM::IL2CPP::Il2CppObject*)this);");
            sb.AppendLine($"{indent}        {varName} = value;");
            sb.AppendLine($"{indent}    }}");
        }

        private static void GeneratePropertyMethods(TypeDefinition type, StringBuilder sb, HashSet<string> generatedNames, string indent, List<string> warnings)
        {
            foreach (var prop in type.Properties.OrderBy(p => p.Name))
            {
                if (prop.Name.Contains("<") || prop.Name.Contains("."))
                    continue;
                string cppType = Utils.GetCppType(prop.PropertyType, type);
                if (cppType.Contains("void*"))
                    continue;
                if (prop.GetMethod != null && generatedNames.Add($"get_{prop.Name}"))
                {
                    sb.AppendLine($"{indent}    {cppType} get_{prop.Name}() {{");
                    sb.AppendLine($"{indent}        static Method<{cppType}> method = GetClass().GetMethod(O(\"get_{prop.Name}\"));");
                    sb.AppendLine($"{indent}        return method();");
                    sb.AppendLine($"{indent}    }}");
                }
                if (prop.SetMethod != null && generatedNames.Add($"set_{prop.Name}"))
                {
                    sb.AppendLine($"{indent}    void set_{prop.Name}({cppType} value) {{");
                    sb.AppendLine($"{indent}        static Method<void> method = GetClass().GetMethod(O(\"set_{prop.Name}\"));");
                    sb.AppendLine($"{indent}        method(value);");
                    sb.AppendLine($"{indent}    }}");
                }
            }
        }

        private static void GenerateMethodDeclarations(TypeDefinition type, StringBuilder sb, HashSet<string> generatedNames, string indent, List<string> warnings)
        {
            foreach (var method in type.Methods.Where(m => !m.IsConstructor && !m.Name.Contains("<") && !m.Name.Contains(".")).OrderBy(m => m.Name))
            {
                string methodName = Utils.FormatInvalidName(method.Name).Replace(".", "_");
                if (!generatedNames.Add(methodName))
                    continue;
                string returnCppType = Utils.GetCppType(method.ReturnType, type);
                if (returnCppType.Contains("void*"))
                    continue;
                var paramNames = Utils.MakeValidParams(method.Parameters.Select(p => p.Name).ToArray());
                var paramTypes = method.Parameters.Select(p => Utils.GetCppType(p.ParameterType, type)).ToArray();
                if (paramTypes.Any(pt => pt.Contains("void*")))
                    continue;
                string paramList = string.Join(", ", paramTypes.Zip(paramNames, (pt, pn) => $"{pt} {pn}"));
                sb.AppendLine($"{indent}    {returnCppType} {methodName}({paramList}) {{");
                sb.AppendLine($"{indent}        static Method<{returnCppType}> method = GetClass().GetMethod(O(\"{method.Name}\"));");
                sb.AppendLine($"{indent}        return method({string.Join(", ", paramNames)});");
                sb.AppendLine($"{indent}    }}");
            }
        }

        private static void LogError(Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            File.WriteAllText("GeneratorError.txt", ex.ToString());
        }

        private static void WaitForExit() => Console.ReadKey();
    }

    internal class TypeDefComparer : IEqualityComparer<TypeDefinition>
    {
        public bool Equals(TypeDefinition x, TypeDefinition y) => x?.FullName == y?.FullName;

        public int GetHashCode(TypeDefinition obj) => obj?.FullName?.GetHashCode() ?? 0;
    }
}