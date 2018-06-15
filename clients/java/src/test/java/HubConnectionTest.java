import org.junit.Test;

import static org.junit.Assert.*;

public class HubConnectionTest {
    @Test
    public void testEmptyCollection() {
        HubConnection hubConnection = new HubConnection();
        assertTrue(hubConnection.methodToTest());
    }
}