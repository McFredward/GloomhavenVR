using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
public static class EnvironmentBankFailureRunner
{
    [Serializable] private class Case { public string name,dll,expected; }
    [Serializable] private class Manifest { public string result,bank; public Case[] cases; }
    private static string Arg(string key)
    {var args=Environment.GetCommandLineArgs();return args[Array.IndexOf(args,key)+1];}
    public static void Run()
    {
        bool passed=true;var manifest=JsonUtility.FromJson<Manifest>(File.ReadAllText(Arg("-failureManifest")));
        using(var output=new StreamWriter(manifest.result))
        foreach(var item in manifest.cases)
        {
            try
            {
                var assembly=Assembly.LoadFile(item.dll);
                int assertions=(int)assembly.GetType("EnvironmentBankFailureProgram").GetMethod("Run").Invoke(null,new object[]{manifest.bank,Directory.GetParent(Application.dataPath).FullName});
                if(!String.IsNullOrEmpty(item.expected))throw new Exception("negative control escaped");
                output.WriteLine("PASS "+item.name+": "+assertions+" assertions");
            }
            catch(Exception error)
            {
                while(error is TargetInvocationException&&error.InnerException!=null)error=error.InnerException;
                if(!String.IsNullOrEmpty(item.expected)&&error.Message.Contains(item.expected))output.WriteLine("PASS negative "+item.name+": "+error.Message);
                else{output.WriteLine("FAIL "+item.name+": "+error);passed=false;}
            }
            finally{foreach(var bundle in AssetBundle.GetAllLoadedAssetBundles())bundle.Unload(true);output.Flush();}
        }
        EditorApplication.Exit(passed?0:1);
    }
}
