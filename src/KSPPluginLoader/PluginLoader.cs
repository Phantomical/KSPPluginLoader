using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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

        using (var guard = new SilenceLogging())
        {
            foreach (var file in GameDatabase.Instance.root.GetFiles(UrlDir.FileType.Assembly))
            {
                var configs = file.parent.GetConfigs("ASSEMBLY", file.name, recursive: false);
                var node = configs.FirstOrDefault()?.config;

                LoadPlugin(new FileInfo(file.fullPath), file.parent.url, node);
            }

            for (int i = 0; i < assemblies.Count; ++i)
            {
                if (i <= index)
                    continue;

                var assembly = assemblies[i];
                SetDependenciesMet.Invoke(
                    assembly,
                    [CheckDependencies(assembly, assemblies, availableAssemblies)]
                );
            }

            var tail = new List<LoadedAssembly>(assemblies.Skip(index + 1));
            assemblies.RemoveRange(index + 1, assemblies.Count - (index + 1));
            tail.RemoveAll(assembly => loadedAssemblies.Contains(assembly.name));

            var sorted = TSort(tail, loadedAssemblies);
            assemblies.AddRange(sorted);
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

    static bool CheckDependencies(
        LoadedAssembly assembly,
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

        foreach (var dependency in assembly.dependencies)
        {
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
                }

                dependency.met = true;
                assembly.deps.Add(loadedAssembly);
                goto SATISFIED;
            }

            return false;

            SATISFIED:
            ;
        }

        return true;
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
                return false;
        }

        sorted.Add(assembly);
        visited[assembly.path] = true;
        return true;
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
