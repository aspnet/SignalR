import com.google.gson.JsonArray;
import org.junit.Assert;
import org.junit.Test;

import static org.junit.Assert.*;

public class JsonHubProtocolTest {
    private JsonHubProtocol jsonHubProtocol = new JsonHubProtocol();
    private static final String RECORD_SEPARATOR = "\u001e";

    @Test
    public void checkProtocolName() {
        assertEquals("json", jsonHubProtocol.getName());
    }

    @Test
    public void checkVersionNumber(){
        assertEquals(1,jsonHubProtocol.getVersion());
    }

    @Test
    public void checkTransferFormat(){
        assertEquals(TransferFormat.Text, jsonHubProtocol.getTransferFormat());
    }

    @Test
    public void VerifyWriteMessage(){
        InvocationMessage invocationMessage = new InvocationMessage("test", new Object[] {"42"});
        String result = jsonHubProtocol.writeMessage(invocationMessage);
        String expectedResult = "{\"type\":1,\"target\":\"test\",\"arguments\":[\"42\"]}\u001E";
        assertEquals(expectedResult, result);
    }

    @Test
    public void ParseSingleMessage(){
        String stringifiedMessage = "{\"type\":1,\"target\":\"test\",\"arguments\":[42]}\u001E";
        InvocationMessage[] messages = jsonHubProtocol.parseMessages(stringifiedMessage);
        //We know it's only one message
        InvocationMessage message = messages[0];
        assertEquals("test", message.target);
        assertEquals(null, message.invocationId);
        assertEquals(1, message.type);
        JsonArray messageResult = (JsonArray) message.arguments[0];
        assertEquals(42, messageResult.getAsInt());
    }
}

/*
*     String getName();
    int getVersion();
    TransferFormat getTransferFormat();
    InvocationMessage[] parseMessages(String message);
    String writeMessage(InvocationMessage message);
* */