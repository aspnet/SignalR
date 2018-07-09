public class PingMessage extends HubMessage {
    int type = 6;

    @Override
    int getMessageType() {
        return this.type;
    }
}
