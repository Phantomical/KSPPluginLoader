using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices.ComTypes;
using Mono.Cecil;
using UnityEngine;
using static AssemblyLoader;

namespace KSPPluginLoader;

internal static class PluginLoader
{
    const BindingFlags InstanceFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    const BindingFlags StaticFlags =
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    static readonly FieldInfo ListVersion = typeof(List<LoadedAssembly>).GetField(
        "_version",
        InstanceFlags
    );

    static readonly FieldInfo ListAssemblies = typeof(LoadedAssembyList).GetField(
        "assemblies",
        InstanceFlags
    );

    static readonly FieldInfo AvailableAssemblies = typeof(AssemblyLoader).GetField(
        "availableAssemblies",
        StaticFlags
    );

    static readonly MethodInfo SetDependenciesMet = typeof(LoadedAssembly)
        .GetProperty(nameof(LoadedAssembly.dependenciesMet))
        .SetMethod;

    public static void LoadPlugins()
    {
        var loadedAssemblies = AssemblyLoader.loadedAssemblies;
        var assemblies = (List<LoadedAssembly>)ListAssemblies.GetValue(loadedAssemblies);
        var version = (int)ListVersion.GetValue(assemblies);
        var availableAssemblies = (List<AssemblyInfo>)AvailableAssemblies.GetValue(null);

        // Everything <= this index has already been loaded and should not be
        // modified by us.
        var index = FindCurrentAssemblyIndex(assemblies);
        if (index < 0)
            throw new Exception("PluginLoader assembly not present in the list of assemblies");

        Debug.Log("PluginLoader: Updating assembly list");

        HashSet<LoadedAssembly> known = [.. assemblies];
        using (var guard = new SilenceLogging())
        {
            foreach (var file in GameDatabase.Instance.root.GetFiles(UrlDir.FileType.Assembly))
            {
                var configs = file.parent.GetConfigs("ASSEMBLY", file.name, recursive: false);
                var node = configs.FirstOrDefault()?.config;

                LoadPlugin(new FileInfo(file.fullPath), file.parent.url, node);
            }
        }

        var infos = CreateInfoMap(assemblies, availableAssemblies);
        for (int i = 0; i < assemblies.Count; ++i)
        {
            if (i <= index)
                continue;

            var assembly = assemblies[i];
            SetDependenciesMet.Invoke(assembly, [CheckDependencies(assembly, assemblies, infos)]);
        }

        var tail = new List<LoadedAssembly>(assemblies.Skip(index + 1));
        assemblies.RemoveRange(index + 1, assemblies.Count - (index + 1));
        tail.RemoveAll(assembly => loadedAssemblies.Contains(assembly.name));

        // We sort by path so that the new dependencies we are adding here are
        // in the same order they would be if KSP itself was actually loading
        // them.
        //
        // Note that we use linq here because List.Sort is not a stable sort.
        // If we don't do this we may end up printing extra log messages for
        // assemblies that have already been validated by KSP.
        tail = [.. tail.OrderBy(assembly => assembly.path)];

        var sorted = TSort(tail, loadedAssemblies);
        assemblies.AddRange(sorted);

        foreach (var assembly in assemblies)
        {
            if (known.Contains(assembly))
                continue;
            Debug.Log($"PluginLoader: Loading assembly at {assembly.path}");
        }

        ListVersion.SetValue(assemblies, version);
    }

    static int FindCurrentAssemblyIndex(List<LoadedAssembly> assemblies)
    {
        var assembly = typeof(PluginLoader).Assembly;
        var count = assemblies.Count;

        for (int i = 0; i < count; ++i)
        {
            if (assemblies[i].assembly == assembly)
                return i;
        }

        return -1;
    }

    static Dictionary<LoadedAssembly, AssemblyInfo> CreateInfoMap(
        List<LoadedAssembly> loadedAssemblies,
        List<AssemblyInfo> availableAssemblies
    )
    {
        Dictionary<LoadedAssembly, AssemblyInfo> infos = [];
        foreach (var loadedAssembly in loadedAssemblies)
        {
            foreach (var available in availableAssemblies)
            {
                if (available.name != loadedAssembly.name)
                    continue;

                infos.Add(loadedAssembly, available);
                break;
            }
        }

        return infos;
    }

