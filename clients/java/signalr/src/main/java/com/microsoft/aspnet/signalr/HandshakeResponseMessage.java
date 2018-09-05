// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license
// information.
package com.microsoft.aspnet.signalr;

public class HandshakeResponseMessage {
  public String error;

  public HandshakeResponseMessage() {
    this(null);
  }

  public HandshakeResponseMessage(String error) {
    this.error = error;
  }
}
