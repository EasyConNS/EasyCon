namespace EasyCon2.Script.Parsing
{
    public static class TypeExt
    {
        public static bool IsTypePlugin(this Type type, Type pluginType)
        {
            if (type.IsInterface || type.IsAbstract)
                return false;
            var name = pluginType.FullName;
            if (name == null)
                return false;
            if (type.GetInterface(name) == null)
                return false;
            return true;
        }
    }
}