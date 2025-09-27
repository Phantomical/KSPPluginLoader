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

internal class PluginLoader
{
    const BindingFlags InstanceFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    const BindingFlags StaticFlags =
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    static readonly FieldInfo ListVersionField = typeof(List<LoadedAssembly>).GetField(
        "_version",
        InstanceFlags
    );

    static readonly FieldInfo ListAssembliesField = typeof(LoadedAssembyList).GetField(
        "assemblies",
        InstanceFlags
    );

    static readonly FieldInfo AvailableAssembliesField = typeof(AssemblyLoader).GetField(
        "availableAssemblies",
        StaticFlags
    );

    static readonly MethodInfo SetDependenciesMet = typeof(LoadedAssembly)
        .GetProperty(nameof(LoadedAssembly.dependenciesMet))
        .SetMethod;

    static readonly MethodInfo LoadExternalAssemblyMethod = typeof(AssemblyLoader).GetMethod(
        "LoadExternalAssembly",
        StaticFlags
    );

    readonly List<LoadedAssembly> assemblies;
    readonly List<AssemblyInfo> availableAssemblies;
    List<PluginInfo> plugins = [];
    int version;
    int index;
    HashSet<LoadedAssembly> known;

    private PluginLoader()
    {
        assemblies = (List<LoadedAssembly>)ListAssembliesField.GetValue(loadedAssemblies);
        version = (int)ListVersionField.GetValue(assemblies);
        availableAssemblies = (List<AssemblyInfo>)AvailableAssembliesField.GetValue(null);
        known = [.. assemblies];

        // Everything <= this index has already been loaded and should not be
        // modified by us.
        index = FindCurrentAssemblyIndex(assemblies);
        if (index < 0)
            throw new Exception("PluginLoader assembly not present in the list of assemblies");
    }

    public static void LoadPlugins()
    {
        new PluginLoader().LoadPluginsImpl();
    }

    void LoadPluginsImpl()
    {
        Debug.Log("PluginLoader: Updating assembly list");

        using (var guard = new SilenceLogging())
        {
            foreach (var assembly in assemblies)
                plugins.Add(PluginInfo.Load(assembly));

            foreach (var file in GameDatabase.Instance.root.GetFiles(UrlDir.FileType.Assembly))
            {
                var configs = file.parent.GetConfigs("ASSEMBLY", file.name, recursive: false);
                var node = configs.FirstOrDefault()?.config;

                var plugin = PluginInfo.Load(new FileInfo(file.fullPath), file.parent.url, node);
                if (plugin is null)
                    continue;

                plugins.Add(plugin);
            }

            foreach (var plugin in plugins)
                plugin.info = availableAssemblies.Find(info => info.name == plugin.assembly.name);

            // This ensures that plugins are processed in the order that they would
            // have been discovered by KSP if it supported all our features natively.
            //
            // We use OrderBy because it is a stable sort and that ensures that the
            // plugins that have already been loaded will be ordered before any
            // duplicate copy we read out of the config nodes.
            plugins = [.. plugins.OrderBy(plugin => plugin.Path)];
        }

        var sorted = TSort();
        assemblies.RemoveRange(index + 1, assemblies.Count - (index + 1));
        assemblies.AddRange(sorted);

        ListVersionField.SetValue(assemblies, version);

        foreach (var assembly in assemblies)
        {
            if (known.Contains(assembly))
                continue;
            Debug.Log($"PluginLoader: Loading assembly at {assembly.path}");
        }
    }

    readonly Dictionary<string, PluginInfo> visited = [];

    List<LoadedAssembly> TSort()
    {
        visited.Clear();
        List<LoadedAssembly> sorted = new(plugins.Count);
        foreach (var plugin in plugins)
            VisitPlugin(plugin, sorted);
        return sorted;
    }

    PluginInfo VisitPlugin(PluginInfo plugin, List<LoadedAssembly> sorted)
    {
        if (visited.TryGetValue(plugin.Name, out var assembly))
            return assembly;

        assembly = VisitPluginImpl(plugin, sorted);
        visited[plugin.Name] = assembly;
        return assembly;
    }

