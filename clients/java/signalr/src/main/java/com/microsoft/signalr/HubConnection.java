// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

package com.microsoft.signalr;

import java.util.ArrayList;
import java.util.Collection;
import java.util.Date;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.Timer;
import java.util.TimerTask;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicInteger;
import java.util.concurrent.atomic.AtomicLong;
import java.util.concurrent.locks.Lock;
import java.util.concurrent.locks.ReentrantLock;

import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import io.reactivex.Completable;
import io.reactivex.Observable;
import io.reactivex.Single;
import io.reactivex.subjects.*;

/**
 * A connection used to invoke hub methods on a SignalR Server.
 */
public class HubConnection {
    private static final String RECORD_SEPARATOR = "\u001e";
    private static final List<Class<?>> emptyArray = new ArrayList<>();
    private static final int MAX_NEGOTIATE_ATTEMPTS = 100;

    private final String baseUrl;
    private Transport transport;
    private OnReceiveCallBack callback;
    private final CallbackMap handlers = new CallbackMap();
    private HubProtocol protocol;
    private Boolean handshakeReceived = false;
    private HubConnectionState hubConnectionState = HubConnectionState.DISCONNECTED;
    private final Lock hubConnectionStateLock = new ReentrantLock();
    private List<OnClosedCallback> onClosedCallbackList;
    private final boolean skipNegotiate;
    private Single<String> accessTokenProvider;
    private final Map<String, String> headers = new HashMap<>();
    private ConnectionState connectionState = null;
    private final HttpClient httpClient;
    private String stopError;
    private Timer pingTimer = null;
    private final AtomicLong nextServerTimeout = new AtomicLong();
    private final AtomicLong nextPingActivation = new AtomicLong();
    private long keepAliveInterval = 15*1000;
    private long serverTimeout = 30*1000;
    private long tickRate = 1000;
    private CompletableSubject handshakeResponseSubject;
    private long handshakeResponseTimeout = 15*1000;
    private final Logger logger = LoggerFactory.getLogger(HubConnection.class);

    /**
     * Sets the server timeout interval for the connection.
     *
     * @param serverTimeoutInMilliseconds The server timeout duration (specified in milliseconds).
     */
    public void setServerTimeout(long serverTimeoutInMilliseconds) {
        this.serverTimeout = serverTimeoutInMilliseconds;
    }

    /**
     * Gets the server timeout duration.
     *
     * @return The server timeout duration (specified in milliseconds).
     */
    public long getServerTimeout() {
        return this.serverTimeout;
    }

    /**
     * Sets the keep alive interval duration.
     *
     * @param keepAliveIntervalInMilliseconds The interval (specified in milliseconds) at which the connection should send keep alive messages.
     */
    public void setKeepAliveInterval(long keepAliveIntervalInMilliseconds) {
        this.keepAliveInterval = keepAliveIntervalInMilliseconds;
    }

    /**
     * Gets the keep alive interval.
     *
     * @return The interval (specified in milliseconds) between keep alive messages.
     */
    public long getKeepAliveInterval() {
        return this.keepAliveInterval;
    }

    // For testing purposes
    void setTickRate(long tickRateInMilliseconds) {
        this.tickRate = tickRateInMilliseconds;
    }

