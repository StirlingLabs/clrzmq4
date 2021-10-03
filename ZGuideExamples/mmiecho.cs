﻿using System;
using System.Threading;
using Examples.MDCliApi;
using ZeroMQ;

// using System.Runtime.Remoting.Messaging;

namespace Examples
{
    //  Lets us build this source without creating a library
    static partial class Program
    {
        //  MMI echo query example
        public static void MMIEcho(string[] args)
        {
            var canceller = new CancellationTokenSource();
            Console.CancelKeyPress += (s, ea) =>
            {
                ea.Cancel = true;
                canceller.Cancel();
            };

            using (var session = new MajordomoClient("tcp://127.0.0.1:5555", Verbose))
            {
                using var request  = ZMessage.Create();
                request.Add(new("echo"));

                var reply = session.Send("mmi.service", request, canceller);
                if (reply != null)
                {
                    var replycode = reply[0].ToString();
                    $"Loopup echo service: {replycode}\n".DumpString();
                    reply.Dispose();
                }
                else
                    "E: no response from broker, make sure it's running\n".DumpString();
            }
        }
    }
}
