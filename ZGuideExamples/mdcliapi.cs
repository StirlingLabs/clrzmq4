﻿using System;
using System.Threading;
using ZeroMQ;

using static Examples.MdpExtensions;

namespace Examples
{
    namespace MDCliApi
    {
        //
        //  mdcliapi class - Majordomo Protocol Client API
        //  Implements the MDP/Worker spec at http://rfc.zeromq.org/spec:7.
        //
        // Author: metadings
        //

        public class MajordomoClient : IDisposable
        {
            //  Structure of our class
            //  We access these properties only via class methods

            // Our context
            readonly ZContext _context;

            // Majordomo broker
            public string Broker { get; protected set; }

            //  Socket to broker
            public ZSocket Client { get; protected set; }

            //  Print activity to console
            public bool Verbose { get; protected set; }

            //  Request timeout
            public TimeSpan Timeout { get; protected set; }

            //  Request retries
            public int Retries { get; protected set; }



            public void ConnectToBroker()
            {
                //  Connect or reconnect to broker

                Client = new(_context, ZSocketType.REQ);
                Client.Connect(Broker);
                if (Verbose)
                    Trace($"I: connecting to broker at '{Broker}'...");

            }

            public MajordomoClient(string broker, bool verbose)
            {
                if(broker == null)
                    throw new InvalidOperationException();
                _context = new();
                Broker = broker;
                Verbose = verbose;
                Timeout = TimeSpan.FromMilliseconds(2500);
                Retries = 3;

                ConnectToBroker();
            }

            ~MajordomoClient()
                => Dispose(false);

            public void Dispose()
            {
                GC.SuppressFinalize(this);
                Dispose(true);
            }

            protected void Dispose(bool disposing)
            {
                if (disposing)
                // Destructor

                    if (Client != null)
                    {
                        Client.Dispose();
                        Client = null;
                    }
                //Do not Dispose Context: cuz of weird shutdown behavior, stucks in using calls 
            }

            //  .split configure retry behavior
            //  These are the class methods. We can set the request timeout and number
            //  of retry attempts before sending requests:

            //  Set request timeout
            public void Set_Timeout(int timeoutInMs)
                => Timeout = TimeSpan.FromMilliseconds(timeoutInMs);

            //  Set request retries
            public void Set_Retries(int retries)
                => Retries = retries;

            //  .split send request and wait for reply
            //  Here is the {{send}} method. It sends a request to the broker and gets
            //  a reply even if it has to retry several times. It takes ownership of 
            //  the request message, and destroys it when sent. It returns the reply
            //  message, or NULL if there was no reply after multiple attempts:
            public ZMessage? Send(string service, ZMessage request, CancellationTokenSource canceller)
            {
                if (request == null)
                    throw new InvalidOperationException();

                //  Prefix request with protocol frames
                //  Frame 1: "MDPCxy" (six bytes, MDP/Client x.y)
                //  Frame 2: Service name (printable string)
                request.Prepend(ZFrame.Create(service));
                request.Prepend(ZFrame.Create(MdpCommon.MDPC_CLIENT));
                if (Verbose)
                    request.DumpZmsg("I: send request to '{0}' service:", service);

                var retriesLeft = Retries;
                while (retriesLeft > 0 && !canceller.IsCancellationRequested)
                {
                    if (canceller.IsCancellationRequested
                        || Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Escape)
                        _context.Shutdown();

                    // Copy the Request and send on Client
                    var msgreq = request.Clone();

                    if (!Client.Send(msgreq, out var error))
                        if (Equals(error, ZError.ETERM))
                        {
                            canceller.Cancel();
                            break; // Interrupted
                        }

                    var p = ZPollItem.CreateReceiver();
                    //  .split body of send 
                    //  On any blocking call, {{libzmq}} will return -1 if there was
                    //  an error; we could in theory check for different error codes,
                    //  but in practice it's OK to assume it was {{EINTR}} (Ctrl-C):

                    // Poll the client Message
                    if (Client.PollIn(p, out var msg, out error, Timeout))
                    {
                        //  If we got a reply, process it
                        if (Verbose)
                            msg.DumpZmsg("I: received reply");

                        if(msg.Count < 3)
                            throw new InvalidOperationException();

                        using (var header = msg.Pop())
                        {
                            if (!header.ToString().Equals(MdpCommon.MDPC_CLIENT))
                                throw new InvalidOperationException();
                        }

                        using (var replyService = msg.Pop())
                        {
                            if(!replyService.ToString().Equals(service))
                                throw new InvalidOperationException();
                        }

                        request.Dispose();
                        return msg;
                    }
                    if (Equals(error, ZError.ETERM))
                    {
                        canceller.Cancel();
                        break; // Interrupted
                    }
                    if (Equals(error, ZError.EAGAIN))
                    {
                        if (--retriesLeft > 0)
                        {
                            if (Verbose)
                                Trace("W: no reply, reconnecting...");

                            ConnectToBroker();
                        }
                        else
                        {
                            if (Verbose)
                                Trace("W: permanent error, abandoning");
                            break; // Give up
                        }
                    }
                }
                if (canceller.IsCancellationRequested)
                    Trace("W: interrupt received, killing client...\n");
                request.Dispose();
                return null;
            }
        }
    }
}
