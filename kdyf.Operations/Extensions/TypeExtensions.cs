using kdyf.Operations.Extensions.Attributes;

namespace kdyf.Operations.Extensions;
public static class TypeExtensions
{
    #region Get Friendly Type Name
    public static string GetFriendlyTypeName(this Type type, object? obj = null)
    {
        if (type.IsGenericType)
        {
            var genericType = type.GetGenericTypeDefinition();
            var genericArguments = type.GetGenericArguments();

            var genericArgsNames = GetFriendlyTypeNameFromTypes(genericArguments);
            var genericTypeDescription = genericType.Name.Substring(0, genericType.Name.IndexOf('`'));

            if (type.IsInterface && obj != null)
            {
                var genericObjType = obj.GetType();
                var attribute = genericObjType.GetCustomAttributes(typeof(OperationDescriptorAttribute), false).FirstOrDefault();
                if (attribute != null)
                {
                    genericTypeDescription = GetFriendlyTypeNameFromType(genericObjType);
                }
            }

            return $"{genericTypeDescription}<{genericArgsNames}>";
        }


        var descriptorAttr = type.GetCustomAttributes(typeof(OperationDescriptorAttribute), false).FirstOrDefault() as OperationDescriptorAttribute;

        return descriptorAttr != null ? descriptorAttr.Name! : type.Name;
    }

    private static string GetFriendlyTypeNameFromTypes(this Type[] types)
    {
        var names = new List<string>();
        foreach (var type in types)
            names.Add(GetFriendlyTypeNameFromType(type));

        return string.Join(", ", names);
    }
    private static string GetFriendlyTypeNameFromType(this Type type)
    {
        var argumentDescriptorAttr = type.GetCustomAttributes(typeof(OperationDescriptorAttribute), false).FirstOrDefault() as OperationDescriptorAttribute;

        return argumentDescriptorAttr != null ? argumentDescriptorAttr.Name! : type.Name;
    }
    #endregion
    #region Get Friendly Type Name and Description
    public static (string, string) GetFriendlyTypeNameAndDescription(this Type type, object? obj = null)
    {
        if (type.IsGenericType)
        {
            var genericType = type.GetGenericTypeDefinition();
            var genericArguments = type.GetGenericArguments();

            var genericArgsNames = GetFriendlyTypeNameAndDescriptionFromTypes(genericArguments);
            var genericTypeDescription = (genericType.Name.Substring(0, genericType.Name.IndexOf('`')), string.Empty);

            if (type.IsInterface && obj != null)
            {
                var genericObjType = obj.GetType();
                var attribute = genericObjType.GetCustomAttributes(typeof(OperationDescriptorAttribute), false).FirstOrDefault();
                if (attribute != null)
                {
                    genericTypeDescription = GetFriendlyTypeNameAndDescriptionFromType(genericObjType);
                }
            }


            return ($"{genericArgsNames.Item1}", $"{genericTypeDescription.Item2}, {genericArgsNames.Item2}");
        }

        return GetFriendlyTypeNameAndDescriptionFromType(type);
    }
    private static (string, string) GetFriendlyTypeNameAndDescriptionFromTypes(this Type[] types)
    {
        var namesAndDescriptions = types
        .Select(type => GetFriendlyTypeNameAndDescriptionFromType(type))
        ;

        var names = string.Join(", ", namesAndDescriptions.Select(x => x.Item1));
        var descriptions = string.Join(", ", namesAndDescriptions.Select(x => x.Item2));

        return (names, descriptions);
    }
    private static (string, string) GetFriendlyTypeNameAndDescriptionFromType(this Type type)
    {
        string name = type.Name;
        string description = string.Empty;

        var argumentDescriptorAttr = type.GetCustomAttributes(typeof(OperationDescriptorAttribute), false).FirstOrDefault() as OperationDescriptorAttribute;

        if (argumentDescriptorAttr != null)
        {
            name = argumentDescriptorAttr.Name!;
            description = argumentDescriptorAttr.Description!;
        }

        return (name, description);
    }
    #endregion
}
