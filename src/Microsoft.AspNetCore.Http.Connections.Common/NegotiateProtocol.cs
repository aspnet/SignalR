// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using Microsoft.AspNetCore.Internal;
using System.Text.JsonLab;
using System.Threading.Tasks;
using System.Text;

namespace Microsoft.AspNetCore.Http.Connections
{
    public static class NegotiateProtocol
    {
        private const string ConnectionIdPropertyName = "connectionId";
        private const string UrlPropertyName = "url";
        private const string AccessTokenPropertyName = "accessToken";
        private const string AvailableTransportsPropertyName = "availableTransports";
        private const string TransportPropertyName = "transport";
        private const string TransferFormatsPropertyName = "transferFormats";

        private static readonly byte[] ConnectionIdPropertyNameUtf8 = Encoding.UTF8.GetBytes("connectionId");
        private static readonly byte[] UrlPropertyNameUtf8 = Encoding.UTF8.GetBytes("url");
        private static readonly byte[] AccessTokenPropertyNameUtf8 = Encoding.UTF8.GetBytes("accessToken");
        private static readonly byte[] AvailableTransportsPropertyNameUtf8 = Encoding.UTF8.GetBytes("availableTransports");
        private static readonly byte[] TransportPropertyNameUtf8 = Encoding.UTF8.GetBytes("transport");
        private static readonly byte[] TransferFormatsPropertyNameUtf8 = Encoding.UTF8.GetBytes("transferFormats");

        public static void WriteResponse(NegotiationResponse response, IBufferWriter<byte> output)
        {
            Utf8JsonWriter<IBufferWriter<byte>> jsonWriter = Utf8JsonWriter.Create(output);

            jsonWriter.WriteObjectStart();

            if (!string.IsNullOrEmpty(response.Url))
            {
                jsonWriter.WriteAttribute(UrlPropertyName, response.Url);
            }

            if (!string.IsNullOrEmpty(response.AccessToken))
            {
                jsonWriter.WriteAttribute(AccessTokenPropertyName, response.AccessToken);
            }

            if (!string.IsNullOrEmpty(response.ConnectionId))
            {
                jsonWriter.WriteAttribute(ConnectionIdPropertyName, response.ConnectionId);
            }

            jsonWriter.WriteArrayStart(AvailableTransportsPropertyName);

            if (response.AvailableTransports != null)
            {
                foreach (var availableTransport in response.AvailableTransports)
                {
                    jsonWriter.WriteObjectStart();
                    jsonWriter.WriteAttribute(TransportPropertyName, availableTransport.Transport);
                    jsonWriter.WriteArrayStart(TransferFormatsPropertyName);

                    if (availableTransport.TransferFormats != null)
                    {
                        foreach (var transferFormat in availableTransport.TransferFormats)
                        {
                            jsonWriter.WriteValue(transferFormat);
                        }
                    }

                    jsonWriter.WriteArrayEnd();
                    jsonWriter.WriteObjectEnd();
                }
            }

            jsonWriter.WriteArrayEnd();
            jsonWriter.WriteObjectEnd();

            jsonWriter.Flush();
        }

        private static async Task<byte[]> ReadFromStreamAsync(Stream stream)
        {
            //TODO: Add a max size to limit how much data gets read.
            var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream);
            return memoryStream.ToArray();
        }

