// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Versioning;
using System.Security;
using Xsl3.Runtime;
using DebuggingModes = System.Diagnostics.DebuggableAttribute.DebuggingModes;

namespace Xsl3.IlGen
{
    internal enum XmlILMethodAttributes
    {
        None = 0,
        NonUser = 1,    // Non-user method which should debugger should step through
        Raw = 2,        // Raw method which should not add an implicit first argument of type XmlQueryRuntime
    }

    [RequiresDynamicCode("Creates DynamicMethods")]
    internal sealed class XmlILModule
    {
        private static long s_assemblyId;                                     // Unique identifier used to ensure that assembly names are unique within AppDomain
        // Module used to emit dynamic lightweight-reflection-emit (LRE) methods.
        // Xsl3: created on first use, so that a precompiled stylesheet runs without Reflection.Emit (NativeAOT;
        // docs/plan/p4-sink-b.md, decision 5).
        private static ModuleBuilder? s_LREModule;
        private static ModuleBuilder LREModule => s_LREModule ?? System.Threading.Interlocked.CompareExchange(ref s_LREModule, CreateLREModule(), null) ?? s_LREModule!;

        private TypeBuilder? _typeBldr;
        private Hashtable _methods;
        private readonly bool _useLRE, _emitSymbols;

        private const string RuntimeName = $"{{{XmlReservedNs.NsXslDebug}}}runtime";

        private static ModuleBuilder CreateLREModule()
        {
            // 1. LRE assembly only needs to execute
            // 2. No temp files need be created
            // 3. Never allow assembly to Assert permissions
            AssemblyName asmName = CreateAssemblyName();
            AssemblyBuilder asmBldr = AssemblyBuilder.DefineDynamicAssembly(asmName, AssemblyBuilderAccess.Run);

            // Add custom attribute to assembly marking it as security transparent so that Assert will not be allowed
            // and link demands will be converted to full demands.
            asmBldr.SetCustomAttribute(new CustomAttributeBuilder(XmlILConstructors.Transparent, Array.Empty<object>()));

            // Store LREModule once.  If multiple threads are doing this, then some threads might get different
            // modules.  This is OK, since it's not mandatory to share, just preferable.
            return asmBldr.DefineDynamicModule("Xsl3.CompiledQuery");
        }

        public XmlILModule(TypeBuilder typeBldr, bool emitSymbols = false)
        {
            _typeBldr = typeBldr;

            // Xsl3 (Sink B): a saved assembly can carry symbols: sequence points to the stylesheet's lines.
            _emitSymbols = emitSymbols;
#if NET9_0_OR_GREATER
            Persisted = typeBldr.Module is ModuleBuilder { Assembly: PersistedAssemblyBuilder };
#endif
            _useLRE = false;
            // Index all methods added to this module by unique name
            _methods = new Hashtable();
        }

#if NET9_0_OR_GREATER
        private readonly System.Collections.Generic.Dictionary<string, System.Diagnostics.SymbolStore.ISymbolDocumentWriter> _documents = new();

        /// <summary>Xsl3 (Sink B): the symbol document for a stylesheet module, in a saved assembly that emits symbols.</summary>
        public System.Diagnostics.SymbolStore.ISymbolDocumentWriter? SourceDocument(string fileName)
        {
            // (Only saved assemblies define documents; the in-memory debug assembly has no symbol writer.)
            if (!_emitSymbols || _typeBldr?.Module is not ModuleBuilder { Assembly: PersistedAssemblyBuilder } module)
            {
                return null;
            }
            if (!_documents.TryGetValue(fileName, out var document))
            {
                _documents[fileName] = document = module.DefineDocument(fileName);
            }
            return document;
        }
#endif

        /// <summary>Xsl3 (Sink B): the methods go into a saved assembly (PersistedAssemblyBuilder).</summary>
        public bool Persisted { get; }

        public bool EmitSymbols
        {
            get
            {
                return _emitSymbols;
            }
        }

