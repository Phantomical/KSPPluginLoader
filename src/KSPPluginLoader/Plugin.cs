using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace KSPPluginLoader;

public class Plugin()
{
    private static readonly string GameDataDirectory = Path.Combine(
        KSPUtil.ApplicationRootPath,
        "GameData"
    );

    public string path;
    public string key;
    public List<AssemblyDependency> dependencies = [];
    public HashSet<string> dependencyNames = [];
    public AssemblyLoader.LoadedAssembly loaded = null;

    public void Load(ConfigNode node)
    {
        if (!node.TryGetValue("path", ref path))
            throw new Exception("KSP_PLUGIN node did not have a `path` key");

        if (!node.TryGetValue("key", ref key))
            key = path;

        foreach (var dep in node.GetNodes("KSP_ASSEMBLY_DEPENDENCY"))
            dependencies.Add(KSPAssemblyDependency.Load(dep));

        foreach (var dep in node.GetNodes("DIRECT_ASSEMBLY_DEPENDENCY"))
            dependencies.Add(DirectAssemblyDependency.Load(dep));

        foreach (var dep in dependencies)
            dependencyNames.Add(dep.name);
    }

    public bool AreDependenciesSatisfied()
    {
        foreach (var dep in dependencies)
        {
            if (!dep.IsSatisfied())
                return false;
        }

        return true;
    }

    public void LoadAssembly()
    {
        var path = Path.Combine(GameDataDirectory, this.path);
        var assembly = Assembly.LoadFile(path);
        loaded = new AssemblyLoader.LoadedAssembly(
            assembly,
            assembly.Location,
            assembly.Location,
            null
        );

        UpdateAssemblyLoaderTypeCache(loaded);
        AssemblyLoader.loadedAssemblies.Add(loaded);

        Debug.Log($"[PluginLoader] Loaded plugin {assembly.FullName}");
    }

    /// <summary>
    /// KSP is already loading addons and adding new assemblies to the list
    /// now won't cause it to pick them up. This means we need to do it ourselves.
    /// </summary>
    public void StartAddons()
    {
        var assembly = loaded.assembly;
        foreach (var type in assembly.GetTypes())
        {
            if (!type.IsSubclassOf(typeof(MonoBehaviour)))
                continue;

            KSPAddon attribute = type.GetCustomAttributes<KSPAddon>(inherit: true).FirstOrDefault();
            if (attribute == null)
                continue;

            if (attribute.startup != KSPAddon.Startup.Instantly)
                continue;

            StartAddon(loaded, type, attribute);
        }
    }

    /// <summary>
    /// Vessel module handling happens before addons running so we need to
    /// duplicate our own copy of it here.
    /// </summary>
    /// <returns>The number of vessel modules that have been discovered</returns>
    public int RegisterVesselModules()
    {
        var assembly = loaded.assembly;
        var types = assembly.GetTypes();
        var count = 0;

        foreach (var type in types)
        {
            if (!type.IsSubclassOf(typeof(VesselModule)))
                continue;
            if (type == typeof(VesselModule))
                continue;

            try
            {
                var wrapper = new VesselModuleManager.VesselModuleWrapper(type);
                var gameObject = new GameObject("Temp");
                var module = gameObject.AddComponent(type) as VesselModule;
                if (module != null)
                {
                    wrapper.order = module.GetOrder();
                    Debug.Log(
                        $"VesselModules: Found VesselModule of type {type.Name} with order {wrapper.order}"
                    );
                    UnityEngine.Object.DestroyImmediate(module);
                }

                UnityEngine.Object.DestroyImmediate(gameObject);
                VesselModuleManager.Modules.Add(wrapper);
                count += 1;
            }
            catch (Exception e)
            {
                Debug.LogError(
                    $"VesselModules: Error getting order of VesselModule of type {type.Name} so it was not added. Exception: {e}"
                );
            }
        }

        return count;
    }

    /// <summary>
    /// KSP maintains a type cache for loaded assemblies that is used to
    /// answer things like <c>GetTypeByName</c>. We need to manually
    /// populate that here for the assemblies we are loading.
    /// </summary>
    private static void UpdateAssemblyLoaderTypeCache(AssemblyLoader.LoadedAssembly loaded)
    {
        var assembly = loaded.assembly;
        var loadedTypes = new AssemblyLoader.LoadedTypes();

        foreach (var type in assembly.GetTypes())
        {
            foreach (Type loadedType in AssemblyLoader.loadedTypes)
            {
                if (type.IsSubclassOf(loadedType) || type == loadedType)
                    loadedTypes.Add(loadedType, type);
            }
        }

        foreach (var (key, items) in loadedTypes)
        {
            foreach (Type item in items)
            {
                loaded.types.Add(key, item);
                loaded.typesDictionary.Add(key, item);
            }
        }
    }

    private static readonly MethodInfo StartAddonMethod = typeof(AddonLoader).GetMethod(
        "StartAddon",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
    );

    private static void StartAddon(AssemblyLoader.LoadedAssembly asm, Type type, KSPAddon addon)
    {
        StartAddonMethod.Invoke(AddonLoader.Instance, [asm, type, addon, addon.startup]);
    }
}
