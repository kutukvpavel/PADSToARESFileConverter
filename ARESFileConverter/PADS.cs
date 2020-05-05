using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading;
using System.Globalization;

namespace ARESFileConverter
{
    public static class PADS
    {

        #region Constants and settings
        /// <summary>
        /// Supported: Thou, Millimeter, Inch, TwoThirdsOfNanometer
        /// </summary>
        public static readonly Dictionary<PcbUnits, string> HeaderUnits = new Dictionary<PcbUnits, string>
        {
            { PcbUnits.Thou, "MILS" },
            { PcbUnits.Millimeter, "METRIC" },
            { PcbUnits.TwoThirdsOfNanometer, "BASIC" },
            { PcbUnits.Inch, "INCHES" }
        };

        public enum ControlStatement
        {
            CLUSTER,
            CONN,
            END,
            GET,
            JUMPER,
            LINES,
            MISC,
            NET,
            PART,
            PARTDECAL,
            PARTTYPE,
            PCB,
            POUR,
            REMARK,
            REUSE,
            ROUTE,
            SIGNAL,
            STANDARD,
            TESTPOINT,
            TEXT,
            VIA
        }
        public static readonly string[] ControlStatements = new string[]
        {
            "*CLUSTER*",
            "*CONN*",
            "*END*",
            "*GET*",
            "*JUMPER*",
            "*LINES*",
            "*MISC*",
            "*NET*",
            "*PART*",
            "*PARTDECAL*",
            "*PARTTYPE*",
            "*PCB*",
            "*POUR*",
            "*REMARK*",
            "*REUSE*",
            "*ROUTE*",
            "*SIGNAL*",
            "*STANDARD*",
            "*TESTPOINT*",
            "*TEXT*",
            "*VIA*"
        };

        public const char FileHeaderDesignator = '!';
        public const char FileHeaderSeparator = '-';
        public const char LineSeparator = '\n';
        public const char CaretReset = '\r';

        /// <summary>
        /// For SamacSys models (at least), they have LINE->PIECE's header format in PARTDECAL->PIECE-s
        /// </summary>
        public static bool PieceHeaderCompatibilityMode { get; set; } = false;
        /// <summary>
        /// Recognizes (-2,-1,0) layer stackup as layer "ALL"
        /// </summary>
        public static bool FourLayerModel { get; set; } = true;
        /// <summary>
        /// Still recognizes (-2,-1,0) stackup as layer "ALL" for large layer models
        /// </summary>
        public static bool LayerModelOverrideForALL { get; set; } = false;
        /// <summary>
        /// Is used when FourLayerModel = false
        /// </summary>
        public static int[] LargeCustomLayerModel { get; set; }
        /// <summary>
        /// Uses padstack binding according to pin line index by default and switches back to pin-designator-binding on error.
        /// </summary>
        public static bool PrioritizePadstackBindingByIndex { get; set; } = false;
        #endregion

        #region Parser
        public static PcbDesign Parse(string contents)
        {
            //Parse units from the header
            int i = contents.IndexOf(FileHeaderDesignator, 1);
            int j = contents.LastIndexOf(FileHeaderSeparator, i) + 1;
            string temp = contents.Substring(j, i - j);
            PcbUnits coordinateUnits = HeaderUnits.First(x => x.Value == temp).Key;
            //Get section boundaries
            i = contents.IndexOf(LineSeparator, i) + 1;
            Dictionary<ControlStatement, int> sections = new Dictionary<ControlStatement, int>(ControlStatements.Length);
            for (int k = 0; k < ControlStatements.Length; k++)
            {
                j = contents.IndexOf(ControlStatements[k], i);
                if (j > -1)
                {
                    j = contents.IndexOf(LineSeparator, j) + 1;
                    if (j < contents.Length)
                    {
                        if (contents[j] == LineSeparator) j++;
                        if (contents[j] == CaretReset) j += 2;
                    }
                    sections.Add((ControlStatement)k, j);
                }
            }
            //Parse partdecal
            int currentSectionStart = sections[ControlStatement.PARTDECAL];
            int currentSectionEnd = FindSectionEnd(sections, ControlStatement.PARTDECAL);
            int currentSectionLength = currentSectionEnd - currentSectionStart;
            Partdecal decal = null;
            try
            {
                decal = Partdecal.Parse(contents.Substring(currentSectionStart, currentSectionLength));
            }
            catch (Exception e)
            {
                WarningListener.Add(e); 
            }
            //Others are not needed yet

            return PADSToPcbDesign(coordinateUnits, decal);
        }
        private static int FindSectionEnd(Dictionary<ControlStatement, int> sections, ControlStatement current)
        {
            int res = int.MaxValue;
            int start = sections[current];
            foreach (var item in sections)
            {
                if (item.Value <= start) continue;
                if (item.Value < res) res = item.Value;
            }
            return res;
        }

