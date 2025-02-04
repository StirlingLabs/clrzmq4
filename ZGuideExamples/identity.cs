﻿using ZeroMQ;

namespace Examples
{
	internal static partial class Program
	{
		public static void Identity(string[] args)
		{
			//
			// Demonstrate request-reply identities
			//
			// Author: metadings
			//

			using (var context = new ZContext())
			using (var sink = new ZSocket(context, ZSocketType.ROUTER))
			{
				sink.Bind("inproc://example");

				// First allow 0MQ to set the identity
				using (var anonymous = new ZSocket(context, ZSocketType.REQ))
				{
					anonymous.Connect("inproc://example");
					anonymous.Send(ZFrame.Create("ROUTER uses REQ's generated 5 byte identity"));
				}
				using (var msg = sink.ReceiveMessage())
					msg.DumpZmsg("--------------------------");

				// Then set the identity ourselves
				using (var identified = new ZSocket(context, ZSocketType.REQ))
				{
					identified.IdentityString = "PEER2";
					identified.Connect("inproc://example");
					identified.Send(ZFrame.Create("ROUTER uses REQ's socket identity"));
				}
				using (var msg = sink.ReceiveMessage())
					msg.DumpZmsg("--------------------------");
			}
		}
	}
}