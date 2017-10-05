﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.SignalR
{
    internal static class TypedClientBuilder<T>
    {
        private const string ClientModuleName = "Microsoft.AspNetCore.SignalR.TypedClientBuilder";

        // There is one static instance of _builder per T
        private static Lazy<Func<IClientProxy, T>> _builder = new Lazy<Func<IClientProxy, T>>(() => GenerateClientBuilder());

        public static T Build(IClientProxy proxy)
        {
            return _builder.Value(proxy);
        }

        public static void Validate()
        {
            // The following will throw if T is not a valid type
            _  = _builder.Value;
        }

        private static Func<IClientProxy, T> GenerateClientBuilder()
        {
            VerifyInterface(typeof(T));

            var assemblyName = new AssemblyName(ClientModuleName);
            var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
            var moduleBuilder = assemblyBuilder.DefineDynamicModule(ClientModuleName);
            var clientType = GenerateInterfaceImplementation(moduleBuilder);

            return proxy => (T)Activator.CreateInstance(clientType, proxy);
        }

        private static Type GenerateInterfaceImplementation(ModuleBuilder moduleBuilder)
        {
            var type = moduleBuilder.DefineType(
                ClientModuleName + "." + typeof(T).Name + "Impl",
                TypeAttributes.Public,
                typeof(Object),
                new[] { typeof(T) });

            var proxyField = type.DefineField("_proxy", typeof(IClientProxy), FieldAttributes.Private);

            BuildConstructor(type, proxyField);

            foreach (var method in GetAllInterfaceMethods(typeof(T)))
            {
                BuildMethod(type, method, proxyField);
            }

            return type.CreateTypeInfo();
        }

        private static IEnumerable<MethodInfo> GetAllInterfaceMethods(Type interfaceType)
        {
            foreach (var parent in interfaceType.GetInterfaces())
            {
                foreach (var parentMethod in GetAllInterfaceMethods(parent))
                {
                    yield return parentMethod;
                }
            }

            foreach (var method in interfaceType.GetMethods())
            {
                yield return method;
            }
        }

        private static void BuildConstructor(TypeBuilder type, FieldInfo proxyField)
        {
            var method = type.DefineMethod(".ctor", System.Reflection.MethodAttributes.Public | System.Reflection.MethodAttributes.HideBySig);

            var ctor = typeof(object).GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new Type[] { }, null);

            method.SetReturnType(typeof(void));
            method.SetParameters(typeof(IClientProxy));

            var generator = method.GetILGenerator();

            // Call object constructor
            generator.Emit(OpCodes.Ldarg_0);
            generator.Emit(OpCodes.Call, ctor);

            // Assign constructor argument to the proxyField
            generator.Emit(OpCodes.Ldarg_0); // type
            generator.Emit(OpCodes.Ldarg_1); // type proxyfield
            generator.Emit(OpCodes.Stfld, proxyField); // type.proxyField = proxyField
            generator.Emit(OpCodes.Ret);
        }

        private static void BuildMethod(TypeBuilder type, MethodInfo interfaceMethodInfo, FieldInfo proxyField)
        {
            var methodAttributes =
                  MethodAttributes.Public
                | MethodAttributes.Virtual
                | MethodAttributes.Final
                | MethodAttributes.HideBySig
                | MethodAttributes.NewSlot;

            ParameterInfo[] parameters = interfaceMethodInfo.GetParameters();
            Type[] paramTypes = parameters.Select(param => param.ParameterType).ToArray();

            var methodBuilder = type.DefineMethod(interfaceMethodInfo.Name, methodAttributes);

            var invokeMethod = typeof(IClientProxy).GetMethod(
                "InvokeAsync", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
                new Type[] { typeof(string), typeof(object[]) }, null);

            methodBuilder.SetReturnType(interfaceMethodInfo.ReturnType);
            methodBuilder.SetParameters(paramTypes);

            // Sets the number of generic type parameters
            var genericTypeNames =
                paramTypes.Where(p => p.IsGenericParameter).Select(p => p.Name).Distinct().ToArray();

            if (genericTypeNames.Any())
            {
                methodBuilder.DefineGenericParameters(genericTypeNames);
            }

            var generator = methodBuilder.GetILGenerator();

            // Declare local variable to store the arguments to IClientProxy.InvokeAsync
            generator.DeclareLocal(typeof(object[]));

            // Get IClientProxy
            generator.Emit(OpCodes.Ldarg_0);
            generator.Emit(OpCodes.Ldfld, proxyField);

            // The first argument to IClientProxy.InvokeAsync is this method's name
            generator.Emit(OpCodes.Ldstr, interfaceMethodInfo.Name);

            // Create an new object array to hold all the parameters to this method
            generator.Emit(OpCodes.Ldc_I4, parameters.Length); // Stack: 
            generator.Emit(OpCodes.Newarr, typeof(object)); // allocate object array
            generator.Emit(OpCodes.Stloc_0);

            // Store each parameter in the object array
            for (int i = 0; i < paramTypes.Length; i++)
            {
                generator.Emit(OpCodes.Ldloc_0); // Object array loaded
                generator.Emit(OpCodes.Ldc_I4, i); 
                generator.Emit(OpCodes.Ldarg, i + 1); // i + 1 
                generator.Emit(OpCodes.Box, paramTypes[i]);
                generator.Emit(OpCodes.Stelem_Ref);
            }

            // Call InvokeAsync
            generator.Emit(OpCodes.Ldloc_0);
            generator.Emit(OpCodes.Callvirt, invokeMethod);

            if (interfaceMethodInfo.ReturnType == typeof(void))
            {
                // void return
                generator.Emit(OpCodes.Pop);
            }

            generator.Emit(OpCodes.Ret); // Return 
        }

        private static void VerifyInterface(Type interfaceType)
        {
            if (!interfaceType.IsInterface)
            {
                throw new InvalidOperationException("Type must be an interface.");
            }

            if (interfaceType.GetProperties().Length != 0)
            {
                throw new InvalidOperationException("Type must not contain properties.");
            }

            if (interfaceType.GetEvents().Length != 0)
            {
                throw new InvalidOperationException("Type can not contain events.");
            }

            foreach (var method in interfaceType.GetMethods())
            {
                VerifyMethod(interfaceType, method);
            }

            foreach (var parent in interfaceType.GetInterfaces())
            {
                VerifyInterface(parent);
            }
        }

        private static void VerifyMethod(Type interfaceType, MethodInfo interfaceMethod)
        {
            if (interfaceMethod.ReturnType != typeof(void) && interfaceMethod.ReturnType != typeof(Task))
            {
                throw new InvalidOperationException("Method must return Void or Task.");
            }

            foreach (var parameter in interfaceMethod.GetParameters())
            {
                VerifyParameter(interfaceType, interfaceMethod, parameter);
            }
        }

        private static void VerifyParameter(Type interfaceType, MethodInfo interfaceMethod, ParameterInfo parameter)
        {
            if (parameter.IsOut)
            {
                throw new InvalidOperationException("Method must not take out parameters.");
            }

            if (parameter.ParameterType.IsByRef)
            {
                throw new InvalidOperationException("Method must not take reference parameters.");
            }
        }
    }
}