        private static PcbDesign PADSToPcbDesign(PcbUnits coordinateUnits, Partdecal decal)
        {
            PcbDesign res = new PcbDesign();
            //Partdecal
            try
            {
                if (decal != null)
                {
                    ProcessDecalPieces(res, coordinateUnits, decal);
                    ProcessDecalPads(res, coordinateUnits, decal);
                }
            }
            catch (Exception e)
            {
                WarningListener.Add(e);
            }
            
            //Others are not implemented yet
                      
            //Header info
            res.SynchronizeCoordinateUnits(coordinateUnits);
            return res;
        }

        private static void ProcessDecalPieces(PcbDesign res, PcbUnits coordinateUnits, Partdecal decal)
        {
            //Pieces are graphics and traces
            res.Traces.Capacity += decal.Pieces.Count(x => x.CurrentType == Piece.PieceType.Copper);
            res.Graphics.Capacity += decal.Pieces.Count(x => x.CurrentType != Piece.PieceType.Copper);
            foreach (var item in decal.Pieces)
            {
                switch (item.CurrentType)
                {
                    case Piece.PieceType.Open:
                        res.Graphics.Add(PolylineHelper(coordinateUnits, item));
                        break;
                    case Piece.PieceType.Closed:
                        res.Graphics.Add(PolylineHelper(coordinateUnits, item));
                        break;
                    case Piece.PieceType.Copper:
                        //Should move it to LINES instead of PARTDECAL
                        res.Traces.Add(
                            new PcbTrace(item.CurrentLayer == Piece.LayerAllNumber ? PcbLayer.All : (PcbLayer)item.CurrentLayer,
                            item.Width, coordinateUnits, coordinateUnits, item.XY));
                        break;
                    default:
                        WarningListener.AddFormat(new NotImplementedException(), 
                            "Decal type {0} ignored.", Enum.GetName(typeof(Piece.PieceType), item.CurrentType));
                        break;
                }
            }
        }
        private static PcbGraphics PolylineHelper(PcbUnits coordinateUnits, Piece item)
        {
            PcbGraphicsLayer l = PcbGraphicsLayer.TopSilk;
            PcbLineType t = PcbLineType.Solid;
            if (Piece.GraphicsLayerMapping.ContainsKey(item.CurrentLayer))
            {
                l = Piece.GraphicsLayerMapping[item.CurrentLayer];
            }
            else
            {
                WarningListener.AddFormat(new NotImplementedException(),
                    "Default graphics layer used instead of #{0}", item.CurrentLayer);
            }
            if (Piece.LineTypeMapping.ContainsKey(item.CurrentLineType))
            {
                t = Piece.LineTypeMapping[item.CurrentLineType];
            }
            else
            {
                WarningListener.AddFormat(new NotImplementedException(),
                    "Default line type used instead of {0}", Enum.GetName(typeof(Piece.LineType), item.CurrentLineType));
            }      
            PcbGraphics g = new PcbGraphics(coordinateUnits, coordinateUnits, l, item.XY.Count - 1);
            for (int i = 1; i < item.XY.Count; i++)
            {
                g.Lines.Add(new PcbSilkLine(item.XY[i - 1], item.XY[i], coordinateUnits, coordinateUnits, item.Width, t));
            }
            return g;
        }

