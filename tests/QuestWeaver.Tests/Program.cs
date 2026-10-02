using System.Reflection;
using System.Runtime.Loader;
using Mono.Cecil;
using QuestWeaver;

string projectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
string fixtureDir = Path.Combine(projectRoot, "tests/QuestWeaver.Tests/FixtureMod/bin/Debug/net8.0");
string temp = Path.Combine(Path.GetTempPath(), "quest-weaver-tests-" + Guid.NewGuid().ToString("N"));
int assertions = 0;
void Check(bool condition, string message) { assertions++; if (!condition) throw new Exception(message); }
try
{
    using (Discovery model = Discovery.Load(Path.Combine(fixtureDir, "FixtureMod.dll"), fixtureDir))
    {
        AuditReport report = model.Audit();
        Check(report.Issues.Count == 0, "Fixture unexpectedly blocked: " + string.Join(";", report.Issues));
        Check(report.Hooks.Count == 9 && report.FieldHelpers.Count == 1, "Fixture hook/helper coverage changed.");
        new Weaver(model).Write(temp, report, false);
        Check(report.Complete && report.WovenTargets == 3, "Fixture integration is incomplete.");
    }
    var context = new AssemblyLoadContext("woven-fixture", isCollectible: true);
    context.Resolving += (_, name) => name.Name == "QuestWeaver.Runtime" ? typeof(QuestWeaver.Runtime.Registry).Assembly
        : File.Exists(Path.Combine(temp, name.Name + ".dll")) ? context.LoadFromAssemblyPath(Path.Combine(temp, name.Name + ".dll")) : null;
    Assembly game = context.LoadFromAssemblyPath(Path.Combine(temp, "FixtureGame.dll"));
    Assembly mod = context.LoadFromAssemblyPath(Path.Combine(temp, "FixtureMod.dll"));
    Type targetType = game.GetType("FixtureGame.Target")!;
    object target = Activator.CreateInstance(targetType)!;
    MethodInfo calculate = targetType.GetMethod("Calculate")!;
    Type entry = mod.GetType("FixtureMod.Entry")!;
    var trace = (List<string>)targetType.GetField("Trace")!.GetValue(null)!;
    object?[] input = { 3 };
    Check((int)calculate.Invoke(target, input)! == 15 && (int)input[0]! == 4, "Inactive wrapper changed vanilla behavior.");
    entry.GetMethod("Install")!.Invoke(null, null); trace.Clear(); input[0] = 3;
    Check((int)calculate.Invoke(target, input)! == 29 && (int)input[0]! == 6, "State/result/ref argument behavior is wrong.");
    Check(string.Join(",", trace) == "prefix,simple-prefix,original,postfix:Calculate,finalizer", "Normal hook ordering is wrong.");
    trace.Clear(); input[0] = -1;
    Check((int)calculate.Invoke(target, input)! == 82 && (int)input[0]! == -1, "Skipped original result/state is wrong.");
    Check(string.Join(",", trace) == "prefix,simple-prefix,postfix:Calculate,finalizer", "Skipped original did not run the simple prefix/postfix/finalizer.");
    trace.Clear(); input[0] = 901;
    Check((int)calculate.Invoke(target, input)! == 92, "Finalizer did not suppress original exception with preserved state.");
    Check(string.Join(",", trace) == "prefix,simple-prefix,original,finalizer", "Exceptional original should skip postfix.");
    input[0] = -201;
    try { calculate.Invoke(target, input); Check(false, "Original exception was lost."); }
    catch (TargetInvocationException e) { Check(e.InnerException is ArgumentException && e.InnerException.Message == "preserved-failure", "Wrong preserved exception."); }
    entry.GetMethod("SetCounter")!.Invoke(null, new object[] { target, 40 });
    Check((int)targetType.GetProperty("Counter")!.GetValue(target)! == 40, "Generated FieldRef delegate did not mutate the private field.");
    entry.GetMethod("Remove")!.Invoke(null, null); trace.Clear(); input[0] = 1;
    Check((int)calculate.Invoke(target, input)! == 43, "UnpatchSelf did not restore vanilla behavior.");
    Check(string.Join(",", trace) == "original", "Inactive hooks remained active.");
    entry.GetMethod("InstallDynamic")!.Invoke(null, null);
    Check((int)targetType.GetMethod("Dynamic")!.Invoke(target, new object[] { 2 })! == 14, "Metadata-derived dynamic target did not run compiled hooks.");
    Check((int)entry.GetField("Preparations")!.GetValue(null)! == 1 && (int)entry.GetField("Selections")!.GetValue(null)! == 1, "Prepare/TargetMethod side effects were lost.");
    entry.GetMethod("InstallDirect")!.Invoke(null, null); input[0] = 2;
    Check((int)targetType.GetMethod("Direct")!.Invoke(target, input)! == 11 && (int)input[0]! == 9, "Direct custom-named prefix/postfix/finalizer registration failed.");
    entry.GetMethod("Remove")!.Invoke(null, null);
    Check((int)targetType.GetMethod("Dynamic")!.Invoke(target, new object[] { 2 })! == 4, "Dynamic target remained active after UnpatchSelf.");
    // A real negative control removes the referenced private field in a fresh input copy.
    string broken = temp + "-broken"; Directory.CreateDirectory(broken);
    File.Copy(Path.Combine(fixtureDir, "FixtureMod.dll"), Path.Combine(broken, "FixtureMod.dll"));
    using (AssemblyDefinition original = AssemblyDefinition.ReadAssembly(Path.Combine(fixtureDir, "FixtureGame.dll")))
    { var t = original.MainModule.GetType("FixtureGame.Target"); t.Fields.First(f => f.Name == "counter").Name = "renamedCounter"; original.Write(Path.Combine(broken, "FixtureGame.dll")); }
    using (Discovery bad = Discovery.Load(Path.Combine(broken, "FixtureMod.dll"), broken))
        Check(bad.Audit().Issues.Any(i => i.Code == "FIELD_HELPER_UNSUPPORTED"), "Missing-field negative control did not fail.");
    Directory.Delete(broken, true);
    context.Unload();
    Console.WriteLine($"QuestWeaver executable fixture: {assertions} assertions passed.");
}
finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
