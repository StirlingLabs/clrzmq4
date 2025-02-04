using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace ZeroMQ.Monitoring
{
    /// <summary>
    /// Monitors state change events on another socket within the same context.
    /// </summary>
    public class ZMonitor : ZThread
    {
        /// <summary>
        /// The polling interval in milliseconds.
        /// </summary>
        public static readonly TimeSpan PollingInterval = TimeSpan.FromMilliseconds(64);

        private ZSocket? _socket;

        private readonly string _endpoint;

        private readonly Dictionary<ZMonitorEvents, Action<ZMonitorEventData>> _eventHandler;

        protected ZMonitor(ZSocket socket, string endpoint)
            : this(ZContext.Current, socket, endpoint) { }

        protected ZMonitor(ZContext context, ZSocket socket, string endpoint)
        {
            // TODO: remove socket argument and create socket within Run?
            _socket = socket;
            _endpoint = endpoint;
            _eventHandler = new()
            {
                { ZMonitorEvents.AllEvents, data => InvokeEvent(AllEvents, () => new(this, data)) },
                { ZMonitorEvents.Connected, data => InvokeEvent(Connected, () => new(this, data)) },
                { ZMonitorEvents.ConnectDelayed, data => InvokeEvent(ConnectDelayed, () => new(this, data)) },
                { ZMonitorEvents.ConnectRetried, data => InvokeEvent(ConnectRetried, () => new(this, data)) },
                { ZMonitorEvents.Listening, data => InvokeEvent(Listening, () => new(this, data)) },
                { ZMonitorEvents.BindFailed, data => InvokeEvent(BindFailed, () => new(this, data)) },
                { ZMonitorEvents.Accepted, data => InvokeEvent(Accepted, () => new(this, data)) },
                { ZMonitorEvents.AcceptFailed, data => InvokeEvent(AcceptFailed, () => new(this, data)) },
                { ZMonitorEvents.Closed, data => InvokeEvent(Closed, () => new(this, data)) },
                { ZMonitorEvents.CloseFailed, data => InvokeEvent(CloseFailed, () => new(this, data)) },
                { ZMonitorEvents.Disconnected, data => InvokeEvent(Disconnected, () => new(this, data)) },
                { ZMonitorEvents.Stopped, data => InvokeEvent(Stopped, () => new(this, data)) },
            };
        }

        public static ZMonitor Create(string endpoint)
            => Create(ZContext.Current, endpoint);

        public static ZMonitor Create(ZContext context, string endpoint)
        {
            ZMonitor? monitor;
            if (null == (monitor = Create(context, endpoint, out var error)))
                throw new ZException(error);
            return monitor;
        }

        /// <summary>
        /// Create a socket with the current context and the specified socket type.
        /// </summary>
        /// <param name="socketType">A <see cref="ZSocketType"/> value for the socket.</param>
        /// <returns>A <see cref="ZSocket"/> instance with the current context and the specified socket type.</returns>
        public static ZMonitor? Create(string endpoint, out ZError? error)
            => Create(ZContext.Current, endpoint, out error);

        /// <summary>
        /// Create a socket with the current context and the specified socket type.
        /// </summary>
        /// <param name="socketType">A <see cref="ZSocketType"/> value for the socket.</param>
        /// <returns>A <see cref="ZSocket"/> instance with the current context and the specified socket type.</returns>
        public static ZMonitor? Create(ZContext context, string endpoint, out ZError? error)
        {
            ZSocket? socket;
            if (null == (socket = ZSocket.Create(context, ZSocketType.PAIR, out error)))
                return default;

            return new(context, socket, endpoint);
        }

        public event EventHandler<ZMonitorEventArgs>? AllEvents;

        /// <summary>
        /// Occurs when a new connection is established.
        /// NOTE: Do not rely on the <see cref="ZMonitorEventArgs.Address"/> value for
        /// 'Connected' messages, as the memory address contained in the message may no longer
        /// point to the correct value.
        /// </summary>
        public event EventHandler<ZMonitorFileDescriptorEventArgs>? Connected;

        /// <summary>
        /// Occurs when a synchronous connection attempt failed, and its completion is being polled for.
        /// </summary>
        public event EventHandler<ZMonitorEventArgs>? ConnectDelayed;

        /// <summary>
        /// Occurs when an asynchronous connect / reconnection attempt is being handled by a reconnect timer.
        /// </summary>
        public event EventHandler<ZMonitorIntervalEventArgs>? ConnectRetried;

        /// <summary>
        /// Occurs when a socket is bound to an address and is ready to accept connections.
        /// </summary>
        public event EventHandler<ZMonitorFileDescriptorEventArgs>? Listening;

        /// <summary>
        /// Occurs when a socket could not bind to an address.
        /// </summary>
        public event EventHandler<ZMonitorEventArgs>? BindFailed;

        /// <summary>
        /// Occurs when a connection from a remote peer has been established with a socket's listen address.
        /// </summary>
        public event EventHandler<ZMonitorFileDescriptorEventArgs>? Accepted;

        /// <summary>
        /// Occurs when a connection attempt to a socket's bound address fails.
        /// </summary>
        public event EventHandler<ZMonitorEventArgs>? AcceptFailed;

        /// <summary>
        /// Occurs when a connection was closed.
        /// NOTE: Do not rely on the <see cref="ZMonitorEventArgs.Address"/> value for
        /// 'Closed' messages, as the memory address contained in the message may no longer
        /// point to the correct value.
        /// </summary>
        public event EventHandler<ZMonitorFileDescriptorEventArgs>? Closed;

        /// <summary>
        /// Occurs when a connection couldn't be closed.
        /// </summary>
        public event EventHandler<ZMonitorEventArgs>? CloseFailed;

        /// <summary>
        /// Occurs when the stream engine (tcp and ipc specific) detects a corrupted / broken session.
        /// </summary>
        public event EventHandler<ZMonitorFileDescriptorEventArgs>? Disconnected;

        /// <summary>
        /// Monitoring on this socket ended.
        /// </summary>
        public event EventHandler<ZMonitorEventArgs>? Stopped;

        /// <summary>
        /// Gets the endpoint to which the monitor socket is connected.
        /// </summary>
        public string Endpoint => _endpoint;

        // private static readonly int sizeof_MonitorEventData = Marshal.SizeOf(typeof(ZMonitorEventData));

        /// <summary>
        /// Begins monitoring for state changes, raising the appropriate events as they arrive.
        /// </summary>
        /// <remarks>NOTE: This is a blocking method and should be run from another thread.</remarks>
        protected override void Run()
        {
            if (_socket is null) throw new InvalidOperationException("No socket specified.");

            using (_socket)
            {
                if (!_socket.Connect(_endpoint, out var error))
                {
                    LogError(error, "connect");
                    return;
                }

                var poller = ZPollItem.CreateReceiver();

                while (!(Canceller?.IsCancellationRequested ?? false))
                {
                    if (!_socket.PollIn(poller, out var incoming, out error, PollingInterval))
                    {
                        if (error == ZError.EAGAIN)
                        {
                            // TODO: why sleep here? the loop frequency is already controlled by PollingInterval
                            Thread.Sleep(1);
                            continue;
                        }

                        LogError(error, "poll");
                    }

                    var eventValue = new ZMonitorEventData();

                    if (incoming is not null)
                        using (incoming)
                        {
                            if (incoming.Count > 0)
                            {
                                eventValue.Event = (ZMonitorEvents)incoming[0].ReadInt16();
                                eventValue.EventValue = incoming[0].ReadInt32();
                            }

                            if (incoming.Count > 1)
                                eventValue.Address = incoming[1].ReadString();
                        }

                    OnMonitor(eventValue);
                }

                if (!_socket.Disconnect(_endpoint, out error))
                    LogError(error, "disconnect");
            }
        }

        private void LogError(ZError? error, string context)
        {
            // TODO: this error handling is somewhat too subtle; the client should be able to retrieve it
            if (error != ZError.ETERM)
                Trace.TraceError("error on {0}: {1}", context, error);
        }

        internal void OnMonitor(ZMonitorEventData data)
        {
            if (_eventHandler.ContainsKey(ZMonitorEvents.AllEvents))
                _eventHandler[ZMonitorEvents.AllEvents](data);
            if (_eventHandler.ContainsKey(data.Event))
                _eventHandler[data.Event](data);
        }

        /// <summary>
        /// Releases the unmanaged resources used by the <see cref="ZMonitor"/>, and optionally disposes of the managed resources.
        /// </summary>
        /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
        protected override void Dispose(bool disposing)
        {
            if (!Disposed)
                if (disposing)
                    if (_socket != null)
                    {
                        _socket.Dispose();
                        _socket = null;
                    }
            base.Dispose(disposing);
        }

        private void InvokeEvent<T>(EventHandler<T>? handler, Func<T> createEventArgs) where T : EventArgs
            => handler?.Invoke(this, createEventArgs());
    }
}