        private static PadStack FindSuitablePadstack(Partdecal decal, Terminal item, PadStack defaultStack)
        {
            PadStack padstack;
            if (PrioritizePadstackBindingByIndex)
            {
                string i = item.InternalIndex.ToString();
                padstack = decal.PadStacks.FirstOrDefault(x => x.PinDesignator == i);
            }
            else
            {
                padstack = decal.PadStacks.FirstOrDefault(x => x.PinDesignator == item.PinDesignator);
            }
            if (padstack == null)
            {
                if (PrioritizePadstackBindingByIndex)
                {
                    padstack = decal.PadStacks.FirstOrDefault(x => x.PinDesignator == item.PinDesignator);
                }
                else
                {
                    string i = item.InternalIndex.ToString();
                    padstack = decal.PadStacks.FirstOrDefault(x => x.PinDesignator == i);
                }
                if (padstack == null) padstack = defaultStack;
            }
            return padstack;
        }
        private static void ProcessDecalPads(PcbDesign res, PcbUnits coordinateUnits, Partdecal decal)
        {
            //Terminals + padstacks = pads
            PadStack defaultStack = decal.PadStacks.FirstOrDefault(x => x.PinDesignator == PadStack.AllTerminalsDesignator);
            res.Pads.Capacity += decal.Terminals.Count;
            foreach (var item in decal.Terminals)
            {
                var padstack = FindSuitablePadstack(decal, item, defaultStack);
                if (padstack == null)
                {
                    WarningListener.Add(new ArgumentException(), "Can't find a suitable padstack for the terminal #" + item.PinDesignator);
                    continue;
                }
                var drillLine = padstack.StackLines.FirstOrDefault(x => x.Arguments[(int)PadStack.StackLine.DependentArguments.DrillSize].Present);
                if (drillLine != null)
                {
                    //Detect drill-only holes
                    double d = (double)drillLine.Arguments[(int)PadStack.StackLine.DependentArguments.DrillSize].Value;
                    if (d >= drillLine.Size)
                    {
                        res.Pads.Add(new PcbPad(item.PinDesignator, coordinateUnits, item.X, item.Y, PcbLayer.Drill, 
                            new PcbPadStyle(PcbPadShape.CircularTH, coordinateUnits, d, 0, d)));
                        continue;
                    }
                }
                List<PadStack.StackLine> usefulLines = 
                    padstack.StackLines.Where(x => (x.Size != 0 || x.Arguments.Any(y => y.Present))).ToList();
                if (usefulLines.Count == 3)   //Recognize layer "ALL"
                {
                    //PADS requires minimum of 3 layers specified, 0x0 size indicates that the pad is not actually present
                    //If some stacklines have 0 size and no useful arguments (shape does not count), then we effectively have less stacklines
                    if (usefulLines.Select(x => x.Layer).Union(PadStack.AllLayerPattern).Count() == 3)
                    {
                        //Find layer that contains the most arguments (drill for example appears only for component-side layer)
                        int maxArgs = 0;
                        for (int i = 1; i < usefulLines.Count; i++)
                        {
                            if (usefulLines[maxArgs].Arguments.Count(x => x.Present) <
                                usefulLines[i].Arguments.Count(x => x.Present)) maxArgs = i;
                        }
                        res.Pads.Add(new PcbPad(item.PinDesignator, coordinateUnits, item.X, item.Y, PcbLayer.All,
                            DeterminePadStyle(usefulLines[maxArgs], coordinateUnits)));
                        continue;
                    }
                }
                //If this is not a special case, parse stacklines one-by-one
                foreach (var line in usefulLines)
                {
                    PcbPadStyle style = DeterminePadStyle(line, coordinateUnits);
                    PcbLayer layer = DeterminePcbLayer(line);
                    res.Pads.Add(new PcbPad(item.PinDesignator, coordinateUnits, item.X, item.Y, layer, style));
                    //-1 stands for both "Internal layers", but we can return only a single layer, so fix it afterwards
                    if (layer == PcbLayer.InternalBottom)
                    {
                        res.Pads.Add(new PcbPad(item.PinDesignator, coordinateUnits, item.X, item.Y,
                            PcbLayer.InternalTop, style));
                    }
                }
            }
        }
        private static PcbPadStyle DeterminePadStyle(PadStack.StackLine line, PcbUnits coordinateUnits)
        {
            PcbPadStyle style;
            bool drilled = line.Arguments[(int)PadStack.StackLine.DependentArguments.DrillSize].Present;
            switch (line.CurrentShape)
            {
                case PadStack.Shape.Round: //"Round" pads with drill argument are used for drilled pads (not annular pads!)
                    style = new PcbPadStyle(drilled ? PcbPadShape.CircularTH : PcbPadShape.CircularSMT,
                        coordinateUnits, line.Size, 0,
                        drilled ? (double)line.Arguments[(int)PadStack.StackLine.DependentArguments.DrillSize].Value : 0);
                    break;
                case PadStack.Shape.Square:
                    style = new PcbPadStyle(drilled ? PcbPadShape.RectangularTH : PcbPadShape.RectangularSMT, 
                        coordinateUnits, line.Size, line.Size,
                        drilled ? (double)line.Arguments[(int)PadStack.StackLine.DependentArguments.DrillSize].Value : 0);
                    break;
                case PadStack.Shape.Annular:
                    style = new PcbPadStyle(drilled ? PcbPadShape.CircularTH : PcbPadShape.CircularSMT,
                        coordinateUnits, line.Size,
                        (double)line.Arguments[(int)PadStack.StackLine.DependentArguments.InternalDiameter].Value,
                        drilled ? (double)line.Arguments[(int)PadStack.StackLine.DependentArguments.DrillSize].Value : 0);
                    break;
                case PadStack.Shape.RectangularFinger:
                    style = new PcbPadStyle(PcbPadShape.RectangularSMT, coordinateUnits, line.Size,
                        (double)line.Arguments[(int)PadStack.StackLine.DependentArguments.FingerLength].Value);
                    WarningListener.Add(new NotImplementedException("Only Length argument is supported for RectangularFinger pad style!"));
                    break;
                default:
                    throw new NotImplementedException(string.Format(
                        "Pad shape {0} ignored.", Enum.GetName(typeof(PadStack.Shape), line.CurrentShape)));
            }
            return style;
        }
        private static PcbLayer DeterminePcbLayer(PadStack.StackLine line)
        {
            PcbLayer layer;
            switch ((PadStack.SpecialLayers)line.Layer)
            {
                case PadStack.SpecialLayers.Top:
                    layer = PcbLayer.Top;
                    break;
                case PadStack.SpecialLayers.Inner:
                    layer = PcbLayer.InternalBottom;
                    break;
                case PadStack.SpecialLayers.Bottom:
                    layer = PcbLayer.Bottom;
                    break;
                default:
                    layer = (PcbLayer)(Enum.GetValues(typeof(PcbLayer)).Length + line.Layer - 1);
                    WarningListener.Add(new NotImplementedException(),
                        "Padstack non-special layer recognition is only partially implemented!");
                    break;
            }
            return layer;
        }

