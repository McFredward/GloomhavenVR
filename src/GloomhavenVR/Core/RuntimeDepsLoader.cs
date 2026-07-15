using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace GloomhavenVR.Core;

/// <summary>
/// Loads the mod-shipped managed XR assemblies (<c>Unity.XR.Management</c>,
/// <c>Unity.XR.CoreUtils</c>, <c>Unity.XR.OpenXR</c>, …) from
/// <c>BepInEx/plugins/GloomhavenVR/RuntimeDeps/</c> via <see cref="Assembly.LoadFile"/> —
/// the LCVR <c>PreloadRuntimeDependencies</c> pattern. Must run before any method
/// that references XR types is JIT-compiled (Mono binds assembly references against
/// already-loaded assemblies by name).
/// </summary>
internal static class RuntimeDepsLoader
{
    private static bool _loaded;

    /// <summary>Idempotency guard: hooks must run at most once per process (hot reload!).</summary>
    private static bool _initializersInvoked;

    /// <summary>Directory the plugin assembly lives in (BepInEx/plugins/GloomhavenVR/).</summary>
    private static string PluginDir => Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;

    internal static string RuntimeDepsDir => Path.Combine(PluginDir, "RuntimeDeps");

    /// <summary>
    /// Load every DLL in RuntimeDeps/. Returns false (with an actionable log) when the
    /// directory is missing/empty or an assembly fails to load — callers must then leave
    /// VR off; the game keeps running flat.
    /// </summary>
    internal static bool LoadAll()
    {
        if (_loaded)
            return true;

        string dir = RuntimeDepsDir;
        if (!Directory.Exists(dir))
        {
            VRLog.Error("Core", $"RuntimeDeps directory not found: {dir} — VR unavailable. " +
                                "Re-install the mod (developers: scripts/build-runtimedeps.sh + scripts/deploy.ps1).");
            return false;
        }

        string[] files = Directory.GetFiles(dir, "*.dll");
        if (files.Length == 0)
        {
            VRLog.Error("Core", $"RuntimeDeps directory is empty: {dir} — VR unavailable.");
            return false;
        }

        // Safety net: resolve by simple name from the RuntimeDeps dir in case anything
        // requests one of our assemblies through a path Mono doesn't map to LoadFile'd ones.
        AppDomain.CurrentDomain.AssemblyResolve += ResolveFromRuntimeDeps;

        var loadedNames = new List<string>();
        var loadedAssemblies = new List<Assembly>();
        foreach (string file in files.OrderBy(f => f))
        {
            try
            {
                Assembly asm = Assembly.LoadFile(file);
                loadedNames.Add(asm.GetName().Name);
                loadedAssemblies.Add(asm);
            }
            catch (Exception e)
            {
                VRLog.Error("Core", $"Failed to load runtime dependency '{Path.GetFileName(file)}': {e}");
                return false;
            }
        }

        VRLog.Info("Core", $"Loaded {loadedNames.Count} runtime dependencies: {string.Join(", ", loadedNames)}");

        // Unity only invokes [RuntimeInitializeOnLoadMethod] statics for assemblies in the
        // build's ScriptingAssemblies.json — assemblies brought in via Assembly.LoadFile are
        // invisible to that scan, so we replay the contract ourselves (see below).
        InvokeRuntimeInitializers(loadedAssemblies);

        _loaded = true;
        return true;
    }

    /// <summary>
    /// Replays Unity's <c>[RuntimeInitializeOnLoadMethod]</c> pass over the LoadFile'd
    /// assemblies, in Unity's real execution order (SubsystemRegistration →
    /// AfterAssembliesLoaded → BeforeSplashScreen → BeforeSceneLoad → AfterSceneLoad).
    ///
    /// Inventory of the shipped RuntimeDeps (verified against the compiled DLLs with ILSpy,
    /// package versions per libs/RuntimeDeps/versions.json):
    ///  - Unity.XR.Management 4.5.0:
    ///      XRGeneralSettings.AttemptInitializeXRSDKOnLoad   (AfterAssembliesLoaded)
    ///      XRGeneralSettings.AttemptStartXRSDKOnBeforeSplashScreen (BeforeSplashScreen)
    ///    Both no-op unless XRGeneralSettings.Instance is non-null — which it never is at
    ///    this point (OpenXRBootstrap creates the instance later and drives
    ///    InitXRSDK()/Start() itself), so invoking them here is safe and side-effect free.
    ///  - Unity.XR.OpenXR 1.10.0: NO RuntimeInitializeOnLoadMethod at all. The critical
    ///    input-layout registration (OpenXRInput.RegisterLayouts →
    ///    OpenXRInteractionFeature.RegisterLayouts) runs inside
    ///    OpenXRLoaderBase.InitializeInternal(), i.e. is covered by our normal init path.
    ///    Static ctors (OpenXRRestarter, DiagnosticReport) run on demand via the CLR.
    ///  - Unity.XR.CoreUtils 2.x: none (XRLoggingUtils static ctor runs on demand).
    /// So today this loop is a correctness safety-net; it becomes load-bearing the moment a
    /// RuntimeDep with real hooks is added (e.g. XR Interaction Toolkit's input composites —
    /// LCVR invokes those manually in Plugin.Awake for exactly this reason).
    /// </summary>
    /// <summary>
    /// AppDomain-wide idempotency key: plugin statics reset on ScriptEngine hot reload
    /// (a fresh copy of this assembly is loaded) but the RuntimeDeps assemblies — and any
    /// side effects of their hooks — persist, so the guard must live on the AppDomain.
    /// </summary>
    private const string InitializersInvokedKey = "GloomhavenVR.RuntimeInitializersInvoked";

