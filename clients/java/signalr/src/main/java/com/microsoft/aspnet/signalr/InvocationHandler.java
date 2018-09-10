// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

package com.microsoft.aspnet.signalr;

import java.util.ArrayList;

public class InvocationHandler {
    InvocationHandler(ActionBase action, ArrayList<Class<?>> classes) {
        this.action = action;
        this.classes = classes;
    }

    public ArrayList<Class<?>> classes;
    public ActionBase action;
}