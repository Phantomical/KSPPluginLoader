using KSP.Testing;

namespace KSPPluginLoader;

public sealed class UnitTestHijack : UnitTest
{
    public UnitTestHijack()
    {
        UnityEngine.Debug.Log("Hello from UnitTestHijack");
    }
}
