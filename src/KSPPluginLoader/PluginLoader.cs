using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace KSPPluginLoader;

// Loading Plugins in KSP
// ======================
// When loading custom plugins in KSP we want to guarantee a few things for
// those plugins:
// 1. They are loaded just like KSP would load binaries. VesselModules,
//    PartModules, and KSPAddons should work normally.
// 2. Any KSPAddons in plugin binaries are sequenced _after_ those that were
//    loaded directly from disk.
//
// As it turns out, this is somewhat tricky to do, for one main reason:
// AddonLoader.StartAddons does not pick up addons from assemblies that are
// added while it is running.
//
// We could patch the method to fix this but that would likely break any other
// mod that loads libraries dynamically.
//
// Instead, we use a bit of a hack. KSP loads VesselModules before it starts
// any KSPAddons so we load the plugin binaries within a static constructor
// for a VesselModule class. This ensures that they are added to
// AssemblyLoader.loadedAssemblies _before_ AssemblyLoader.StartAddons is
// called, so everything works normally from there.
//
// Then, in PluginLoader.Awake all we need to do is register any VesselModules
// in the plugin assemblies and remove the dummy one we added from the list.

[KSPAddon(KSPAddon.Startup.Instantly, true)]
internal sealed class PluginLoader : MonoBehaviour
{
    static List<Plugin> Loaded = [];

    internal static void Preinit()
    {
        LoadPlugins();
    }

    void Awake()
    {
        CleanUpVesselModuleHack();
        LoadVesselModules();

        Loaded = null;

        // And finally, we clean up after ourselves
        DestroyImmediate(this);
    }

    private static void CleanUpVesselModuleHack()
    {
        var modules = VesselModuleManager.Modules;
        for (int i = 0; i < modules.Count; ++i)
        {
            var module = modules[i];
            if (module.type != typeof(PluginLoaderStaticConstructorHack))
                continue;

            modules.RemoveAt(i);
            break;
        }
    }

    private static void LoadVesselModules()
    {
        // KSP has already enumerated vessel modules so we need to add any
        // new ones ourselves.
        var numVesselModules = 0;
        foreach (var plugin in Loaded)
            numVesselModules += plugin.RegisterVesselModules();

        if (numVesselModules != 0)
            Debug.Log(
                $"[PluginLoader] VesselModules: Found {numVesselModules} additional vessel modules"
            );
    }

    private static void LoadPlugins()
    {
        var plugins = LoadPluginConfigs();
        var queue = new PriorityQueue<Plugin, string>(plugins.Count);
        Loaded = new List<Plugin>(plugins.Count);

        // A list of known dependency identifiers.
        var known = new HashSet<string>(
            plugins.SelectMany(plugin => plugin.dependencies.Select(dep => dep.name))
        );

        plugins.RemoveAll(plugin =>
        {
            if (!plugin.AreDependenciesSatisfied())
                return false;

            queue.Enqueue(plugin, plugin.key);
            return true;
        });

        while (queue.TryDequeue(out var plugin, out var _))
        {
            try
            {
                plugin.LoadAssembly();
                Loaded.Add(plugin);
            }
            catch (ReflectionTypeLoadException e)
            {
                Debug.LogError($"[PluginLoader] Failed to load plugin {plugin.path}");
                Debug.LogException(e);
                StringBuilder message = new("Additional information:");
                foreach (Exception inner in e.LoaderExceptions)
                {
                    message.Append("\n");
                    message.Append(inner);
                }
                Debug.LogError(message);
                continue;
            }
            catch (Exception e)
            {
                Debug.LogError($"[PluginLoader] Failed to load plugin {plugin.path}");
                Debug.LogException(e);
                continue;
            }

            if (
                !known.Contains(plugin.loaded.name)
                && !known.Contains(plugin.loaded.assembly.FullName)
            )
                continue;

            plugins.RemoveAll(plugin =>
            {
                if (!plugin.AreDependenciesSatisfied())
                    return false;

                queue.Enqueue(plugin, plugin.key);
                return true;
            });
        }
    }

    private static List<Plugin> LoadPluginConfigs()
    {
        var nodes = GameDatabase.Instance.GetConfigNodes("PLUGIN_DLL");
        var plugins = new List<Plugin>(nodes.Length);

        foreach (var node in nodes)
        {
            try
            {
                var plugin = new Plugin();
                plugin.Load(node);
                plugins.Add(plugin);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to load PLUGIN_DLL config node {node.id}");
                Debug.LogException(e);
            }
        }

        return plugins;
    }
}

/// <summary>
/// This is a very ugly hack in order to run code before any KSPAddon is able
/// to run. VesselModules are instantiated in order to get their order, so we
/// can take advantage of that to run some code in a static constructor.
/// </summary>
internal class PluginLoaderStaticConstructorHack : VesselModule
{
    static PluginLoaderStaticConstructorHack()
    {
        // PluginLoader.Preinit();
    }
}
