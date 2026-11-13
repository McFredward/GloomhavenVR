using Mono.Cecil;

namespace QuestWeaver;

/// <summary>Import target metadata without creating host CLR or self-assembly dependencies.</summary>
internal static class TargetMetadata
{
    internal static readonly IMetadataImporterProvider Importer = new ImporterProvider();
    internal sealed class Resolver : DefaultAssemblyResolver
    {
        public override AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters)
        {
            parameters.MetadataImporterProvider = Importer;
            return base.Resolve(name, parameters);
        }
    }

    sealed class ImporterProvider : IMetadataImporterProvider
    {
        public IMetadataImporter GetMetadataImporter(ModuleDefinition module) => new ImporterForModule(module);
    }

    sealed class ImporterForModule : DefaultMetadataImporter
    {
        readonly ModuleDefinition target;
        internal ImporterForModule(ModuleDefinition module) : base(module) { target = module; }
        protected override IMetadataScope ImportScope(TypeReference type)
        {
            // A hook's parameter/return type may refer back to the assembly being
            // woven. Cecil's default importer turns that reference into an
            // AssemblyRef to itself. Unity rejects the whole plugin, hiding all
            // its MonoScripts and causing a false serialized-roster mismatch.
            // Bind only the exact same assembly identity to its local module;
            // a same-named but different-version dependency remains external.
            string? identity = type.Scope switch {
                AssemblyNameReference assembly => assembly.FullName,
                ModuleDefinition module => module.Assembly?.Name.FullName,
                _ => null
            };
            return identity != null && identity == target.Assembly?.Name.FullName ? target : base.ImportScope(type);
        }
    }

    internal static MethodReference RuntimeMethod(ModuleDefinition target, string typeName, string method)
    {
        // Reflection MethodInfo carries System.Private.CoreLib from the build
        // host even when the method was compiled for netstandard2.0. Import
        // the original target-compatible metadata instead, just as Registry
        // imports do. Never add the host core library to a Unity2021 plugin.
        using var runtime = AssemblyDefinition.ReadAssembly(typeof(Runtime.OfflineProfile).Assembly.Location);
        return target.ImportReference(runtime.MainModule.GetType(typeName).Methods.Single(m => m.Name == method));
    }
}