        #endregion


        private class Partdecal
        {
            private Partdecal(int pieces, int terminals, int stacks)
            {
                Terminals = new List<Terminal>(terminals);
                PadStacks = new List<PadStack>(stacks);
                Pieces = new List<Piece>(pieces);
            }
            private Partdecal(int pieces, int terminals, int stacks, string name, PcbUnits units)
                : this(pieces, terminals, stacks)
            {
                Name = name;
                Units = units;
            }

            /// <summary>
            /// Supported: Thou, Millimeter (i.e. imperial and metric) 
            /// </summary>
            public static readonly Dictionary<PcbUnits, string> PartdecalUnits = new Dictionary<PcbUnits, string>
            {
                { PcbUnits.Thou, "I" },
                { PcbUnits.Millimeter, "M" }
            };
            public enum Header
            {
                Name,
                Units,
                X,
                Y,
                Pieces,
                Terminals,
                Stacks,
                Text,
                Labels
            }

            public string Name { get; set; } = "N/A";
            public PcbUnits Units { get; set; }
            public List<Piece> Pieces { get; }
            public List<Terminal> Terminals { get; }
            public List<PadStack> PadStacks { get; }

            public static Partdecal Parse(string section)
            {
                if (section.Length == 0) return null;
                TextReader reader = new StringReader(section);
                //StringBuilder temp = new StringBuilder(section.Length); 
                //Header
                string[] split = reader.ReadLine().Split(' ');
                int pieces = int.Parse(split[(int)Header.Pieces]);
                int padstacks = int.Parse(split[(int)Header.Stacks]);
                int pinTerminals = int.Parse(split[(int)Header.Terminals]);
                Partdecal res = new Partdecal(pieces, pinTerminals, padstacks,
                    split[(int)Header.Name], PartdecalUnits.First(x => x.Value == split[(int)Header.Units]).Key);
                int headerEnded = section.IndexOf(LineSeparator) + 1;
                int j;
                //Pieces
                foreach (var item in Piece.TypeString)
                {
                    j = headerEnded;
                    for (int i = 0; i < pieces; i++)
                    {
                        j = section.IndexOf(item.Value, j);
                        if (j < 0) break;
                        try
                        {
                            res.Pieces.Add(Piece.Parse(section.Substring(j), typeof(Piece.PartdecalHeader)));
                        }
                        catch (Exception e)
                        {
                            WarningListener.Add(e);
                        }
                        finally
                        {
                            j += item.Value.Length + 1;
                        }
                    }
                }
                //padstacks
                j = headerEnded;
                for (int i = 0; i < padstacks; i++)
                {
                    j = section.IndexOf(PadStack.PadstackPrefix, j);
                    if (j < 0) break;
                    try
                    {
                        res.PadStacks.Add(PadStack.Parse(section.Substring(j)));
                    }
                    catch (Exception e)
                    {
                        WarningListener.Add(e);
                    }
                    finally
                    {
                        j += PadStack.PadstackPrefix.Length + 1;
                    }
                }
                //terminals
                j = headerEnded;
                for (int i = 0; i < pinTerminals; i++)
                {
                    j = section.IndexOf(Terminal.TerminalPrefix, j);
                    if (j < 0) break;
                    try
                    {
                        res.Terminals.Add(Terminal.Parse(section.Substring(j), i + 1));
                    }
                    catch (Exception e)
                    {
                        WarningListener.Add(e);
                    }
                    finally
                    {
                        j += Terminal.TerminalPrefix.Length;
                    }
                }
                return res;
            }
        }

