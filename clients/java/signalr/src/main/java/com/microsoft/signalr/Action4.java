// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

package com.microsoft.signalr;

/**
 * A callback that takes four parameters.
 *
 * @param <T1> The type of the first parameter to the callback.
 * @param <T2> The type of the second parameter to the callback.
 * @param <T3> The type of the third parameter to the callback.
 * @param <T4> The type of the fourth parameter to the callback.
 */
public interface Action4<T1, T2, T3, T4> {
    // We can't use the @FunctionalInterface annotation because it's only
    // available on Android API Level 24 and above.
    void invoke(T1 param1, T2 param2, T3 param3, T4 param4);
}
