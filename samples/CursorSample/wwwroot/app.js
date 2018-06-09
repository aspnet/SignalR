function addLine(list, line, color) {
    var child = document.createElement("li");
    if (color) {
        child.style.color = color;
    }
    child.innerText = line;
    list.appendChild(child);
}

document.addEventListener("DOMContentLoaded", function () {
    let resultsList = document.getElementById("resultsList");
    let startButton = document.getElementById("startButton");
    let stopButton = document.getElementById("stopButton");
    let clearButton = document.getElementById("clearButton");
    let userNameTextBox = document.getElementById("userNameTextBox");

    let connectButton = document.getElementById("connectButton");
    let disconnectButton = document.getElementById("disconnectButton");

    let connection = new signalR.HubConnectionBuilder()
        .configureLogging(signalR.LogLevel.Trace)
        .withUrl("/hubs/broadcast")
        .build();

    connection.onclose(function () {
        startButton.disabled = true;
        stopButton.disabled = true;
        connectButton.disabled = false;
        disconnectButton.disabled = true;

        addLine(resultsList, "disconnected", "green");
    });

    connection.on("Receive",
        (user, message) => {
            addLine(resultsList, user + ": " + message);
        });

    clearButton.addEventListener("click", (event) => {
        event.preventDefault();
        resultsList.innerHTML = "";
    });

    disconnectButton.addEventListener("click", (event) => {
        event.preventDefault();
        connection.stop();
    });

    connectButton.addEventListener("click", (event) => {
        event.preventDefault();

        connection.start()
            .then(function () {
                startButton.disabled = false;
                stopButton.disabled = true;
                connectButton.disabled = true;
                disconnectButton.disabled = false;
                addLine(resultsList, "connected", "green");
            });
    });

    let running = false;
    let timer;
    let counter = 0;
    const TICK_INTERVAL = 5000;
    startButton.addEventListener("click", (event) => {
        event.preventDefault();
        running = true;
        counter = 0;
        timer = setTimeout(tick, TICK_INTERVAL);
        stopButton.disabled = false;
        startButton.disabled = true;
    });

    stopButton.addEventListener("click", (event) => {
        event.preventDefault();
        running = false;
        if (timer) {
            clearTimeout(timer);
        }
        stopButton.disabled = true;
        startButton.disabled = false;
    });

    function tick() {
        if (running) {
            connection.send("Broadcast", userNameTextBox.value, counter)
                .catch((e) => console.error("Error sending broadcast: " + e.toString()));
            counter += 1;
            timer = setTimeout(tick, TICK_INTERVAL);
        }
    }
});