        private class Piece
        {
            public Piece()
            {
                XY = new List<Point>();
            }

            public const int LayerAllNumber = 0;
            public enum LineHeader
            {
                Type,
                CoordinateLinesCount,
                Width,
                LineType,
                Layer
            }
            public enum PartdecalHeader
            {
                Type,
                CoordinateLinesCount,
                Width,
                Layer,
                PinCount
            }
            public enum PieceType
            {
                Open,
                Closed,
                Copper
            }
            public static readonly Dictionary<PieceType, string> TypeString = new Dictionary<PieceType, string>
            {
                { PieceType.Closed, "CLOSED" },
                { PieceType.Open, "OPEN" },
                { PieceType.Copper, "COPPER" }  //TODO: add and test copper traces support
            };
            public enum LineType
            {
                Solid,
                Dashed,
                Dotted,
                DashDotted,
                DashDoubleDotted
            }
            public static readonly Dictionary<int, PcbGraphicsLayer> GraphicsLayerMapping = new Dictionary<int, PcbGraphicsLayer>
            {
                { 27, PcbGraphicsLayer.BottomSilk },
                { 26, PcbGraphicsLayer.TopSilk },
                { 20, PcbGraphicsLayer.Other }
            };
            public static readonly Dictionary<LineType, PcbLineType> LineTypeMapping = new Dictionary<LineType, PcbLineType>
            {
                { LineType.Solid, PcbLineType.Solid },
                { LineType.Dashed, PcbLineType.Dashed },
                { LineType.Dotted, PcbLineType.Dotted }
            };

            public double Width { get; set; }
            public LineType CurrentLineType { get; set; }
            public PieceType CurrentType { get; set; }
            public int LayerCount { get; set; }
            public int PinCount { get; set; }
            public int CurrentLayer { get; set; }
            public List<Point> XY { get; }

