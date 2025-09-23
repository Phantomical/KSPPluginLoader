using System;

namespace KSPPluginLoader;

/// <summary>
/// A base class for all dependency types
/// </summary>
/// <param name="name"></param>
/// <param name="version"></param>
public abstract class AssemblyDependency(string name, Version version)
{
    public string name = name;
    public Version version = version;

    public abstract bool IsSatisfied();

    internal static bool IsVersionCompatible(Version existing, Version required)
    {
        if (existing.Major != required.Major)
            return false;

        if (existing.Minor < required.Minor)
            return false;
        if (existing.Minor > required.Minor)
            return true;

        if (existing.Build < required.Build)
            return false;
        return true;
    }
}

/// <summary>
/// A dependency that requires a <see cref="KSPAssembly"/> with a matching
/// name and compatible version.
/// </summary>
public class KSPAssemblyDependency(string name, Version version) : AssemblyDependency(name, version)
{
    public override bool IsSatisfied()
    {
        var loaded = AssemblyLoader.loadedAssemblies;

        foreach (var assembly in loaded)
        {
            if (assembly.name != name)
                continue;

            var assemblyVersion = new Version(
                assembly.versionMajor,
                assembly.versionMajor,
                assembly.versionRevision
            );
            if (IsVersionCompatible(assemblyVersion, version))
                return true;
        }

        return false;
    }

    public static KSPAssemblyDependency Load(ConfigNode node)
    {
        string name = null;
        if (!node.TryGetValue("name", ref name))
            throw new Exception("KSP_ASSEMBLY_DEPENDENCY node was missing a `name` field");

        string vstr = "0.0.0";
        node.TryGetValue("version", ref vstr);
        var version = Version.Parse(vstr);

        return new(name, version);
    }
}

public class DirectAssemblyDependency(string name, Version version)
    : AssemblyDependency(name, version)
{
    public override bool IsSatisfied()
    {
        var domain = AppDomain.CurrentDomain;

        foreach (var assembly in domain.GetAssemblies())
        {
            var asmName = assembly.GetName();
            if (asmName.Name != name)
                continue;
            if (!IsVersionCompatible(asmName.Version, version))
                continue;

            return true;
        }

        return false;
    }

    public static DirectAssemblyDependency Load(ConfigNode node)
    {
        string name = null;
        if (!node.TryGetValue("name", ref name))
            throw new Exception("DIRECT_ASSEMBLY_DEPENDENCY node was missing a `name` field");

        string vstr = "0.0.0";
        node.TryGetValue("version", ref vstr);
        var version = Version.Parse(vstr);

        return new(name, version);
    }
}
