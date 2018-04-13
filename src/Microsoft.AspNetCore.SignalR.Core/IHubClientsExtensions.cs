// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace Microsoft.AspNetCore.SignalR
{
    public static class IHubClientsExtensions
    {
        /// <summary>
        /// </summary>
        /// <param name="hubClients"></param>
        /// <param name="excludedConnectionId1">The first connection to exclude.</param>
        /// <returns></returns>
        public static T AllExcept<T>(this IHubClients<T> hubClients, string excludedConnectionId1)
        {
            return hubClients.AllExcept(new List<string> { excludedConnectionId1 });
        }

        /// <summary>
        /// </summary>
        /// <param name="hubClients"></param>
        /// <param name="excludedConnectionId1">The first connection to exclude.</param>
        /// <param name="excludedConnectionId2">The second connection to exclude.</param>
        /// <returns></returns>
        public static T AllExcept<T>(this IHubClients<T> hubClients, string excludedConnectionId1, string excludedConnectionId2)
        {
            return hubClients.AllExcept(new List<string> { excludedConnectionId1, excludedConnectionId2 });
        }

        /// <summary>
        /// </summary>
        /// <param name="hubClients"></param>
        /// <param name="excludedConnectionId1">The first connection to exclude.</param>
        /// <param name="excludedConnectionId2">The second connection to exclude.</param>
        /// <param name="excludedConnectionId3">The third connection to exclude.</param>
        /// <returns></returns>
        public static T AllExcept<T>(this IHubClients<T> hubClients, string excludedConnectionId1, string excludedConnectionId2, string excludedConnectionId3)
        {
            return hubClients.AllExcept(new List<string> { excludedConnectionId1, excludedConnectionId2, excludedConnectionId3 });
        }

        /// <summary>
        /// </summary>
        /// <param name="hubClients"></param>
        /// <param name="excludedConnectionId1">The first connection to exclude.</param>
        /// <param name="excludedConnectionId2">The second connection to exclude.</param>
        /// <param name="excludedConnectionId3">The third connection to exclude.</param>
        /// <param name="excludedConnectionId4">The fourth connection to exclude.</param>
        /// <returns></returns>
        public static T AllExcept<T>(this IHubClients<T> hubClients, string excludedConnectionId1, string excludedConnectionId2, string excludedConnectionId3, string excludedConnectionId4)
        {
            return hubClients.AllExcept(new List<string> { excludedConnectionId1, excludedConnectionId2, excludedConnectionId3, excludedConnectionId4 });
        }

        /// <summary>
        /// </summary>
        /// <param name="hubClients"></param>
        /// <param name="excludedConnectionId1">The first connection to exclude.</param>
        /// <param name="excludedConnectionId2">The second connection to exclude.</param>
        /// <param name="excludedConnectionId3">The third connection to exclude.</param>
        /// <param name="excludedConnectionId4">The fourth connection to exclude.</param>
        /// <param name="excludedConnectionId5">The fifth connection to exclude.</param>
        /// <returns></returns>
        public static T AllExcept<T>(this IHubClients<T> hubClients, string excludedConnectionId1, string excludedConnectionId2, string excludedConnectionId3, string excludedConnectionId4, string excludedConnectionId5)
        {
            return hubClients.AllExcept(new List<string> { excludedConnectionId1, excludedConnectionId2, excludedConnectionId3, excludedConnectionId4, excludedConnectionId5 });
        }

        /// <summary>
        /// </summary>
        /// <param name="hubClients"></param>
        /// <param name="excludedConnectionId1">The first connection to exclude.</param>
        /// <param name="excludedConnectionId2">The second connection to exclude.</param>
        /// <param name="excludedConnectionId3">The third connection to exclude.</param>
        /// <param name="excludedConnectionId4">The fourth connection to exclude.</param>
        /// <param name="excludedConnectionId5">The fifth connection to exclude.</param>
        /// <param name="excludedConnectionId6">The sixth connection to exclude.</param>
        /// <returns></returns>
        public static T AllExcept<T>(this IHubClients<T> hubClients, string excludedConnectionId1, string excludedConnectionId2, string excludedConnectionId3, string excludedConnectionId4, string excludedConnectionId5, string excludedConnectionId6)
        {
            return hubClients.AllExcept(new List<string> { excludedConnectionId1, excludedConnectionId2, excludedConnectionId3, excludedConnectionId4, excludedConnectionId5, excludedConnectionId6 });
        }

        /// <summary>
        /// </summary>
        /// <param name="hubClients"></param>
        /// <param name="excludedConnectionId1">The first connection to exclude.</param>
        /// <param name="excludedConnectionId2">The second connection to exclude.</param>
        /// <param name="excludedConnectionId3">The third connection to exclude.</param>
        /// <param name="excludedConnectionId4">The fourth connection to exclude.</param>
        /// <param name="excludedConnectionId5">The fifth connection to exclude.</param>
        /// <param name="excludedConnectionId6">The sixth connection to exclude.</param>
        /// <param name="excludedConnectionId7">The seventh connection to exclude.</param>
        /// <returns></returns>
        public static T AllExcept<T>(this IHubClients<T> hubClients, string excludedConnectionId1, string excludedConnectionId2, string excludedConnectionId3, string excludedConnectionId4, string excludedConnectionId5, string excludedConnectionId6, string excludedConnectionId7)
        {
            return hubClients.AllExcept(new List<string> { excludedConnectionId1, excludedConnectionId2, excludedConnectionId3, excludedConnectionId4, excludedConnectionId5, excludedConnectionId6, excludedConnectionId7 });
        }

        /// <summary>
        /// </summary>
        /// <param name="hubClients"></param>
        /// <param name="excludedConnectionId1">The first connection to exclude.</param>
        /// <param name="excludedConnectionId2">The second connection to exclude.</param>
        /// <param name="excludedConnectionId3">The third connection to exclude.</param>
        /// <param name="excludedConnectionId4">The fourth connection to exclude.</param>
        /// <param name="excludedConnectionId5">The fifth connection to exclude.</param>
        /// <param name="excludedConnectionId6">The sixth connection to exclude.</param>
        /// <param name="excludedConnectionId7">The seventh connection to exclude.</param>
        /// <param name="excludedConnectionId8">The eigth connection to exclude.</param>
        /// <returns></returns>
        public static T AllExcept<T>(this IHubClients<T> hubClients, string excludedConnectionId1, string excludedConnectionId2, string excludedConnectionId3, string excludedConnectionId4, string excludedConnectionId5, string excludedConnectionId6, string excludedConnectionId7, string excludedConnectionId8)
        {
            return hubClients.AllExcept(new List<string> { excludedConnectionId1, excludedConnectionId2, excludedConnectionId3, excludedConnectionId4, excludedConnectionId5, excludedConnectionId6, excludedConnectionId7, excludedConnectionId8 });
        }

        /// <summary>
        /// </summary>
        /// <param name="hubClients"></param>
        /// <param name="connection1">The first connection to include.</param>
        /// <returns></returns>
        public static T Clients<T>(this IHubClients<T> hubClients, string connection1)
        {
            return hubClients.Clients(new List<string> { connection1 });
        }

        /// <summary>
        /// </summary>
        /// <param name="hubClients"></param>
        /// <param name="connection1">The first connection to include.</param>
        /// <param name="connection2">The second connection to include.</param>
        /// <returns></returns>
        public static T Clients<T>(this IHubClients<T> hubClients, string connection1, string connection2)
        {
            return hubClients.Clients(new List<string> { connection1, connection2 });
        }

        /// <summary>
        /// </summary>
        /// <param name="hubClients"></param>
        /// <param name="connection1">The first connection to include.</param>
        /// <param name="connection2">The second connection to include.</param>
        /// <param name="connection3">The third connection to include.</param>
        /// <returns></returns>
        public static T Clients<T>(this IHubClients<T> hubClients, string connection1, string connection2, string connection3)
        {
            return hubClients.Clients(new List<string> { connection1, connection2, connection3 });
        }

        /// <summary>
        /// </summary>
        /// <param name="hubClients"></param>
        /// <param name="connection1">The first connection to include.</param>
        /// <param name="connection2">The second connection to include.</param>
        /// <param name="connection3">The third connection to include.</param>
        /// <param name="connection4">The fourth connection to include.</param>
        /// <returns></returns>
        public static T Clients<T>(this IHubClients<T> hubClients, string connection1, string connection2, string connection3, string connection4)
        {
            return hubClients.Clients(new List<string> { connection1, connection2, connection3, connection4 });
        }

        /// <summary>
        /// </summary>
        /// <param name="hubClients"></param>
        /// <param name="connection1">The first connection to include.</param>
        /// <param name="connection2">The second connection to include.</param>
        /// <param name="connection3">The third connection to include.</param>
        /// <param name="connection4">The fourth connection to include.</param>
        /// <param name="connection5">The fifth connection to include.</param>
        /// <returns></returns>
        public static T Clients<T>(this IHubClients<T> hubClients, string connection1, string connection2, string connection3, string connection4, string connection5)
        {
            return hubClients.Clients(new List<string> { connection1, connection2, connection3, connection4, connection5 });
        }

        /// <summary>
        /// </summary>
        /// <param name="hubClients"></param>
        /// <param name="connection1">The first connection to include.</param>
        /// <param name="connection2">The second connection to include.</param>
        /// <param name="connection3">The third connection to include.</param>
        /// <param name="connection4">The fourth connection to include.</param>
        /// <param name="connection5">The fifth connection to include.</param>
        /// <param name="connection6">The sixth connection to include.</param>
        /// <returns></returns>
        public static T Clients<T>(this IHubClients<T> hubClients, string connection1, string connection2, string connection3, string connection4, string connection5, string connection6)
        {
            return hubClients.Clients(new List<string> { connection1, connection2, connection3, connection4, connection5, connection6 });
        }

        /// <summary>
        /// </summary>
        /// <param name="hubClients"></param>
        /// <param name="connection1">The first connection to include.</param>
        /// <param name="connection2">The second connection to include.</param>
        /// <param name="connection3">The third connection to include.</param>
        /// <param name="connection4">The fourth connection to include.</param>
        /// <param name="connection5">The fifth connection to include.</param>
        /// <param name="connection6">The sixth connection to include.</param>
        /// <param name="connection7">The seventh connection to include.</param>
        /// <returns></returns>
        public static T Clients<T>(this IHubClients<T> hubClients, string connection1, string connection2, string connection3, string connection4, string connection5, string connection6, string connection7)
        {
            return hubClients.Clients(new List<string> { connection1, connection2, connection3, connection4, connection5, connection6, connection7 });
        }

        /// <summary>
        /// </summary>
        /// <param name="hubClients"></param>
        /// <param name="connection1">The first connection to include.</param>
        /// <param name="connection2">The second connection to include.</param>
        /// <param name="connection3">The third connection to include.</param>
        /// <param name="connection4">The fourth connection to include.</param>
        /// <param name="connection5">The fifth connection to include.</param>
        /// <param name="connection6">The sixth connection to include.</param>
        /// <param name="connection7">The seventh connection to include.</param>
        /// <param name="connection8">The eigth connection to include.</param>
        /// <returns></returns>
        public static T Clients<T>(this IHubClients<T> hubClients, string connection1, string connection2, string connection3, string connection4, string connection5, string connection6, string connection7, string connection8)
        {
            return hubClients.Clients(new List<string> { connection1, connection2, connection3, connection4, connection5, connection6, connection7, connection8 });
        }

        /// <summary>
        /// </summary>
        /// <param name="hubClients"></param>
        /// <param name="connection1">The first connection to include.</param>
        /// <returns></returns>
        public static T Groups<T>(this IHubClients<T> hubClients, string connection1)
        {
            return hubClients.Groups(new List<string> { connection1 });
        }

        /// <summary>
        /// </summary>
        /// <param name="hubClients"></param>
        /// <param name="connection1">The first connection to include.</param>
        /// <param name="connection2">The second connection to include.</param>
        /// <returns></returns>
        public static T Groups<T>(this IHubClients<T> hubClients, string connection1, string connection2)
        {
            return hubClients.Groups(new List<string> { connection1, connection2 });
        }

        /// <summary>
        /// </summary>
        /// <param name="hubClients"></param>
        /// <param name="connection1">The first connection to include.</param>
        /// <param name="connection2">The second connection to include.</param>
        /// <param name="connection3">The third connection to include.</param>
        /// <returns></returns>
        public static T Groups<T>(this IHubClients<T> hubClients, string connection1, string connection2, string connection3)
        {
            return hubClients.Groups(new List<string> { connection1, connection2, connection3 });
        }

        /// <summary>
        /// </summary>
        /// <param name="hubClients"></param>
        /// <param name="connection1">The first connection to include.</param>
        /// <param name="connection2">The second connection to include.</param>
        /// <param name="connection3">The third connection to include.</param>
        /// <param name="connection4">The fourth connection to include.</param>
        /// <returns></returns>
        public static T Groups<T>(this IHubClients<T> hubClients, string connection1, string connection2, string connection3, string connection4)
        {
            return hubClients.Groups(new List<string> { connection1, connection2, connection3, connection4 });
        }

        /// <summary>
        /// </summary>
        /// <param name="hubClients"></param>
        /// <param name="connection1">The first connection to include.</param>
        /// <param name="connection2">The second connection to include.</param>
        /// <param name="connection3">The third connection to include.</param>
        /// <param name="connection4">The fourth connection to include.</param>
        /// <param name="connection5">The fifth connection to include.</param>
        /// <returns></returns>
        public static T Groups<T>(this IHubClients<T> hubClients, string connection1, string connection2, string connection3, string connection4, string connection5)
        {
            return hubClients.Groups(new List<string> { connection1, connection2, connection3, connection4, connection5 });
        }

        /// <summary>
        /// </summary>
        /// <param name="hubClients"></param>
        /// <param name="connection1">The first connection to include.</param>
        /// <param name="connection2">The second connection to include.</param>
        /// <param name="connection3">The third connection to include.</param>
        /// <param name="connection4">The fourth connection to include.</param>
        /// <param name="connection5">The fifth connection to include.</param>
        /// <param name="connection6">The sixth connection to include.</param>
        /// <returns></returns>
        public static T Groups<T>(this IHubClients<T> hubClients, string connection1, string connection2, string connection3, string connection4, string connection5, string connection6)
        {
            return hubClients.Groups(new List<string> { connection1, connection2, connection3, connection4, connection5, connection6 });
        }

        /// <summary>
        /// </summary>
        /// <param name="hubClients"></param>
        /// <param name="connection1">The first connection to include.</param>
        /// <param name="connection2">The second connection to include.</param>
        /// <param name="connection3">The third connection to include.</param>
        /// <param name="connection4">The fourth connection to include.</param>
        /// <param name="connection5">The fifth connection to include.</param>
        /// <param name="connection6">The sixth connection to include.</param>
        /// <param name="connection7">The seventh connection to include.</param>
        /// <returns></returns>
        public static T Groups<T>(this IHubClients<T> hubClients, string connection1, string connection2, string connection3, string connection4, string connection5, string connection6, string connection7)
        {
            return hubClients.Groups(new List<string> { connection1, connection2, connection3, connection4, connection5, connection6, connection7 });
        }

        /// <summary>
        /// </summary>
        /// <param name="hubClients"></param>
        /// <param name="connection1">The first connection to include.</param>
        /// <param name="connection2">The second connection to include.</param>
        /// <param name="connection3">The third connection to include.</param>
        /// <param name="connection4">The fourth connection to include.</param>
        /// <param name="connection5">The fifth connection to include.</param>
        /// <param name="connection6">The sixth connection to include.</param>
        /// <param name="connection7">The seventh connection to include.</param>
        /// <param name="connection8">The eigth connection to include.</param>
        /// <returns></returns>
        public static T Groups<T>(this IHubClients<T> hubClients, string connection1, string connection2, string connection3, string connection4, string connection5, string connection6, string connection7, string connection8)
        {
            return hubClients.Groups(new List<string> { connection1, connection2, connection3, connection4, connection5, connection6, connection7, connection8 });
        }

        /// <summary>
        /// </summary>
        /// <param name="hubClients"></param>
        /// <param name="groupName"></param>
        /// <param name="excludedConnectionId1">The first connection to exclude.</param>
        /// <returns></returns>
        public static T GroupExcept<T>(this IHubClients<T> hubClients, string groupName, string excludedConnectionId1)
        {
            return hubClients.GroupExcept(groupName, new List<string> { excludedConnectionId1 });
        }

        /// <summary>
        /// </summary>
        /// <param name="hubClients"></param>
        /// <param name="groupName"></param>
        /// <param name="excludedConnectionId1">The first connection to exclude.</param>
        /// <param name="excludedConnectionId2">The second connection to exclude.</param>
        /// <returns></returns>
        public static T GroupExcept<T>(this IHubClients<T> hubClients, string groupName, string excludedConnectionId1, string excludedConnectionId2)
        {
            return hubClients.GroupExcept(groupName, new List<string> { excludedConnectionId1, excludedConnectionId2 });
        }

        /// <summary>
        /// </summary>
        /// <param name="hubClients"></param>
        /// <param name="groupName"></param>
        /// <param name="excludedConnectionId1">The first connection to exclude.</param>
        /// <param name="excludedConnectionId2">The second connection to exclude.</param>
        /// <param name="excludedConnectionId3">The third connection to exclude.</param>
        /// <returns></returns>
        public static T GroupExcept<T>(this IHubClients<T> hubClients, string groupName, string excludedConnectionId1, string excludedConnectionId2, string excludedConnectionId3)
        {
            return hubClients.GroupExcept(groupName, new List<string> { excludedConnectionId1, excludedConnectionId2, excludedConnectionId3 });
        }

        /// <summary>
        /// </summary>
        /// <param name="hubClients"></param>
        /// <param name="groupName"></param>
        /// <param name="excludedConnectionId1">The first connection to exclude.</param>
        /// <param name="excludedConnectionId2">The second connection to exclude.</param>
        /// <param name="excludedConnectionId3">The third connection to exclude.</param>
        /// <param name="excludedConnectionId4">The fourth connection to exclude.</param>
        /// <returns></returns>
        public static T GroupExcept<T>(this IHubClients<T> hubClients, string groupName, string excludedConnectionId1, string excludedConnectionId2, string excludedConnectionId3, string excludedConnectionId4)
        {
            return hubClients.GroupExcept(groupName, new List<string> { excludedConnectionId1, excludedConnectionId2, excludedConnectionId3, excludedConnectionId4 });
        }

        /// <summary>
        /// </summary>
        /// <param name="hubClients"></param>
        /// <param name="groupName"></param>
        /// <param name="excludedConnectionId1">The first connection to exclude.</param>
        /// <param name="excludedConnectionId2">The second connection to exclude.</param>
        /// <param name="excludedConnectionId3">The third connection to exclude.</param>
        /// <param name="excludedConnectionId4">The fourth connection to exclude.</param>
        /// <param name="excludedConnectionId5">The fifth connection to exclude.</param>
        /// <returns></returns>
        public static T GroupExcept<T>(this IHubClients<T> hubClients, string groupName, string excludedConnectionId1, string excludedConnectionId2, string excludedConnectionId3, string excludedConnectionId4, string excludedConnectionId5)
        {
            return hubClients.GroupExcept(groupName, new List<string> { excludedConnectionId1, excludedConnectionId2, excludedConnectionId3, excludedConnectionId4, excludedConnectionId5 });
        }

        /// <summary>
        /// </summary>
        /// <param name="hubClients"></param>
        /// <param name="groupName"></param>
        /// <param name="excludedConnectionId1">The first connection to exclude.</param>
        /// <param name="excludedConnectionId2">The second connection to exclude.</param>
        /// <param name="excludedConnectionId3">The third connection to exclude.</param>
        /// <param name="excludedConnectionId4">The fourth connection to exclude.</param>
        /// <param name="excludedConnectionId5">The fifth connection to exclude.</param>
        /// <param name="excludedConnectionId6">The sixth connection to exclude.</param>
        /// <returns></returns>
        public static T GroupExcept<T>(this IHubClients<T> hubClients, string groupName, string excludedConnectionId1, string excludedConnectionId2, string excludedConnectionId3, string excludedConnectionId4, string excludedConnectionId5, string excludedConnectionId6)
        {
            return hubClients.GroupExcept(groupName, new List<string> { excludedConnectionId1, excludedConnectionId2, excludedConnectionId3, excludedConnectionId4, excludedConnectionId5, excludedConnectionId6 });
        }

        /// <summary>
        /// </summary>
        /// <param name="hubClients"></param>
        /// <param name="groupName"></param>
        /// <param name="excludedConnectionId1">The first connection to exclude.</param>
        /// <param name="excludedConnectionId2">The second connection to exclude.</param>
        /// <param name="excludedConnectionId3">The third connection to exclude.</param>
        /// <param name="excludedConnectionId4">The fourth connection to exclude.</param>
        /// <param name="excludedConnectionId5">The fifth connection to exclude.</param>
        /// <param name="excludedConnectionId6">The sixth connection to exclude.</param>
        /// <param name="excludedConnectionId7">The seventh connection to exclude.</param>
        /// <returns></returns>
        public static T GroupExcept<T>(this IHubClients<T> hubClients, string groupName, string excludedConnectionId1, string excludedConnectionId2, string excludedConnectionId3, string excludedConnectionId4, string excludedConnectionId5, string excludedConnectionId6, string excludedConnectionId7)
        {
            return hubClients.GroupExcept(groupName, new List<string> { excludedConnectionId1, excludedConnectionId2, excludedConnectionId3, excludedConnectionId4, excludedConnectionId5, excludedConnectionId6, excludedConnectionId7 });
        }

        /// <summary>
        /// </summary>
        /// <param name="hubClients"></param>
        /// <param name="groupName"></param>
        /// <param name="excludedConnectionId1">The first connection to exclude.</param>
        /// <param name="excludedConnectionId2">The second connection to exclude.</param>
        /// <param name="excludedConnectionId3">The third connection to exclude.</param>
        /// <param name="excludedConnectionId4">The fourth connection to exclude.</param>
        /// <param name="excludedConnectionId5">The fifth connection to exclude.</param>
        /// <param name="excludedConnectionId6">The sixth connection to exclude.</param>
        /// <param name="excludedConnectionId7">The seventh connection to exclude.</param>
        /// <param name="excludedConnectionId8">The eigth connection to exclude.</param>
        /// <returns></returns>
        public static T GroupExcept<T>(this IHubClients<T> hubClients, string groupName, string excludedConnectionId1, string excludedConnectionId2, string excludedConnectionId3, string excludedConnectionId4, string excludedConnectionId5, string excludedConnectionId6, string excludedConnectionId7, string excludedConnectionId8)
        {
            return hubClients.GroupExcept(groupName, new List<string> { excludedConnectionId1, excludedConnectionId2, excludedConnectionId3, excludedConnectionId4, excludedConnectionId5, excludedConnectionId6, excludedConnectionId7, excludedConnectionId8 });
        }

        /// <summary>
        /// </summary>
        /// <param name="hubClients"></param>
        /// <param name="connection1">The first connection to include.</param>
        /// <returns></returns>
        public static T Users<T>(this IHubClients<T> hubClients, string connection1)
        {
            return hubClients.Users(new List<string> { connection1 });
        }

        /// <summary>
        /// </summary>
        /// <param name="hubClients"></param>
        /// <param name="connection1">The first connection to include.</param>
        /// <param name="connection2">The second connection to include.</param>
        /// <returns></returns>
        public static T Users<T>(this IHubClients<T> hubClients, string connection1, string connection2)
        {
            return hubClients.Users(new List<string> { connection1, connection2 });
        }

        /// <summary>
        /// </summary>
        /// <param name="hubClients"></param>
        /// <param name="connection1">The first connection to include.</param>
        /// <param name="connection2">The second connection to include.</param>
        /// <param name="connection3">The third connection to include.</param>
        /// <returns></returns>
        public static T Users<T>(this IHubClients<T> hubClients, string connection1, string connection2, string connection3)
        {
            return hubClients.Users(new List<string> { connection1, connection2, connection3 });
        }

        /// <summary>
        /// </summary>
        /// <param name="hubClients"></param>
        /// <param name="connection1">The first connection to include.</param>
        /// <param name="connection2">The second connection to include.</param>
        /// <param name="connection3">The third connection to include.</param>
        /// <param name="connection4">The fourth connection to include.</param>
        /// <returns></returns>
        public static T Users<T>(this IHubClients<T> hubClients, string connection1, string connection2, string connection3, string connection4)
        {
            return hubClients.Users(new List<string> { connection1, connection2, connection3, connection4 });
        }

        /// <summary>
        /// </summary>
        /// <param name="hubClients"></param>
        /// <param name="connection1">The first connection to include.</param>
        /// <param name="connection2">The second connection to include.</param>
        /// <param name="connection3">The third connection to include.</param>
        /// <param name="connection4">The fourth connection to include.</param>
        /// <param name="connection5">The fifth connection to include.</param>
        /// <returns></returns>
        public static T Users<T>(this IHubClients<T> hubClients, string connection1, string connection2, string connection3, string connection4, string connection5)
        {
            return hubClients.Users(new List<string> { connection1, connection2, connection3, connection4, connection5 });
        }

        /// <summary>
        /// </summary>
        /// <param name="hubClients"></param>
        /// <param name="connection1">The first connection to include.</param>
        /// <param name="connection2">The second connection to include.</param>
        /// <param name="connection3">The third connection to include.</param>
        /// <param name="connection4">The fourth connection to include.</param>
        /// <param name="connection5">The fifth connection to include.</param>
        /// <param name="connection6">The sixth connection to include.</param>
        /// <returns></returns>
        public static T Users<T>(this IHubClients<T> hubClients, string connection1, string connection2, string connection3, string connection4, string connection5, string connection6)
        {
            return hubClients.Users(new List<string> { connection1, connection2, connection3, connection4, connection5, connection6 });
        }

        /// <summary>
        /// </summary>
        /// <param name="hubClients"></param>
        /// <param name="connection1">The first connection to include.</param>
        /// <param name="connection2">The second connection to include.</param>
        /// <param name="connection3">The third connection to include.</param>
        /// <param name="connection4">The fourth connection to include.</param>
        /// <param name="connection5">The fifth connection to include.</param>
        /// <param name="connection6">The sixth connection to include.</param>
        /// <param name="connection7">The seventh connection to include.</param>
        /// <returns></returns>
        public static T Users<T>(this IHubClients<T> hubClients, string connection1, string connection2, string connection3, string connection4, string connection5, string connection6, string connection7)
        {
            return hubClients.Users(new List<string> { connection1, connection2, connection3, connection4, connection5, connection6, connection7 });
        }

        /// <summary>
        /// </summary>
        /// <param name="hubClients"></param>
        /// <param name="connection1">The first connection to include.</param>
        /// <param name="connection2">The second connection to include.</param>
        /// <param name="connection3">The third connection to include.</param>
        /// <param name="connection4">The fourth connection to include.</param>
        /// <param name="connection5">The fifth connection to include.</param>
        /// <param name="connection6">The sixth connection to include.</param>
        /// <param name="connection7">The seventh connection to include.</param>
        /// <param name="connection8">The eigth connection to include.</param>
        /// <returns></returns>
        public static T Users<T>(this IHubClients<T> hubClients, string connection1, string connection2, string connection3, string connection4, string connection5, string connection6, string connection7, string connection8)
        {
            return hubClients.Users(new List<string> { connection1, connection2, connection3, connection4, connection5, connection6, connection7, connection8 });
        }
    }
}
