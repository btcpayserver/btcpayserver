// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;


namespace Microsoft.Extensions.Logging.Console.Internal
{
    /// <summary>
    /// For non-Windows platform consoles which understand the ANSI escape code sequences to represent color
    /// </summary>
    internal class AnsiLogConsole : IConsole
    {
        private readonly StringBuilder _outputBuilder;
        private readonly IAnsiSystemConsole _systemConsole;

        public AnsiLogConsole(IAnsiSystemConsole systemConsole)
        {
            _outputBuilder = new StringBuilder();
            _systemConsole = systemConsole;
        }

        public void Write(string message, ConsoleColor? background, ConsoleColor? foreground)
        {
            // Order: backgroundcolor, foregroundcolor, Message, reset foregroundcolor, reset backgroundcolor
            if (background.HasValue)
            {
                _outputBuilder.Append(GetBackgroundColorEscapeCode(background.Value));
            }

            if (foreground.HasValue)
            {
                _outputBuilder.Append(GetForegroundColorEscapeCode(foreground.Value));
            }

            _outputBuilder.Append(message);

            if (foreground.HasValue)
            {
                _outputBuilder.Append("\x1B[39m\x1B[22m"); // reset to default foreground color
            }

            if (background.HasValue)
            {
                _outputBuilder.Append("\x1B[49m"); // reset to the background color
            }
        }

        public void WriteLine(string message, ConsoleColor? background, ConsoleColor? foreground)
        {
            Write(message, background, foreground);
            _outputBuilder.AppendLine();
        }

        public void Flush()
        {
            _systemConsole.Write(_outputBuilder.ToString());
            _outputBuilder.Clear();
        }

        private static string GetForegroundColorEscapeCode(ConsoleColor color)
        {
            switch (color)
            {
                case ConsoleColor.Black:
                    return "\x1B[30m";
                case ConsoleColor.DarkRed:
                    return "\x1B[31m";
                case ConsoleColor.DarkGreen:
                    return "\x1B[32m";
                case ConsoleColor.DarkYellow:
                    return "\x1B[33m";
                case ConsoleColor.DarkBlue:
                    return "\x1B[34m";
                case ConsoleColor.DarkMagenta:
                    return "\x1B[35m";
                case ConsoleColor.DarkCyan:
                    return "\x1B[36m";
                case ConsoleColor.Gray:
                    return "\x1B[37m";
                case ConsoleColor.Red:
                    return "\x1B[1m\x1B[31m";
                case ConsoleColor.Green:
                    return "\x1B[1m\x1B[32m";
                case ConsoleColor.Yellow:
                    return "\x1B[1m\x1B[33m";
                case ConsoleColor.Blue:
                    return "\x1B[1m\x1B[34m";
                case ConsoleColor.Magenta:
                    return "\x1B[1m\x1B[35m";
                case ConsoleColor.Cyan:
                    return "\x1B[1m\x1B[36m";
                case ConsoleColor.White:
                    return "\x1B[1m\x1B[37m";
                default:
                    return "\x1B[39m\x1B[22m"; // default foreground color
            }
        }

        private static string GetBackgroundColorEscapeCode(ConsoleColor color)
        {
            switch (color)
            {
                case ConsoleColor.Black:
                    return "\x1B[40m";
                case ConsoleColor.Red:
                    return "\x1B[41m";
                case ConsoleColor.Green:
                    return "\x1B[42m";
                case ConsoleColor.Yellow:
                    return "\x1B[43m";
                case ConsoleColor.Blue:
                    return "\x1B[44m";
                case ConsoleColor.Magenta:
                    return "\x1B[45m";
                case ConsoleColor.Cyan:
                    return "\x1B[46m";
                case ConsoleColor.White:
                    return "\x1B[47m";
                default:
                    return "\x1B[49m"; // Use default background color
            }
        }
    }

	internal class AnsiSystemConsole : IAnsiSystemConsole
    {
        private readonly TextWriter _textWriter;

        /// <inheritdoc />
        public AnsiSystemConsole(bool stdErr = false)
        {
            _textWriter = stdErr ? System.Console.Error : System.Console.Out;
        }

        public void Write(string message)
        {
            _textWriter.Write(message);
        }
    }
}

namespace Microsoft.Extensions.Logging.Console
{
    internal interface IAnsiSystemConsole
    {
        void Write(string message);
    }
	public interface IConsole
    {
        void Write(string message, ConsoleColor? background, ConsoleColor? foreground);
        void WriteLine(string message, ConsoleColor? background, ConsoleColor? foreground);
        void Flush();
    }
	internal class WindowsLogConsole : IConsole
    {
        private readonly TextWriter _textWriter;

        /// <inheritdoc />
        public WindowsLogConsole(bool stdErr = false)
        {
            _textWriter = stdErr ? System.Console.Error : System.Console.Out;
        }

        private bool SetColor(ConsoleColor? background, ConsoleColor? foreground)
        {
            if (background.HasValue)
            {
                System.Console.BackgroundColor = background.Value;
            }

            if (foreground.HasValue)
            {
                System.Console.ForegroundColor = foreground.Value;
            }

            return background.HasValue || foreground.HasValue;
        }

        private void ResetColor()
        {
            System.Console.ResetColor();
        }

        public void Write(string message, ConsoleColor? background, ConsoleColor? foreground)
        {
            var colorChanged = SetColor(background, foreground);
            _textWriter.Write(message);
            if (colorChanged)
            {
                ResetColor();
            }
        }

        public void WriteLine(string message, ConsoleColor? background, ConsoleColor? foreground)
        {
            var colorChanged = SetColor(background, foreground);
            _textWriter.WriteLine(message);
            if (colorChanged)
            {
                ResetColor();
            }
        }

        public void Flush()
        {
            // No action required as for every write, data is sent directly to the console
            // output stream
        }
    }
}

namespace Microsoft.Extensions.Logging.Abstractions.Internal
{
    /// <summary>
    /// An empty scope without any logic
    /// </summary>
    internal class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new NullScope();

        private NullScope()
        {
        }

        /// <inheritdoc />
        public void Dispose()
        {
        }
    }
}
