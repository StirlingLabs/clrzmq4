﻿using System;
using System.Diagnostics;
using System.Threading;
using ZeroMQ;

namespace Examples
{
	internal static partial class Program
	{
		static int RTReq_Workers = 10;

		public static void RTReq(string[] args)
		{
			//
			// ROUTER-to-REQ example
			//
			// While this example runs in a single process, that is only to make
			// it easier to start and stop the example. Each thread has its own
			// context and conceptually acts as a separate process.
			//
			// Author: metadings
			//

			using (var context = new ZContext())
			using (var broker = new ZSocket(context, ZSocketType.ROUTER))
			{
				broker.Bind("tcp://*:5671");

				for (var i = 0; i < RTReq_Workers; ++i)
				{
					var j = i; new Thread(() => RTReq_Worker(j)).Start();
				}

				var stopwatch = new Stopwatch();
				stopwatch.Start();

				// Run for five seconds and then tell workers to end
				var workers_fired = 0;
				while (true)
				{
					// Next message gives us least recently used worker
					using (var identity = broker.ReceiveMessage())
					{
						broker.SendMore(identity[0]);
						broker.SendMore(ZFrame.Create());

						// Encourage workers until it's time to fire them
						if (stopwatch.Elapsed < TimeSpan.FromSeconds(5))
							broker.Send(ZFrame.Create("Work harder!"));
						else
						{
							broker.Send(ZFrame.Create("Fired!"));

							if (++workers_fired == RTReq_Workers)
								break;
						}
					}
				}
			}
		}

		static void RTReq_Worker(int i) 
		{
			using (var context = new ZContext())
			using (var worker = new ZSocket(context, ZSocketType.REQ))
			{
				worker.IdentityString = "PEER" + i;	// Set a printable identity
				worker.Connect("tcp://127.0.0.1:5671");

				var total = 0;
				while (true)
				{
					// Tell the broker we're ready for work
					worker.Send(ZFrame.Create("Hi Boss"));

					// Get workload from broker, until finished
					using (var frame = worker.ReceiveFrame())
					{
						var finished = frame.ReadString() == "Fired!";
						if (finished)
							break;
					}

					total++;

					// Do some random work
					Thread.Sleep(1);
				}

				Console.WriteLine("Completed: PEER{0}, {1} tasks", i, total);
			}
		}
	}
}