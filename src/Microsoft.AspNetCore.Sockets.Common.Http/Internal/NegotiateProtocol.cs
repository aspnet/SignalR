﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.AspNetCore.SignalR.Internal;
using Newtonsoft.Json;

namespace Microsoft.AspNetCore.Sockets.Internal
{
    public static class NegotiateProtocol
    {
        private static readonly UTF8Encoding _utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        private const string ConnectionIdPropertyName = "connectionId";
        private const string AvailableTransportsPropertyName = "availableTransports";
        private const string TransportPropertyName = "transport";
        private const string TransferFormatsPropertyName = "transferFormats";

        public static void WriteResponse(NegotiationResponse response, Stream output)
        {
            using (var jsonWriter = new JsonTextWriter(new StreamWriter(output, _utf8NoBom, 1024, leaveOpen: true)))
            {
                jsonWriter.WriteStartObject();
                jsonWriter.WritePropertyName(ConnectionIdPropertyName);
                jsonWriter.WriteValue(response.ConnectionId);
                jsonWriter.WritePropertyName(AvailableTransportsPropertyName);
                jsonWriter.WriteStartArray();

                foreach (AvailableTransport availableTransport in response.AvailableTransports)
                {
                    jsonWriter.WriteStartObject();
                    jsonWriter.WritePropertyName(TransportPropertyName);
                    jsonWriter.WriteValue(availableTransport.Transport);
                    jsonWriter.WritePropertyName(TransferFormatsPropertyName);
                    jsonWriter.WriteStartArray();

                    foreach (string transferFormat in availableTransport.TransferFormats)
                    {
                        jsonWriter.WriteValue(transferFormat);
                    }

                    jsonWriter.WriteEndArray();
                    jsonWriter.WriteEndObject();
                }

                jsonWriter.WriteEndArray();
                jsonWriter.WriteEndObject();

                jsonWriter.Flush();
            }
        }

        public static NegotiationResponse ParseResponse(Stream content)
        {
            try
            {
                using (var reader = new JsonTextReader(new StreamReader(content)))
                {
                    JsonUtils.CheckRead(reader);
                    JsonUtils.EnsureObjectStart(reader);

                    string connectionId = null;
                    List<AvailableTransport> availableTransports = null;

                    var completed = false;
                    while (!completed && JsonUtils.CheckRead(reader))
                    {
                        switch (reader.TokenType)
                        {
                            case JsonToken.PropertyName:
                                var memberName = reader.Value.ToString();

                                switch (memberName)
                                {
                                    case ConnectionIdPropertyName:
                                        connectionId = JsonUtils.ReadAsString(reader, ConnectionIdPropertyName);
                                        break;
                                    case AvailableTransportsPropertyName:
                                        JsonUtils.CheckRead(reader);
                                        JsonUtils.EnsureArrayStart(reader);

                                        availableTransports = new List<AvailableTransport>();
                                        while (JsonUtils.CheckRead(reader))
                                        {
                                            if (reader.TokenType == JsonToken.StartObject)
                                            {
                                                availableTransports.Add(ParseAvailableTransport(reader));
                                            }
                                            else if (reader.TokenType == JsonToken.EndArray)
                                            {
                                                break;
                                            }
                                        }
                                        break;
                                }
                                break;
                            case JsonToken.EndObject:
                                completed = true;
                                break;
                            default:
                                throw new InvalidDataException($"Unexpected token '{reader.TokenType}' when reading negotiation response JSON.");
                        }
                    }

                    if (connectionId == null)
                    {
                        throw new InvalidDataException($"Missing required property '{ConnectionIdPropertyName}'.");
                    }

                    if (availableTransports == null)
                    {
                        throw new InvalidDataException($"Missing required property '{AvailableTransportsPropertyName}'.");
                    }

                    return new NegotiationResponse
                    {
                        ConnectionId = connectionId,
                        AvailableTransports = availableTransports
                    };
                }
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("Invalid negotiation response received.", ex);
            }
        }

        private static AvailableTransport ParseAvailableTransport(JsonTextReader reader)
        {
            AvailableTransport availableTransport = new AvailableTransport();

            while (JsonUtils.CheckRead(reader))
            {
                switch (reader.TokenType)
                {
                    case JsonToken.PropertyName:
                        string memberName = reader.Value.ToString();

                        switch (memberName)
                        {
                            case TransportPropertyName:
                                availableTransport.Transport = JsonUtils.ReadAsString(reader, TransportPropertyName);
                                break;
                            case TransferFormatsPropertyName:
                                JsonUtils.CheckRead(reader);
                                JsonUtils.EnsureArrayStart(reader);

                                bool completed = false;
                                availableTransport.TransferFormats = new List<string>();
                                while (!completed && JsonUtils.CheckRead(reader))
                                {
                                    switch (reader.TokenType)
                                    {
                                        case JsonToken.String:
                                            availableTransport.TransferFormats.Add(reader.Value.ToString());
                                            break;
                                        case JsonToken.EndArray:
                                            completed = true;
                                            break;
                                        default:
                                            throw new InvalidDataException($"Unexpected token '{reader.TokenType}' when reading transfer formats JSON.");
                                    }
                                }
                                break;
                        }
                        break;
                    case JsonToken.EndObject:
                        if (availableTransport.Transport == null)
                        {
                            throw new InvalidDataException($"Missing required property '{TransportPropertyName}'.");
                        }

                        if (availableTransport.TransferFormats == null)
                        {
                            throw new InvalidDataException($"Missing required property '{TransferFormatsPropertyName}'.");
                        }

                        return availableTransport;
                    default:
                        throw new InvalidDataException($"Unexpected token '{reader.TokenType}' when reading available transport JSON.");
                }
            }

            throw new InvalidDataException("Unexpected end when reading JSON.");
        }
    }
}
