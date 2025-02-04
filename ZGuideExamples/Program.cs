﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Mime;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using ZeroMQ;

namespace Examples
{
    internal static partial class Program
    {
        internal static event Action<ConsoleKeyInfo> ConsoleKeyPressed;

        // INFO: You will find a "static int Main(string[] args)" in ProgramRunner.cs

        public static bool Verbose = false;
        public static bool Quiet = false;

        [Conditional("TRACE")]
        [DebuggerStepThrough]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Trace(string? str, params object[]? args)
            => str.Trace(args);

        [Conditional("TRACE")]
        [DebuggerStepThrough]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Trace(FormattableString str)
            => MdpExtensions.Trace(str);

        [DebuggerStepThrough]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Info(string? str, params object[]? args)
            => str.Info(args);

        [DebuggerStepThrough]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Info(FormattableString str)
            => MdpExtensions.Info(str);

        static void Console_WriteZFrame(string format, ZFrame frame, params object[] data)
        {
            var renderer = new StringBuilder();

            var list = new List<object>(data);

            // here the renderer

            renderer.Append(format);
            renderer.Append(": ");
            renderer.Append("{");
            renderer.Append(0 + data.Length);
            renderer.Append("}");

            // now the message

            frame.Position = 0;

            if (frame.Length == 0)
                list.Add("0");
            else
                list.Add(frame.ReadString());

            frame.Position = 0;

            Console.WriteLine(renderer.ToString(), list.ToArray());
        }

        static void Console_WriteZMessage(string format, ZMessage? message, params object[] data)
            => Console_WriteZMessage(format, 0, message, data);

        static void Console_WriteZMessage(string format, int messagesNotToRead, ZMessage? message, params object[] data)
        {
            var renderer = new StringBuilder();

            var list = new List<object>(data);

            for (int i = messagesNotToRead, c = message.Count; i < c; ++i)
            {
                // here the renderer
                if (i == messagesNotToRead)
                {
                    renderer.Append(format);
                    renderer.Append(": ");
                }
                else
                    renderer.Append(", ");
                renderer.Append("{");
                renderer.Append(i - messagesNotToRead + data.Length);
                renderer.Append("}");

                // now the message
                var frame = message[i];

                frame.Position = 0;

                if (frame.Length == 0)
                    list.Add("0");
                else
                    list.Add(frame.ReadString());

                frame.Position = 0;
            }

            Console.WriteLine(renderer.ToString(), list.ToArray());
        }

        public static void ConsoleKeyPressedProcLoop()
        {
            var keyPressedLoopThread = Thread.CurrentThread;
            AppDomain.CurrentDomain.ProcessExit += (_, _) => {
                keyPressedLoopThread.Interrupt();
                if (!keyPressedLoopThread.Join(1000))
                    keyPressedLoopThread.IsBackground = true;
            };
            try
            {
                var magicSleepTime = TimeSpan.FromMilliseconds(15.625);
                for (;;)
                {
                    if (Console.KeyAvailable)
                        ConsoleKeyPressed?.Invoke(Console.ReadKey(true));
                    else
                        try
                        {
                            Thread.Sleep(magicSleepTime);
                        }
                        catch (ThreadInterruptedException) { break; }
                }
            }
            catch (ThreadAbortException) { }
        }
    }

    public static class Ext
    {
        public static string ToHexString(this byte[] hex)
        {
            if (hex == null)
                return null;
            if (hex.Length == 0)
                return string.Empty;
            var s = new StringBuilder();
            foreach (var b in hex)
                s.Append(b.ToString("x2"));
            return s.ToString();
        }

        public static byte[] ToHexBytes(this string hex)
        {
            if (hex == null)
                return null;
            if (hex.Length == 0)
                return Array.Empty<byte>();
            var l = hex.Length / 2;
            var b = new byte[l];
            for (var i = 0; i < l; ++i)
                b[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return b;
        }
    }
}