    private static void InvokeRuntimeInitializers(List<Assembly> assemblies)
    {
        if (_initializersInvoked || AppDomain.CurrentDomain.GetData(InitializersInvokedKey) is true)
        {
            VRLog.Debug("Core", "RuntimeInitializeOnLoad hooks already invoked this process — skipping.");
            return;
        }
        _initializersInvoked = true;
        AppDomain.CurrentDomain.SetData(InitializersInvokedKey, true);

        // Unity's real chronological order. Enum values are NOT in execution order
        // (SubsystemRegistration=4 ... AfterSceneLoad=1), hence the explicit list.
        UnityEngine.RuntimeInitializeLoadType[] executionOrder =
        [
            UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration,
            UnityEngine.RuntimeInitializeLoadType.AfterAssembliesLoaded,
            UnityEngine.RuntimeInitializeLoadType.BeforeSplashScreen,
            UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad,
            UnityEngine.RuntimeInitializeLoadType.AfterSceneLoad,
        ];

        var byLoadType = new Dictionary<UnityEngine.RuntimeInitializeLoadType, List<MethodInfo>>();
        foreach (Assembly asm in assemblies)
        {
            Type[] types;
            try
            {
                types = asm.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                types = e.Types.Where(t => t != null).ToArray()!;
                VRLog.Warn("Core", $"Some types in {asm.GetName().Name} failed to load while " +
                                   $"scanning for RuntimeInitializeOnLoad methods: {e.Message}");
            }

            foreach (Type type in types)
            foreach (MethodInfo method in type.GetMethods(
                         BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                var attr = method.GetCustomAttribute<UnityEngine.RuntimeInitializeOnLoadMethodAttribute>();
                if (attr == null || method.GetParameters().Length != 0)
                    continue;

                if (!byLoadType.TryGetValue(attr.loadType, out List<MethodInfo>? list))
                    byLoadType[attr.loadType] = list = [];
                list.Add(method);
            }
        }

        int total = byLoadType.Values.Sum(l => l.Count);
        VRLog.Info("Core", $"RuntimeDeps declare {total} [RuntimeInitializeOnLoadMethod] hook(s) " +
                           "(Unity never invokes these for LoadFile'd assemblies — invoking now).");

        foreach (UnityEngine.RuntimeInitializeLoadType loadType in executionOrder)
        {
            if (!byLoadType.TryGetValue(loadType, out List<MethodInfo>? methods))
                continue;

            foreach (MethodInfo method in methods)
            {
                string label = $"{method.DeclaringType!.FullName}.{method.Name} [{loadType}]";
                try
                {
                    method.Invoke(null, null);
                    VRLog.Info("Core", $"RuntimeInitializeOnLoad invoked: {label} — OK");
                }
                catch (Exception e)
                {
                    // Unwrap so the log shows the actual failure, not the reflection wrapper.
                    if (e is TargetInvocationException { InnerException: not null } tie)
                        e = tie.InnerException!;
                    VRLog.Warn("Core", $"RuntimeInitializeOnLoad invocation FAILED: {label} — {e}");
                }
            }
        }
    }

    private static Assembly? ResolveFromRuntimeDeps(object sender, ResolveEventArgs args)
    {
        try
        {
            string simpleName = new AssemblyName(args.Name).Name;
            string candidate = Path.Combine(RuntimeDepsDir, simpleName + ".dll");
            return File.Exists(candidate) ? Assembly.LoadFile(candidate) : null;
        }
        catch
        {
            return null;
        }
    }
}
