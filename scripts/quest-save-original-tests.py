#!/usr/bin/env python3
"""Exercise actual owned RootSaveData/SaveOwner serializers in an isolated process.

No game/save input is altered. This is real original type/binder execution on the
host CLR, not Android/IL2CPP campaign or GlobalData continuation evidence.
"""
from pathlib import Path
import argparse
import hashlib
import json
import shutil
import subprocess

PROGRAM=r'''
using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text.Json;

string managed=Path.GetFullPath(args[0]); string output=Path.GetFullPath(args[1]);
Directory.CreateDirectory(output);
AssemblyLoadContext.Default.Resolving+=(_,name)=>File.Exists(Path.Combine(managed,name.Name+".dll"))
    ?AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(managed,name.Name+".dll")):null;
Assembly game=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(managed,"GH.Runtime.dll"));
Type rootType=game.GetType("RootSaveData",true)!;
Type ownerType=game.GetType("SaveOwner",true)!;
var binder=(SerializationBinder)Activator.CreateInstance(game.GetType("SerializationBinding",true)!)!;
object root=Activator.CreateInstance(rootType)!;
rootType.GetField("Version")!.SetValue(root,1);rootType.GetField("CurrentLanguage")!.SetValue(root,"German");
var info=new SerializationInfo(ownerType,new FormatterConverter());
info.AddValue("PlatformPlayerID","76561198000000000");info.AddValue("PlatformAccountID","39734272");
info.AddValue("PlatformNetworkAccountID","39734272");info.AddValue("PlatformName","Steam");
info.AddValue("Username","Quest original serializer validation (DUMMY)");
info.AddValue("Avatar",null,game.GetType("SaveOwner+SerializedAvatar",true)!);info.AddValue("AvatarSet",false);
object owner=Activator.CreateInstance(ownerType,new object[]{info,new StreamingContext()})!;
int assertions=0;
void Check(bool condition,string message){assertions++;if(!condition)throw new Exception(message);}
object RoundTrip(object value,string name)
{
    using var stream=new MemoryStream();new BinaryFormatter().Serialize(stream,value);
    byte[] bytes=stream.ToArray();Check(bytes.Length>17&&bytes[0]==0,"Original BinaryFormatter did not emit its native stream header.");
    File.WriteAllBytes(Path.Combine(output,name+".dat"),bytes);stream.Position=0;
    object copy=new BinaryFormatter{Binder=binder}.Deserialize(stream);
    Check(copy.GetType()==value.GetType(),"Original binder resolved another serialization type.");return copy;
}
object copiedRoot=RoundTrip(root,"GloomSaven");object copiedOwner=RoundTrip(owner,"SaveOwner-fixture");
Check((int)rootType.GetField("Version")!.GetValue(copiedRoot)! ==1,"Root save version changed.");
Check((string)rootType.GetField("CurrentLanguage")!.GetValue(copiedRoot)! =="German","Root language changed.");
foreach(string field in new[]{"PlatformPlayerID","PlatformAccountID","PlatformNetworkAccountID","PlatformName","Username"})
    Check((string)ownerType.GetProperty(field)!.GetValue(owner)! ==(string)ownerType.GetProperty(field)!.GetValue(copiedOwner)!,"Original owner field changed: "+field);
Check(ownerType.GetProperty("Avatar")!.GetValue(copiedOwner)==null&&!((bool)ownerType.GetProperty("AvatarSet")!.GetValue(copiedOwner)!),"Null static owner avatar metadata changed.");
File.WriteAllText(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new{schema=1,assertions,originalRootOwnerRoundtrip=true,originalAssembly=game.FullName,androidVerified=false,campaignGlobalScenarioRoundtrip=false},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine("Actual original RootSaveData/SaveOwner BinaryFormatter + original SerializationBinding: "+assertions+" assertions passed. Android/campaign graph remains unverified.");
'''


def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument("--managed",type=Path,required=True);p.add_argument("--output",type=Path,required=True);a=p.parse_args()
    managed=a.managed.resolve();output=a.output.resolve();output.mkdir(parents=True,exist_ok=True)
    source=output/"harness";source.mkdir(exist_ok=True)
    (source/"Program.cs").write_text(PROGRAM)
    (source/"OriginalSaves.csproj").write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><ImplicitUsings>disable</ImplicitUsings><Nullable>enable</Nullable><NoWarn>SYSLIB0050;SYSLIB0011</NoWarn><TreatWarningsAsErrors>true</TreatWarningsAsErrors><EnableUnsafeBinaryFormatterSerialization>true</EnableUnsafeBinaryFormatterSerialization></PropertyGroup></Project>')
    dotnet=shutil.which("dotnet") or str(Path.home()/".dotnet/dotnet")
    run=subprocess.run([dotnet,"run","--project",str(source/"OriginalSaves.csproj"),"--configuration","Release","--",str(managed),str(output)],capture_output=True,text=True)
    (output/"execution.log").write_text(run.stdout+run.stderr)
    print(run.stdout+run.stderr,end="")
    if run.returncode:return run.returncode
    result=json.loads((output/"result.json").read_text());result["originalAssemblySha256"]=hashlib.sha256((managed/"GH.Runtime.dll").read_bytes()).hexdigest()
    result["fixturePayloads"]={path.name:hashlib.sha256(path.read_bytes()).hexdigest() for path in sorted(output.glob("*.dat"))}
    (output/"result.json").write_text(json.dumps(result,indent=2)+"\n")
    return 0


if __name__=="__main__":raise SystemExit(main())
