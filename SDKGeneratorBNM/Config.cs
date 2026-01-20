using System;

namespace SDKGeneratorBNM
{
    public static class Config
    {
        public enum NamingStyle
        {
            GetSet,
            GetterSetter,
        }

        public static NamingStyle MethodNamingStyle = NamingStyle.GetSet; // i like GetSet better

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
