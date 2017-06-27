// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;

namespace Microsoft.AspNetCore.SignalR.Tools
{
    public class HubDiscovery : IDisposable
    {
        private readonly TypeDefinition _hubTypeDefinition;
        private readonly ModuleDefinition _module;
        private readonly Predicate<TypeDefinition> _hubFilter;
        private readonly IAssemblyResolver _assemblyResolver;

        public HubDiscovery(string path, Predicate<TypeDefinition> hubFilter = null)
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            _hubFilter = hubFilter ?? (t => true);
            _assemblyResolver = new SameFolderAssemblyResolver(Path.GetDirectoryName(path));
            var parameters = new ReaderParameters { AssemblyResolver = _assemblyResolver };

            _module = ModuleDefinition.ReadModule(path, parameters);

            _hubTypeDefinition = _module.ImportReference(typeof(Hub<>)).Resolve();
        }

        public List<HubProxy> GetHubProxies()
        {
            var proxies = new List<HubProxy>();

            // _hubTypeDefinition will be null if the assembly containing the `Hub<>` type
            // (i.e. Microsoft.AspNetCore.SignalR) is not referenced
            if (_hubTypeDefinition == null || _hubTypeDefinition.Module.Assembly.FullName == _module.Assembly.FullName)
            {
                return proxies;
            }

            foreach (var type in _module.GetTypes().Where(t => _hubFilter(t) && IsHub(t)))
            {
                proxies.Add(new HubProxy(type.Name, type.Namespace, GetHubMethods(type).ToArray()));
            }

            return proxies;
        }

        private IEnumerable<MethodDefinition> GetHubMethods(TypeDefinition type)
        {
            var hubMethods = new Dictionary<string, MethodDefinition>(StringComparer.OrdinalIgnoreCase);

            while (type != null && type.Module.Assembly.FullName != _hubTypeDefinition.Module.Assembly.FullName)
            {
                foreach (var methodDefinition in type.Methods.Where(IsHubMethod))
                {
                    if (!hubMethods.TryGetValue(methodDefinition.Name, out var overrideMethodDefinition))
                    {
                        hubMethods[methodDefinition.Name] = methodDefinition;
                    }
                    else if (!IsOverride(overrideMethodDefinition, methodDefinition))
                    {
                        throw new InvalidOperationException($"Duplicate definitions of '{methodDefinition.Name}'. Overloading is not supported.");
                    }
                }

                type = type.BaseType.Resolve();
            }

            return hubMethods.Values;
        }

        private bool IsHub(TypeDefinition type)
        {
            if (!type.IsPublic || type.IsAbstract || type.IsSpecialName)
            {
                return false;
            }

            while (type != null)
            {
                if (IsHubType(type))
                {
                    return true;
                }

                // BaseType can be null for interfaces
                type = type.BaseType?.Resolve();
            }

            return false;
        }

        private bool IsHubMethod(MethodDefinition method)
        {
            if (method.IsSpecialName || method.IsStatic || !method.IsPublic)
            {
                return false;
            }

            var hubTypeMethod = _hubTypeDefinition.Methods.SingleOrDefault(m => m.Name.Equals(method.Name, StringComparison.Ordinal));

            return hubTypeMethod == null || !MethodsMatch(hubTypeMethod, method);
        }

        private bool IsHubType(TypeReference type)
        {
            return IsSameType(type, _hubTypeDefinition);
        }

        private bool IsOverride(MethodDefinition overrideCandidate, MethodDefinition baseMethodDefinition)
        {
            return MethodsMatch(overrideCandidate, baseMethodDefinition);
        }

        private bool MethodsMatch(MethodDefinition method1, MethodDefinition method2)
        {
            return string.Equals(method1.Name, method2.Name, StringComparison.Ordinal) &&
                method1.Parameters.Count == method2.Parameters.Count &&
                method1.Parameters.Zip(method2.Parameters, (p1, p2) => IsSameType(p1.ParameterType, p2.ParameterType)).All(t => t);
        }

        private bool IsSameType(TypeReference t1, TypeReference t2)
        {
            if (t1.Namespace != t2.Namespace || t1.Name != t2.Name)
            {
                return false;
            }

            var type1 = _module.ImportReference(t1).Resolve();
            var type2 = _module.ImportReference(t2).Resolve();

            if (type1 == null || type2 == null)
            {
                throw new InvalidOperationException($"Could not resolve type: `{(type1 == null ? t1.FullName : t2.FullName)}`");
            }

            return type1.Module.Assembly.FullName == type2.Module.Assembly.FullName;
        }

        public void Dispose()
        {
            _assemblyResolver?.Dispose();
        }

        private class SameFolderAssemblyResolver : IAssemblyResolver
        {
            private readonly Dictionary<string, AssemblyDefinition> _assemblyCache = new Dictionary<string, AssemblyDefinition>();
            private readonly List<string> _searchPaths = new List<string>();

            public SameFolderAssemblyResolver(string directory)
            {
                _searchPaths.Add(directory);
                _searchPaths.Add(Path.GetDirectoryName(typeof(object).Module.FullyQualifiedName));
            }

            public AssemblyDefinition Resolve(AssemblyNameReference name)
            {
                return Resolve(name, null);
            }

            public AssemblyDefinition Resolve(AssemblyNameReference assemblyReferenceName, ReaderParameters parameters)
            {
                if (_assemblyCache.TryGetValue(assemblyReferenceName.FullName, out var assemblyDefinition))
                {
                    return assemblyDefinition;
                }

                if (parameters == null)
                {
                    parameters = new ReaderParameters();
                }

                if (parameters.AssemblyResolver == null)
                {
                    parameters.AssemblyResolver = this;
                }

                foreach (var directory in _searchPaths)
                {
                    var pathWithoutExtension = Path.Combine(directory, assemblyReferenceName.Name);
                    foreach (var extension in new[] { ".dll", ".exe" })
                    {
                        var fullPath = pathWithoutExtension + extension;
                        if (File.Exists(fullPath))
                        {
                            try
                            {
                                var assemblyDefintion = ModuleDefinition.ReadModule(fullPath, parameters).Assembly;
                                _assemblyCache[assemblyReferenceName.FullName] = assemblyDefintion;
                                return assemblyDefintion;
                            }
                            catch
                            {
                                // Unable to read module
                                // TODO: Log?
                            }
                        }

                    }
                }

                // Unable to resolve module
                // TOOD: Log?

                return null;
            }

            public void Dispose()
            {
                foreach (var assemblyDefinition in _assemblyCache.Values)
                {
                    assemblyDefinition.Dispose();
                }

                _assemblyCache.Clear();
            }
        }
    }
}