        // SxS note: AssemblyBuilder.DefineDynamicModule() below may be using name which is not SxS safe.
        // This file is written only for internal tracing/debugging purposes. In retail builds persistAsm
        // will be always false and the file should never be written. As a result it's fine just to suppress
        // the SxS warning.
        public XmlILModule(bool useLRE, bool emitSymbols)
        {
            AssemblyName asmName;
            AssemblyBuilder asmBldr;
            ModuleBuilder modBldr;
            Debug.Assert(!(useLRE && emitSymbols));

            _useLRE = useLRE;
            _emitSymbols = emitSymbols;

            // Index all methods added to this module by unique name
            _methods = new Hashtable();

            if (!useLRE)
            {
                // 1. If assembly needs to support debugging, then it must be saved and re-loaded (rule of CLR)
                // 2. Get path of temp directory, where assembly will be saved
                // 3. Never allow assembly to Assert permissions
                asmName = CreateAssemblyName();

                asmBldr = AssemblyBuilder.DefineDynamicAssembly(
                            asmName, AssemblyBuilderAccess.Run);

                // Add custom attribute to assembly marking it as security transparent so that Assert will not be allowed
                // and link demands will be converted to full demands.
                asmBldr.SetCustomAttribute(new CustomAttributeBuilder(XmlILConstructors.Transparent, Array.Empty<object>()));

                if (emitSymbols)
                {
                    // Add DebuggableAttribute to assembly so that debugging is a better experience
                    DebuggingModes debuggingModes = DebuggingModes.Default | DebuggingModes.IgnoreSymbolStoreSequencePoints | DebuggingModes.DisableOptimizations;
                    asmBldr.SetCustomAttribute(new CustomAttributeBuilder(XmlILConstructors.Debuggable, new object[] { debuggingModes }));
                }

                // Create ModuleBuilder
                modBldr = asmBldr.DefineDynamicModule("Xsl3.CompiledQuery");

                _typeBldr = modBldr.DefineType("Xsl3.CompiledQuery.Query", TypeAttributes.Public);
            }
        }

        /// <summary>
        /// Define a method in this module with the specified name and parameters.
        /// </summary>
        public MethodInfo DefineMethod(string name, Type returnType, Type[] paramTypes, string?[] paramNames, XmlILMethodAttributes xmlAttrs)
        {
            MethodInfo methResult;
            int uniqueId = 1;
            string nameOrig = name;
            Type[] paramTypesNew;
            bool isRaw = (xmlAttrs & XmlILMethodAttributes.Raw) != 0;

            // Ensure that name is unique
            while (_methods[name] != null)
            {
                // Add unique id to end of name in order to make it unique within this module
                uniqueId++;
                name = $"{nameOrig} ({uniqueId})";
            }

            if (!isRaw)
            {
                // XmlQueryRuntime is always 0th parameter
                paramTypesNew = new Type[paramTypes.Length + 1];
                paramTypesNew[0] = typeof(XmlQueryRuntime);
                Array.Copy(paramTypes, 0, paramTypesNew, 1, paramTypes.Length);
                paramTypes = paramTypesNew;
            }

            if (!_useLRE)
            {
                MethodBuilder methBldr = _typeBldr!.DefineMethod(
                            name,
                            MethodAttributes.Private | MethodAttributes.Static,
                            returnType,
                            paramTypes);

                if (_emitSymbols && (xmlAttrs & XmlILMethodAttributes.NonUser) != 0)
                {
                    // Add DebuggerStepThroughAttribute and DebuggerNonUserCodeAttribute to non-user methods so that debugging is a better experience
                    methBldr.SetCustomAttribute(new CustomAttributeBuilder(XmlILConstructors.StepThrough, Array.Empty<object>()));
                    methBldr.SetCustomAttribute(new CustomAttributeBuilder(XmlILConstructors.NonUserCode, Array.Empty<object>()));
                }

                if (!isRaw)
                    methBldr.DefineParameter(1, ParameterAttributes.None, RuntimeName);

                for (int i = 0; i < paramNames.Length; i++)
                {
                    if (!string.IsNullOrEmpty(paramNames[i]))
                        methBldr.DefineParameter(i + (isRaw ? 1 : 2), ParameterAttributes.None, paramNames[i]);
                }

                methResult = methBldr;
            }
            else
            {
                DynamicMethod methDyn = new DynamicMethod(name, returnType, paramTypes, LREModule);
                methDyn.InitLocals = true;

                methResult = methDyn;
            }

            // Index method by name
            _methods[name] = methResult;
            return methResult;
        }