            public static Piece Parse(string definition, Type headerEnumType)
            {
                Piece res = new Piece();
                TextReader reader = new StringReader(definition);
                //Header
                string[] split = reader.ReadLine().Split(' ');
                if (headerEnumType == typeof(PartdecalHeader))
                {
                    if (PieceHeaderCompatibilityMode)
                    {
                        res.XY.Capacity = int.Parse(split[(int)LineHeader.CoordinateLinesCount]);
                        res.CurrentType = TypeString.First(x => x.Value == split[(int)LineHeader.Type]).Key;
                        res.CurrentLayer = int.Parse(split[(int)LineHeader.Layer]);
                        res.Width = double.Parse(split[(int)LineHeader.Width], CultureInfo.InvariantCulture);
                        res.CurrentLineType = (LineType)int.Parse(split[(int)LineHeader.LineType]);
                    }
                    else
                    {
                        res.XY.Capacity = int.Parse(split[(int)PartdecalHeader.CoordinateLinesCount]);
                        res.CurrentType = TypeString.First(x => x.Value == split[(int)PartdecalHeader.Type]).Key;
                        res.CurrentLayer = int.Parse(split[(int)PartdecalHeader.Layer]);
                        res.PinCount = int.Parse(split[(int)PartdecalHeader.PinCount]);
                        res.Width = double.Parse(split[(int)PartdecalHeader.Width], CultureInfo.InvariantCulture);
                    }
                }
                else
                {
                    throw new NotImplementedException();
                }
                //Items
                int c = res.XY.Capacity;
                for (int i = 0; i < c; i++)
                {
                    split = reader.ReadLine().Split(' ');
                    res.XY.Add(new Point(
                        double.Parse(split[0], CultureInfo.InvariantCulture),
                        double.Parse(split[1], CultureInfo.InvariantCulture)));
                }
                return res;
            }
        }
        private class Terminal
        {
            public static readonly string TerminalPrefix = "\nT";

            public enum Arguments
            {
                PinX,
                PinY,
                NumberX,
                NumberY,
                PinDesignator
            }

            public Terminal(double x, double y, double numX, double numY, string designator, int index = -1)
            {
                X = x;
                Y = y;
                NumberX = numX;
                NumberY = numY;
                PinDesignator = designator;
                InternalIndex = index;
            }

            public double X { get; set; }
            public double Y { get; set; }
            public double NumberX { get; set; }
            public double NumberY { get; set; }
            public string PinDesignator { get; set; }
            /// <summary>
            /// Starts from 1!
            /// </summary>
            public int InternalIndex { get; set; }

            public static Terminal Parse(string definition, int index = -1)
            {
                TextReader reader = new StringReader(definition.Remove(0, TerminalPrefix.Length));
                string[] split = reader.ReadLine().Split(' ');
                return new Terminal(double.Parse(split[(int)Arguments.PinX], CultureInfo.InvariantCulture),
                    double.Parse(split[(int)Arguments.PinY], CultureInfo.InvariantCulture),
                    double.Parse(split[(int)Arguments.NumberX], CultureInfo.InvariantCulture),
                    double.Parse(split[(int)Arguments.NumberY], CultureInfo.InvariantCulture),
                    split[(int)Arguments.PinDesignator], index);
            }
        }
        private class PadStack
        {
            public PadStack()
            {
                StackLines = new List<StackLine>();
            }

            public static readonly string AllTerminalsDesignator = "0";
            public static readonly string PadstackPrefix = "PAD ";
            public static int[] AllLayerPattern
            {
                get
                {
                    if (FourLayerModel || LayerModelOverrideForALL)
                    {
                        return new int[] { -2, -1, 0 };
                    }
                    try
                    {
                        return LargeCustomLayerModel;
                    }
                    catch (NullReferenceException)
                    {
                        throw new ArgumentException("Custom layer model was activated, but not provided.");
                    }
                }
            }
            public enum SpecialLayers
            {
                Top = -2,
                Inner,
                Bottom
            }
            public enum Header
            {
                PadstackPrefix,
                PinNumber,
                StackLines
            }
            public enum Shape
            {
                Round,
                Square,
                Annular,
                Odd,
                OvalFinger,
                RectangularFinger
            }
            public static readonly Dictionary<Shape, string> ShapeString = new Dictionary<Shape, string>
            {
                { Shape.Annular, "A" },
                { Shape.Odd, "O" },
                { Shape.OvalFinger, "OF" },
                { Shape.RectangularFinger, "RF" },
                { Shape.Round, "R" },
                { Shape.Square, "S" }
            };
            /*public static readonly Dictionary<string, bool> PlatingValue = new Dictionary<string, bool>
            {
                { "P", true },
                { "N", false }
            };*/

            public struct DependentArgument
            {
                public DependentArgument(Types type, Dictionary<Shape, int> dic, object val = null)
                {
                    Type = type;
                    Indexes = dic;
                    Value = val;
                    Present = false;
                }

