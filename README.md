# KSP Plugin Loader

This is a KSP mod that allows `KSPAssemblyDependency` attributes to be used to
depend on other KSP mods that do not have a `KSPAssembly` attribute.

If you are not a mod author then this will show as a dependency of some other
mod you have installed and you can otherwise ignore it.

## How to Use
1. Add a dependency on `KSPPluginLoader`.
   ```cs
   [assembly: KSPAssemblyDependency("KSPPluginLoader", 1, 0)]
   ```
   Dependencies on mods without a `KSPAssembly` attribute will not be resolved
   unless your mod DLL has this dependency.
2. Add `KSPAssemblyDependency` attributes for any other mods that you want to
   depend on. See the next section to pick the right version.

#### Figuring out the version to use
1. Open up `KSP.log` in your KSP directory.
2. Look up the `AssemblyLoader` log messages for your mod. They should look
   something like this:
   ```
   AssemblyLoader: Loading assembly at C:\KSP\GameData\ModuleManager.4.2.3.dll
   AssemblyLoader: KSPAssembly 'ModuleManager' V2.5.0
   ```
3. If there is a `KSPAssembly` message, then that's the version you should be
   using. If all your dependencies have a `KSPAssembly` message, then you
   don't actually need `KSPPluginLoader` (unless you're making use of the
   advanced use cases section below).
4. Otherwise, scroll on down to the section that starts with
   ```
   Mod DLLs found:
   ```
5. Find the entry for the mod you want to depend on. It should look something
   like this:
   ```
   KSPBurst v1.5.5.2
   Kopernicus v1.0.0.0 / v1.12.227.0
   ContractConfigurator v1.0.0.0 / v2.11.2.0 KSP-RO / v2.11.2.0
   ```
6. The version number you want is the _last_ version number after the mod
   name, since that is the version that `KSPPluginLoader` will use.
   You only need to care about the version number so you can ignore any text
   after the `+` in the version.


As an example, here's what the dependencies would look like for latest versions
of the three mods in step 5.
```cs
[assembly: KSPAssemblyDependency("KSPBurst", 1, 5, 5)]
[assembly: KSPAssemblyDependency("Kopernicus", 1, 12, 227)]
[assembly: KSPAssemblyDependency("ContractConfigurator", 2, 11, 2)]
```

## Advanced Use Cases
To support more advanced use cases, KSPPluginLoader also has some extra
assembly attributes you can use.

To use these you will need to actually depend on `KSPPluginLoader.dll`.
They also will not be checked unless your mod DLL has a `KSPAssemblyDependency`
attribute for `KSPPluginLoader`.

### Maximum dependency version constraint
Sometimes you have some code that only works with a small range of mod
versions. `KSPAssemblyDependency` only lets you set a lower bound, so this
mod introduces a `KSPAssemblyDependencyMax` assembly attribute that lets
you set an upper bound on the dependency version.

Declaring it works pretty much the same as `KSPAssemblyDependency`.
This would only allow your mod to be loaded if `SomeMod`'s version is
< 1.0.0.
```cs
[assembly: KSPAssemblyDependencyMax("SomeMod", 1, 0, 0)]
```

As an example, the following two attributes would ensure your mod is only
loaded if a version of `PersistentThrust` in the range v1.7.5 <= v < v1.8.0
is present:
```cs
using KSPPluginLoader;

[assembly: KSPAssemblyDependency("PersistentThrust", 1, 7, 5)]
[assembly: KSPAssemblyDependencyMax("PersistentThrust", 1, 8, 0)]
```

Some things to be aware of when using `KSPAssemblyDependencyMax`:
* It does not affect the order that assemblies are loaded in. Make sure to
  pair it with an appropriate `KSPAssemblyDependency` if your mod DLL actually
  uses the dependency DLL.
* If there are multiple DLLs with the same name are present then KSP will only
  load one of them. This happens before constraints like
  `KSPAssemblyDependencyMax` are taken into account and means that you cannot
  have multiple DLLs with the same `KSPAssembly` name that use 
  `KSPAssemblyDependencyMax` to only allow one to load. You can still make this
  work, however, as long as the DLLs have different names.

## Examples
### Depending on CryoTanks
CryoTanks contains DLL called `SimpleBoiloff.dll` but that DLL does not
have a `KSPAssembly` attribute. With this mod, however, you can ignore
that by adding the following to your `AssemblyInfo.cs`:
```cs
[assembly: KSPAssemblyDependency("SimpleBoiloff", 0, 2, 1)]
[assembly: KSPAssemblyDependency("KSPPluginLoader", 1, 0)]
```

### Loading a mod only for a specific set of KSP versions
Suppose we want to support multiple different KSP versions and have different
versions of our mod for different KSP versions. You can use a combination of
`KSPAssemblyDependency` and `KSPAssemblyDependencyMax` to represent this.
Suppose we only wanted to support KSP v1.8, for example:
```cs
[assembly: KSPAssemblyDependency("KSP", 1, 8)]
[assembly: KSPAssemblyDependencyMax("KSP", 1, 9)]
[assembly: KSPAssemblyDependency("KSPPluginLoader", 1, 0)]
```