    HubConnection(String url, Transport transport, boolean skipNegotiate, HttpClient httpClient,
                  Single<String> accessTokenProvider, long handshakeResponseTimeout, Map<String, String> headers) {
        if (url == null || url.isEmpty()) {
            throw new IllegalArgumentException("A valid url is required.");
        }

        this.baseUrl = url;
        this.protocol = new JsonHubProtocol();

        if (accessTokenProvider != null) {
            this.accessTokenProvider = accessTokenProvider;
        } else {
            this.accessTokenProvider = Single.just("");
        }

        if (httpClient != null) {
            this.httpClient = httpClient;
        } else {
            this.httpClient = new DefaultHttpClient();
        }

        if (transport != null) {
            this.transport = transport;
        }

        if (handshakeResponseTimeout > 0) {
            this.handshakeResponseTimeout = handshakeResponseTimeout;
        }

        if (headers != null) {
            this.headers.putAll(headers);
        }

        this.skipNegotiate = skipNegotiate;

        this.callback = (payload) -> {
            resetServerTimeout();
            if (!handshakeReceived) {
                int handshakeLength = payload.indexOf(RECORD_SEPARATOR) + 1;
                String handshakeResponseString = payload.substring(0, handshakeLength - 1);
                HandshakeResponseMessage handshakeResponse;
                try {
                    handshakeResponse = HandshakeProtocol.parseHandshakeResponse(handshakeResponseString);
                } catch (RuntimeException ex) {
                    RuntimeException exception = new RuntimeException("An invalid handshake response was received from the server.", ex);
                    handshakeResponseSubject.onError(exception);
                    throw exception;
                }
                if (handshakeResponse.getHandshakeError() != null) {
                    String errorMessage = "Error in handshake " + handshakeResponse.getHandshakeError();
                    logger.error(errorMessage);
                    RuntimeException exception = new RuntimeException(errorMessage);
                    handshakeResponseSubject.onError(exception);
                    throw exception;
                }
                handshakeReceived = true;
                handshakeResponseSubject.onComplete();

                payload = payload.substring(handshakeLength);
                // The payload only contained the handshake response so we can return.
                if (payload.length() == 0) {
                    return;
                }
            }

            HubMessage[] messages = protocol.parseMessages(payload, connectionState);

            for (HubMessage message : messages) {
                logger.debug("Received message of type {}.", message.getMessageType());
                switch (message.getMessageType()) {
                    case INVOCATION_BINDING_FAILURE:
                        InvocationBindingFailureMessage msg = (InvocationBindingFailureMessage)message;
                        logger.error("Failed to bind arguments received in invocation '{}' of '{}'.", msg.getInvocationId(), msg.getTarget(), msg.getException());
                        break;
                    case INVOCATION:
                        InvocationMessage invocationMessage = (InvocationMessage) message;
                        List<InvocationHandler> handlers = this.handlers.get(invocationMessage.getTarget());
                        if (handlers != null) {
                            for (InvocationHandler handler : handlers) {
                                handler.getAction().invoke(invocationMessage.getArguments());
                            }
                        } else {
                            logger.warn("Failed to find handler for '{}' method.", invocationMessage.getTarget());
                        }
                        break;
                    case CLOSE:
                        logger.info("Close message received from server.");
                        CloseMessage closeMessage = (CloseMessage) message;
                        stop(closeMessage.getError());
                        break;
                    case PING:
                        // We don't need to do anything in the case of a ping message.
                        break;
                    case COMPLETION:
                        CompletionMessage completionMessage = (CompletionMessage)message;
                        InvocationRequest irq = connectionState.tryRemoveInvocation(completionMessage.getInvocationId());
                        if (irq == null) {
                            logger.warn("Dropped unsolicited Completion message for invocation '{}'.", completionMessage.getInvocationId());
                            continue;
                        }
                        irq.complete(completionMessage);
                        break;
                    case STREAM_ITEM:
                        StreamItem streamItem = (StreamItem)message;
                        InvocationRequest streamInvocationRequest = connectionState.getInvocation(streamItem.getInvocationId());
                        if (streamInvocationRequest == null) {
                            logger.warn("Dropped unsolicited Completion message for invocation '{}'.", streamItem.getInvocationId());
                            continue;
                        }

                        streamInvocationRequest.addItem(streamItem);
                        break;
                    case STREAM_INVOCATION:
                    case CANCEL_INVOCATION:
                        logger.error("This client does not support {} messages.", message.getMessageType());

                        throw new UnsupportedOperationException(String.format("The message type %s is not supported yet.", message.getMessageType()));
                }
            }
        };
    }

    private void timeoutHandshakeResponse(long timeout, TimeUnit unit) {
        ScheduledExecutorService scheduledThreadPool = Executors.newSingleThreadScheduledExecutor();
        scheduledThreadPool.schedule(() -> {
            // If onError is called on a completed subject the global error handler is called
            if (!(handshakeResponseSubject.hasComplete() || handshakeResponseSubject.hasThrowable()))
            {
                handshakeResponseSubject.onError(
                    new TimeoutException("Timed out waiting for the server to respond to the handshake message."));
            }
        }, timeout, unit);
    }

