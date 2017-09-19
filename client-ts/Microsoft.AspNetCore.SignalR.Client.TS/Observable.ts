// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

// TODO: Seamless RxJs integration
// From RxJs: https://github.com/ReactiveX/rxjs/blob/master/src/Observer.ts
export interface Observer<T> {
    closed?: boolean;
    next: (value: T) => void;
    error: (err: any) => void;
    complete: () => void;
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
        for (let i: number = this.observers.length - 1; i >= 0; i--) {
            let observer: Observer<T> = this.observers[i];
            if (observer.closed === true) {
                continue;
            } else {
                observer.next(item);
            }

            if (observer.closed === <boolean>true) {
                this.unsubscribe(this.observers, observer);
            }
        }
    }

    public error(err: any): void {
        // loop backwards because array.splice can happen
        // and a backwards loop avoids missing items
        for (let i: number = this.observers.length - 1; i >= 0; i--) {
            this.observers[i].error(err);
        }
    }

    public complete(): void {
        let observers: Observer<T>[] = this.observers;
        this.observers = [];
        // loop backwards because array.splice can happen
        // and a backwards loop avoids missing items
        for (let i: number = observers.length - 1; i >= 0; i--) {
            this.unsubscribe(observers, observers[i]);
        }
        // TODO: send some kind of completion to let server know to stop streaming
    }

    public subscribe(observer: Observer<T>): void {
        this.observers.push(observer);
    }

    private unsubscribe(observers: Observer<T>[], observer: Observer<T>): void {
        let index: number = observers.indexOf(observer);
        if (index !== -1) {
            observers.splice(index, 1);
            observer.complete();
        }
    }
}