        public static NegotiationResponse ParseResponse(Stream content)
        {
            byte[] data = ReadFromStreamAsync(content).GetAwaiter().GetResult();
            try
            {
                var reader = new Utf8JsonReader(data);

                JsonUtils.CheckRead(ref reader);
                JsonUtils.EnsureObjectStart(ref reader);

                string connectionId = null;
                string url = null;
                string accessToken = null;
                List<AvailableTransport> availableTransports = null;

                while (JsonUtils.CheckRead(ref reader))
                {
                    if (reader.TokenType == JsonTokenType.PropertyName)
                    {
                        ReadOnlySpan<byte> memberName = reader.Value;

                        if (memberName.SequenceEqual(UrlPropertyNameUtf8))
                        {
                            url = JsonUtils.ReadAsString(ref reader, UrlPropertyName);
                        }
                        else if (memberName.SequenceEqual(AccessTokenPropertyNameUtf8))
                        {
                            accessToken = JsonUtils.ReadAsString(ref reader, AccessTokenPropertyName);
                        }
                        else if (memberName.SequenceEqual(ConnectionIdPropertyNameUtf8))
                        {
                            connectionId = JsonUtils.ReadAsString(ref reader, ConnectionIdPropertyName);
                        }
                        else if (memberName.SequenceEqual(AvailableTransportsPropertyNameUtf8))
                        {
                            JsonUtils.CheckRead(ref reader);
                            JsonUtils.EnsureArrayStart(ref reader);

                            availableTransports = new List<AvailableTransport>();
                            while (JsonUtils.CheckRead(ref reader))
                            {
                                if (reader.TokenType == JsonTokenType.StartObject)
                                {
                                    availableTransports.Add(ParseAvailableTransport(ref reader));
                                }
                                else if (reader.TokenType == JsonTokenType.EndArray)
                                {
                                    break;
                                }
                            }
                        }
                        else
                        {
                            reader.Skip();
                        }
                    }
                    else if (reader.TokenType == JsonTokenType.EndObject)
                    {
                        break;
                    }
                    else
                    {
                        throw new InvalidDataException($"Unexpected token '{reader.TokenType}' when reading negotiation response JSON.");
                    }
                }

                if (url == null)
                {
                    // if url isn't specified, connectionId and available transports are required
                    if (connectionId == null)
                    {
                        throw new InvalidDataException($"Missing required property '{ConnectionIdPropertyName}'.");
                    }

                    if (availableTransports == null)
                    {
                        throw new InvalidDataException($"Missing required property '{AvailableTransportsPropertyName}'.");
                    }
                }

                return new NegotiationResponse
                {
                    ConnectionId = connectionId,
                    Url = url,
                    AccessToken = accessToken,
                    AvailableTransports = availableTransports
                };
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("Invalid negotiation response received.", ex);
            }
        }

        private static AvailableTransport ParseAvailableTransport(ref Utf8JsonReader reader)
        {
            var availableTransport = new AvailableTransport();

            while (JsonUtils.CheckRead(ref reader))
            {
                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    ReadOnlySpan<byte> memberName = reader.Value;

                    if (memberName.SequenceEqual(TransportPropertyNameUtf8))
                    {
                        availableTransport.Transport = JsonUtils.ReadAsString(ref reader, TransportPropertyName);
                    }
                    else if (memberName.SequenceEqual(TransferFormatsPropertyNameUtf8))
                    {
                        JsonUtils.CheckRead(ref reader);
                        JsonUtils.EnsureArrayStart(ref reader);

                        availableTransport.TransferFormats = new List<string>();
                        while (JsonUtils.CheckRead(ref reader))
                        {
                            if (reader.TokenType == JsonTokenType.Value && reader.ValueType == JsonValueType.String)
                            {
                                availableTransport.TransferFormats.Add(JsonUtils.ConvertToString(reader.Value));
                            }
                            else if (reader.TokenType == JsonTokenType.EndArray)
                            {
                                break;
                            }
                            else
                            {
                                throw new InvalidDataException($"Unexpected token '{reader.TokenType}' when reading transfer formats JSON.");
                            }
                        }
                    }
                    else
                    {
                        reader.Skip();
                    }
                }
                else if (reader.TokenType == JsonTokenType.EndObject)
                {
                    if (availableTransport.Transport == null)
                    {
                        throw new InvalidDataException($"Missing required property '{TransportPropertyName}'.");
                    }

                    if (availableTransport.TransferFormats == null)
                    {
                        throw new InvalidDataException($"Missing required property '{TransferFormatsPropertyName}'.");
                    }

                    return availableTransport;
                }
                else
                {
                    throw new InvalidDataException($"Unexpected token '{reader.TokenType}' when reading available transport JSON.");
                }
            }

            throw new InvalidDataException("Unexpected end when reading JSON.");
        }
    }
}