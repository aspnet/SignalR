// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq.Expressions;
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
        private readonly object _nameTable;
        private readonly Action<JsonTextReader, object> _nameTableSetter;
        private readonly Func<string, string> _addMethod;

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

                _nameTableSetter = GetJsonNameTableSetter(propertyNameTableType);
            }
        }

        public void Apply(JsonTextReader reader)
        {
            _nameTableSetter.Invoke(reader, _nameTable);
        }

        public void Add(string key)
        {
            _addMethod.Invoke(key);
        }

        public static Action<JsonTextReader, object> GetJsonNameTableSetter(Type nameTableType)
        {
            // (textReader, nameTable) => textReader.NameTable = (NameTable)nameTable;
            ParameterExpression textReader = Expression.Parameter(typeof(JsonTextReader), "textReader");

            ParameterExpression nameTable = Expression.Parameter(typeof(object), "nameTable");

            MemberExpression member = Expression.Field(textReader, "NameTable");

            LambdaExpression lambda =
            Expression.Lambda(typeof(Action<JsonTextReader, object>),
                Expression.Assign(member, Expression.Convert(nameTable, nameTableType)), textReader, nameTable);

            var compiled = (Action<JsonTextReader, object>)lambda.Compile();
            return compiled;
        }
    }
}