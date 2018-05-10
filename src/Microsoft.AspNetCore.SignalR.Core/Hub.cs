// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.SignalR
{
    /// <summary>
    /// A base class for a SignalR hub.
    /// </summary>
    public abstract class Hub : IDisposable
    {
        private bool _disposed;
        private IHubCallerClients _clients;
        private HubCallerContext _context;
        private IGroupManager _groups;

        /// <summary>
        /// Gets or sets an object that can be used to invoke methods on the clients connected to this hub.
        /// </summary>
        public IHubCallerClients Clients
        {
            get
            {
                CheckDisposed();
                return _clients;
            }
            set
            {
                CheckDisposed();
                _clients = value;
            }
        }

        /// <summary>
        /// Gets or sets the hub caller context.
        /// </summary>
        public HubCallerContext Context
        {
            get
            {
                CheckDisposed();
                return _context;
            }
            set
            {
                CheckDisposed();
                _context = value;
            }
        }

        /// <summary>
        /// Gets or sets the group manager.
        /// </summary>
        public IGroupManager Groups
        {
            get
            {
                CheckDisposed();
                return _groups;
            }
            set
            {
                CheckDisposed();
                _groups = value;
            }
        }

        /// <summary>
        /// Called when a new connection is established with the hub.
        /// </summary>
        /// <returns>A <see cref="Task"/> that represents the asynchronous connect.</returns>
        public virtual Task OnConnectedAsync()
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Called when a connection with the hub is terminated.
        /// </summary>
        /// <returns>A <see cref="Task"/> that represents the asynchronous disconnect.</returns>
        public virtual Task OnDisconnectedAsync(Exception exception)
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Releases all resources currently used by this <see cref="Hub"/> instance.
        /// </summary>
        /// <param name="disposing"><c>true</c> if this method is being invoked by the <see cref="Dispose()"/> method,
        /// otherwise <c>false</c>.</param>
        protected virtual void Dispose(bool disposing)
        {
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Dispose(true);

            _disposed = true;
        }

        private void CheckDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(GetType().Name);
            }
        }
    }
}
