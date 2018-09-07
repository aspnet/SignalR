package com.microsoft.aspnet.signalr;

import java.util.ArrayList;

public class Binder {
    Binder(ActionBase action, ArrayList<Class<?>> classes) {
        this.action = action;
        this.classes = classes;
    }
    public ArrayList<Class<?>> classes;
    public ActionBase action;
}