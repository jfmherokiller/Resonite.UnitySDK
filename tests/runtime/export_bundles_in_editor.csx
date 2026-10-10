var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();var beforePath=scene.path;var beforeDirty=scene.isDirty;
var root=System.IO.Path.Combine(Environment.GetEnvironmentVariable("RESONITE_SDK_REPO")??throw new InvalidOperationException("Set RESONITE_SDK_REPO to the Resonite.UnitySDK checkout"),"tests","runtime")+"/";
var outDir=System.IO.Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA"),"ResoniteConverterHarness","bundles","latest");
var refs=AppDomain.CurrentDomain.GetAssemblies().Where(a=>!a.IsDynamic&&!string.IsNullOrEmpty(a.Location)&&System.IO.File.Exists(a.Location)).Select(a=>a.Location).Distinct().Select(p=>Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(p));
var parse=new Microsoft.CodeAnalysis.CSharp.CSharpParseOptions(Microsoft.CodeAnalysis.CSharp.LanguageVersion.Latest,preprocessorSymbols:new[]{"UNITY_EDITOR"});
var trees=new[]{"IsolatedPreviewScene.cs","ConverterOutputBundles.cs"}.Select(f=>Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(System.IO.File.ReadAllText(root+f),parse,root+f));
var comp=Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create("ConverterBundles_"+Guid.NewGuid().ToString("N"),trees,refs,new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));
using(var stream=new System.IO.MemoryStream()){
 var emit=comp.Emit(stream);if(!emit.Success)return emit.Diagnostics.Where(d=>d.Severity==Microsoft.CodeAnalysis.DiagnosticSeverity.Error).Select(d=>d.ToString()).Take(20).ToArray();
 var errors=new List<string>();UnityEngine.Application.LogCallback logger=(m,s,t)=>{if(t==UnityEngine.LogType.Error||t==UnityEngine.LogType.Exception||t==UnityEngine.LogType.Assert)errors.Add(m+"\n"+s);};
 UnityEngine.Application.logMessageReceived+=logger;string[] written=null;string failure=null;
 try{written=(string[])System.Reflection.Assembly.Load(stream.ToArray()).GetType("ConverterOutputBundles").GetMethod("ExportAll").Invoke(null,new object[]{outDir});}
 catch(Exception e){failure=(e.InnerException??e).ToString();}
 finally{UnityEngine.Application.logMessageReceived-=logger;}
 var after=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
 var result=new{written,failure,consoleErrors=errors,beforePath,beforeDirty,afterPath=after.path,afterDirty=after.isDirty};
 System.IO.File.WriteAllText(System.IO.Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA"),"ResoniteConverterHarness","bundles","export-result.json"),Newtonsoft.Json.JsonConvert.SerializeObject(result,Newtonsoft.Json.Formatting.Indented));
 return result;
}
