using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading;
using System.Globalization;

namespace ARESFileConverter
{
    public enum PcbUnits
    {
        Thou,
        Millimeter,
        TenNanometers,
        Inch,
        TwoThirdsOfNanometer,
        Arbitrary //For styles etc
    }
    public enum PcbLayer
    {
        Drill = -1,
        All,
        Bottom,
        InternalBottom,
        InternalTop,
        Top
    }
    public enum PcbGraphicsLayer
    {
        BottomSilk,
        TopSilk,
        Boundary,
        Other
    }
    public enum PcbPadShape
    {
        CircularTH,
        RectangularTH,
        RectangularSMT,
        CircularSMT
    }
    public enum PcbLineType
    {
        Solid,
        Dashed,
        Dotted
    }

    public struct Point
    {
        public Point(double x, double y)
        {
            X = x;
            Y = y;
        }

        public double X { get; }
        public double Y { get; }
    }

    public class PcbPadStyle
    {
        public PcbPadStyle(PcbPadShape shape, PcbUnits units, double dim1, double dim2, double drill = 0)
        {
            Shape = shape;
            Units = units;
            Dimension1 = dim1;
            Dimension2 = dim2;
            Drill = drill;
        }

        public PcbPadShape Shape { get; set; }
        public PcbUnits Units { get; private set; }
        /// <summary>
        /// "Outer" dimension, also width for rectangular pads (currently for drill-only hole dim1=drill, dim2=0)
        /// </summary>
        public double Dimension1 { get; set; }
        /// <summary>
        /// "Inner" dimension, also height for rectangular pads (currently for drill-only hole dim1=drill, dim2=0)
        /// </summary>
        public double Dimension2 { get; set; }
        /// <summary>
        /// Drill hole diameter (currently for drill-only hole dim1=drill, dim2=0)
        /// </summary>
        public double Drill { get; set; } 

        public void SetUnits(PcbUnits units)
        {
            double mult = PcbDesign.GetUnitConversionMultiplier(Units) / PcbDesign.GetUnitConversionMultiplier(units);
            Dimension1 *= mult;
            Dimension2 *= mult;
            Drill *= mult;
            Units = units;
        }
    }
    public class PcbSilkLine
    {
        public PcbSilkLine(Point start, Point end, PcbUnits units, PcbUnits coordinateUnits, double thickness,
            PcbLineType type = PcbLineType.Solid)
        {
            Start = start;
            End = end;
            Units = units;
            CoordinateUnits = coordinateUnits;
            Thickness = thickness;
            Type = type;
        }

        public Point Start { get; set; }
        public Point End { get; set; }
        public PcbUnits Units { get; private set; }
        public PcbUnits CoordinateUnits { get; private set; }
        public double Thickness { get; set; }
        public PcbLineType Type { get; set; }

        public void SetUnits(PcbUnits units)
        {
            double mult = PcbDesign.GetUnitConversionMultiplier(Units) / PcbDesign.GetUnitConversionMultiplier(units);
            Thickness *= mult;
            Units = units;
        }
        public void SetCoordinateUnits(PcbUnits units)
        {
            double mult = PcbDesign.GetUnitConversionMultiplier(CoordinateUnits) / PcbDesign.GetUnitConversionMultiplier(units);
            Start = new Point(Start.X * mult, Start.Y * mult);
            End = new Point(End.X * mult, End.Y * mult);
            CoordinateUnits = units;
        }
    }
    public class PcbGraphics : IPcbObject
    {
        public PcbGraphics(PcbUnits units, PcbUnits coordinateUnits, PcbGraphicsLayer layer, int capacity = 4)
        {
            Lines = new List<PcbSilkLine>(capacity);
            Units = units;
            CoordinateUnits = coordinateUnits;
            Layer = layer;
        }

        public List<PcbSilkLine> Lines { get; }
        public PcbUnits Units { get; private set; }
        public PcbUnits CoordinateUnits { get; private set; }
        public PcbGraphicsLayer Layer { get; set; }

        public void SynchronizeUnits(PcbUnits units)
        {
            foreach (var item in Lines)
            {
                if (item.Units != units) item.SetUnits(units);
            }
            Units = units;
        }
        public void SynchronizeCoordinateUnits(PcbUnits units)
        {
            foreach (var item in Lines)
            {
                if (item.CoordinateUnits != units) item.SetCoordinateUnits(units);
            }
            CoordinateUnits = units;
        }
    }
    public class PcbTrace : IPcbObject
    {
        public PcbTrace(PcbLayer layer, double thickness, PcbUnits units, PcbUnits coordinateUnits, List<Point> points)
        {
            XY = points;
            Layer = layer;
            Units = units;
            CoordinateUnits = coordinateUnits;
            Thickness = thickness;
        }
        public PcbTrace(PcbLayer layer, double thickness, PcbUnits units, PcbUnits coordinateUnits, int capacity = 4)
            : this(layer, thickness, units, coordinateUnits, new List<Point>(capacity))
        { }

        public int Segments
        {
            get { return XY.Count; }
        }
        /// <summary>
        /// (X,Y) defining pairs
        /// </summary>
        public List<Point> XY { get; private set; }
        public PcbLayer Layer { get; set; }
        public double Thickness { get; set; }
        /// <summary>
        /// For dimensions
        /// </summary>
        public PcbUnits Units { get; private set; }
        /// <summary>
        /// For location
        /// </summary>
        public PcbUnits CoordinateUnits { get; private set; }

        /// <summary>
        /// For easy modification inside foreach
        /// </summary>
        /// <param name="units">Target units</param>
        public void SetCoordinateUnits(PcbUnits units)
        {
            double mult = PcbDesign.GetUnitConversionMultiplier(CoordinateUnits) / PcbDesign.GetUnitConversionMultiplier(units);
            XY = XY.Select(x => new Point(x.X * mult, x.Y * mult)).ToList();
            CoordinateUnits = units;
        }