    private Single<NegotiateResponse> handleNegotiate(String url) {
        HttpRequest request = new HttpRequest();
        request.addHeaders(this.headers);

        return httpClient.post(Negotiate.resolveNegotiateUrl(url), request).map((response) -> {
            if (response.getStatusCode() != 200) {
                throw new RuntimeException(String.format("Unexpected status code returned from negotiate: %d %s.", response.getStatusCode(), response.getStatusText()));
            }
            NegotiateResponse negotiateResponse = new NegotiateResponse(response.getContent());

            if (negotiateResponse.getError() != null) {
                throw new RuntimeException(negotiateResponse.getError());
            }

            if (negotiateResponse.getAccessToken() != null) {
                this.accessTokenProvider = Single.just(negotiateResponse.getAccessToken());
                String token = "";
                // We know the Single is non blocking in this case
                // It's fine to call blockingGet() on it.
                token = this.accessTokenProvider.blockingGet();
                this.headers.put("Authorization", "Bearer " + token);
            }

            return negotiateResponse;
        });
    }

    /**
     * Indicates the state of the {@link HubConnection} to the server.
     *
     * @return HubConnection state enum.
     */
    public HubConnectionState getConnectionState() {
        return hubConnectionState;
    }

    /**
     * Starts a connection to the server.
     *
     * @return A Completable that completes when the connection has been established.
     */
    public Completable start() {
        if (hubConnectionState != HubConnectionState.DISCONNECTED) {
            return Completable.complete();
        }

        handshakeResponseSubject = CompletableSubject.create();
        handshakeReceived = false;
        CompletableSubject tokenCompletable = CompletableSubject.create();
        accessTokenProvider.subscribe(token -> {
            if (token != null && !token.isEmpty()) {
                this.headers.put("Authorization", "Bearer " + token);
            }
            tokenCompletable.onComplete();
        });

        stopError = null;
        Single<String> negotiate = null;
        if (!skipNegotiate) {
            negotiate = tokenCompletable.andThen(Single.defer(() -> startNegotiate(baseUrl, 0)));
        } else {
            negotiate = tokenCompletable.andThen(Single.defer(() -> Single.just(baseUrl)));
        }

        CompletableSubject start = CompletableSubject.create();

        negotiate.flatMapCompletable(url -> {
            logger.debug("Starting HubConnection.");
            if (transport == null) {
                transport = new WebSocketTransport(headers, httpClient);
            }

            transport.setOnReceive(this.callback);
            transport.setOnClose((message) -> stopConnection(message));

            return transport.start(url).andThen(Completable.defer(() -> {
                String handshake = HandshakeProtocol.createHandshakeRequestMessage(
                        new HandshakeRequestMessage(protocol.getName(), protocol.getVersion()));

                return transport.send(handshake).andThen(Completable.defer(() -> {
                    timeoutHandshakeResponse(handshakeResponseTimeout, TimeUnit.MILLISECONDS);
                    return handshakeResponseSubject.andThen(Completable.defer(() -> {
                        hubConnectionStateLock.lock();
                        try {
                            connectionState = new ConnectionState(this);
                            hubConnectionState = HubConnectionState.CONNECTED;
                            logger.info("HubConnection started.");

                            resetServerTimeout();
                            this.pingTimer = new Timer();
                            this.pingTimer.schedule(new TimerTask() {
                                @Override
                                public void run() {
                                    try {
                                        if (System.currentTimeMillis() > nextServerTimeout.get()) {
                                            stop("Server timeout elapsed without receiving a message from the server.");
                                            return;
                                        }

                                        if (System.currentTimeMillis() > nextPingActivation.get()) {
                                            sendHubMessage(PingMessage.getInstance());
                                        }
                                    } catch (Exception e) {
                                        logger.warn("Error sending ping: {}.", e.getMessage());
                                        // The connection is probably in a bad or closed state now, cleanup the timer so
                                        // it stops triggering
                                        pingTimer.cancel();
                                    }
                                }
                            }, new Date(0), tickRate);
                        } finally {
                            hubConnectionStateLock.unlock();
                        }

                        return Completable.complete();
                    }));
                }));
            }));
        // subscribe makes this a "hot" completable so this runs immediately
        }).subscribeWith(start);

        return start;
    }

