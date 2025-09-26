using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using UnityEngine;
using static AssemblyLoader;

namespace KSPPluginLoader;

internal interface IAssemblyConstraint
{
    bool IsSatisfied(LoadedAssembly assembly);
}

internal static class AttributeLoader
{
    internal static List<IAssemblyConstraint> LoadAttributes(AssemblyDefinition def)
    {
        List<IAssemblyConstraint> list = [];
        var assembly = typeof(AttributeLoader).Assembly;

        foreach (var attribute in def.CustomAttributes)
        {
            var type = assembly.GetType(attribute.AttributeType.FullName);
            if (!typeof(IAssemblyConstraint).IsAssignableFrom(type))
                continue;

            try
            {
                var instance = (IAssemblyConstraint)
                    Activator.CreateInstance(
                        type,
                        args: [.. attribute.ConstructorArguments.Select(arg => arg.Value)]
                    );

                list.Add(instance);
            }
            catch (Exception e)
            {
                Debug.LogError(
                    $"PluginLoader: Assembly attribute `{type.Name}` could not be parsed."
                );
                Debug.LogException(e);
                return null;
            }
        }

        return list;
    }
}

/// <summary>
/// Declares the upper bound on dependency versions that this assembly is
/// compatible with.
/// </summary>
///
/// <remarks>
/// This is useful for when you want to only be compatible with a certain range
/// of dependency versions. (e.g. one integration for versions 1.5 to 1.7, and
/// another for versions 1.7+).
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class KSPAssemblyDependencyMaxAttribute(
    string name,
    int major,
    int minor = 0,
    int revision = 0
) : Attribute(), IAssemblyConstraint
{
    public string Name = name;
    public int VersionMajor = major;
    public int VersionMinor = minor;
    public int VersionRevision = revision;

    public Version Version => new(VersionMajor, VersionMinor, VersionRevision);

    public bool IsSatisfied(LoadedAssembly assembly)
    {
        var dep = assembly.dependencies.Find(dep => dep.name == Name);
        if (dep == null)
            return false;

        var depVersion = new Version(dep.versionMajor, dep.versionMinor, dep.versionRevision);
        return depVersion < Version;
    }

    public override string ToString() => $"dependency '{Name}' has version less than {Version}";
}
