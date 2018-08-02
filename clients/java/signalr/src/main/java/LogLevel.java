// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

public enum LogLevel {
    Debug(1),
    Information(2),
    Warning(3),
    Error(4),
    Critical(5),
    None(6);

    public int value;
    LogLevel(int id) { this.value = id; }
}

/*
*
*export enum LogLevel {
    /** Log level for very low severity diagnostic messages.
    Trace = 0,
            /** Log level for low severity diagnostic messages.
            Debug = 1,
            /** Log level for informational diagnostic messages.
            Information = 2,
            /** Log level for diagnostic messages that indicate a non-fatal problem.
            Warning = 3,
            /** Log level for diagnostic messages that indicate a failure in the current operation.
            Error = 4,
            /** Log level for diagnostic messages that indicate a failure that will terminate the entire application.
            Critical = 5,
            /** The highest possible log level. Used when configuring logging to indicate that no log messages should be emitted.
            None = 6,
            }
*
* */
