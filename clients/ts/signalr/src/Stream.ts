// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

// This is an API that is similar to Observable, but we don't want users to confuse it for that. Someone could
// easily adapt it into the Rx interface if they wanted to.

export abstract class StreamSubscriber<T> {
    public abstract get closed(): boolean;
    public abstract next(value: T): void;
    public abstract error(err: any): void;
    public abstract complete(): void;
}

export abstract class StreamResult<T> {
    public abstract subscribe(observer: StreamSubscriber<T>): Subscription<T>;
}

export abstract class Subscription<T> {
    public abstract dispose(): void;
}
