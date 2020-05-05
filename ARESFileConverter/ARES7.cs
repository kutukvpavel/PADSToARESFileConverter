using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading;
using System.Globalization;

namespace ARESFileConverter
{
    public class ARES7
    {
        //Singleton. The first case where I found it being necessary: this[] keywords won't let me make the class static.
        private static readonly ARES7 _Instance = new ARES7();
        private ARES7() { }
        public static ARES7 Instance
        {
            get { return _Instance; }
        }

        /// <summary>
        /// The ARES Region file begins with this line (\r\n is included in all of the string constants where it's needed).
        /// </summary>
        public static readonly string Signature = @"ARES REGION FILE

";

        /// <summary>
        /// {0} = units (10nm usually)
        /// </summary>
        public static readonly string Header = @"*HEADER
VERSION 710 600
UNITS {0}

";

        public static readonly string StandardPadFlags = "0 1";
        /// <summary>
        /// {0} = Number, {1} = Style (e.g. C-60-40, where C=Circular or 15X17 for rectangular),
        /// {2} = Layer, {3,4} = X,Y, {5} = Flags
        /// </summary>
        public static readonly string PadInObjects = "PAD \"{0}\" \"{1}\" {2} {3} {4} {5}" + Environment.NewLine;
        /// <summary>
        /// {0} = contents
        /// </summary>
        public static readonly string Vias = @"*VIAS
{0}
*END_VIAS

";

        /// <summary>
        /// {0} = contents
        /// </summary>
        public static readonly string Objects = @"*OBJECTS
{0}
*END_OBJECTS

";

        /// <summary>
        /// {0} = Name (TOP, BOTTOM, I# where # is internal layer number, DRL, ALL), {1} = contents
        /// </summary>
        public static readonly string Layer = @"*LAYER {0}
{1}
*END_LAYER

";

        /// <summary>
        /// {0} = thickness, {1} = Number of segments (XY pair number - 1), {2} = XY pairs separated with spaces
        /// </summary>
        public static readonly string TraceInLayer = "\"T{0:F0}\" S {1} {2}" + Environment.NewLine;

        /// <summary>
        /// {0} = X or outer dimension (overall diameter etc), {1} = Y or hole diameter
        /// </summary>
        /// <param name="shape">Supported: CircularTH, RectangularSMT, SquareTH.</param>
        /// <returns>Format string</returns>
        public string this[PcbPadShape shape]
        {
            get
            {
                switch (shape)
                {
                    case PcbPadShape.CircularTH:
                        return "C-{0:F0}-{1:F0}";
                    case PcbPadShape.RectangularSMT:
                        return "{0:F0}X{1:F0}";
                    case PcbPadShape.RectangularTH:
                        return "S-{0:F0}-{1:F0}";
                    case PcbPadShape.CircularSMT:
                        return "CSMT-{0:F0}";
                    default:
                        throw new ArgumentException("Bad pad shape.");
                }
            }
        }

        /// <summary>
        /// Get layer name
        /// </summary>
        /// <param name="layer">Supported: Drill, Bottom, Top, All, InternalTop, InternalBottom</param>
        /// <returns>Layer name</returns>
        public string this[PcbLayer layer]
        {
            get
            {
                switch (layer)
                {
                    case PcbLayer.Drill:
                        return "DRL";
                    case PcbLayer.Bottom:
                        return "BOT";
                    case PcbLayer.Top:
                        return "TOP";
                    case PcbLayer.InternalTop:
                        return "I1";
                    case PcbLayer.InternalBottom:
                        return "I2";
                    case PcbLayer.All:
                        return "ALL";
                    default:
                        throw new ArgumentException("Bad layer.");
                }
            }
        }

        /// <summary>
        /// Get PCB (coordinate) units name
        /// </summary>
        /// <param name="units">Supported: Thou, Millimeter, TenNanometers, Inch</param>
        /// <returns>Name string</returns>
        public string this[PcbUnits units]
        {
            get
            {
                switch (units)
                {
                    case PcbUnits.Thou:
                        return "1th";
                    case PcbUnits.Millimeter:
                        return "1mm";
                    case PcbUnits.TenNanometers:
                        return "10nm";
                    case PcbUnits.Inch:
                        return "1in";
                    default:
                        throw new ArgumentException("Bad units.");
                }
            }
        }

        public string this[PcbGraphicsLayer layer]
        {
            get
            {
                switch (layer)
                {
                    case PcbGraphicsLayer.BottomSilk:
                        return "BS";
                    case PcbGraphicsLayer.TopSilk:
                        return "TS";
                    default:
                        throw new NotImplementedException(string.Format(
                            "Graphics layer {0} is not supported", Enum.GetName(typeof(PcbGraphicsLayer), layer)));
                }
            }
        }

        /// <summary>
        /// {0} = layer, {1} = X1, {2} = Y1, ...
        /// </summary>
        public static readonly string GraphicsLineInObjects = @"GRAPHIC {0} LINE 4 {1} {2} {3} {4}" + Environment.NewLine;

        /// <summary>
        /// Write *.RGN file contents into a string
        /// </summary>
        /// <param name="design"></param>
        /// <returns>String ready to be written into a file</returns>
        public string Write(PcbDesign design)
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            design.SynchronizeUnits(PcbUnits.Thou);
            design.SynchronizeCoordinateUnits(PcbUnits.TenNanometers);
            StringBuilder res = new StringBuilder(Signature);
            res.AppendFormat(Header, this[design.CoordinateUnits]);
            StringBuilder temp = new StringBuilder();
            //Objects (pads and graphics)
            foreach (var item in design.Pads)
            {
                try
                {
                    temp.AppendFormat(PadInObjects, item.Number, 
                        string.Format(this[item.Style.Shape],
                        item.Style.Dimension1 + (item.Layer == PcbLayer.Drill ? 10 : 0),     //10 thou ring for drill holes (ARES7 does not support Dim1=Dim2)
                        item.Style.Drill > 0 ? item.Style.Drill : item.Style.Dimension2),
                        this[item.Layer], item.X, item.Y, item.Flags ?? StandardPadFlags);
                }
                catch (NotImplementedException e)
                {
                    WarningListener.Add(e);
                }
            }  
            foreach (var item in design.Graphics)
            {
                foreach (var line in item.Lines)
                {
                    try
                    {
                        temp.AppendFormat(GraphicsLineInObjects, this[item.Layer], line.Start.X, line.Start.Y, line.End.X, line.End.Y);
                    }
                    catch (NotImplementedException e)
                    {
                        WarningListener.Add(e);
                    }
                }
            }
            res.AppendFormat(Objects, temp.ToString().TrimEnd(Environment.NewLine.ToCharArray()));
            temp.Clear();
            //Vias
            res.AppendFormat(Vias, "");
            //Layers and traces
            var layers = design.GetLayers(true);
            foreach (var item in layers)
            {
                var objects = design.GetObjectsFromLayer(item, true).Cast<PcbTrace>();
                foreach (var trace in objects)
                {
                    temp.AppendFormat(TraceInLayer, trace.Thickness, trace.Segments,
                        string.Join(" ", trace.XY.ToList().Select(x => string.Format("{0} {1}", x.X, x.Y))));
                }
                res.AppendFormat(Layer, this[item], temp.ToString().TrimEnd(Environment.NewLine.ToCharArray()));
            }

            return res.ToString();
        }
    }
}
