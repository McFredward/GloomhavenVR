using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace QuestWeaver.Runtime
{
    /// <summary>Activates precompiled hooks. Never constructs code or invokes patch bodies by reflection.</summary>
    public static class Registry
    {
        private sealed class Hook
        {
            internal string Class = "", Target = "", Patch = "", Kind = "", Owner = "";
            internal int Slot, Priority, Sequence;
            internal bool Active;
        }
        private static readonly object Sync = new object();
        private static readonly List<Hook> Hooks = new List<Hook>();
        private static readonly HashSet<Assembly> Loaded = new HashSet<Assembly>();
        private static Dictionary<string, int[]> Orders = new Dictionary<string, int[]>(StringComparer.Ordinal);
        private static int sequence;

        private static void Load(Assembly assembly)
        {
            lock (Sync)
            {
                if (Loaded.Contains(assembly)) return;
                if (Loaded.Any(a => a.GetName().Name == assembly.GetName().Name)) throw new InvalidOperationException("Two mod snapshots were loaded into one Quest process.");
                var parsed = new List<Hook>();
                using (Stream? stream = assembly.GetManifestResourceStream("QuestWeaver.Hooks.v1"))
                {
                    if (stream == null) throw new InvalidOperationException("Assembly has no compiled Quest hook manifest: " + assembly.FullName);
                    using (var reader = new StreamReader(stream))
                    {
                        string? line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            string[] row = line.Split('|');
                            if (row.Length != 6) throw new InvalidDataException("Invalid Quest hook manifest row.");
                            parsed.Add(new Hook { Class = row[0], Target = row[1], Patch = row[2], Kind = row[3], Priority = int.Parse(row[4], System.Globalization.CultureInfo.InvariantCulture), Slot = int.Parse(row[5], System.Globalization.CultureInfo.InvariantCulture) });
                        }
                    }
                }
                Hooks.AddRange(parsed);
                Loaded.Add(assembly);
            }
        }

        public static void PatchAll(object owner, Type patchClass)
        {
            Load(patchClass.Assembly);
            string className = patchClass.FullName!.Replace('+', '/');
            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            MethodInfo? prepare = patchClass.GetMethod("Prepare", flags, null, Type.EmptyTypes, null);
            if (prepare != null && prepare.Invoke(null, null) is bool prepared && !prepared) return;
            MethodInfo? selector = patchClass.GetMethod("TargetMethod", flags, null, Type.EmptyTypes, null)
                ?? patchClass.GetMethod("TargetMethods", flags, null, Type.EmptyTypes, null);
            HashSet<string>? selected = null;
            if (selector != null)
            {
                object? result = selector.Invoke(null, null);
                selected = new HashSet<string>(StringComparer.Ordinal);
                if (result is MethodBase one) selected.Add(Key(one));
                else if (result is IEnumerable<MethodBase> many) foreach (MethodBase method in many) if (method != null) selected.Add(Key(method));
            }
            lock (Sync)
            {
                Hook[] matches = Hooks.Where(h => h.Class == className).ToArray();
                if (matches.Length == 0) throw new NotSupportedException("No compiled Quest hooks for " + className);
                if (selected != null && selected.Any(target => !matches.Any(h => h.Target == target)))
                    throw new NotSupportedException("Runtime selector chose a target outside the compiled Quest closure: " + className);
                foreach (Hook hook in matches) if (selected == null || selected.Contains(hook.Target)) Activate(hook, Owner(owner));
                Rebuild();
            }
        }

        public static MethodInfo? Patch(object owner, MethodBase original, object? prefix, object? postfix, object? transpiler, object? finalizer, object? ilManipulator)
        {
            if (transpiler != null || ilManipulator != null) throw new NotSupportedException("Quest AOT registry does not accept runtime IL manipulation.");
            lock (Sync)
            {
                foreach (var pair in new[] { Tuple.Create("prefix", prefix), Tuple.Create("postfix", postfix), Tuple.Create("finalizer", finalizer) })
                {
                    if (pair.Item2 == null) continue;
                    MethodInfo patch = (MethodInfo)(pair.Item2.GetType().GetField("method")?.GetValue(pair.Item2)
                        ?? throw new ArgumentException("HarmonyMethod.method is unavailable."));
                    Load(patch.DeclaringType!.Assembly);
                    Hook? hook = Hooks.FirstOrDefault(h => h.Target == Key(original) && h.Patch == Key(patch) && h.Kind == pair.Item1);
                    if (hook == null) throw new NotSupportedException("Runtime patch has no compiled Quest target: " + Key(original) + " <- " + Key(patch));
                    Activate(hook, Owner(owner));
                }
                Rebuild();
            }
            // Harmony's generated wrapper result is unused by current callers. Returning the
            // actual precompiled original avoids suggesting that runtime code was constructed.
            return original as MethodInfo;
        }

        public static void UnpatchSelf(object owner)
        {
            lock (Sync)
            {
                string id = Owner(owner);
                foreach (Hook hook in Hooks) if (hook.Owner == id) hook.Active = false;
                Rebuild();
            }
        }

        private static void Activate(Hook hook, string owner)
        {
            if (hook.Active && hook.Owner == owner) return;
            if (hook.Active) throw new InvalidOperationException("A compiled hook cannot belong to two Harmony owners.");
            hook.Owner = owner; hook.Sequence = sequence++; hook.Active = true;
        }

        private static void Rebuild()
        {
            var replacement = new Dictionary<string, int[]>(StringComparer.Ordinal);
            foreach (IGrouping<string, Hook> group in Hooks.Where(h => h.Active).GroupBy(h => h.Target + "|" + h.Kind))
                replacement.Add(group.Key, group.OrderByDescending(h => h.Priority).ThenBy(h => h.Sequence).Select(h => h.Slot).ToArray());
            System.Threading.Volatile.Write(ref Orders, replacement);
        }

        /// <summary>Returns an immutable activation snapshot without allocation.</summary>
        public static int[] Ordered(string key)
        {
            return System.Threading.Volatile.Read(ref Orders).TryGetValue(key, out int[]? slots) ? slots : Empty;
        }
        private static readonly int[] Empty = new int[0];
        private static string Owner(object owner) => (string)(owner.GetType().GetProperty("Id")?.GetValue(owner, null)
            ?? throw new ArgumentException("Harmony owner ID is unavailable."));

        public static string Key(MethodBase method) => method.DeclaringType!.Assembly.GetName().Name + "/" + TypeKey(method.DeclaringType)
            + "::" + method.Name + "(" + string.Join(",", method.GetParameters().Select(p => TypeKey(p.ParameterType))) + ")";

        private static string TypeKey(Type type)
        {
            if (type.IsByRef) return TypeKey(type.GetElementType()!) + "&";
            if (type.IsPointer) return TypeKey(type.GetElementType()!) + "*";
            if (type.IsArray) return TypeKey(type.GetElementType()!) + "[" + new string(',', type.GetArrayRank() - 1) + "]";
            if (type.IsGenericType && !type.IsGenericTypeDefinition)
                return type.GetGenericTypeDefinition().FullName!.Replace('+', '/') + "<" + string.Join(",", type.GetGenericArguments().Select(TypeKey)) + ">";
            return type.FullName!.Replace('+', '/');
        }
    }
}
