using System;
using System.Collections.Generic;
using System.Reflection;
using KSP.Testing;
using UnityEngine;

namespace KSPPluginLoader;

/// <summary>
/// This class gets constructed while KSP is actually loading assemblies.
/// We use it to get code running at that point in time.
/// <see cref="PluginLoader"/> takes care of the actual details of modifying
/// the list of assemblies to be loaded.
/// </summary>
internal sealed class PluginLoaderTestHijack : UnitTest
{
    public PluginLoaderTestHijack()
    {
        PluginLoader.LoadPlugins();
    }
}

/// <summary>
/// This just cleans up the test case once we're done using it to hijack the
/// loading process. That way there are no test cases registered (unless some
/// other mod includes one).
/// </summary>
[KSPAddon(KSPAddon.Startup.Instantly, once: true)]
internal sealed class CleanupTestHijack : MonoBehaviour
{
    const BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.NonPublic;
    static readonly FieldInfo TestsField = typeof(TestManager).GetField("tests", StaticFlags);
    static readonly FieldInfo TypesField = typeof(TestManager).GetField("types", StaticFlags);

    void Awake()
    {
        var tests = (List<UnitTest>)TestsField.GetValue(null);
        var types = (HashSet<Type>)TypesField.GetValue(null);

        tests?.RemoveAll(test => test is PluginLoaderTestHijack);
        types?.Remove(typeof(PluginLoaderTestHijack));
    }
}
