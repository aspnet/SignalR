// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.


export declare module Rx {
    // From RxJs: https://github.com/ReactiveX/rxjs/blob/master/src/Observer.ts
    export interface Observer<T> {
        closed?: boolean;
        next: (value: T) => void;
        error: (err: any) => void;
        complete: () => void;
    }

    export class Observable<T> {
        public _isScalar: boolean;
        // TODO: Return a Subscription so the caller can unsubscribe? IDisposable in System.IObservable
        subscribe(observer: Observer<T>): void;
        // complete(): void;
    }
}

export class Subject<T> implements Rx.Observable<T> {
    public _isScalar: boolean = false;

    observers: Rx.Observer<T>[];
    completed: boolean;

    constructor() {
        this.observers = [];
        this.completed = false;
    }

    public next(item: T): void {
        // loop backwards because array.splice can happen
        // and a backwards loop avoids missing items
        for (let i: number = this.observers.length - 1; i >= 0; i--) {
            let observer: Rx.Observer<T> = this.observers[i];
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
        this.completed = true;
        let observers: Rx.Observer<T>[] = this.observers;
        this.observers = [];
        // loop backwards because array.splice can happen
        // and a backwards loop avoids missing items
        for (let i: number = observers.length - 1; i >= 0; i--) {
            this.unsubscribe(observers, observers[i]);
        }
        // TODO: send some kind of completion to let server know to stop streaming
    }

    public subscribe(observer: Rx.Observer<T>): void {
        if (this.completed === false) {
            this.observers.push(observer);
        }
    }

    private unsubscribe(observers: Rx.Observer<T>[], observer: Rx.Observer<T>): void {
        let index: number = observers.indexOf(observer);
        if (index !== -1) {
            observers.splice(index, 1);
            observer.complete();
        }
    }
}