        /// <summary>
        /// Get an XmlILGenerator that can be used to generate the body of the specified method.
        /// </summary>
        public static ILGenerator DefineMethodBody(MethodBase methInfo)
        {
            DynamicMethod? methDyn = methInfo as DynamicMethod;
            if (methDyn != null)
                return methDyn.GetILGenerator();

            MethodBuilder? methBldr = methInfo as MethodBuilder;
            if (methBldr != null)
                return methBldr.GetILGenerator();

            return ((ConstructorBuilder)methInfo).GetILGenerator();
        }

        /// <summary>
        /// Find a MethodInfo of the specified name and return it.  Return null if no such method exists.
        /// </summary>
        public MethodInfo? FindMethod(string name)
        {
            return (MethodInfo?)_methods[name];
        }

        /// <summary>
        /// Define ginitialized data field with the specified name and value.
        /// </summary>
        public FieldInfo DefineInitializedData(string name, byte[] data)
        {
            Debug.Assert(!_useLRE, "Cannot create initialized data for an LRE module");
            return _typeBldr!.DefineInitializedData(name, data, FieldAttributes.Private | FieldAttributes.Static);
        }

        /// <summary>
        /// Define private static field with the specified name and value.
        /// </summary>
        public FieldInfo DefineField(string fieldName, Type type)
        {
            Debug.Assert(!_useLRE, "Cannot create field for an LRE module");
            return _typeBldr!.DefineField(fieldName, type, FieldAttributes.Private | FieldAttributes.Static);
        }

        /// <summary>
        /// Define static constructor for this type.
        /// </summary>
        public ConstructorInfo DefineTypeInitializer()
        {
            Debug.Assert(!_useLRE, "Cannot create type initializer for an LRE module");
            return _typeBldr!.DefineTypeInitializer();
        }

        /// <summary>
        /// Once all methods have been defined, CreateModule must be called in order to "bake" the methods within
        /// this module.
        /// </summary>
        public void BakeMethods()
        {
            Type typBaked;
            Hashtable methodsBaked;

            if (!_useLRE)
            {
                typBaked = _typeBldr!.CreateType();

                // Replace all MethodInfos in this.methods
                methodsBaked = new Hashtable(_methods.Count);
                foreach (string methName in _methods.Keys)
                {
                    methodsBaked[methName] = typBaked.GetMethod(methName, BindingFlags.NonPublic | BindingFlags.Static);
                }
                _methods = methodsBaked;

                // Release TypeBuilder and symbol writer resources
                _typeBldr = null;
            }
        }

        /// <summary>
        /// Wrap a delegate around a MethodInfo of the specified name and type and return it.
        /// </summary>
        public Delegate CreateDelegate(string name, Type typDelegate)
        {
            if (!_useLRE)
                return ((MethodInfo)_methods[name]!).CreateDelegate(typDelegate);

            return ((DynamicMethod)_methods[name]!).CreateDelegate(typDelegate);
        }

        /// <summary>
        /// Define unique assembly name (within AppDomain).
        /// </summary>
        private static AssemblyName CreateAssemblyName()
        {
            AssemblyName name;

            System.Threading.Interlocked.Increment(ref s_assemblyId);
            name = new AssemblyName();
            name.Name = $"Xsl3.CompiledQuery.{s_assemblyId}";

            return name;
        }
    }
}
