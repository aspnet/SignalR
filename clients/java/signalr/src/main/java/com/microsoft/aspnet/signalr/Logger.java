// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license
// information.
package com.microsoft.aspnet.signalr;

public interface Logger {
  void log(LogLevel logLevel, String message);

  void log(LogLevel logLevel, String formattedMessage, Object... args);
}
