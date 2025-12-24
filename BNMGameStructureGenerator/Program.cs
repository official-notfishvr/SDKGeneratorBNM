using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using dnlib.DotNet;

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

            EnsureDirectories();

            if (!File.Exists(DllPath))
            {
                Console.WriteLine($"Error: {DllPath} not found.");
                Console.WriteLine("Please place Assembly-CSharp.dll in the ./Files folder.");
                WaitForExit();
                return;
            }

            try
            {
                var types = LoadTypes();
                if (types.Count == 0)
                {
                    Console.WriteLine("No valid types found to process.");
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

        private static void EnsureDirectories()
        {
            Directory.CreateDirectory("./Files");
            Directory.CreateDirectory($"./{OutputDir}");
        }

        private static List<object> LoadTypes()
        {
            var allTypes = new List<object>();

            try
            {
                var assembly = Assembly.LoadFrom(DllPath);
                var reflectionTypes = new List<Type>();

                try
                {
                    reflectionTypes.AddRange(assembly.GetTypes());
                }
                catch (ReflectionTypeLoadException ex)
                {
                    if (ex.Types != null)
                        reflectionTypes.AddRange(ex.Types.Where(t => t != null));
                }

                allTypes.AddRange(reflectionTypes.Cast<object>());
                Console.WriteLine($"Loaded {reflectionTypes.Count} types via reflection");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Reflection load failed: {ex.Message}");
            }

            try
            {
                var module = ModuleDefMD.Load(DllPath);
                var dnlibTypes = module.GetTypes().Cast<object>().ToList();
                allTypes.AddRange(dnlibTypes);
                Console.WriteLine($"Loaded {dnlibTypes.Count} types via dnlib");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: dnlib load failed: {ex.Message}");
            }

            return allTypes.Where(t => t != null && IsValidType(t)).GroupBy(t => Utils.GetFullName(t)).Select(g => g.First()).ToList();
        }

        private static bool IsValidType(object type)
        {
            if (!Utils.IsClassType(type) && !Utils.IsEnum(type))
                return false;

            string fullName = Utils.GetFullName(type);
            string typeName = Utils.GetTypeName(type);

            return !ExcludedTypePatterns.Any(p => fullName.Contains(p) || typeName.Contains(p));
        }

        private static void ProcessTypes(List<object> types, bool singleFileMode)
        {
            Console.WriteLine($"\nProcessing {types.Count} types in {(singleFileMode ? "single-file" : "folder")} mode...");
            Console.WriteLine(new string('=', 50));

            if (!singleFileMode && Directory.Exists(OutputDir))
                Directory.Delete(OutputDir, true);
            Directory.CreateDirectory(OutputDir);

            foreach (var type in types)
                DefinedTypes.Add(Utils.CleanTypeName(Utils.GetTypeName(type)));

            var grouped = types.Where(t => Utils.SafeGetNamespace(t) != null).GroupBy(t => Utils.SafeGetNamespace(t)).OrderBy(g => g.Key).ToArray();

            var warnings = new List<string>();

            if (singleFileMode)
                GenerateSingleFile(types, grouped, warnings);
            else
                GenerateMultipleFiles(grouped, warnings);

            if (warnings.Count > 0)
            {
                var warnPath = Path.Combine(OutputDir, "GenerationWarnings.txt");
                File.WriteAllLines(warnPath, warnings);
                Console.WriteLine($"\nWarnings saved to: {warnPath}");
            }

            Console.WriteLine($"\nC++ headers saved to: {OutputDir}/");
        }

        private static void GenerateSingleFile(List<object> types, IGrouping<string, object>[] grouped, List<string> warnings)
        {
            var sb = new StringBuilder();
            WriteHeader(sb);

            var generatedEnums = new HashSet<string>();
            var generatedTypes = new HashSet<string>();

            sb.AppendLine("// Forward declarations");
            foreach (var group in grouped)
            {
                WriteForwardDeclarations(sb, group, generatedEnums, generatedTypes);
            }

            generatedEnums.Clear();
            generatedTypes.Clear();

            foreach (var group in grouped)
            {
                bool isGlobal = group.Key == "Global";

                if (!isGlobal)
                    sb.AppendLine($"namespace {group.Key.Replace(".", "::")} {{");

                foreach (var type in group.OrderBy(t => Utils.GetTypeName(t)))
                {
                    string cleanName = Utils.CleanTypeName(Utils.GetTypeName(type));
                    if (Utils.ReservedTypeNames.Contains(cleanName) || generatedTypes.Contains(cleanName))
                        continue;

                    generatedTypes.Add(cleanName);
                    Console.WriteLine($"Processing: {Utils.GetFullName(type)}");

                    if (Utils.IsEnum(type))
                    {
                        string enumName = Utils.FormatTypeNameForStruct(Utils.GetTypeName(type), Utils.GetNamespace(type));
                        if (!generatedEnums.Contains(enumName))
                        {
                            generatedEnums.Add(enumName);
                            GenerateEnum(type, sb, isGlobal, warnings);
                        }
                    }
                    else
                    {
                        GenerateClass(type, sb, isGlobal, warnings);
                    }
                }

                if (!isGlobal)
                    sb.AppendLine("}");
                sb.AppendLine();
            }

            var outputPath = Path.Combine(OutputDir, $"BNMResolves{OutputExtension}");
            File.WriteAllText(outputPath, sb.ToString().Replace("StringComparison", "int"));

            ValidateOutput(outputPath);
        }

        private static void GenerateMultipleFiles(IGrouping<string, object>[] grouped, List<string> warnings)
        {
            var generatedTypes = new HashSet<string>();

            foreach (var group in grouped)
            {
                string nsDir = group.Key == "Global" ? Path.Combine(OutputDir, "Global") : Path.Combine(OutputDir, group.Key.Replace(".", "/"));

                Directory.CreateDirectory(nsDir);
                var generatedEnums = new HashSet<string>();

                foreach (var type in group.OrderBy(t => Utils.GetTypeName(t)))
                {
                    string cleanName = Utils.CleanTypeName(Utils.GetTypeName(type));
                    if (Utils.ReservedTypeNames.Contains(cleanName) || generatedTypes.Contains(cleanName))
                        continue;

                    generatedTypes.Add(cleanName);
                    Console.WriteLine($"Processing: {Utils.GetFullName(type)}");

                    var sb = new StringBuilder();
                    WriteHeader(sb);

                    string typeNamespace = Utils.GetNamespace(type);
                    bool isGlobal = string.IsNullOrEmpty(typeNamespace) || typeNamespace == "Global";
                    string actualDir = isGlobal ? Path.Combine(OutputDir, "Global") : nsDir;
                    Directory.CreateDirectory(actualDir);

                    if (!isGlobal)
                    {
                        sb.AppendLine($"namespace {typeNamespace.Replace(".", "::")} {{");
                        sb.AppendLine();
                    }

                    WriteNamespaceForwardDeclarations(sb, group, type, generatedEnums, isGlobal);

                    if (Utils.IsEnum(type))
                    {
                        string enumName = Utils.FormatTypeNameForStruct(Utils.GetTypeName(type), typeNamespace);
                        if (!generatedEnums.Contains(enumName))
                        {
                            generatedEnums.Add(enumName);
                            GenerateEnum(type, sb, isGlobal, warnings);
                        }
                    }
                    else
                    {
                        GenerateClass(type, sb, isGlobal, warnings);
                    }

                    if (!isGlobal)
                        sb.AppendLine("}");

                    string className = Utils.FormatTypeNameForStruct(Utils.GetTypeName(type), typeNamespace);
                    string filePath = Path.Combine(actualDir, $"{className}{OutputExtension}");
                    File.WriteAllText(filePath, sb.ToString().Replace("StringComparison", "int"));
                }
            }

            ValidateOutput(OutputDir);
        }

        private static void WriteHeader(StringBuilder sb)
        {
            sb.AppendLine("#pragma once");
            sb.AppendLine();
            sb.AppendLine("using namespace BNM;");
            sb.AppendLine("using namespace BNM::IL2CPP;");
            sb.AppendLine("using namespace BNM::Structures;");
            sb.AppendLine("using namespace BNM::Structures::Unity;");
            sb.AppendLine("using namespace BNM::UnityEngine;");
            sb.AppendLine();
            sb.AppendLine("#define O(str) BNM_OBFUSCATE(str)");
            sb.AppendLine("#include \"BNMResolve.hpp\"");
            sb.AppendLine();
        }

        private static void WriteForwardDeclarations(StringBuilder sb, IGrouping<string, object> group, HashSet<string> generatedEnums, HashSet<string> generatedTypes)
        {
            bool isGlobal = group.Key == "Global";
            string indent = isGlobal ? "" : "    ";

            if (isGlobal)
                sb.AppendLine("// Global namespace types");
            else
                sb.AppendLine($"namespace {group.Key.Replace(".", "::")} {{");

            foreach (var type in group.OrderBy(t => Utils.GetTypeName(t)))
            {
                string typeName = Utils.GetTypeName(type);
                string cleanName = Utils.CleanTypeName(typeName);

                if (Utils.ReservedTypeNames.Contains(cleanName) || generatedTypes.Contains(cleanName))
                    continue;

                generatedTypes.Add(cleanName);
                string formattedName = Utils.FormatTypeNameForStruct(typeName, Utils.GetNamespace(type));

                if (Utils.IsEnum(type))
                {
                    if (generatedEnums.Contains(formattedName))
                        continue;
                    generatedEnums.Add(formattedName);
                    sb.AppendLine($"{indent}enum class {formattedName} : {Utils.GetEnumUnderlyingType(type)};");
                }
                else
                {
                    sb.AppendLine($"{indent}struct {formattedName};");
                }
            }

            if (!isGlobal)
                sb.AppendLine("}");
            sb.AppendLine();
        }

        private static void WriteNamespaceForwardDeclarations(StringBuilder sb, IGrouping<string, object> group, object currentType, HashSet<string> generatedEnums, bool isGlobal)
        {
            var others = group.Where(t => !t.Equals(currentType)).ToList();
            if (!others.Any())
                return;

            string indent = isGlobal ? "" : "    ";
            sb.AppendLine($"{indent}// Forward declarations");

            foreach (var other in others)
            {
                string otherName = Utils.GetTypeName(other);
                string cleanName = Utils.CleanTypeName(otherName);

                if (Utils.ReservedTypeNames.Contains(cleanName) || generatedEnums.Contains(cleanName))
                    continue;

                string formattedName = Utils.FormatTypeNameForStruct(otherName, Utils.GetNamespace(other));

                if (Utils.IsEnum(other))
                {
                    if (generatedEnums.Contains(formattedName))
                        continue;
                    generatedEnums.Add(formattedName);
                    sb.AppendLine($"{indent}enum class {formattedName} : {Utils.GetEnumUnderlyingType(other)};");
                }
                else
                {
                    sb.AppendLine($"{indent}struct {formattedName};");
                }
            }
            sb.AppendLine();
        }

        private static void GenerateClass(object type, StringBuilder sb, bool isGlobal, List<string> warnings)
        {
            try
            {
                string typeName = Utils.GetTypeName(type);
                string ns = Utils.GetNamespace(type);
                string className = Utils.FormatTypeNameForStruct(typeName, ns);
                string indent = isGlobal ? "" : "    ";
                string fullName = Utils.GetFullName(type);

                if (ExcludedTypePatterns.Any(p => fullName.Contains(p) || typeName.Contains(p)))
                {
                    sb.AppendLine($"{indent}// SKIPPED: {typeName} is a collection type");
                    sb.AppendLine();
                    return;
                }

                string baseClass = Utils.GetBaseClass(type, type);

                if (baseClass.Contains("__REMOVE__"))
                {
                    warnings.Add($"REMOVED: {className} inherits from unsupported base");
                    sb.AppendLine($"{indent}// REMOVED: {className} inherits from unsupported base");
                    sb.AppendLine();
                    return;
                }

                if (baseClass.Contains("__SELF_REF__"))
                {
                    warnings.Add($"REMOVED: {className} has self-reference inheritance");
                    sb.AppendLine($"{indent}// NOTE: {className} has self-reference inheritance");
                    sb.AppendLine($"{indent}struct {className} : Behaviour {{");
                }
                else if (!string.IsNullOrEmpty(baseClass))
                {
                    sb.AppendLine($"{indent}struct {className}{baseClass} {{");
                }
                else
                {
                    sb.AppendLine($"{indent}struct {className} : Behaviour {{");
                }

                sb.AppendLine($"{indent}public:");

                var generatedNames = new HashSet<string> { "GetClass", "GetType", "ToString", "Equals", "GetHashCode", "MemberwiseClone", "Finalize" };

                sb.AppendLine($"{indent}    static Class GetClass() {{");
                sb.AppendLine($"{indent}        static BNM::Class clazz = Class(O(\"{ns ?? ""}\"), O(\"{typeName}\"), Image(O(\"Assembly-CSharp.dll\")));");
                sb.AppendLine($"{indent}        return clazz;");
                sb.AppendLine($"{indent}    }}");
                sb.AppendLine();

                sb.AppendLine($"{indent}    static MonoType* GetType() {{");
                sb.AppendLine($"{indent}        return GetClass().GetMonoType();");
                sb.AppendLine($"{indent}    }}");
                sb.AppendLine();

                GenerateSingletonMethods(type, sb, generatedNames, indent);

                var fields = Utils.GetFields(type).Where(f => !Utils.IsLiteral(f) && !Utils.GetFieldName(f).Contains("<")).OrderBy(f => Utils.GetFieldName(f)).ToArray();

                foreach (var field in fields)
                {
                    string getterName = $"Get{Utils.ToPascalCase(Utils.FormatInvalidName(Utils.GetFieldName(field)))}";
                    if (generatedNames.Add(getterName))
                        GenerateFieldGetter(field, sb, type, indent, warnings);
                }

                foreach (var field in fields)
                {
                    string setterName = $"Set{Utils.ToPascalCase(Utils.FormatInvalidName(Utils.GetFieldName(field)))}";
                    if (generatedNames.Add(setterName))
                        GenerateFieldSetter(field, sb, type, indent, warnings);
                }

                GeneratePropertyMethods(type, sb, generatedNames, indent, warnings);
                GenerateMethodDeclarations(type, sb, generatedNames, indent, warnings);

                sb.AppendLine($"{indent}}};");
                sb.AppendLine();
            }
            catch (Exception ex)
            {
                string indent = isGlobal ? "" : "    ";
                warnings.Add($"Error generating {Utils.GetTypeName(type)}: {ex.Message}");
                sb.AppendLine($"{indent}// Error: {ex.Message}");
                sb.AppendLine();
            }
        }

        private static void GenerateEnum(object type, StringBuilder sb, bool isGlobal, List<string> warnings)
        {
            try
            {
                string typeName = Utils.GetTypeName(type);
                string ns = Utils.GetNamespace(type);
                string enumName = Utils.FormatTypeNameForStruct(typeName, ns);
                string underlyingType = Utils.GetEnumUnderlyingType(type);
                string indent = isGlobal ? "" : "    ";

                sb.AppendLine($"{indent}enum class {enumName} : {underlyingType} {{");

                var values = Utils.GetEnumValues(type);
                var names = Utils.GetEnumNames(type);
                var usedNames = new HashSet<string>();

                for (int i = 0; i < values.Length; i++)
                {
                    string name = names[i];
                    string uniqueName = name;
                    int suffix = 1;

                    while (usedNames.Contains(uniqueName))
                        uniqueName = $"{name}_{suffix++}";

                    usedNames.Add(uniqueName);

                    string valueStr;
                    try
                    {
                        valueStr = Convert.ToInt64(values.GetValue(i)).ToString();
                    }
                    catch
                    {
                        valueStr = values.GetValue(i).ToString();
                    }

                    string formattedName = Utils.FormatInvalidName(uniqueName);

                    if (formattedName.ToLower() == "delete")
                    {
                        warnings.Add($"Enum value 'delete' removed (reserved keyword)");
                        sb.AppendLine($"{indent}    // {formattedName} = {valueStr}, // reserved keyword");
                    }
                    else
                    {
                        sb.AppendLine($"{indent}    {formattedName} = {valueStr},");
                    }
                }

                sb.AppendLine($"{indent}}};");
                sb.AppendLine();
            }
            catch (Exception ex)
            {
                string indent = isGlobal ? "" : "    ";
                warnings.Add($"Error generating enum {Utils.GetTypeName(type)}: {ex.Message}");
                sb.AppendLine($"{indent}// Error: {ex.Message}");
                sb.AppendLine();
            }
        }

        private static void GenerateSingletonMethods(object type, StringBuilder sb, HashSet<string> generatedNames, string indent)
        {
            try
            {
                if (type is Type t)
                {
                    var instanceProp = t.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                    var instanceField = t.GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
                    string className = Utils.FormatTypeNameForStruct(t.Name, t.Namespace);

                    if (instanceProp != null && generatedNames.Add("get_Instance"))
                    {
                        sb.AppendLine($"{indent}    static {className}* get_Instance() {{");
                        sb.AppendLine($"{indent}        static Method<{className}*> method = GetClass().GetMethod(O(\"get_Instance\"));");
                        sb.AppendLine($"{indent}        return method();");
                        sb.AppendLine($"{indent}    }}");
                        sb.AppendLine();
                    }

                    if (instanceField != null && generatedNames.Add("GetInstance"))
                    {
                        sb.AppendLine($"{indent}    static {className}* GetInstance() {{");
                        sb.AppendLine($"{indent}        static Field<{className}*> field = GetClass().GetField(O(\"_instance\"));");
                        sb.AppendLine($"{indent}        return field();");
                        sb.AppendLine($"{indent}    }}");
                        sb.AppendLine();
                    }
                }
                else if (type is TypeDef td)
                {
                    var instanceProp = td.Properties.FirstOrDefault(p => p.Name == "Instance");
                    var instanceField = td.Fields.FirstOrDefault(f => f.Name == "_instance" && f.IsStatic);
                    string className = Utils.FormatTypeNameForStruct(td.Name, td.Namespace?.ToString());

                    if (instanceProp != null && generatedNames.Add("get_Instance"))
                    {
                        sb.AppendLine($"{indent}    static {className}* get_Instance() {{");
                        sb.AppendLine($"{indent}        static Method<{className}*> method = GetClass().GetMethod(O(\"get_Instance\"));");
                        sb.AppendLine($"{indent}        return method();");
                        sb.AppendLine($"{indent}    }}");
                        sb.AppendLine();
                    }

                    if (instanceField != null && generatedNames.Add("GetInstance"))
                    {
                        sb.AppendLine($"{indent}    static {className}* GetInstance() {{");
                        sb.AppendLine($"{indent}        static Field<{className}*> field = GetClass().GetField(O(\"_instance\"));");
                        sb.AppendLine($"{indent}        return field();");
                        sb.AppendLine($"{indent}    }}");
                        sb.AppendLine();
                    }
                }
            }
            catch { }
        }

        private static void GenerateFieldGetter(object field, StringBuilder sb, object currentClass, string indent, List<string> warnings)
        {
            try
            {
                object fieldType = Utils.GetFieldType(field);
                if (Utils.IsNullableType(fieldType))
                    fieldType = Utils.GetNullableUnderlyingType(fieldType);

                string fieldTypeName = Utils.GetTypeName(fieldType);
                if (Utils.ReservedTypeNames.Contains(Utils.CleanTypeName(fieldTypeName)))
                {
                    warnings.Add($"Field '{Utils.GetFieldName(field)}' uses reserved type");
                    return;
                }

                string cppType = Utils.GetCppType(fieldType, currentClass);
                if (cppType.Contains("__REMOVE__") || cppType.Contains("__SELF_REF__"))
                    return;

                if (Utils.IsClassType(fieldType) && !Utils.IsStringType(fieldType))
                {
                    string typeNs = Utils.GetTypeNamespace(fieldType);
                    if (typeNs != null && !typeNs.StartsWith("System") && !typeNs.StartsWith("UnityEngine"))
                        return;
                }

                string fieldName = Utils.GetFieldName(field);
                string methodName = $"Get{Utils.ToPascalCase(Utils.FormatInvalidName(fieldName))}";
                string varName = Utils.FormatInvalidName(Utils.ToCamelCase(Utils.FormatInvalidName(fieldName)));

                sb.AppendLine($"{indent}    {cppType} {methodName}() {{");
                sb.AppendLine($"{indent}        static Field<{cppType}> {varName} = GetClass().GetField(O(\"{fieldName}\"));");

                if (!Utils.IsStatic(field))
                    sb.AppendLine($"{indent}        {varName}.SetInstance(this);");

                sb.AppendLine($"{indent}        return {varName}();");
                sb.AppendLine($"{indent}    }}");
                sb.AppendLine();
            }
            catch (Exception ex)
            {
                warnings.Add($"Error generating getter for {Utils.GetFieldName(field)}: {ex.Message}");
            }
        }

        private static void GenerateFieldSetter(object field, StringBuilder sb, object currentClass, string indent, List<string> warnings)
        {
            try
            {
                if (Utils.IsInitOnly(field))
                    return;

                object fieldType = Utils.GetFieldType(field);
                if (Utils.IsNullableType(fieldType))
                    fieldType = Utils.GetNullableUnderlyingType(fieldType);

                string fieldTypeName = Utils.GetTypeName(fieldType);
                if (Utils.ReservedTypeNames.Contains(Utils.CleanTypeName(fieldTypeName)))
                    return;

                string cppType = Utils.GetCppType(fieldType, currentClass);
                if (cppType.Contains("__REMOVE__") || cppType.Contains("__SELF_REF__"))
                    return;

                if (Utils.IsClassType(fieldType) && !Utils.IsStringType(fieldType))
                {
                    string typeNs = Utils.GetTypeNamespace(fieldType);
                    if (typeNs != null && !typeNs.StartsWith("System") && !typeNs.StartsWith("UnityEngine"))
                        return;
                }

                string fieldName = Utils.GetFieldName(field);
                string methodName = $"Set{Utils.ToPascalCase(Utils.FormatInvalidName(fieldName))}";
                string varName = Utils.FormatInvalidName(Utils.ToCamelCase(Utils.FormatInvalidName(fieldName)));

                sb.AppendLine($"{indent}    void {methodName}({cppType} value) {{");
                sb.AppendLine($"{indent}        static Field<{cppType}> {varName} = GetClass().GetField(O(\"{fieldName}\"));");

                if (!Utils.IsStatic(field))
                    sb.AppendLine($"{indent}        {varName}.SetInstance(this);");

                sb.AppendLine($"{indent}        {varName} = value;");
                sb.AppendLine($"{indent}    }}");
                sb.AppendLine();
            }
            catch (Exception ex)
            {
                warnings.Add($"Error generating setter for {Utils.GetFieldName(field)}: {ex.Message}");
            }
        }

        private static void GeneratePropertyMethods(object type, StringBuilder sb, HashSet<string> generatedNames, string indent, List<string> warnings)
        {
            try
            {
                if (type is Type t)
                {
                    var properties = t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);

                    foreach (var prop in properties.OrderBy(p => p.Name))
                    {
                        if (prop.Name.Contains("<") || prop.Name.Contains("."))
                            continue;
                        if (Utils.ReservedTypeNames.Contains(Utils.FormatInvalidName(prop.Name)))
                            continue;

                        Type propType = prop.PropertyType;
                        if (propType.IsGenericType && propType.GetGenericTypeDefinition() == typeof(Nullable<>))
                            propType = Nullable.GetUnderlyingType(propType);

                        string cppType = Utils.GetCppType(propType, t);
                        if (cppType.Contains("__REMOVE__") || cppType.Contains("__SELF_REF__"))
                            continue;

                        if (prop.CanRead && generatedNames.Add($"get_{prop.Name}"))
                        {
                            sb.AppendLine($"{indent}    {cppType} get_{prop.Name}() {{");
                            sb.AppendLine($"{indent}        static Method<{cppType}> method = GetClass().GetMethod(O(\"get_{prop.Name}\"));");
                            sb.AppendLine($"{indent}        return method();");
                            sb.AppendLine($"{indent}    }}");
                            sb.AppendLine();
                        }

                        if (prop.CanWrite && generatedNames.Add($"set_{prop.Name}"))
                        {
                            sb.AppendLine($"{indent}    void set_{prop.Name}({cppType} value) {{");
                            sb.AppendLine($"{indent}        static Method<void> method = GetClass().GetMethod(O(\"set_{prop.Name}\"));");
                            sb.AppendLine($"{indent}        method(value);");
                            sb.AppendLine($"{indent}    }}");
                            sb.AppendLine();
                        }
                    }
                }
                else if (type is TypeDef td)
                {
                    foreach (var prop in td.Properties.OrderBy(p => p.Name))
                    {
                        if (prop.Name.Contains("<") || prop.Name.Contains("."))
                            continue;
                        if (Utils.ReservedTypeNames.Contains(Utils.FormatInvalidName(prop.Name)))
                            continue;

                        TypeSig propType = prop.PropertySig.RetType;
                        if (propType is GenericInstSig genericSig && propType.FullName.Contains("System.Nullable"))
                            propType = genericSig.GenericArguments[0];

                        string cppType = Utils.GetCppType(propType, td);
                        if (cppType.Contains("__REMOVE__") || cppType.Contains("__SELF_REF__"))
                            continue;

                        if (prop.GetMethod != null && generatedNames.Add($"get_{prop.Name}"))
                        {
                            sb.AppendLine($"{indent}    {cppType} get_{prop.Name}() {{");
                            sb.AppendLine($"{indent}        static Method<{cppType}> method = GetClass().GetMethod(O(\"get_{prop.Name}\"));");
                            sb.AppendLine($"{indent}        return method();");
                            sb.AppendLine($"{indent}    }}");
                            sb.AppendLine();
                        }

                        if (prop.SetMethod != null && generatedNames.Add($"set_{prop.Name}"))
                        {
                            sb.AppendLine($"{indent}    void set_{prop.Name}({cppType} value) {{");
                            sb.AppendLine($"{indent}        static Method<void> method = GetClass().GetMethod(O(\"set_{prop.Name}\"));");
                            sb.AppendLine($"{indent}        method(value);");
                            sb.AppendLine($"{indent}    }}");
                            sb.AppendLine();
                        }
                    }
                }
            }
            catch { }
        }

        private static void GenerateMethodDeclarations(object type, StringBuilder sb, HashSet<string> generatedNames, string indent, List<string> warnings)
        {
            try
            {
                if (type is Type t)
                {
                    var methods = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static).Where(m => !m.IsSpecialName && !m.Name.Contains("<") && !m.Name.Contains(".")).OrderBy(m => m.Name);

                    foreach (var method in methods)
                    {
                        if (Utils.ReservedTypeNames.Contains(Utils.CleanTypeName(method.Name)))
                            continue;

                        string methodName = Utils.FormatInvalidName(method.Name).Replace(".", "_");
                        if (!generatedNames.Add(methodName))
                            continue;

                        Type returnType = method.ReturnType;
                        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Nullable<>))
                            returnType = Nullable.GetUnderlyingType(returnType);

                        if (Utils.ReservedTypeNames.Contains(Utils.CleanTypeName(returnType.Name)))
                            continue;

                        string returnCppType = Utils.GetCppType(returnType, t);
                        if (returnCppType.Contains("__REMOVE__") || returnCppType.Contains("__SELF_REF__"))
                            continue;

                        var parameters = method.GetParameters();
                        var paramNames = Utils.MakeValidParams(parameters.Select(p => p.Name).ToArray());
                        var paramTypes = parameters
                            .Select(p =>
                            {
                                Type paramType = p.ParameterType;
                                if (paramType.IsGenericType && paramType.GetGenericTypeDefinition() == typeof(Nullable<>))
                                    paramType = Nullable.GetUnderlyingType(paramType);
                                if (Utils.ReservedTypeNames.Contains(Utils.CleanTypeName(paramType.Name)))
                                    return "__REMOVE__";
                                return Utils.GetCppType(paramType, t);
                            })
                            .ToArray();

                        if (paramTypes.Any(pt => pt.Contains("__REMOVE__") || pt.Contains("__SELF_REF__") || pt == "Method"))
                            continue;

                        string paramList = string.Join(", ", paramTypes.Zip(paramNames, (pt, pn) => $"{pt} {pn}"));

                        sb.AppendLine($"{indent}    {returnCppType} {methodName}({paramList}) {{");
                        sb.AppendLine($"{indent}        static Method<{returnCppType}> method = GetClass().GetMethod(O(\"{method.Name}\"));");
                        sb.AppendLine($"{indent}        return method({string.Join(", ", paramNames)});");
                        sb.AppendLine($"{indent}    }}");
                        sb.AppendLine();
                    }
                }
                else if (type is TypeDef td)
                {
                    var methods = td.Methods.Where(m => !m.IsConstructor && !m.IsStaticConstructor && !m.Name.Contains("<") && !m.Name.Contains(".")).OrderBy(m => m.Name);

                    foreach (var method in methods)
                    {
                        if (Utils.ReservedTypeNames.Contains(Utils.CleanTypeName(method.Name)))
                            continue;

                        string methodName = Utils.FormatInvalidName(method.Name).Replace(".", "_");
                        if (!generatedNames.Add(methodName))
                            continue;

                        TypeSig returnType = method.MethodSig.RetType;
                        if (returnType is GenericInstSig genericSig && returnType.FullName.Contains("System.Nullable"))
                            returnType = genericSig.GenericArguments[0];

                        if (Utils.ReservedTypeNames.Contains(Utils.CleanTypeName(returnType.TypeName)))
                            continue;

                        string returnCppType = Utils.GetCppType(returnType, td);
                        if (returnCppType.Contains("__REMOVE__") || returnCppType.Contains("__SELF_REF__"))
                            continue;

                        var parameters = method.Parameters;
                        var paramNames = Utils.MakeValidParams(parameters.Select(p => p.Name).ToArray());
                        var paramTypes = parameters
                            .Select(p =>
                            {
                                TypeSig paramType = p.Type;
                                if (paramType is GenericInstSig gs && paramType.FullName.Contains("System.Nullable"))
                                    paramType = gs.GenericArguments[0];
                                if (Utils.ReservedTypeNames.Contains(Utils.CleanTypeName(paramType.TypeName)))
                                    return "__REMOVE__";
                                return Utils.GetCppType(paramType, td);
                            })
                            .ToArray();

                        if (paramTypes.Any(pt => pt.Contains("__REMOVE__") || pt.Contains("__SELF_REF__") || pt == "Method"))
                            continue;

                        string paramList = string.Join(", ", paramTypes.Zip(paramNames, (pt, pn) => $"{pt} {pn}"));

                        sb.AppendLine($"{indent}    {returnCppType} {methodName}({paramList}) {{");
                        sb.AppendLine($"{indent}        static Method<{returnCppType}> method = GetClass().GetMethod(O(\"{method.Name}\"));");
                        sb.AppendLine($"{indent}        return method({string.Join(", ", paramNames)});");
                        sb.AppendLine($"{indent}    }}");
                        sb.AppendLine();
                    }
                }
            }
            catch { }
        }

        private static void ValidateOutput(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    string content = File.ReadAllText(path);
                    if (string.IsNullOrWhiteSpace(content))
                        Console.WriteLine("Warning: Generated file is empty");
                }
                else if (Directory.Exists(path))
                {
                    var files = Directory.GetFiles(path, $"*{OutputExtension}", SearchOption.AllDirectories);
                    if (files.Length == 0)
                        Console.WriteLine($"Warning: No {OutputExtension} files generated");
                    else
                        Console.WriteLine($"Generated {files.Length} {OutputExtension} files");
                }
            }
            catch { }
        }

        private static void LogError(Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            File.WriteAllText("GeneratorError.txt", $"Error: {ex.Message}\n\nStack Trace:\n{ex}");
            Console.WriteLine("Error details saved to: GeneratorError.txt");
        }

        private static void WaitForExit()
        {
            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }
    }
}
