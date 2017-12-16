// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

// TODO: Seamless RxJs integration
// From RxJs: https://github.com/ReactiveX/rxjs/blob/master/src/Observer.ts
export interface Observer<T> {
    closed?: boolean;
    next: (value: T) => void;
    error?: (err: any) => void;
    complete?: () => void;
}

export class ObserverDisposable<T> {
    subject: Subject<T>;
    observer: Observer<T>;

    constructor(subject: Subject<T>, observer: Observer<T>) {
        this.subject = subject;
        this.observer = observer;
    }

    public dispose(): void {
        let index: number = this.subject.observers.indexOf(this.observer);
        if (index > -1) {
            this.subject.observers.splice(index, 1);
        }

        if (this.subject.observers.length === 0) {
            // TODO: cancel streaming on server
        }
    }
}

export interface Observable<T> {
    // TODO: Return a Subscription so the caller can unsubscribe? IDisposable in System.IObservable
    subscribe(observer: Observer<T>): ObserverDisposable<T>;
}

export class Subject<T> implements Observable<T> {
    observers: Observer<T>[];

    constructor() {
        this.observers = [];
    }

    public next(item: T): void {
        for (let observer of this.observers) {
            observer.next(item);
        }
    }

    public error(err: any): void {
        for (let observer of this.observers) {
            if (observer.error) {
                observer.error(err);
            }
        }
    }

    public complete(): void {
        for (let observer of this.observers) {
            if (observer.complete) {
                observer.complete();
            }
        }
    }

    public subscribe(observer: Observer<T>): ObserverDisposable<T> {
        this.observers.push(observer);
        return new ObserverDisposable(this, observer);
    }
}
