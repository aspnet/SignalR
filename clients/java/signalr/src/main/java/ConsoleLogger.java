// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

public class ConsoleLogger implements Logger {
    private LogLevel logLevel;
    public ConsoleLogger(LogLevel logLevel) {
            this.logLevel = logLevel;
    }

    @Override
    public void log(LogLevel logLevel, String message) {
        if(logLevel.value >= this.logLevel.value){
            switch (logLevel) {
                case Debug:
                case Information:
                case Warning:
                    System.out.println(message);
                    break;
                case Error:
                case Critical:
                    System.err.println(message);
                    break;
            }
        }
    }
}
