// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

package com.microsoft.aspnet.signalr;

import java.util.List;

public interface InvocationBinder {
    Class<?> GetReturnType(String invocationId);
    List<Class<?>> GetParameterTypes(String methodName) throws Exception;
}