    private Single<String> startNegotiate(String url, int negotiateAttempts) {
        if (hubConnectionState != HubConnectionState.DISCONNECTED) {
            return Single.just(null);
        }

        return handleNegotiate(url).flatMap(response -> {
            if (response.getRedirectUrl() != null && negotiateAttempts >= MAX_NEGOTIATE_ATTEMPTS) {
                throw new RuntimeException("Negotiate redirection limit exceeded.");
            }

            if (response.getRedirectUrl() == null) {
                if (!response.getAvailableTransports().contains("WebSockets")) {
                    throw new RuntimeException("There were no compatible transports on the server.");
                }

                String finalUrl = url;
                if (response.getConnectionId() != null) {
                    if (url.contains("?")) {
                        finalUrl = url + "&id=" + response.getConnectionId();
                    } else {
                        finalUrl = url + "?id=" + response.getConnectionId();
                    }
                }

                return Single.just(finalUrl);
            }

            return startNegotiate(response.getRedirectUrl(), negotiateAttempts + 1);
        });
    }

    /**
     * Stops a connection to the server.
     *
     * @param errorMessage An error message if the connected needs to be stopped because of an error.
     * @return A Completable that completes when the connection has been stopped.
     */
    private Completable stop(String errorMessage) {
        hubConnectionStateLock.lock();
        try {
            if (hubConnectionState == HubConnectionState.DISCONNECTED) {
                return Completable.complete();
            }

            if (errorMessage != null) {
                stopError = errorMessage;
                logger.error("HubConnection disconnected with an error: {}.", errorMessage);
            } else {
                logger.debug("Stopping HubConnection.");
            }
        } finally {
            hubConnectionStateLock.unlock();
        }

        return transport.stop();
    }

    /**
     * Stops a connection to the server.
     *
     * @return A Completable that completes when the connection has been stopped.
     */
    public Completable stop() {
        return stop(null);
    }

    private void stopConnection(String errorMessage) {
        RuntimeException exception = null;
        hubConnectionStateLock.lock();
        try {
            // errorMessage gets passed in from the transport. An already existing stopError value
            // should take precedence.
            if (stopError != null) {
                errorMessage = stopError;
            }
            if (errorMessage != null) {
                exception = new RuntimeException(errorMessage);
                logger.error("HubConnection disconnected with an error {}.", errorMessage);
            }
            connectionState.cancelOutstandingInvocations(exception);
            connectionState = null;
            logger.info("HubConnection stopped.");
            hubConnectionState = HubConnectionState.DISCONNECTED;
            handshakeResponseSubject.onComplete();
        } finally {
            hubConnectionStateLock.unlock();
        }

        // Do not run these callbacks inside the hubConnectionStateLock
        if (onClosedCallbackList != null) {
            for (OnClosedCallback callback : onClosedCallbackList) {
                callback.invoke(exception);
            }
        }
    }

    /**
     * Invokes a hub method on the server using the specified method name.
     * Does not wait for a response from the receiver.
     *
     * @param method The name of the server method to invoke.
     * @param args   The arguments to be passed to the method.
     */
    public void send(String method, Object... args) {
        if (hubConnectionState != HubConnectionState.CONNECTED) {
            throw new RuntimeException("The 'send' method cannot be called if the connection is not active");
        }

        InvocationMessage invocationMessage = new InvocationMessage(null, method, args);
        sendHubMessage(invocationMessage);
    }

