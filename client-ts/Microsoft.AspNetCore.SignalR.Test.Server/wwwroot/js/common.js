const ECHOENDPOINT_URL = `http://${document.location.host}/echo`;

function eachTransport(action) {
    let transportTypes = [
        signalR.TransportType.WebSockets,
        signalR.TransportType.ServerSentEvents,
        signalR.TransportType.LongPolling ];
    transportTypes.forEach(t => action(t));
}

function eachTransportAndProtocol(action) {
    let transportTypes = [
        signalR.TransportType.WebSockets,
        signalR.TransportType.ServerSentEvents,
        signalR.TransportType.LongPolling ];
    let protocols = [
        new signalR.JsonHubProtocol(),
        new signalRMsgPack.MessagePackHubProtocol()
    ];
    transportTypes.forEach(t =>
        protocols.forEach(p => {
            if (!(t == signalR.TransportType.ServerSentEvents && p.name() == "messagepack")) {
                action(t, p);
            }
        }));
}