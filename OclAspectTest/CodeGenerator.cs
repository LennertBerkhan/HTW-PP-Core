﻿using System;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using OCL;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace OclAspectTest
{
    public class CodeGenerator
    {
        private struct Options
        {
            public readonly System.Type Context;
            public readonly string ClassName;
            public readonly string BeforeCode;
            public readonly string AfterCode;
            public readonly string HookedFuncName;

            public Options(string className, System.Type ctx, string hookedFuncName,
                string beforeCode, string afterCode)
            {
                Context = ctx;
                ClassName = className;
                BeforeCode = beforeCode;
                AfterCode = afterCode;
                HookedFuncName = hookedFuncName;
            }
        }

        private readonly Options _options;
        private readonly dynamic _runtimeCode;

        public CodeGenerator(IEnumerable<Assembly> targetAssemblies, Aspect aspect)
        {
            // HarmonyLib.Harmony.DEBUG = true;
            _options = new Options(aspect.ConstraintName,
                GetTypeByName(aspect.ContextName)[0],
                aspect.FunctionName, aspect.BeforeCode, aspect.AfterCode);

            var dd = typeof(Enumerable).GetTypeInfo().Assembly.Location;
            var coreDir = Directory.GetParent(dd);
            
            var refsNames = new[]
            {
                typeof(HarmonyLib.Harmony).Assembly.Location,
                typeof(object).Assembly.Location,
                typeof(System.Linq.Enumerable).Assembly.Location,
                typeof(System.Runtime.CompilerServices.DynamicAttribute).Assembly.Location,
                typeof(Console).GetTypeInfo().Assembly.Location,
                typeof(FormatterServices).GetTypeInfo().Assembly.Location,
                typeof(Stack).GetTypeInfo().Assembly.Location,
                typeof(Microsoft.CSharp.RuntimeBinder.CSharpArgumentInfo).GetTypeInfo().Assembly.Location,
                coreDir.FullName + Path.DirectorySeparatorChar + "mscorlib.dll",
                coreDir.FullName + Path.DirectorySeparatorChar + "netstandard.dll",
                coreDir.FullName + Path.DirectorySeparatorChar + "System.Runtime.dll",
                coreDir.FullName + Path.DirectorySeparatorChar + "Microsoft.CSharp.dll",
                coreDir.FullName + Path.DirectorySeparatorChar + "System.Collections.dll",
                coreDir.FullName + Path.DirectorySeparatorChar + "Newtonsoft.Json.dll",
                "OclAspectTest.dll"
                // "Microsoft.CSharp.dll"
            };

            refsNames = targetAssemblies.Aggregate(refsNames, (current, assembly) => current.AddToArray(assembly.Location));

            var refs = refsNames.Select(x => MetadataReference.CreateFromFile(x));
            
            // param.ReferencedAssemblies.Add("System.dll");
            // param.ReferencedAssemblies.Add("System.Xml.dll");
            // param.ReferencedAssemblies.Add("System.Data.dll");
            // param.ReferencedAssemblies.Add("System.Core.dll");
            // param.ReferencedAssemblies.Add("System.Xml.Linq.dll");
            // param.ReferencedAssemblies.Add("0Harmony.dll");
            // param.ReferencedAssemblies.Add(typeof(HarmonyInstance).Assembly.Location);
            // param.ReferencedAssemblies.Add(typeof(Operation).Assembly.Location);
            // param.ReferencedAssemblies.Add("Microsoft.CSharp.dll");
            // // param.ReferencedAssemblies.Add("Designer.dll"); // new System.Uri(System.Reflection.Assembly.GetExecutingAssembly().EscapedCodeBase).LocalPath);

            // var codeProvider = new CSharpCodeProvider();
            var code = GenerateCodeString();
            // var results = codeProvider.CompileAssemblyFromSource(param, code);

            System.IO.File.WriteAllText(_options.Context.Namespace + "-" + _options.ClassName + "_generated.cs", code);


            var typeName = "HookClass_" + _options.Context.Assembly.GetName().Name + "." +
                           _options.ClassName;

            SyntaxTree tree = SyntaxFactory.ParseSyntaxTree(code);

            CSharpCompilation compilation = CSharpCompilation.Create(
                typeName + ".dll",
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
                syntaxTrees: new[] {tree},
                references: refs); //new[] {MetadataReference.CreateFromFile(typeof(object).Assembly.Location)});

            using (MemoryStream stream = new MemoryStream())
            {
                Microsoft.CodeAnalysis.Emit.EmitResult compileResult = compilation.Emit(stream);
                if (!compileResult.Success)
                {
                    throw new Exception(compileResult.Diagnostics.Join(x => x.ToString()));
                }

                _assembly = Assembly.Load(stream.GetBuffer());
            }

            // Type calculator = _assembly.GetType(typeName);
            // MethodInfo evaluate = calculator.GetMethod("Evaluate");
            // string answer = evaluate.Invoke(null, null).ToString();


            _instance = _assembly.CreateInstance(typeName);
            _runtimeCode = _instance.GetType();
            // throw new Exception("CodeGen failed!");
        }

        private readonly dynamic _instance;
        private readonly Assembly _assembly;

        private string GetMethodArgumentsList()
        {
            var original = _options.Context.GetMethod(_options.HookedFuncName);
            string args = "";
            if (original == null) return args;
            foreach (var pi in original.GetParameters())
            {
                var pt = pi.ParameterType.ToString();
                int pos = -1;
                while (true)
                {
                    pos = pt.IndexOf("`", StringComparison.Ordinal);
                    if (pos > 0)
                        pt = pt.Remove(pos, 2);
                    else break;
                }

                pos = -1;
                while (true)
                {
                    pos = pt.IndexOf("[", StringComparison.Ordinal);
                    if (pos > 0)
                        pt = pt.Substring(0, pos) + "<" + pt.Substring(pos + 1);
                    else break;
                }

                pos = -1;
                while (true)
                {
                    pos = pt.IndexOf("]", StringComparison.Ordinal);
                    if (pos > 0)
                        pt = pt.Substring(0, pos) + ">" + pt.Substring(pos + 1);
                    else break;
                }
                // if (!pt.StartsWith("System.Int"))
                // {
                //     if (pt.StartsWith("List<"))
                //     {
                //         pt = "List<dynamic>";
                //     }
                //     else
                //         pt = "dynamic";
                // }

                args += ", " + pt + " " + pi.Name;
            }

            return args;
        }

        private static Type[] GetTypeByName(string className)
        {
            var returnVal = new List<Type>();
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var assemblyTypes = a.GetTypes();
                    returnVal.AddRange(assemblyTypes.Where(t => t.Name == className));
                }
                catch (Exception e)
                {
                    // (reflection-) type load exceptions can be ignored
                }
            }

            return returnVal.ToArray();
        }

        public void InvokeApplyMethod()
        {
            ResolveEventHandler @object = (object obj, ResolveEventArgs args) => _assembly;
            AppDomain currentDomain = AppDomain.CurrentDomain;
            currentDomain.AssemblyResolve += @object.Invoke;
            var method = _runtimeCode.GetMethod("Apply");
            Tuple<HarmonyLib.Harmony, MethodInfo, HarmonyMethod, HarmonyMethod> tup = method.Invoke(_instance,
                new object[]
                {
                    _options.Context
                });
            tup.Item1.Patch(tup.Item2, tup.Item3, tup.Item4);
        }

        public bool HasPlanningError
        {
            get
            {
                var method = _runtimeCode.GetProperty("HasPlanningError");
                return method.GetValue(_instance);
            }
        }

        private string GenerateCodeString()
        {
            var ns = _options.Context.Assembly.GetName().Name;
            var funcArgsAddStr = GetMethodArgumentsList();
            return @"
using System;
using " + ns + @";
using OclAspectTest;
using HarmonyLib;
using System.Reflection;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.IO;
using System.Text;
using Newtonsoft.Json; 

namespace HookClass_" + ns + @"
{
    public class " + _options.ClassName + @"
    {
        private static Stack stack = new Stack();
        public " + _options.ClassName + @"() {}
        public Tuple<HarmonyLib.Harmony, MethodInfo, HarmonyMethod, HarmonyMethod> Apply(System.Type ctx)
        {
            if (ctx == null)
                throw new Exception(""[" + _options.ClassName + @"] ctx is null!"");
            var harmony = new HarmonyLib.Harmony(""" + _options.ClassName + @""");
            var original = ctx.GetMethod(""" + _options.HookedFuncName + @""");
            if (original == null) 
                throw new Exception(""[" + _options.ClassName + @"] original method == null."");
            var prefix = typeof(" + _options.ClassName + @").GetMethod(""BeforeCall"");
            var postfix = typeof(" + _options.ClassName + @").GetMethod(""AfterCall"");
            // harmony.Patch(original, new HarmonyMethod(prefix), new HarmonyMethod(postfix));
            // if (!harmony.HasAnyPatches(""" + _options.ClassName + @"""))
            //     throw new Exception(""[" + _options.ClassName + @"] applying hook failed."");
            return new Tuple<HarmonyLib.Harmony, MethodInfo, HarmonyMethod, HarmonyMethod>(harmony, original, new HarmonyMethod(prefix), new HarmonyMethod(postfix));
        }

        public static void BeforeCall(" + _options.Context + " __instance" + funcArgsAddStr + @")
        {
            var self = __instance;
            var foo = Traverse.Create(self);
            if (!(" + _options.BeforeCode + @"))
            {
                SetPlanningError(self);
            }

            Type newObjectType = self.GetType();
	        object newObject = FormatterServices.GetSafeUninitializedObject(newObjectType);
            foreach (var propInfo in self.GetType().GetFields())
            {
                object orgValue = propInfo.GetValue(self);
                propInfo.SetValue(newObject, orgValue);
            }
            stack.Push(newObject);
        }
        public static void AfterCall(" + _options.Context + " __instance" + funcArgsAddStr + @")
        {
            var self = __instance;
            object pre = stack.Pop();

            if (!(" + _options.AfterCode + @"))
            {
                SetPlanningError(self);
            }
        }

        public static bool HasPlanningError { get; private set; }
        private static void SetPlanningError(" + _options.Context + @" __instance)
        {
            Console.WriteLine(""Protocol Type 0: Planning Error " + _options.ClassName + @"."");
            HasPlanningError = true;

            System.Diagnostics.Debugger.Break();
            var self = __instance;   
            using (StreamWriter outputFile = new StreamWriter(Path.Combine(@""C:\temp\"", ""Log.txt""), true))
            {
                outputFile.WriteLine(""\nProtocol Type 0: Planning Error " + _options.ClassName + @"."");

                //JsonConvert.SerializeObject()
                if (true)
                {
                    outputFile.WriteLine(String.Format(""Protocol Type 1: Planning Error " + _options.ClassName + @" at Object: {0}"", JsonConvert.SerializeObject(self)));
                    Console.WriteLine(String.Format(""Protocol Type 1: Planning Error " + _options.ClassName + @" at Object: {0}"", JsonConvert.SerializeObject(self)));
                }
                
                //Additional Method
                if (true)
                {
                    IList<PropertyInfo> props = new List<PropertyInfo>(self.GetType().GetProperties());
                    string msg = """";                    

                    switch (self.GetType().Name)
                    {
                        case ""T_ProductionOrder"":
                            msg = String.Format(""Protocol Type 2: Customized Methode for " + _options.ClassName + @". Error at Production Order with Id: {0} and Name: {1}"",
                                props.Single(p => p.Name == ""Id"").GetValue(self),
                                props.Single(p => p.Name == ""Name"").GetValue(self));
                            break;

                        case ""T_ProductionOrderOperation"":
                            msg = String.Format(""Protocol Type 2: Customized Methode for " + _options.ClassName + @". Error at Production Order Operation with Id: {0} and Name: {1}"",
                                props.Single(p => p.Name == ""Id"").GetValue(self),
                                props.Single(p => p.Name == ""Name"").GetValue(self));
                            break;

                        default:
                            msg = String.Format(""Object type not handled. Object: {0}"", JsonConvert.SerializeObject(self));
                            break;

                    }
                    outputFile.WriteLine(msg);
                    Console.WriteLine(msg);
                    
                }

                //Debugger
                if (true)
                {
                    outputFile.WriteLine(""Protocol Type 3: Debugger called for " + _options.ClassName + @"."");
                    Console.WriteLine(""Protocol Type 3: Debugger called for " + _options.ClassName + @"."");
                }
            }
        }
    }


    public static class Extensions
    {
        public static List<dynamic>CastToList(this object self)
        {
            return (List<dynamic>) self; // as List<dynamic>;
        }
        public static dynamic GetValue(this object instance, string variableName)
        {
            PropertyInfo prop = instance.GetType().GetProperty(variableName,
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.GetProperty);
            var methInf = prop.GetGetMethod(nonPublic: true);
            var workload = methInf.Invoke(instance, null);
            return workload;
        }
        public static List<dynamic>Cast(this object self, Type innerType)
        {
            var methodInfo = typeof (Enumerable).GetMethod(""Cast"");
            var genericMethod = methodInfo.MakeGenericMethod(innerType);
            return genericMethod.Invoke(null, new [] {self}) as List<dynamic>;
        }
    } 
}
            ";
        }
    }
}