    static bool CheckDependencies(
        LoadedAssembly assembly,
        List<LoadedAssembly> loadedAssemblies,
        Dictionary<LoadedAssembly, AssemblyInfo> infos
    )
    {
        bool allowNonKspDeps = DependsOnPluginLoader(assembly);
        bool satisfied = true;
        foreach (var dependency in assembly.dependencies)
        {
            bool couldHaveBeenSatisfied = false;

            foreach (var loadedAssembly in loadedAssemblies)
            {
                if (ReferenceEquals(assembly, loadedAssembly))
                    continue;
                if (loadedAssembly.name != dependency.name)
                    continue;

                if (!IsMissingKSPAssembly(loadedAssembly))
                {
                    if (!IsVersionCompatible(loadedAssembly, dependency))
                        continue;
                }
                else
                {
                    if (!infos.TryGetValue(loadedAssembly, out var info))
                        continue;

                    if (!IsVersionCompatible(info.assemblyVersion, dependency))
                        continue;

                    if (!allowNonKspDeps)
                    {
                        couldHaveBeenSatisfied = true;
                        continue;
                    }
                }

                dependency.met = true;
                assembly.deps.Add(loadedAssembly);
                goto SATISFIED;
            }

            if (couldHaveBeenSatisfied)
            {
                Debug.LogWarning(
                    $"PluginLoader: Assembly '{assembly.name}' could have met dependency "
                        + $"'{dependency.name}' V{dependency.versionMajor}.{dependency.versionMinor}.{dependency.versionRevision} "
                        + $"if it had a KSPAssemblyDependency on KSPPluginLoader"
                );
            }
            else
            {
                Debug.LogWarning(
                    $"PluginLoader: Assembly '{assembly.name}' has not met dependency "
                        + $"'{dependency.name}' V{dependency.versionMajor}.{dependency.versionMinor}.{dependency.versionRevision}"
                );
            }

            satisfied = false;

            SATISFIED:
            ;
        }

        return satisfied;
    }

    static bool IsVersionCompatible(LoadedAssembly assembly, AssemblyDependency dependency)
    {
        var version = new Version(
            assembly.versionMajor,
            assembly.versionMinor,
            assembly.versionRevision
        );

        return IsVersionCompatible(version, dependency);
    }

    static bool IsVersionCompatible(Version version, AssemblyDependency dependency)
    {
        if (version.Major < dependency.versionMajor)
            return false;
        if (version.Major > dependency.versionMajor)
            return !dependency.requireEqualMajor;
        if (version.Minor < dependency.versionMinor)
            return false;
        if (version.Minor > dependency.versionMinor)
            return true;
        return version.Build >= dependency.versionRevision;
    }

    static bool IsMissingKSPAssembly(LoadedAssembly assembly) =>
        assembly.versionMajor == 0 && assembly.versionMinor == 0 && assembly.versionRevision == 0;

    static bool DependsOnPluginLoader(LoadedAssembly assembly)
    {
        foreach (var dep in assembly.dependencies)
        {
            if (dep.name == "KSPPluginLoader")
                return true;
        }

        return false;
    }

    static List<LoadedAssembly> TSort(
        IEnumerable<LoadedAssembly> source,
        IEnumerable<LoadedAssembly> loadedAssemblies
    )
    {
        List<LoadedAssembly> sorted = [];
        Dictionary<string, bool> visited = [];

        foreach (var assembly in loadedAssemblies)
            visited[assembly.path] = assembly.dependenciesMet;

        foreach (var assembly in source)
            Visit(assembly, visited, sorted);

        return sorted;
    }

    static bool Visit(
        LoadedAssembly assembly,
        Dictionary<string, bool> visited,
        List<LoadedAssembly> sorted
    )
    {
        if (visited.TryGetValue(assembly.path, out var depsMet))
            return depsMet;

        visited[assembly.path] = false;
        if (!assembly.dependenciesMet)
            return false;

        foreach (var dep in assembly.deps)
        {
            if (!Visit(dep, visited, sorted))
            {
                Debug.LogWarning(
                    $"PluginLoader: Assembly '{assembly.name}' has not met dependency "
                        + $"'{dep.name}' V{dep.versionMajor}.{dep.versionMinor}.{dep.versionRevision}"
                );

                return false;
            }
        }

        if (DependsOnPluginLoader(assembly))
        {
            var constraints = LoadAssemblyConstraints(assembly);
            if (constraints is null)
            {
                Debug.LogWarning(
                    $"PluginLoader: Assembly '{assembly.name}' is not being loaded because its attributes could not be parsed."
                );
                return false;
            }

            foreach (var constraint in constraints)
            {
                if (!constraint.IsSatisfied(assembly))
                {
                    Debug.LogWarning(
                        $"PluginLoader: Assembly '{assembly.name}' is not being loaded because constraint `{constraint}` is not satisfied."
                    );
                    return false;
                }
            }
        }

        sorted.Add(assembly);
        visited[assembly.path] = true;
        return true;
    }

    static List<IAssemblyConstraint> LoadAssemblyConstraints(LoadedAssembly assembly)
    {
        var def = AssemblyDefinition.ReadAssembly(assembly.path);
        return AttributeLoader.LoadAttributes(def);
    }

    static void LogAssemblies(string header, List<LoadedAssembly> assemblies)
    {
        Debug.Log($"PluginLoader: {header}");

        foreach (var assembly in assemblies)
        {
            Debug.Log(
                $"PluginLoader: - {assembly.name} V{assembly.versionMajor}.{assembly.versionMinor}.{assembly.versionRevision}"
            );
        }
    }

    readonly struct SilenceLogging : IDisposable
    {
        readonly LogType filter = Debug.unityLogger.filterLogType;

        public SilenceLogging() => Debug.unityLogger.filterLogType = LogType.Warning;

        public void Dispose() => Debug.unityLogger.filterLogType = filter;
    }
}