        /// <summary>
        /// Affects thickness
        /// </summary>
        /// <param name="units">Target units</param>
        public void SetUnits(PcbUnits units)
        {
            Thickness *= PcbDesign.GetUnitConversionMultiplier(Units) / PcbDesign.GetUnitConversionMultiplier(units);
            Units = units;
        }
    }
    public class PcbPad : IPcbObject
    {
        public PcbPad(string number, PcbUnits coordinateUnits, double x, double y,
            PcbLayer layer, PcbPadStyle style, string flags = null)
        {
            Number = number;
            CoordinateUnits = coordinateUnits;
            X = x;
            Y = y;
            Layer = layer;
            Style = style;
            Flags = flags;
        }

        public string Number { get; set; }
        /// <summary>
        /// For dimensions
        /// </summary>
        public PcbUnits Units
        {
            get { return Style.Units; }
        }
        /// <summary>
        /// For location
        /// </summary>
        public PcbUnits CoordinateUnits { get; private set; }
        public double X { get; set; }
        public double Y { get; set; }
        /// <summary>
        /// Only Top, Bottom or All (other values don't make sense)
        /// </summary>
        public PcbLayer Layer { get; set; }
        public PcbPadStyle Style { get; set; }
        public string Flags { get; set; }

        /// <summary>
        /// For easy modification inside foreach
        /// </summary>
        /// <param name="units">Target units</param>
        public void SetCoordinateUnits(PcbUnits units)
        {
            double mult = PcbDesign.GetUnitConversionMultiplier(CoordinateUnits) / PcbDesign.GetUnitConversionMultiplier(units);
            X *= mult;
            Y *= mult;
            CoordinateUnits = units;
        }

        /// <summary>
        /// Affects dimensions inside PcbPadStyle
        /// </summary>
        /// <param name="units">Target units</param>
        public void SetUnits(PcbUnits units)
        {
            Style.SetUnits(units);
        }
    }

    public interface IPcbObject
    { }
    public class PcbDesign
    {
        public PcbDesign()
        {
            Pads = new List<PcbPad>();
            Traces = new List<PcbTrace>();
            Graphics = new List<PcbGraphics>();
        }

        public List<PcbGraphics> Graphics { get; }
        public List<PcbPad> Pads { get; }
        public List<PcbTrace> Traces { get; }
        /// <summary>
        /// May differ from units of pads and traces if the latter have to be converted
        /// </summary>
        public PcbUnits CoordinateUnits { get; private set; }

        /// <summary>
        /// Gets all mentioned layers.
        /// </summary>
        /// <param name="tracesOnly">True = layers specified for traces only; False = layers for both pads and traces.</param>
        /// <returns></returns>
        public List<PcbLayer> GetLayers(bool tracesOnly = false)
        {
            List<PcbLayer> res = new List<PcbLayer>(Enum.GetNames(typeof(PcbLayer)).Length);
            if (!tracesOnly)
            {
                foreach (var item in Pads)
                {
                    if (!res.Contains(item.Layer)) res.Add(item.Layer);
                }
            }
            foreach (var item in Traces)
            {
                if (!res.Contains(item.Layer)) res.Add(item.Layer);
            }
            return res;
        }
        /// <summary>
        /// Gets objects that belong to the specified layer.
        /// </summary>
        /// <param name="layer"></param>
        /// <param name="tracesOnly">True = get only traces; False = get everything.</param>
        /// <returns></returns>
        public List<IPcbObject> GetObjectsFromLayer(PcbLayer layer, bool tracesOnly = false)
        {
            List<IPcbObject> res = new List<IPcbObject>(Pads.Count + Traces.Count);
            if (!tracesOnly)
            {
                foreach (var item in Pads)
                {
                    if (item.Layer == layer) res.Add(item);
                }
            }
            foreach (var item in Traces)
            {
                if (item.Layer == layer) res.Add(item);
            }
            return res;
        }

        /// <summary>
        /// Converts CoordinateUnits of objects to the units of the design.
        /// </summary>
        public void SynchronizeCoordinateUnits(PcbUnits units)
        {
            foreach (var item in Pads)
            {
                if (item.CoordinateUnits != units) item.SetCoordinateUnits(units);
            }
            foreach (var item in Traces)
            {
                if (item.CoordinateUnits != units) item.SetCoordinateUnits(units);
            }
            foreach (var item in Graphics)
            {
                item.SynchronizeCoordinateUnits(units);
            }
            CoordinateUnits = units;
        }

        public void SynchronizeUnits(PcbUnits units)
        {
            foreach (var item in Pads)
            {
                if (item.Units != units) item.SetUnits(units);
            }
            foreach (var item in Traces)
            {
                if (item.Units != units) item.SetUnits(units);
            }
            foreach (var item in Graphics)
            {
                item.SynchronizeUnits(units);
            }
        }

        /// <summary>
        /// Intended only for internal use
        /// </summary>
        /// <param name="from">Coordinate units</param>
        /// <returns>Multiplier that converts specified unit into 0.1nm-s (angstroms)</returns>
        public static double GetUnitConversionMultiplier(PcbUnits from)
        {
            switch (from)
            {
                case PcbUnits.Thou:
                    return 254000;
                case PcbUnits.Millimeter:
                    return 10000000;
                case PcbUnits.TenNanometers:
                    return 100;
                case PcbUnits.Inch:
                    return 254000000;
                case PcbUnits.TwoThirdsOfNanometer:
                    return 20 / 3;
                default:
                    throw new ArgumentException("Bad coordinate units.");
            }
        }
    }
}
