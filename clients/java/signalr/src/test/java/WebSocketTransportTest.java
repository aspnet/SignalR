import org.junit.Test;

import java.net.URISyntaxException;

import static org.junit.Assert.*;

public class WebSocketTransportTest {

    @Test
    public void checkThatWsIsUnchanged() throws URISyntaxException {
        WebSocketTransport webSocketTransport = new WebSocketTransport("ws://example.com");
        assertEquals("ws://example.com", webSocketTransport.getUrl().toString());
    }

    @Test
    public void checkThatWssIsUnchanged() throws URISyntaxException {
        WebSocketTransport webSocketTransport = new WebSocketTransport("wss://example.com");
        assertEquals("wss://example.com", webSocketTransport.getUrl().toString());
    }

    @Test
    public void checkThatHttpIsChangedToWs() throws URISyntaxException {
        WebSocketTransport webSocketTransport = new WebSocketTransport("http://example.com");
        assertEquals("ws://example.com", webSocketTransport.getUrl().toString());
    }

    @Test
    public void checkThatHttpsIsChangedToWss() throws URISyntaxException {
        WebSocketTransport webSocketTransport = new WebSocketTransport("https://example.com");
        assertEquals("wss://example.com", webSocketTransport.getUrl().toString());
    }

}