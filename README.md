# KSP Plugin Loader

This is a KSP mod that allows `KSPAssemblyDependency` attributes to be used to
depend on other KSP mods that do not have a `KSPAssembly` attribute.

If you are not a mod author then this will show as a dependency of some other
mod you have installed and you can otherwise ignore it.

## How to Use
1. Add the following assembly attribute to your mod
   ```cs
   [assembly: KSPAssemblyDependency("KSPPluginLoader", 1, 0)]
   ```
2. Add further `KSPAssemblyDependency` attributes for any other mods that you
   want to depend on, regardless of whether they have a `KSPAssembly` attribute
   or not.

#### Figuring out the version to use
Once KSP has loaded mods it prints a section that starts with
```
Mod DLLs found:
```
to `KSP.log`. Within, you'll see a list of DLL names and their versions in one
of the following formats:
```
KSPBurst v1.5.5.2
Kopernicus v1.0.0.0 / v1.12.227.0
ContractConfigurator v1.0.0.0 / v2.11.2.0 KSP-RO / v2.11.2.0
```
In each case, you want to use the _last_ version number that is printed in your
`KSPAssemblyAttribute`. So if you wanted to depend on the latest version (or any
newer version) of each of the mods above you would add these attributes to your
mod DLL:
```cs
[assembly: KSPAssemblyDependency("KSPBurst", 1, 5, 5)]
[assembly: KSPAssemblyDependency("Kopernicus", 1, 12, 227)]
[assembly: KSPAssemblyDependency("ContractConfigurator", 2, 11, 2)]
```

## Advanced Use Cases
This mod also introduces some new attributes you can use in order to express
more advanced dependency relationships.

To use these you will need to actually depend on the `KSPPluginLoader` DLL.

### `KSPAssemblyDependencyMax`
This allows you to specify the maximum version of a dependency that your mod
supports. If the loaded dependency version is >= than the one specified here
then your mod assembly will not be loaded.

As an example, the following two attributes would ensure your mod is only
loaded if a version of `PersistentThrust` in the range v1.7.5 <= v < v1.8.0
is present:
```cs
using KSPPluginLoader;

[assembly: KSPAssemblyDependency("PersistentThrust", 1, 7, 5)]
[assembly: KSPAssemblyDependencyMax("PersistentThrust", 1, 8, 0)]
```

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
