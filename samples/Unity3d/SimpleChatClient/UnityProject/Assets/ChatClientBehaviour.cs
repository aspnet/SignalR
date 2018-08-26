using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using UnityEngine;

public class ChatClientBehaviour : MonoBehaviour
{

    public string SignalRServer;
    public bool EnableDebugLogging = false;

    private TextMesh textComponent;
    private HubConnection connection;
    
    private readonly Queue<Action> actionQueue = new Queue<Action>();
    private string deviceName;

    // Use this for initialization
    // ReSharper disable once UnusedMember.Local
    void Start () {
        textComponent = GetComponent<TextMesh>();
        deviceName = SystemInfo.deviceName;
        if (string.IsNullOrEmpty(SignalRServer) )
        {
            SignalRServer = "http://signalrsimplechatserverdotnetcore.azurewebsites.net/chatHub";
            QueueTextChange($"No connection specified. Defaulting to {SignalRServer}");
        }


        if (!Uri.IsWellFormedUriString(SignalRServer, UriKind.Absolute))
        {
            QueueTextChange("Connection URL is not valid");

        }
        else
        {
            Task.Run(() => Connect());
        }
    }

    // ReSharper disable once UnusedMember.Local
    void Update()
    {

        while (actionQueue.Count > 0)
        {
            actionQueue.Dequeue()();
        }
    }

    private void QueueTextChange(string message)
    {
        actionQueue.Enqueue(() => SetText(message));
    }

    private void SetText(string message)
    {
        textComponent.text = message;
    }
    

    private void Connect()
    {
        QueueTextChange("Initiating connection");
        
        ILoggerProvider logProvider = EnableDebugLogging ? (ILoggerProvider) new Unity3DDebugLogProvider() : NullLoggerProvider.Instance;
        
        connection = new HubConnectionBuilder()
            .WithUrl(SignalRServer)
            .ConfigureLogging(logging => logging.AddProvider(logProvider))
            .Build();

        connection.On<string, string>("ReceiveMessage", ReceiveMessage);
        try
        {
            connection.StartAsync().Wait();
            connection.InvokeAsync("SendMessage", deviceName, "connected").Wait();

        }
        catch (Exception ex)
        {
            Debug.LogError(ex.Message, this);
            QueueTextChange($"Error connecting: {ex.Message}");
        }
    }


    private void ReceiveMessage(string nick, string message)
    {
        QueueTextChange($"{nick} : {message}");
    }
}
