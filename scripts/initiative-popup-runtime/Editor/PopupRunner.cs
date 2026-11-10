using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
public static class PopupRunner
{
    [Serializable]private sealed class Case { public string name,dll,expected; }
    [Serializable]private sealed class Manifest { public string result,inputs; public Case[] cases; }
    public static void Run()
    {
        string[] args=Environment.GetCommandLineArgs();var path=args[Array.IndexOf(args,"-popupManifest")+1];
        var manifest=JsonUtility.FromJson<Manifest>(File.ReadAllText(path));bool failed=false;
        using(var writer=new StreamWriter(manifest.result))
        {
            writer.WriteLine("Unity "+Application.unityVersion);
            foreach(var entry in manifest.cases)
            {
                try
                {
                    Assembly assembly=Array.Find(AppDomain.CurrentDomain.GetAssemblies(),a=>a.GetName().Name==Path.GetFileNameWithoutExtension(entry.dll));
                    string result=(string)assembly.GetType("InitiativePopupChecks").GetMethod("Run").Invoke(null,new object[]{Path.GetDirectoryName(manifest.result),manifest.inputs});
                    if(!string.IsNullOrEmpty(entry.expected))throw new Exception("negative control escaped: "+entry.name);
                    writer.WriteLine("PASS "+entry.name+": "+result);
                }
                catch(Exception error)
                {
                    while(error is TargetInvocationException&&error.InnerException!=null)error=error.InnerException;
                    if(!string.IsNullOrEmpty(entry.expected)&&error.Message.Contains(entry.expected))writer.WriteLine("PASS negative control "+entry.name+": "+error.Message);
                    else {failed=true;writer.WriteLine("FAIL "+entry.name+": "+error);}
                }
                writer.Flush();
            }
        }
        EditorApplication.Exit(failed?1:0);
    }
}