                public enum Types
                {
                    Integer,
                    Double,
                    Plating
                }

                public Types Type { get; set; }
                public Dictionary<Shape, int> Indexes { get; }
                public object Value { get; set; }
                public bool Present { get; private set; }

                public void Parse(Shape shape, string args)
                {
                    try
                    {
                        args = args.Split(' ')[Indexes[shape]];
                        switch (Type)
                        {
                            case Types.Integer:
                                Value = int.Parse(args);
                                break;
                            case Types.Double:
                                Value = double.Parse(args, CultureInfo.InvariantCulture);
                                break;
                            case Types.Plating:
                                Value = args;
                                break;
                            default:
                                throw new ArgumentException("Unsupported DependentArgument type.");
                        }
                        Present = true;
                    }
                    catch (IndexOutOfRangeException)
                    { }
                    catch (KeyNotFoundException)
                    { }
                }
            }

            public string PinDesignator { get; set; }
            public List<StackLine> StackLines { get; private set; }
            public StackLine this[int i]
            {
                get
                {
                    return StackLines[i];
                }
            }

            public class StackLine
            {
                public enum Beginning
                {
                    Layer,
                    Size,
                    Shape
                }
                public int Layer;
                public double Size;
                public Shape CurrentShape;

                public enum DependentArguments
                {
                    InternalDiameter,
                    FingerRotation,
                    FingerLength,
                    FingerOffset,
                    CornerRadius,
                    DrillSize
                    //Plated
                }
                public DependentArgument[] Arguments = new DependentArgument[]
                {
                    new DependentArgument(DependentArgument.Types.Double,
                        new Dictionary<Shape, int>
                        {
                            { Shape.Annular, 3 }
                        }),
                    new DependentArgument(DependentArgument.Types.Double,
                        new Dictionary<Shape, int>
                        {
                            { Shape.OvalFinger, 3 },
                            { Shape.RectangularFinger, 3 }
                        }),
                    new DependentArgument(DependentArgument.Types.Double,
                        new Dictionary<Shape, int>
                        {
                            { Shape.OvalFinger, 4 },
                            { Shape.RectangularFinger, 4 }
                        }),
                    new DependentArgument(DependentArgument.Types.Double,
                        new Dictionary<Shape, int>
                        {
                            { Shape.OvalFinger, 5 },
                            { Shape.RectangularFinger, 5 }
                        }),
                    new DependentArgument(DependentArgument.Types.Double,
                        new Dictionary<Shape, int>
                        {
                            { Shape.Square, 3 },
                            { Shape.RectangularFinger, 6 }
                        }),
                    new DependentArgument(DependentArgument.Types.Double,
                        new Dictionary<Shape, int>
                        {
                            { Shape.Round, 3 },
                            { Shape.Annular, 4 },
                            { Shape.Square, 4 },
                            { Shape.RectangularFinger, 7 }
                        })
                };

                public static StackLine Parse(string line)
                {
                    StackLine res = new StackLine();
                    //Common part
                    string[] split = line.Split(' ');
                    res.Layer = int.Parse(split[(int)Beginning.Layer]);
                    res.Size = double.Parse(split[(int)Beginning.Size], CultureInfo.InvariantCulture);
                    res.CurrentShape = ShapeString.First(x => x.Value == split[(int)Beginning.Shape]).Key;
                    //Varying part
                    //res.Arguments = res.Arguments.Where(x => x.Indexes.ContainsKey(res.CurrentShape)).ToArray();
                    for (int i = 0; i < res.Arguments.Length; i++)
                    {
                        res.Arguments[i].Parse(res.CurrentShape, line);
                    }
                    return res;
                }
            }

            public static PadStack Parse(string definition)
            {
                TextReader reader = new StringReader(definition);
                //Header
                string[] split = reader.ReadLine().Split(' ');
                PadStack res = new PadStack();
                res.PinDesignator = split[(int)Header.PinNumber];
                int lineCount = int.Parse(split[(int)Header.StackLines]);
                //Stacklines
                res.StackLines.Capacity += lineCount;
                for (int i = 0; i < lineCount; i++)
                {
                    res.StackLines.Add(StackLine.Parse(reader.ReadLine()));
                }
                return res;
            }
        }
    }
}
