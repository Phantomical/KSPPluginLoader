using KSP.Testing;

namespace KSPPluginLoader;

internal sealed class PluginLoaderTestHijack : UnitTest
{
    public PluginLoaderTestHijack()
    {
        PluginLoader.LoadPlugins();
    }
}