    /**
     * Invokes a hub method on the server using the specified method name and arguments.
     *
     * @param returnType The expected return type.
     * @param method The name of the server method to invoke.
     * @param args The arguments used to invoke the server method.
     * @param <T> The expected return type.
     * @return A Single that yields the return value when the invocation has completed.
     */
    @SuppressWarnings("unchecked")
    public <T> Single<T> invoke(Class<T> returnType, String method, Object... args) {
        String id = connectionState.getNextInvocationId();
        InvocationMessage invocationMessage = new InvocationMessage(id, method, args);

        SingleSubject<T> subject = SingleSubject.create();
        InvocationRequest irq = new InvocationRequest(returnType, id);
        connectionState.addInvocation(irq);

        // forward the invocation result or error to the user
        // run continuations on a separate thread
        Subject<Object> pendingCall = irq.getPendingCall();
        pendingCall.subscribe(result -> {
            // Primitive types can't be cast with the Class cast function
            if (returnType.isPrimitive()) {
                subject.onSuccess((T)result);
            } else {
                subject.onSuccess(returnType.cast(result));
            }
        }, error -> subject.onError(error));

        // Make sure the actual send is after setting up the callbacks otherwise there is a race
        // where the map doesn't have the callbacks yet when the response is returned
        sendHubMessage(invocationMessage);

        return subject;
    }

    /**
     * Invokes a streaming hub method on the server using the specified name and arguments.
     *
     * @param returnType The expected return type of the stream items.
     * @param method The name of the server method to invoke.
     * @param args The arguments used to invoke the server method.
     * @param <T> The expected return type.
     * @return An observable that yields the streaming results from the server.
     */
    @SuppressWarnings("unchecked")
    public <T> Observable<T> stream(Class<T> returnType, String method, Object ... args) {
        String invocationId = connectionState.getNextInvocationId();
        AtomicInteger subscriptionCount = new AtomicInteger();
        StreamInvocationMessage streamInvocationMessage = new StreamInvocationMessage(invocationId, method, args);
        InvocationRequest irq = new InvocationRequest(returnType, invocationId);
        connectionState.addInvocation(irq);
        ReplaySubject<T> subject = ReplaySubject.create();

        Subject<Object> pendingCall = irq.getPendingCall();
        pendingCall.subscribe(result -> {
            // Primitive types can't be cast with the Class cast function
            if (returnType.isPrimitive()) {
                subject.onNext((T)result);
            } else {
                subject.onNext(returnType.cast(result));
            }
        }, error -> subject.onError(error),
                () -> subject.onComplete());

        sendHubMessage(streamInvocationMessage);
        Observable<T> observable = subject.doOnSubscribe((subscriber) -> subscriptionCount.incrementAndGet());

        return observable.doOnDispose(() -> {
            if (subscriptionCount.decrementAndGet() == 0) {
                CancelInvocationMessage cancelInvocationMessage = new CancelInvocationMessage(invocationId);
                sendHubMessage(cancelInvocationMessage);
                connectionState.tryRemoveInvocation(invocationId);
                subject.onComplete();
            }
        });
    }

    private void sendHubMessage(HubMessage message) {
        String serializedMessage = protocol.writeMessage(message);
        if (message.getMessageType() == HubMessageType.INVOCATION ) {
            logger.debug("Sending {} message '{}'.", message.getMessageType().name(), ((InvocationMessage)message).getInvocationId());
        } else  if (message.getMessageType() == HubMessageType.STREAM_INVOCATION) {
            logger.debug("Sending {} message '{}'.", message.getMessageType().name(), ((StreamInvocationMessage)message).getInvocationId());
        } else {
            logger.debug("Sending {} message.", message.getMessageType().name());
        }
        transport.send(serializedMessage);

        resetKeepAlive();
    }

    private void resetServerTimeout() {
        this.nextServerTimeout.set(System.currentTimeMillis() + serverTimeout);
    }

    private void resetKeepAlive() {
        this.nextPingActivation.set(System.currentTimeMillis() + keepAliveInterval);
    }

    /**
     * Removes all handlers associated with the method with the specified method name.
     *
     * @param name The name of the hub method from which handlers are being removed.
     */
    public void remove(String name) {
        handlers.remove(name);
        logger.trace("Removing handlers for client method: {}.", name);
    }

    /**
     * Registers a callback to run when the connection is closed.
     *
     * @param callback A callback to run when the connection closes.
     */
    public void onClosed(OnClosedCallback callback) {
        if (onClosedCallbackList == null) {
            onClosedCallbackList = new ArrayList<>();
        }

        onClosedCallbackList.add(callback);
    }

