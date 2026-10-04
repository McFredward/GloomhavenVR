using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;
using TMPro;

namespace GloomhavenVR.Core
{
    public static class QuestStandalonePlatform { public static bool Enabled => false; }
}

// Verify the exact owned game ABI and original call relationships without
// constructing Unity objects, invoking engine internals or replacing native text editing.
static class NativeSDKProbe
{
    static int assertions;
    static readonly Dictionary<ushort, OpCode> Codes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.FieldType == typeof(OpCode)).Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(code => unchecked((ushort)code.Value));
    static void Check(bool condition, string message) { assertions++; if (!condition) throw new Exception("FAIL native keyboard ABI " + message); }
    static MethodInfo Method(Type owner, string name, params Type[] arguments)
        => owner.GetMethod(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            null, arguments, null) ?? throw new Exception("FAIL native keyboard ABI missing " + owner.FullName + "." + name);
    static List<MemberInfo> Called(MethodInfo method)
    {
        var calls = new List<MemberInfo>();
        byte[] bytes = method.GetMethodBody()?.GetILAsByteArray() ?? throw new Exception("FAIL native keyboard ABI method body missing");
        for (int position = 0; position < bytes.Length;)
        {
            ushort key = bytes[position++];
            if (key == 0xfe) key = (ushort)(0xfe00 | bytes[position++]);
            var code = Codes[key];
            if (code.OperandType == OperandType.InlineMethod)
                calls.Add(method.Module.ResolveMember(BitConverter.ToInt32(bytes, position))!);
            position += code.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(bytes, position),
                _ => 4
            };
        }
        return calls;
    }
    static bool Calls(List<MemberInfo> calls, string owner, string name) => calls.Any(call => call.DeclaringType?.FullName == owner && call.Name == name);
    public static int Main(string[] args)
    {
        try
        {
            string game = Path.GetFullPath(args[0]);
            AssemblyLoadContext.Default.Resolving += (_, name) =>
            {
                string path = Path.Combine(game, name.Name + ".dll");
                return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
            };
            Type tmp = typeof(TMP_InputField);
            var hide = tmp.GetProperty("shouldHideSoftKeyboard");
            Check(hide != null && hide.PropertyType == typeof(bool) && hide.CanRead && hide.CanWrite, "software keyboard flag read/write bool");
            Check(tmp.GetProperty("shouldHideMobileInput")?.PropertyType == typeof(bool), "mobile flag remains a distinct native option");
            var admission = Called(Method(tmp, "InPlaceEditing"));
            Check(Calls(admission, "UnityEngine.TouchScreenKeyboard", "get_isSupported"), "original gate checks supported platform keyboard");
            Check(Calls(admission, "TMPro.TMP_InputField", "get_shouldHideSoftKeyboard"), "original admission uses software keyboard flag");
            Check(Calls(admission, "TMPro.TMP_InputField", "get_shouldHideMobileInput"), "original admission uses authored mobile flag");
            var append = Called(Method(tmp, "Append", typeof(char)));
            Check(Calls(append, "TMPro.TMP_InputField", "InPlaceEditing"), "original character append checks in place editing");
            Check(Calls(append, "TMPro.TMP_InputField", "Insert"), "original append retains native insertion");
            Check(Calls(append, "TMPro.TMP_InputField+OnValidateInput", "Invoke"), "original append retains validation callback");
            Check(append.FindIndex(call => call.Name == "InPlaceEditing") < append.FindIndex(call => call.Name == "Insert"), "admission precedes original insertion");
            var activation = Called(Method(tmp, "ActivateInputFieldInternal"));
            Check(Calls(activation, "TMPro.TMP_InputField", "get_shouldHideSoftKeyboard") && Calls(activation, "UnityEngine.TouchScreenKeyboard", "Open"), "same flag gates native overlay opening");
            var process = Called(Method(tmp, "ProcessEvent", typeof(UnityEngine.Event)));
            Check(Calls(process, "TMPro.TMP_InputField", "KeyPressed"), "native ProcessEvent reaches normal key editing");
            var original = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(game, "GH.Runtime.dll"));
            var controller = original.GetType("Script.GUI.Controller.ControllerInputKeyboard", true)!;
            var keys = Called(Method(controller, "ProcessKeyCode", typeof(UnityEngine.KeyCode)));
            Check(Calls(keys, "TMPro.TMP_InputField", "ProcessEvent") && Calls(keys, "TMPro.TMP_InputField", "ForceLabelUpdate"), "original keyboard uses same character and label path");
            Check(!GloomhavenVR.Core.QuestStandalonePlatform.Enabled, "native ABI fixture never acquires a runtime field");
            Console.WriteLine("PASS Quest keyboard native SDK: " + assertions + " original API/CIL assertions; no engine callbacks invoked");
            return 0;
        }
        catch (Exception error) { Console.WriteLine(error); return 1; }
    }
}
