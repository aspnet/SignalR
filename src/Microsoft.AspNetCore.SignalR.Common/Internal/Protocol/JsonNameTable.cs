// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Reflection;
using Newtonsoft.Json;

namespace Microsoft.AspNetCore.SignalR.Internal.Protocol
{
    // JSON.NET has an internal optimization it uses between JsonSerializer and
    // JsonTextReader to avoid allocating property names that exists as properties on .NET Types.
    // This class tries to use that same optimization for the known property names
    // in the JsonHubProtocol
    internal class JsonNameTable
    {
        // See https://github.com/JamesNK/Newtonsoft.Json/blob/993215529562866719689206e27e413013d4439c/Src/Newtonsoft.Json/Utilities/PropertyNameTable.cs
        private object _nameTable;
        private Func<string, string> _addMethod;

        private FieldInfo _nameTableFieldInfo;

        public JsonNameTable()
        {
            // Find the type in the JSON.NET assembly
            Type propertyNameTableType = typeof(JsonTextReader).Assembly.GetType("Newtonsoft.Json.Utilities.PropertyNameTable", throwOnError: false);

            // Something might have changed so be defensive
            if (propertyNameTableType != null)
            {
                var addMethod = propertyNameTableType.GetMethod("Add", BindingFlags.Public | BindingFlags.Instance);

                if (addMethod != null)
                {
                    try
                    {
                        _nameTable = Activator.CreateInstance(propertyNameTableType);

                        _addMethod = (Func<string, string>)addMethod.CreateDelegate(typeof(Func<string, string>), _nameTable);
                    }
                    catch
                    {
                        // Ignore errors
                        _nameTable = null;

                        return;
                    }
                }

                // REVIEW: Compile an expression tree?
                _nameTableFieldInfo = typeof(JsonTextReader).GetField("NameTable", BindingFlags.NonPublic | BindingFlags.Instance);
            }
        }

        public void Apply(JsonTextReader reader)
        {
            _nameTableFieldInfo.SetValue(reader, _nameTable);
        }

        public void Add(string key)
        {
            _addMethod.Invoke(key);
        }
    }
}