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

   If they have a `KSPAssembly` attribute then the version number within will
   be used, otherwise the version number of the assembly will be used.