    /**
     * Registers a handler that will be invoked when the hub method with the specified method name is invoked.
     *
     * @param target   The name of the hub method to define.
     * @param callback The handler that will be raised when the hub method is invoked.
     * @return A {@link Subscription} that can be disposed to unsubscribe from the hub method.
     */
    public Subscription on(String target, Action callback) {
        ActionBase action = args -> callback.invoke();
        return registerHandler(target, action);
    }

    /**
     * Registers a handler that will be invoked when the hub method with the specified method name is invoked.
     *
     * @param target   The name of the hub method to define.
     * @param callback The handler that will be raised when the hub method is invoked.
     * @param param1   The first parameter.
     * @param <T1>     The first argument type.
     * @return A {@link Subscription} that can be disposed to unsubscribe from the hub method.
     */
    public <T1> Subscription on(String target, Action1<T1> callback, Class<T1> param1) {
        ActionBase action = params -> callback.invoke(param1.cast(params[0]));
        return registerHandler(target, action, param1);

    }

    /**
     * Registers a handler that will be invoked when the hub method with the specified method name is invoked.
     *
     * @param target   The name of the hub method to define.
     * @param callback The handler that will be raised when the hub method is invoked.
     * @param param1   The first parameter.
     * @param param2   The second parameter.
     * @param <T1>     The first parameter type.
     * @param <T2>     The second parameter type.
     * @return A {@link Subscription} that can be disposed to unsubscribe from the hub method.
     */
    public <T1, T2> Subscription on(String target, Action2<T1, T2> callback, Class<T1> param1, Class<T2> param2) {
        ActionBase action = params -> {
            callback.invoke(param1.cast(params[0]), param2.cast(params[1]));
        };
        return registerHandler(target, action, param1, param2);
    }

    /**
     * Registers a handler that will be invoked when the hub method with the specified method name is invoked.
     *
     * @param target   The name of the hub method to define.
     * @param callback The handler that will be raised when the hub method is invoked.
     * @param param1   The first parameter.
     * @param param2   The second parameter.
     * @param param3   The third parameter.
     * @param <T1>     The first parameter type.
     * @param <T2>     The second parameter type.
     * @param <T3>     The third parameter type.
     * @return A {@link Subscription} that can be disposed to unsubscribe from the hub method.
     */
    public <T1, T2, T3> Subscription on(String target, Action3<T1, T2, T3> callback,
                                        Class<T1> param1, Class<T2> param2, Class<T3> param3) {
        ActionBase action = params -> {
            callback.invoke(param1.cast(params[0]), param2.cast(params[1]), param3.cast(params[2]));
        };
        return registerHandler(target, action, param1, param2, param3);
    }

    /**
     * Registers a handler that will be invoked when the hub method with the specified method name is invoked.
     *
     * @param target   The name of the hub method to define.
     * @param callback The handler that will be raised when the hub method is invoked.
     * @param param1   The first parameter.
     * @param param2   The second parameter.
     * @param param3   The third parameter.
     * @param param4   The fourth parameter.
     * @param <T1>     The first parameter type.
     * @param <T2>     The second parameter type.
     * @param <T3>     The third parameter type.
     * @param <T4>     The fourth parameter type.
     * @return A {@link Subscription} that can be disposed to unsubscribe from the hub method.
     */
    public <T1, T2, T3, T4> Subscription on(String target, Action4<T1, T2, T3, T4> callback,
                                            Class<T1> param1, Class<T2> param2, Class<T3> param3, Class<T4> param4) {
        ActionBase action = params -> {
            callback.invoke(param1.cast(params[0]), param2.cast(params[1]), param3.cast(params[2]), param4.cast(params[3]));
        };
        return registerHandler(target, action, param1, param2, param3, param4);
    }

