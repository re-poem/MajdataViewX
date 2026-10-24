using Cimai;
using MajdataViewX.Base;
using MajdataViewX.Utils.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace MajdataViewX.Notes.SlideUtils
{
    public static class SlideCodeParser
    {
        public enum CommandType
        {
            Invalid = -1,
            NodeA = 0,
            NodeB = 1,
            NodeC = 2,
            OrbitCcw = 3,
            OrbitCw = 4,
            NodeEnd = 5
        }

        public readonly struct Command
        {
            public readonly CommandType Type;
            public readonly int Value;

            public Command(CommandType type, int value)
            {
                Type = type;
                Value = value;
            }

            public static bool IsSame(Command a, Command b)
            {
                return a.Type == b.Type && a.Value == b.Value;
            }
        }

        public static readonly char[] CommandChars = {
            'A', 'B', 'C', 'P', 'Q', 'K'
        };

        public static int TryParseDigit(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            return -1;
        }

        public static List<Command> ParseCommands(string code)
        {
            if (!CommandChars.Contains(code[1]))
            {
                throw new ArgumentException($"the 2nd char should be a command");
            }

            if (code[^2] != 'K')
            {
                throw new ArgumentException($"should end with 'K' command");
            }

            var commands = new List<Command>();
            var currentType = CommandType.NodeA;
            var value = TryParseDigit(code[0]);
            if (value < 0) throw new ArgumentException($"invalid char '{code[0]}'");

            commands.Add(new Command(currentType, value));

            for (var ptr = 1; ptr < code.Length; ptr++)
            {
                var ch = code[ptr];
                if (CommandChars.Contains(ch))
                {
                    currentType = (CommandType)Array.IndexOf(CommandChars, ch);
                    if (currentType == CommandType.NodeC)
                    {
                        commands.Add(new Command(CommandType.NodeC, 0));
                    }
                }
                else
                {
                    value = TryParseDigit(ch);
                    if (value < 0) throw new ArgumentException($"invalid char '{ch}'");
                    if (currentType == CommandType.NodeC)
                    {
                        throw new ArgumentException($"digit should not follow 'C'");
                    }
                    commands.Add(new Command(currentType, value));
                }
            }
            return commands;
        }

        public static Complex GetNodePosition(Command cmd)
        {
            switch (cmd.Type)
            {
                case CommandType.NodeA:
                case CommandType.NodeEnd:
                    return MajGeo.PointGroupA(cmd.Value);
                case CommandType.NodeB:
                    return MajGeo.PointGroupB(cmd.Value);
                case CommandType.NodeC:
                    return MajGeo.PointCenter();
                default:
                    throw new ArgumentException($"invalid type for node: {cmd.Type}");
            }
        }

        public static void NodeToNode(SlidePathConstructor constructor, Command last, Command current)
        {
            if (Command.IsSame(last, current)) return;
            constructor.LineToPoint(GetNodePosition(current));
        }

        public static void NodeToOrbit(SlidePathConstructor constructor, Command last, Command current)
        {
            var isCcw = (current.Type == CommandType.OrbitCcw);
            var node = GetNodePosition(last);
            var orbit = MajGeo.GetCircle(current.Value);
            var diff = node - orbit.Center;
            if (Math.Abs(diff.Magnitude - orbit.Radius) < 0.1)
            {
                if (last.Type == CommandType.NodeA && current.Value == 9)
                {
                    constructor.TrySetLastParseMarker(SlideParseMarker.ForceAlign);
                }
                return;  // node on circle, do nothing
            }

            if (diff.Magnitude < orbit.Radius)
                throw new ArgumentException($"impossible: {last.Type}{last.Value} -> Orbit{current.Value}");

            constructor.TangentToCircle(orbit, isCcw);
        }

        public static void OrbitToNode(SlidePathConstructor constructor, Command last, Command current)
        {
            var isCcw = (last.Type == CommandType.OrbitCcw);
            var node = GetNodePosition(current);
            var orbit = MajGeo.GetCircle(last.Value);
            var diff = node - orbit.Center;
            if (Math.Abs(diff.Magnitude - orbit.Radius) < 0.1)
            {
                constructor.ArcToAngle(orbit.Center, diff.Phase, isCcw, false);
                return;
            }

            if (diff.Magnitude < orbit.Radius)
                throw new ArgumentException($"impossible: Orbit{last.Value} -> {current.Type}{current.Value}");

            constructor.ArcToTangentTowards(node, orbit.Center, isCcw).LineToPoint(node);
        }

        public static void OrbitToOrbit(SlidePathConstructor constructor, Command last, Command current)
        {
            if (current.Type != last.Type) throw new ArgumentException($"orbit type mismatch");

            var isCcw = (last.Type == CommandType.OrbitCcw);
            var lastOrbit = MajGeo.GetCircle(last.Value);
            var currentOrbit = MajGeo.GetCircle(current.Value);
            if (current.Value == last.Value)
            {
                constructor.FullCircle(lastOrbit.Center, isCcw);
                return;
            }

            if (last.Value == 0 && current.Value == 9 || last.Value == 9 && current.Value == 0)
                throw new ArgumentException($"impossible: Orbit{last.Value} -> Orbit{current.Value}");

            if (current.Value == 9)
            {
                var data = MajGeo.TransferOutData(last.Value, isCcw);
                constructor.ArcToAngle(lastOrbit.Center, data.Item2, isCcw, false)
                    .ArcToAngle(data.Item1.Center, data.Item3, isCcw, false)
                    .TrySetLastParseMarker(SlideParseMarker.SmoothAlign);
                return;
            }

            if (last.Value == 9)
            {
                var data = MajGeo.TransferOutData(current.Value, !isCcw);
                constructor.ArcToAngle(lastOrbit.Center, data.Item3, isCcw, true)
                    .ArcToAngle(data.Item1.Center, data.Item2, isCcw, false);
                return;
            }

            constructor.ExternTangentTransfer(lastOrbit.Center, currentOrbit, isCcw);
        }

        /// <summary>
        /// Parse a specific slide-code into a path object
        /// </summary>
        /// <param name="code">Slide-code string</param>
        /// <returns>Generated slide path</returns>
        /// <exception cref="ArgumentException">The slide-code contains some invalid command sequence</exception>
        /// <exception cref="ArgumentOutOfRangeException">Command type overflow</exception>
        public static ParametricSlidePath Parse(string code)
        {
            return Parse(ParseCommands(code));
        }

        public static ParametricSlidePath Parse(int start, ReadOnlySpan<SlidePart> parts)
        {
            var commands = new List<Command>(parts.Length + 1) { new(CommandType.NodeA, start) };
            foreach (var part in parts)
            {
                var type = part.Type switch
                {
                    SlideType.A_CMD => CommandType.NodeA,
                    SlideType.B_CMD => CommandType.NodeB,
                    SlideType.C_CMD => CommandType.NodeC,
                    SlideType.P_CMD => CommandType.OrbitCcw,
                    SlideType.Q_CMD => CommandType.OrbitCw,
                    SlideType.K_CMD => CommandType.NodeEnd,
                    _ => throw new ArgumentOutOfRangeException(nameof(parts))
                };
                commands.Add(new Command(type, type == CommandType.NodeC ? 0 : part.End.GetPosNum()));
            }
            return Parse(commands);
        }

        private static ParametricSlidePath Parse(IReadOnlyList<Command> commands)
        {
            var lastCmd = commands[0];
            // The first command is guarantee to be 'A'
            var generator = SlidePathConstructor.BeginAt(MajGeo.PointGroupA(lastCmd.Value));

            for (var i = 1; i < commands.Count; i++)
            {
                var cmd = commands[i];
                switch (cmd.Type)
                {
                    case CommandType.NodeA:
                    case CommandType.NodeB:
                    case CommandType.NodeC:
                    case CommandType.NodeEnd:
                        {
                            switch (lastCmd.Type)
                            {
                                case CommandType.NodeA:
                                case CommandType.NodeB:
                                case CommandType.NodeC:
                                    NodeToNode(generator, lastCmd, cmd);
                                    break;
                                case CommandType.OrbitCcw:
                                case CommandType.OrbitCw:
                                    OrbitToNode(generator, lastCmd, cmd);
                                    break;
                                case CommandType.NodeEnd:
                                    throw new ArgumentException($"'K' should be the last command");
                                default:
                                    throw new ArgumentOutOfRangeException();
                            }
                            break;
                        }
                    case CommandType.OrbitCcw:
                    case CommandType.OrbitCw:
                        {
                            switch (lastCmd.Type)
                            {
                                case CommandType.NodeA:
                                case CommandType.NodeB:
                                case CommandType.NodeC:
                                    NodeToOrbit(generator, lastCmd, cmd);
                                    break;
                                case CommandType.OrbitCcw:
                                case CommandType.OrbitCw:
                                    OrbitToOrbit(generator, lastCmd, cmd);
                                    break;
                                case CommandType.NodeEnd:
                                    throw new ArgumentException($"'K' should be the last command");
                                default:
                                    throw new ArgumentOutOfRangeException();
                            }
                            break;
                        }
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                lastCmd = cmd;
            }

            return generator.GeneratePath();
        }
    }
}
