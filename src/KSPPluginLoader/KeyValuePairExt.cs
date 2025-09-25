using System.Collections.Generic;

namespace KSPPluginLoader;

internal static class KeyValuePairExt
{
    internal static void Deconstruct<K, V>(this KeyValuePair<K, V> pair, out K key, out V value)
    {
        key = pair.Key;
        value = pair.Value;
    }

    internal static KeyValuePair<K, V> Create<K, V>(K key, V value) => new(key, value);
}
