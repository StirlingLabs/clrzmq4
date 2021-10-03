﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace Examples
{
    static class ProgramRunner
    {
        static int Main(string[] args)
        {
            var returnMain = 0; // C good

            var leaveOut = 0;
            var dict = new Dictionary<string, string>();
            var fields = typeof(Program).GetFields(BindingFlags.Public | BindingFlags.Static)
                .OrderBy(field => field.Name)
                .ToArray();

            foreach (var arg in args)
            {
                if (!arg.StartsWith("--", StringComparison.OrdinalIgnoreCase)) break;

                leaveOut++;

                var iOfEquals = arg.IndexOf('=');
                string key, value;
                if (-1 < iOfEquals)
                {
                    key = arg.Substring(0, iOfEquals);
                    value = arg.Substring(iOfEquals + 1);
                }
                else
                {
                    key = arg.Substring(0);
                    value = null;
                }
                dict.Add(key, value);

                var keyField = fields
                    .FirstOrDefault(field => string.Equals(field.Name, key.Substring(2), StringComparison.OrdinalIgnoreCase));

                if (keyField == null)
                    continue;

                if (keyField.FieldType == typeof(string))
                    keyField.SetValue(null, value);

                else if (keyField.FieldType == typeof(bool))
                {
                    var equalsTrue = string.IsNullOrEmpty(value);
                    if (!equalsTrue)
                        equalsTrue = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                    if (!equalsTrue)
                        equalsTrue = string.Equals(value, "+", StringComparison.OrdinalIgnoreCase);

                    keyField.SetValue(null, equalsTrue);
                }
            }

            var methods = typeof(Program)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .OrderBy(method => method.Name)
                .ToList();

            var command = args.Length == 0 ? "help" : args[0 + leaveOut].ToLower();
            if (command != "help")
            {
                var method = methods.FirstOrDefault(m => m.Name.Equals(command, StringComparison.OrdinalIgnoreCase));
                if (method != null)
                {
                    object[]? parameters = null;
                    var arguments = method.GetParameters();

                    parameters = arguments.Length switch
                    {
                        2 => new object[] { dict, args.Skip(1 + leaveOut).ToArray() /* string[] args */ },
                        1 => new object[] { args.Skip(1 + leaveOut).ToArray() /* string[] args */ },
                        _ => parameters
                    };

                    var result = DebugStackTrace<TargetInvocationException>
                        .Invoke(method, /* static */null, parameters);

                    if (method.ReturnType == typeof(bool))
                        return (bool)result ? 0 : 1;
                    if (method.ReturnType == typeof(int))
                        return (int)result;

                    return 0; // method.ReturnType == typeof(Void)
                }

                returnMain = 1; // C bad
                Console.WriteLine();
                Console.WriteLine("Command invalid.");
            }

            Console.WriteLine();
            Console.WriteLine("Usage: ./" + AppDomain.CurrentDomain.FriendlyName + " [--option] <command>");

            if (fields.Length > 0)
            {
                Console.WriteLine();
                Console.WriteLine("Available [option]s:");
                foreach (var field in fields)
                    Console.WriteLine("  --{0}", field.Name);
                Console.WriteLine();
            }

            Console.WriteLine();
            Console.WriteLine("Available <command>s:");

            foreach (var method in methods)
            {
                if (method.Name == "Main")
                    continue;
                if (0 < method.GetCustomAttributes(typeof(CompilerGeneratedAttribute), true).Length)
                    continue;

                Console.WriteLine("    {0}", method.Name);
            }

            Console.WriteLine();
            return returnMain;
        }
    }


    internal static class DebugStackTrace<TException>
        where TException : Exception
    {
        [DebuggerNonUserCode]
        [DebuggerStepThrough]
        public static object Invoke(MethodInfo method, object target, params object[] args)
        {
            $"Invoking \"{method.Name}\"".DumpString();
            try
            {
                return method.Invoke(target, args);
            }
            catch (TException te)
            {
                if (te.InnerException == null)
                    throw;

                ExceptionDispatchInfo.Capture(te.InnerException).Throw();

                throw;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.ToString());
                // wtf
                Debugger.Launch();
                Debugger.Break();
                throw;
            }
        }
    }
}
