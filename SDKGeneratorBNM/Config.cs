using SDKGeneratorBNM;

namespace SDKGeneratorBNM
{
    public static class Config
    {
        public enum NamingStyle
        {
            GetSet,
            GetterSetter,
        }

        public enum MethodStyle
        {
            Wrapper, // GetField(), SetField(), method.Call()
            Accessor, // field->Get(), field->Set(), method->Call()
        }

        public static NamingStyle MethodNamingStyle = NamingStyle.GetSet;
        public static MethodStyle MethodAccessorStyle = MethodStyle.Wrapper;
        public static bool UseBNMResolve = false; // Use BNMResolve.hpp for (GameObject, Text, etc.) or fallback to Il2CppObject

        public static string FormatGetterName(string name)
        {
            string formatted = Utils.ToPascalCase(Utils.FormatInvalidName(name));
            return MethodNamingStyle == NamingStyle.GetSet ? $"Get{formatted}" : $"get_{Utils.ToCamelCase(Utils.FormatInvalidName(name))}";
        }

        public static string FormatSetterName(string name)
        {
            string formatted = Utils.ToPascalCase(Utils.FormatInvalidName(name));
            return MethodNamingStyle == NamingStyle.GetSet ? $"Set{formatted}" : $"set_{Utils.ToCamelCase(Utils.FormatInvalidName(name))}";
        }

        public static string GetPropertyMethodName(string propertyName, bool isGetter)
        {
            return isGetter ? $"get_{propertyName}" : $"set_{propertyName}";
        }
    }
}