    /**
     * Registers a handler that will be invoked when the hub method with the specified method name is invoked.
     *
     * @param target   The name of the hub method to define.
     * @param callback The handler that will be raised when the hub method is invoked.
     * @param param1   The first parameter.
     * @param param2   The second parameter.
     * @param param3   The third parameter.
     * @param param4   The fourth parameter.
     * @param param5   The fifth parameter.
     * @param <T1>     The first parameter type.
     * @param <T2>     The second parameter type.
     * @param <T3>     The third parameter type.
     * @param <T4>     The fourth parameter type.
     * @param <T5>     The fifth parameter type.
     * @return A {@link Subscription} that can be disposed to unsubscribe from the hub method.
     */
    public <T1, T2, T3, T4, T5> Subscription on(String target, Action5<T1, T2, T3, T4, T5> callback,
                                                Class<T1> param1, Class<T2> param2, Class<T3> param3, Class<T4> param4, Class<T5> param5) {
        ActionBase action = params -> {
            callback.invoke(param1.cast(params[0]), param2.cast(params[1]), param3.cast(params[2]), param4.cast(params[3]),
                    param5.cast(params[4]));
        };
        return registerHandler(target, action, param1, param2, param3, param4, param5);
    }

    /**
     * Registers a handler that will be invoked when the hub method with the specified method name is invoked.
     *
     * @param target   The name of the hub method to define.
     * @param callback The handler that will be raised when the hub method is invoked.
     * @param param1   The first parameter.
     * @param param2   The second parameter.
     * @param param3   The third parameter.
     * @param param4   The fourth parameter.
     * @param param5   The fifth parameter.
     * @param param6   The sixth parameter.
     * @param <T1>     The first parameter type.
     * @param <T2>     The second parameter type.
     * @param <T3>     The third parameter type.
     * @param <T4>     The fourth parameter type.
     * @param <T5>     The fifth parameter type.
     * @param <T6>     The sixth parameter type.
     * @return A {@link Subscription} that can be disposed to unsubscribe from the hub method.
     */
    public <T1, T2, T3, T4, T5, T6> Subscription on(String target, Action6<T1, T2, T3, T4, T5, T6> callback,
                                                    Class<T1> param1, Class<T2> param2, Class<T3> param3, Class<T4> param4, Class<T5> param5, Class<T6> param6) {
        ActionBase action = params -> {
            callback.invoke(param1.cast(params[0]), param2.cast(params[1]), param3.cast(params[2]), param4.cast(params[3]),
                    param5.cast(params[4]), param6.cast(params[5]));
        };
        return registerHandler(target, action, param1, param2, param3, param4, param5, param6);
    }

    /**
     * Registers a handler that will be invoked when the hub method with the specified method name is invoked.
     *
     * @param target   The name of the hub method to define.
     * @param callback The handler that will be raised when the hub method is invoked.
     * @param param1   The first parameter.
     * @param param2   The second parameter.
     * @param param3   The third parameter.
     * @param param4   The fourth parameter.
     * @param param5   The fifth parameter.
     * @param param6   The sixth parameter.
     * @param param7   The seventh parameter.
     * @param <T1>     The first parameter type.
     * @param <T2>     The second parameter type.
     * @param <T3>     The third parameter type.
     * @param <T4>     The fourth parameter type.
     * @param <T5>     The fifth parameter type.
     * @param <T6>     The sixth parameter type.
     * @param <T7>     The seventh parameter type.
     * @return A {@link Subscription} that can be disposed to unsubscribe from the hub method.
     */
    public <T1, T2, T3, T4, T5, T6, T7> Subscription on(String target, Action7<T1, T2, T3, T4, T5, T6, T7> callback,
                                                        Class<T1> param1, Class<T2> param2, Class<T3> param3, Class<T4> param4, Class<T5> param5, Class<T6> param6, Class<T7> param7) {
        ActionBase action = params -> {
            callback.invoke(param1.cast(params[0]), param2.cast(params[1]), param3.cast(params[2]), param4.cast(params[3]),
                    param5.cast(params[4]), param6.cast(params[5]), param7.cast(params[6]));
        };
        return registerHandler(target, action, param1, param2, param3, param4, param5, param6, param7);
    }

