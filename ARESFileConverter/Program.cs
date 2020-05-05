/*
 * PADS ASCII to ARES7 Region File converter
 * Kutukov Pavel, 2020
 * 
 * TODO:
 * - Add more complicated graphics (circle etc) conversion capabilities
 * - Add/test copper traces support
 * - Implement command line interface
 */

#define DEBUG

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading;
using System.Globalization;

namespace ARESFileConverter
{
    public enum EdaToolFormat
    {
        PADS_ASCII_PowerPcb,
        RegionFile_ARES7
    }
    
    public static class WarningListener
    {
        private static List<Exception> Warnings = new List<Exception>();

        public static bool Any { get { return Warnings.Any(); } }

        public static void Add(Exception e, string m = "")
        {
            e = new Exception(m, e);
            Warnings.Add(e);
        }
        public static void AddFormat(Exception e, string f, params object[] args)
        {
            e = new Exception(string.Format(f, args), e);
            Warnings.Add(e);
        }
        public static void Clear()
        {
            Warnings.Clear();
        }
        public static new string ToString()
        {
            StringBuilder b = new StringBuilder();
            foreach (var item in Warnings)
            {
                if (item.Message.Length > 0)
                {
                    b.AppendFormat("{0}: {1}" + Environment.NewLine, item.InnerException.GetType().ToString(), item.Message);
                }
                else
                {
                    if (item.InnerException.Message.Length > 0)
                    {
                        b.AppendFormat("{0}: {1}" + Environment.NewLine,
                            item.InnerException.GetType().ToString(), item.InnerException.Message);
                    }
                    else
                    {
                        b.AppendLine(item.InnerException.GetType().ToString());
                    }
                }
            }
            return b.ToString();
        }
    }

    public class Program
    {
        public static string Convert(string original, EdaToolFormat source, EdaToolFormat destination)
        {
            PcbDesign des = null;
            string res = null;
            switch (source)
            {
                case EdaToolFormat.PADS_ASCII_PowerPcb:
                    des = PADS.Parse(original);
                    break;
                default:
                    Console.WriteLine("Unsupported source EDA tool/format.");
                    break;
            }
            if (des != null)
            {
                switch (destination)
                {
                    case EdaToolFormat.RegionFile_ARES7:
                        res = ARES7.Instance.Write(des);
                        break;
                    default:
                        Console.WriteLine("Unsupported target EDA tool/format.");
                        break;
                }
            }
            else
            {
                Console.WriteLine("PCB design wasn't parsed properly.");
            }
            return res;
        }

        static void Main(string[] args)
        {
            PADS.PieceHeaderCompatibilityMode = true;
#if DEBUG
            args = new string[] { @"E:\cr.asc", @"E:\cr.RGN" };
#endif
            string output = Convert(File.ReadAllText(args[0]), EdaToolFormat.PADS_ASCII_PowerPcb, EdaToolFormat.RegionFile_ARES7);
            if (args.Length > 1)
            {
                File.WriteAllText(args[1], output);
            }
            if (WarningListener.Any)
            {
                Console.WriteLine("Warnings:");
                Console.WriteLine(WarningListener.ToString());
            }
            Console.WriteLine("Done.");
            Console.ReadKey();
        }
    }
}
