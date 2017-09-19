// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

// TODO: Seamless RxJs integration
// From RxJs: https://github.com/ReactiveX/rxjs/blob/master/src/Observer.ts
export interface Observer<T> {
    closed?: boolean;
    next: (value: T) => void;
    error: (err: any) => void;
    complete: () => void;
    isCompleted?: boolean;
}

export interface Observable<T> {
    // TODO: Return a Subscription so the caller can unsubscribe? IDisposable in System.IObservable
    subscribe(observer: Observer<T>): void;
}

export class Subject<T> implements Observable<T> {
    observers: Observer<T>[];

    constructor() {
        this.observers = [];
    }

    public next(item: T): void {
        // loop backwards because array.splice can happen
        // and a backwards loop avoids missing items
        for (let i = this.observers.length - 1; i >= 0; i--) {
            let observer = this.observers[i];
            if (observer.closed === true) {
                continue;
            }
            else {
                observer.next(item);
            }

            if (observer.closed === true) {
                this.unsubscribe(observer);
            }
        }
    }

    public error(err: any): void {
        // loop backwards because array.splice can happen
        // and a backwards loop avoids missing items
        for (let i = this.observers.length - 1; i >= 0; i--) {
            this.observers[i].error(err);
        }
    }

    public complete(): void {
        // loop backwards because array.splice can happen
        // and a backwards loop avoids missing items
        for (let i = this.observers.length - 1; i >= 0; i--) {
            this.unsubscribe(this.observers[i]);
        }
    }

    public subscribe(observer: Observer<T>): void {
        this.observers.push(observer);
    }

    public unsubscribe(observer: Observer<T>): void {
        if (observer.isCompleted !== true) {
            observer.isCompleted = true;
            let index = this.observers.indexOf(observer);
            if (index !== -1) {
                this.observers.splice(index, 1);
                // TODO: send some kind of completion to let server know
                observer.complete();
            }
        }
    }
}
