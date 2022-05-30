﻿// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Net;

namespace IceRpc.Transports
{
    /// <summary>The base options class for TCP transports.</summary>
    public record class TcpTransportOptions
    {
        /// <summary>Gets or sets the idle timeout. This timeout is used to monitor the network connection. If
        /// the connection is idle within this timeout period, the connection is gracefully closed.</summary>
        /// <value>The network connection idle timeout value. The default is 60s.</value>
        public TimeSpan IdleTimeout
        {
            get => _idleTimeout;
            set => _idleTimeout = value != TimeSpan.Zero ? value :
                throw new ArgumentException($"0 is not a valid value for {nameof(IdleTimeout)}", nameof(value));
        }

        /// <summary>Gets or sets the socket receive buffer size in bytes.</summary>
        /// <value>The receive buffer size in bytes. It can't be less than 1KB. <c>null</c> means use the operating
        /// system default.</value>
        public int? ReceiveBufferSize
        {
            get => _receiveBufferSize;
            set => _receiveBufferSize = value == null || value >= 1024 ? value :
                throw new ArgumentException($"{nameof(ReceiveBufferSize)} can't be less than 1KB", nameof(value));
        }

        /// <summary>Gets or sets the socket send buffer size in bytes.</summary>
        /// <value>The send buffer size in bytes. It can't be less than 1KB. <c>null</c> means use the OS default.
        /// </value>
        public int? SendBufferSize
        {
            get => _sendBufferSize;
            set => _sendBufferSize = value == null || value >= 1024 ? value :
                throw new ArgumentException($"{nameof(SendBufferSize)} can't be less than 1KB", nameof(value));
        }

        private TimeSpan _idleTimeout = TimeSpan.FromSeconds(60);
        private int? _receiveBufferSize;
        private int? _sendBufferSize;
    }

    /// <summary>The options class for configuring <see cref="TcpClientTransport"/>.</summary>
    public sealed record class TcpClientTransportOptions : TcpTransportOptions
    {
        /// <summary>Gets or sets the address and port represented by a .NET IPEndPoint to use for a client
        /// socket. If specified the client socket will bind to this address and port before connection establishment.
        /// </summary>
        /// <value>The address and port to bind the socket to.</value>
        public IPEndPoint? LocalEndPoint { get; set; }
    }

    /// <summary>The options class for configuring <see cref="TcpServerTransport"/>.</summary>
    public sealed record class TcpServerTransportOptions : TcpTransportOptions
    {
        /// <summary>Gets or sets the length of the server socket queue for accepting new connections. If a new
        /// connection request arrives and the queue is full, the client connection establishment will fail with a
        /// <see cref="ConnectionRefusedException"/> exception.</summary>
        /// <value>The server socket backlog size. The default is 511</value>
        public int ListenerBackLog
        {
            get => _listenerBackLog;
            set => _listenerBackLog = value > 0 ? value :
                throw new ArgumentException($"{nameof(ListenerBackLog)} can't be less than 1", nameof(value));
        }

        private int _listenerBackLog = 511;
    }
}