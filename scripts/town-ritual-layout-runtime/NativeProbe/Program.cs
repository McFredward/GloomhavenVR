#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
var managed=Path.GetFullPath(args[0]);
AssemblyLoadContext.Default.Resolving+=(context,name)=> {var path=Path.Combine(managed,name.Name+".dll");return File.Exists(path)?context.LoadFromAssemblyPath(path):null;};
var assembly=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(managed,"ScenarioRuleLibrary.dll"));
var ability=assembly.GetType("ScenarioRuleLibrary.CAbility",true);
var method=ability.GetMethod("GetValidEnhancements",BindingFlags.Public|BindingFlags.Static);
var parameters=method.GetParameters();
var positive=Activator.CreateInstance(parameters[1].ParameterType);
var negative=Activator.CreateInstance(parameters[2].ParameterType);
var results=new List<object>();int maximum=0,calls=0;
foreach(var type in Enum.GetValues(parameters[0].ParameterType)) foreach(var line in Enum.GetValues(parameters[3].ParameterType)) {
var value=(ICollection)method.Invoke(null,new[]{type,positive,negative,line});calls++;maximum=Math.Max(maximum,value.Count);
if(value.Count>0)results.Add(new{ability=type.ToString(),line=line.ToString(),count=value.Count});
}
Console.WriteLine(JsonSerializer.Serialize(new {maximum,calls,results}));