    PluginInfo VisitPluginImpl(PluginInfo plugin, List<LoadedAssembly> sorted)
    {
        // If this plugin has already been loaded then we cannot change that
        // we should always use it.
        if (plugin.assembly.assembly is not null)
            return plugin;

        var assembly = plugin.assembly;
        assembly.deps.Clear();

        bool satisfied = true;
        bool allowNonKspDeps = DependsOnPluginLoader(assembly);
        foreach (var dependency in assembly.dependencies)
        {
            bool couldHaveBeenSatisfied = false;
            var dep = VisitPluginDependency(dependency.name, sorted);
            if (dep is null)
                goto UNSATISFIED;

            if (!IsMissingKSPAssembly(dep.assembly))
            {
                if (!IsVersionCompatible(dep.assembly, dependency))
                    goto UNSATISFIED;
            }
            else
            {
                if (plugin.info is null)
                    goto UNSATISFIED;

                if (!IsVersionCompatible(dep.Version, dependency))
                    goto UNSATISFIED;

                if (!allowNonKspDeps)
                {
                    couldHaveBeenSatisfied = true;
                    goto UNSATISFIED;
                }
            }

            assembly.deps.Add(dep.assembly);
            continue;

            UNSATISFIED:
            satisfied = false;

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
        }

        if (!satisfied)
            return null;

        if (allowNonKspDeps)
        {
            if (plugin.constraints is null)
            {
                Debug.LogWarning(
                    $"PluginLoader: Assembly '{assembly.name}' is not being loaded because its attributes could not be parsed."
                );
                return null;
            }

            foreach (var constraint in plugin.constraints)
            {
                if (constraint.IsSatisfied(assembly))
                    continue;

                Debug.LogWarning(
                    $"PluginLoader: Assembly '{assembly.name}' is not being loaded because constraint `{constraint}` is not satisfied."
                );
                return null;
            }
        }

        sorted.Add(assembly);
        return plugin;
    }

    PluginInfo VisitPluginDependency(string name, List<LoadedAssembly> sorted)
    {
        List<PluginInfo> matching =
        [
            .. plugins
                .Where(plugin => plugin.assembly.name == name)
                // We use OrderBy because it is a stable sort
                .OrderBy(plugin => plugin.Version, InverseVersionComparer.Instance),
        ];

        // Unconditionally prefer any plugins that are already loaded since we
        // cannot unload them.
        var loaded = matching.Find(plugin => plugin.assembly.assembly is not null);
        if (loaded is not null)
            return VisitPlugin(loaded, sorted);

        foreach (var plugin in matching)
        {
            var res = VisitPlugin(plugin, sorted);
            if (res is not null)
                return res;
        }

        return null;
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

    static bool LoadExternalAssembly(string path) =>
        (bool)LoadExternalAssemblyMethod.Invoke(null, [path]);

    readonly struct SilenceLogging : IDisposable
    {
        readonly LogType filter = Debug.unityLogger.filterLogType;

        public SilenceLogging() => Debug.unityLogger.filterLogType = LogType.Warning;

        public void Dispose() => Debug.unityLogger.filterLogType = filter;
    }

    class PluginInfo
    {
        public LoadedAssembly assembly;
        public AssemblyInfo info;
        public List<IAssemblyConstraint> constraints;

        public Version Version
        {
            get
            {
                if (
                    assembly.versionMajor == 0
                    && assembly.versionMinor == 0
                    && assembly.versionRevision == 0
                )
                    return info?.assemblyVersion ?? default;
                return new(assembly.versionMajor, assembly.versionMinor, assembly.versionRevision);
            }
        }
        public string Name => assembly.name;
        public string Path => assembly.path;

        public static PluginInfo Load(FileInfo file, string url, ConfigNode assemblyNode)
        {
            if (!LoadExternalAssembly(file.FullName))
                return null;

            LoadedAssembly assembly;
            try
            {
                assembly = new(null, file.FullName, url, assemblyNode);
            }
            catch (Exception ex)
            {
                string text = ex.ToString();
                if (ex is ReflectionTypeLoadException exception)
                {
                    text += "\n\nAdditional information about this exception:";
                    Exception[] loaderExceptions = exception.LoaderExceptions;
                    foreach (Exception ex2 in loaderExceptions)
                    {
                        text = text + "\n\n " + ex2.ToString();
                    }
                }
                Debug.LogError($"Exception when loading {file.FullName}: {text}");
                return null;
            }

            return Load(assembly);
        }

        public static PluginInfo Load(LoadedAssembly assembly)
        {
            List<IAssemblyConstraint> constraints = null;
            if (DependsOnPluginLoader(assembly))
            {
                constraints = LoadAssemblyConstraints(assembly);
                if (constraints is null)
                    return null;
            }

            return new() { assembly = assembly, constraints = constraints ?? [] };
        }
    }

    class InverseVersionComparer : IComparer<Version>
    {
        public static readonly InverseVersionComparer Instance = new();

        private InverseVersionComparer() { }

        public int Compare(Version x, Version y) => -x.CompareTo(y);
    }
}
