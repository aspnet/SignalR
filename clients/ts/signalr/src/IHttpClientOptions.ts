/** Options provided to {@link @aspnet/signalr.DefaultHttpClient} and {@link @aspnet/signalr.XhrHttpClient} to configure options for the HTTP-based transports. */
export interface IHttpClientOptions {
    /** Disable withCredentials for the default {@link @aspnet/signalr.HttpClient} */
    disableXhrWithCredentials?: boolean;
}