    /**
     * Registers a handler that will be invoked when the hub method with the specified method name is invoked.
     *
     * @param target   The name of the hub method to define.
     * @param callback The handler that will be raised when the hub method is invoked.
     * @param param1   The first parameter.
     * @param param2   The second parameter.
     * @param param3   The third parameter.
     * @param param4   The fourth parameter.
     * @param param5   The fifth parameter.
     * @param param6   The sixth parameter.
     * @param param7   The seventh parameter.
     * @param param8   The eighth parameter
     * @param <T1>     The first parameter type.
     * @param <T2>     The second parameter type.
     * @param <T3>     The third parameter type.
     * @param <T4>     The fourth parameter type.
     * @param <T5>     The fifth parameter type.
     * @param <T6>     The sixth parameter type.
     * @param <T7>     The seventh parameter type.
     * @param <T8>     The eighth parameter type.
     * @return A {@link Subscription} that can be disposed to unsubscribe from the hub method.
     */
    public <T1, T2, T3, T4, T5, T6, T7, T8> Subscription on(String target, Action8<T1, T2, T3, T4, T5, T6, T7, T8> callback,
                                                            Class<T1> param1, Class<T2> param2, Class<T3> param3, Class<T4> param4, Class<T5> param5, Class<T6> param6, Class<T7> param7, Class<T8> param8) {
        ActionBase action = params -> {
            callback.invoke(param1.cast(params[0]), param2.cast(params[1]), param3.cast(params[2]), param4.cast(params[3]),
                    param5.cast(params[4]), param6.cast(params[5]), param7.cast(params[6]), param8.cast(params[7]));
        };
        return registerHandler(target, action, param1, param2, param3, param4, param5, param6, param7, param8);
    }

    private Subscription registerHandler(String target, ActionBase action, Class<?>... types) {
        InvocationHandler handler = handlers.put(target, action, types);
        logger.debug("Registering handler for client method: '{}'.", target);
        return new Subscription(handlers, handler, target);
    }

    private final class ConnectionState implements InvocationBinder {
        private final HubConnection connection;
        private final AtomicInteger nextId = new AtomicInteger(0);
        private final HashMap<String, InvocationRequest> pendingInvocations = new HashMap<>();
        private final Lock lock = new ReentrantLock();

        public ConnectionState(HubConnection connection) {
            this.connection = connection;
        }

        public String getNextInvocationId() {
            int i = nextId.incrementAndGet();
            return Integer.toString(i);
        }

        public void cancelOutstandingInvocations(Exception ex) {
            lock.lock();
            try {
                Collection<String> keys = pendingInvocations.keySet();
                for (String key : keys) {
                    if (ex == null) {
                        pendingInvocations.get(key).cancel();
                    } else {
                        pendingInvocations.get(key).fail(ex);
                    }
                }

                pendingInvocations.clear();
            } finally {
                lock.unlock();
            }
        }

        public void addInvocation(InvocationRequest irq) {
            lock.lock();
            try {
                if (pendingInvocations.containsKey(irq.getInvocationId())) {
                    throw new IllegalStateException("Invocation Id is already used");
                } else {
                    pendingInvocations.put(irq.getInvocationId(), irq);
                }
            } finally {
                lock.unlock();
            }
        }

        public InvocationRequest getInvocation(String id) {
            lock.lock();
            try {
                return pendingInvocations.get(id);
            } finally {
                lock.unlock();
            }
        }

        public InvocationRequest tryRemoveInvocation(String id) {
            lock.lock();
            try {
                return pendingInvocations.remove(id);
            } finally {
                lock.unlock();
            }
        }

        @Override
        public Class<?> getReturnType(String invocationId) {
            InvocationRequest irq = getInvocation(invocationId);
            if (irq == null) {
                return null;
            }

            return irq.getReturnType();
        }

        @Override
        public List<Class<?>> getParameterTypes(String methodName) {
            List<InvocationHandler> handlers = connection.handlers.get(methodName);
            if (handlers == null) {
                logger.warn("Failed to find handler for '{}' method.", methodName);
                return emptyArray;
            }

            if (handlers.isEmpty()) {
                throw new RuntimeException(String.format("There are no callbacks registered for the method '%s'.", methodName));
            }

            return handlers.get(0).getClasses();
        }
    }
}
