using System.Composition;
using System.Reflection;

namespace NetAgents.BuildTasks;

internal static class CompositionParts
{
    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    public static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            // Analyzer assemblies can embed optional helpers for older Roslyn interfaces.
            return exception.Types.OfType<Type>();
        }
    }

    // The SDK hands this formatter code fixes that only run inside an IDE: the ASP.NET Core
    // AddPackageFixer needs Microsoft.CodeAnalysis.ExternalAccess.AspNetCore, which ships with Visual
    // Studio instead of the SDK. MEF instantiates every attribute of an exporting part while the
    // container composes, so resolving that fixer's async state machine threw before any consumer source
    // was formatted. Repeating the same reflection here drops the single unusable export instead.
    public static CompositionSelection Select(IEnumerable<Assembly> assemblies)
    {
        List<Type> composable = [];
        List<string> skipped = [];

        foreach (Type type in assemblies.SelectMany(GetLoadableTypes))
        {
            if (CanCompose(type))
                composable.Add(type);
            else
                skipped.Add(type.FullName ?? type.Name);
        }

        return new([.. composable], [.. skipped]);
    }

    private static bool CanCompose(Type type)
    {
        try
        {
            Attribute[] attributes = Attribute.GetCustomAttributes(type, inherit: false);

            // Discovery reads every type's own attributes and stops there unless the type exports
            // something; only an exporting part has its members and their parameters inspected.
            if (!attributes.OfType<ExportAttribute>().Any())
                return true;

            ConstructorInfo[] constructors = type.GetConstructors(Declared);
            MemberInfo[] members = [.. constructors, .. type.GetProperties(Declared), .. type.GetMethods(Declared)];
            ParameterInfo[] parameters = [.. constructors.SelectMany(static constructor => constructor.GetParameters())];

            Attribute[] declared = [
                .. members.SelectMany(static member => Attribute.GetCustomAttributes(member, inherit: false)),
                .. parameters.SelectMany(static parameter => Attribute.GetCustomAttributes(parameter, inherit: false))];

            // Materializing them is the point: an attribute this runtime cannot build throws instead.
            return declared.All(static attribute => attribute is not null);
        }
        catch (Exception exception) when (IsUnresolvedMetadata(exception))
        {
            return false;
        }
    }

    private static bool IsUnresolvedMetadata(Exception exception)
    {
        return exception is TypeLoadException or FileNotFoundException or FileLoadException
            or BadImageFormatException or MissingMemberException or CustomAttributeFormatException;
    }

    internal sealed record CompositionSelection(Type[] Types, string[] Skipped);
}
