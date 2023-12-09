﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Drawing;
using Shapoco.Calctus.Model.Standards;
using Shapoco.Calctus.Model.Types;
using Shapoco.Calctus.Model.Mathematics;
using Shapoco.Calctus.Model.Parsers;
using Shapoco.Calctus.Model.Evaluations;

namespace Shapoco.Calctus.Model {
    class FuncDef {
        public static readonly Regex PrototypePattern = new Regex(@"^(?<name>\w+)\((?<args>\w+\*?(, *\w+\*?)*((\[\])?\.\.\.)?)?\)$");

        private static readonly Random rng = new Random((int)DateTime.Now.Ticks);

        public readonly string Name;
        public readonly ArgDef[] ArgDefs;
        public Func<EvalContext, Val[], Val> Method { get; protected set; }
        public VariadicAragumentMode VariadicArgMode;
        public int VectorizableArgIndex;
        public readonly string Description;

        public FuncDef(string name, ArgDef[] argDefs, Func<EvalContext, Val[], Val> method, VariadicAragumentMode variadic, int vecArgIndex, string desc) {
            this.Name = name;
            this.ArgDefs = argDefs;
            this.Method = method;
            this.VariadicArgMode = variadic;
            this.VectorizableArgIndex = vecArgIndex;
            this.Description = desc;
        }

        public FuncDef(string prototype, Func<EvalContext, Val[], Val> method, string desc) {
            var m = PrototypePattern.Match(prototype);
            if (!m.Success) throw new CalctusError("Invalid function prototype: \"" + prototype + "\"");
            var args = new List<ArgDef>();
            VariadicAragumentMode variadicArgMode = VariadicAragumentMode.None;
            var vecArgIndex = -1;
            if (m.Groups["args"].Success) {
                var argsStr = m.Groups["args"].Value;
                if (argsStr.EndsWith("[]...")) {
                    variadicArgMode = VariadicAragumentMode.Array;
                    argsStr = argsStr.Substring(0, argsStr.Length - 5);
                }
                else if (argsStr.EndsWith("...")) {
                    variadicArgMode = VariadicAragumentMode.Flatten;
                    argsStr = argsStr.Substring(0, argsStr.Length - 3);
                }
                var caps = argsStr.Split(',').Select(p => p.Trim()).ToArray();
                for (int i = 0; i < caps.Length; i++) {
                    var cap = caps[i];
                    if (cap.EndsWith("*")) {
                        if (vecArgIndex >= 0) throw new CalctusError("Only one argument is vectorizable.");
                        if (variadicArgMode != VariadicAragumentMode.None) throw new CalctusError("Variadic argument and vectorizable argument cannot coexist.");
                        vecArgIndex = i;
                        args.Add(new ArgDef(cap.Substring(0, cap.Length - 1)));
                    }
                    else {
                        args.Add(new ArgDef(cap));
                    }
                }
            }
            this.Name = m.Groups["name"].Value;
            this.ArgDefs = args.ToArray();
            this.Method = method;
            this.VariadicArgMode = variadicArgMode;
            this.VectorizableArgIndex = vecArgIndex;
            this.Description = desc;
        }

        public Val Call(EvalContext e, Val[] args) {
            if (VariadicArgMode == VariadicAragumentMode.Flatten) {
                // 可変長引数はフラットに展開する
                if (args.Length < ArgDefs.Length - 1) {
                    throw new CalctusError("Too few arguments");
                }
                else if (ArgDefs.Length == args.Length && args[args.Length - 1] is ArrayVal extVals) {
                    // 可変長引数部分が1個の配列の場合はフラットに展開する
                    var tempArgs = new Val[this.ArgDefs.Length - 1 + extVals.Length];
                    Array.Copy(args, tempArgs, args.Length - 1);
                    Array.Copy((Val[])extVals.Raw, 0, tempArgs, this.ArgDefs.Length - 1, extVals.Length);
                    return Method(e, tempArgs);
                }
                else {
                    // 上記以外はそのまま
                    return Method(e, args);
                }
            }
            else if (VariadicArgMode == VariadicAragumentMode.Array) {
                // 可変長引数は配列にまとめる
                if (args.Length < ArgDefs.Length - 1) {
                    throw new CalctusError("Too few arguments");
                }
                else if (args.Length == ArgDefs.Length - 1) {
                    // 可変長配列部分が空配列
                    var tempArgs = new Val[this.ArgDefs.Length];
                    Array.Copy(args, tempArgs, ArgDefs.Length - 1);
                    tempArgs[this.ArgDefs.Length - 1] = new ArrayVal(new Val[0]);
                    return Method(e, tempArgs);
                }
                else if (ArgDefs.Length == args.Length && args[args.Length - 1] is ArrayVal) {
                    // 配列で渡された場合はそのまま
                    return Method(e, args);
                }
                else {
                    // 可変長配列部分を配列にまとめる
                    var tempArgs = new Val[this.ArgDefs.Length];
                    Array.Copy(args, tempArgs, ArgDefs.Length - 1);
                    var array = new Val[args.Length - ArgDefs.Length + 1];
                    Array.Copy(args, ArgDefs.Length - 1, array, 0, array.Length);
                    tempArgs[ArgDefs.Length - 1] = new ArrayVal(array);
                    return Method(e, tempArgs);
                }
            }
            else if (VectorizableArgIndex >= 0 && args[VectorizableArgIndex] is ArrayVal vecVals) {
                var tempArgs = new Val[args.Length];
                Array.Copy(args, tempArgs, args.Length);
                var results = new Val[vecVals.Length];
                for (int i = 0; i < vecVals.Length; i++) {
                    tempArgs[VectorizableArgIndex] = vecVals[i];
                    results[i] = Method(e, tempArgs);
                }
                return new ArrayVal(results);
            }
            else {
                return Method(e, args);
            }
        }

        public bool Match(string name, Val[] args) {
            if (name != this.Name) return false;
            if (VariadicArgMode != VariadicAragumentMode.None) return args.Length >= this.ArgDefs.Length;
            return args.Length == this.ArgDefs.Length;
        }

        public override string ToString() {
            var sb = new StringBuilder();
            sb.Append(Name);
            sb.Append('(');
            for (int i = 0; i < ArgDefs.Length; i++) {
                if (i > 0) sb.Append(", ");
                sb.Append(ArgDefs[i].ToString());
                if (i == VectorizableArgIndex) sb.Append('*');
            }
            switch(VariadicArgMode) {
                case VariadicAragumentMode.Flatten: sb.Append("...");break;
                case VariadicAragumentMode.Array: sb.Append("[]...");break;
            }
            sb.Append(')');
            return sb.ToString();
        }

        public static readonly FuncDef dec = new FuncDef("dec(x*)", (e, a) => a[0].FormatInt(), "Converts the value to decimal representation.");
        public static readonly FuncDef hex = new FuncDef("hex(x*)", (e, a) => a[0].FormatHex(), "Converts the value to hexdecimal representation.");
        public static readonly FuncDef bin = new FuncDef("bin(x*)", (e, a) => a[0].FormatBin(), "Converts the value to binary representation.");
        public static readonly FuncDef oct = new FuncDef("oct(x*)", (e, a) => a[0].FormatOct(), "Converts the value to octal representation.");
        public static readonly FuncDef si = new FuncDef("si(x*)", (e, a) => a[0].FormatSiPrefix(), "Converts the value to SI prefixed representation.");
        public static readonly FuncDef bi = new FuncDef("bi(x*)", (e, a) => a[0].FormatBinaryPrefix(), "Converts the value to binary prefixed representation.");
        public static readonly FuncDef char_1 = new FuncDef("char(x*)", (e, a) => a[0].FormatChar(), "Converts the value to character representation.");
        public static readonly FuncDef datetime = new FuncDef("datetime(x*)", (e, a) => a[0].FormatDateTime(), "Converts the value to datetime representation.");
        public static readonly FuncDef array = new FuncDef("array(x[]...)", (e, a) => a[0].FormatDefault(), "Converts the string value to array representation.");
        public static readonly FuncDef str = new FuncDef("str(x[]...)", (e, a) => a[0].FormatString(), "Converts the array value to string representation.");

        public static readonly FuncDef real = new FuncDef("real(x*)", (e, a) => a[0].AsRealVal().FormatReal(), "Converts the value to a real number.");
        public static readonly FuncDef rat = new FuncDef("rat(x*)", (e, a) => new FracVal(RMath.FindFrac(a[0].AsReal)), "Rational fraction approximation.");
        public static readonly FuncDef rat_2 = new FuncDef("rat(x*, max)", (e, a) => new FracVal(RMath.FindFrac(a[0].AsReal, a[1].AsReal, a[1].AsReal), a[0].FormatHint), "Rational fraction approximation.");

        public static readonly FuncDef pow = new FuncDef("pow(x*, y)", (e, a) => new RealVal(RMath.Pow(a[0].AsReal, a[1].AsReal), a[0].FormatHint), "Power");
        public static readonly FuncDef exp = new FuncDef("exp(x*)", (e, a) => new RealVal(RMath.Exp(a[0].AsReal)), "Exponential");
        public static readonly FuncDef sqrt = new FuncDef("sqrt(x*)", (e, a) => new RealVal(RMath.Sqrt(a[0].AsReal)), "Square root");
        public static readonly FuncDef log = new FuncDef("log(x*)", (e, a) => new RealVal(RMath.Log(a[0].AsReal)), "Logarithm");
        public static readonly FuncDef log2 = new FuncDef("log2(x*)", (e, a) => new RealVal(RMath.Log2(a[0].AsReal, e.Settings.AccuracyPriority)), "Binary logarithm");
        public static readonly FuncDef log10 = new FuncDef("log10(x*)", (e, a) => new RealVal(RMath.Log10(a[0].AsReal)), "Common logarithm");
        public static readonly FuncDef clog2 = new FuncDef("clog2(x*)", (e, a) => new RealVal(RMath.Ceiling(RMath.Log2(a[0].AsReal, e.Settings.AccuracyPriority))).FormatInt(), "Ceiling of binary logarithm");
        public static readonly FuncDef clog10 = new FuncDef("clog10(x*)", (e, a) => new RealVal(RMath.Ceiling(RMath.Log10(a[0].AsReal))).FormatInt(), "Ceiling of common logarithm");

        public static readonly FuncDef sin = new FuncDef("sin(x*)", (e, a) => new RealVal(RMath.Sin(a[0].AsReal)), "Sine");
        public static readonly FuncDef cos = new FuncDef("cos(x*)", (e, a) => new RealVal(RMath.Cos(a[0].AsReal)), "Cosine");
        public static readonly FuncDef tan = new FuncDef("tan(x*)", (e, a) => new RealVal(RMath.Tan(a[0].AsReal)), "Tangent");
        public static readonly FuncDef asin = new FuncDef("asin(x*)", (e, a) => new RealVal(RMath.Asin(a[0].AsReal)), "Arcsine");
        public static readonly FuncDef acos = new FuncDef("acos(x*)", (e, a) => new RealVal(RMath.Acos(a[0].AsReal)), "Arccosine");
        public static readonly FuncDef atan = new FuncDef("atan(x*)", (e, a) => new RealVal(RMath.Atan(a[0].AsReal)), "Arctangent");
        public static readonly FuncDef atan2 = new FuncDef("atan2(a, b)", (e, a) => new RealVal(RMath.Atan2(a[0].AsReal, a[1].AsReal)), "Arctangent of a / b");
        public static readonly FuncDef sinh = new FuncDef("sinh(x*)", (e, a) => new RealVal(RMath.Sinh(a[0].AsReal)), "Hyperbolic sine");
        public static readonly FuncDef cosh = new FuncDef("cosh(x*)", (e, a) => new RealVal(RMath.Cosh(a[0].AsReal)), "Hyperbolic cosine");
        public static readonly FuncDef tanh = new FuncDef("tanh(x*)", (e, a) => new RealVal(RMath.Tanh(a[0].AsReal)), "Hyperbolic tangent");

        public static readonly FuncDef floor = new FuncDef("floor(x*)", (e, a) => new RealVal(RMath.Floor(a[0].AsReal), a[0].FormatHint).FormatInt(), "Largest integral value less than or equal to a");
        public static readonly FuncDef ceil = new FuncDef("ceil(x*)", (e, a) => new RealVal(RMath.Ceiling(a[0].AsReal), a[0].FormatHint).FormatInt(), "Smallest integral value greater than or equal to a");
        public static readonly FuncDef trunc = new FuncDef("trunc(x*)", (e, a) => new RealVal(RMath.Truncate(a[0].AsReal), a[0].FormatHint).FormatInt(), "Integral part of a");
        public static readonly FuncDef round = new FuncDef("round(x*)", (e, a) => new RealVal(RMath.Round(a[0].AsReal), a[0].FormatHint).FormatInt(), "Nearest integer to a");

        public static readonly FuncDef abs = new FuncDef("abs(x*)", (e, a) => new RealVal(RMath.Abs(a[0].AsReal), a[0].FormatHint), "Absolute");
        public static readonly FuncDef sign = new FuncDef("sign(x*)", (e, a) => new RealVal(RMath.Sign(a[0].AsReal)).FormatInt(), "Returns 1 for positives, -1 for negatives, 0 otherwise.");

        public static readonly FuncDef gcd = new FuncDef("gcd(x...)", (e, a) => new RealVal(RMath.Gcd(a.Select(p => (decimal)p.AsReal).ToArray()), a[0].FormatHint), "Greatest common divisor");
        public static readonly FuncDef lcm = new FuncDef("lcm(x...)", (e, a) => new RealVal(RMath.Lcm(a.Select(p => (decimal)p.AsReal).ToArray()), a[0].FormatHint), "Least common multiple");

        public static readonly FuncDef max = new FuncDef("max(x...)", (e, a) => new RealVal(a.Max(p => p.AsReal), a[0].FormatHint), "Maximum value of the arguments");
        public static readonly FuncDef min = new FuncDef("min(x...)", (e, a) => new RealVal(a.Min(p => p.AsReal), a[0].FormatHint), "Minimum value of the arguments");

        public static readonly FuncDef sum = new FuncDef("sum(x...)", (e, a) => new RealVal(a.Sum(p => p.AsReal), a[0].FormatHint), "Sum of the arguments");
        public static readonly FuncDef ave = new FuncDef("ave(x...)", (e, a) => new RealVal(a.Average(p => p.AsReal), a[0].FormatHint), "Arithmetic mean of the arguments");
        public static readonly FuncDef invSum = new FuncDef("invSum(x...)", (e, a) => new RealVal(1m / a.Sum(p => 1m / p.AsReal), a[0].FormatHint), "Inverse of the sum of the inverses");
        public static readonly FuncDef harMean = new FuncDef("harMean(x...)", (e, a) => new RealVal((real)a.Length / a.Sum(p => 1m / p.AsReal), a[0].FormatHint), "Harmonic mean of the arguments");
        public static readonly FuncDef geoMean = new FuncDef("geoMean(x...)", (e, a) => {
            var prod = (real)1;
            foreach (var p in a) prod *= p.AsReal;
            return new RealVal(RMath.Pow(prod, 1m / a.Length), a[0].FormatHint);
        }, "Geometric mean of the arguments");

        public static readonly FuncDef pack = new FuncDef("pack(b, array[]...)", (e, a) => new RealVal(LMath.Pack(a[0].AsInt, a[1].AsLongArray)).FormatHex(), "Packs the array elements to a value.");
        public static readonly FuncDef unpack = new FuncDef("unpack(b, x)", (e, a) => new ArrayVal(LMath.Unpack(a[0].AsInt, a[1].AsLong)).FormatInt(), "Unpacks the value to an array.");

        public static readonly FuncDef swapNib = new FuncDef("swapNib(x*)", (e, a) => new RealVal(LMath.SwapNibbles(a[0].AsLong), a[0].FormatHint), "Swaps the nibble of each byte.");
        public static readonly FuncDef swap2 = new FuncDef("swap2(x*)", (e, a) => new RealVal(LMath.Swap2(a[0].AsLong), a[0].FormatHint), "Swaps even and odd bytes.");
        public static readonly FuncDef swap4 = new FuncDef("swap4(x*)", (e, a) => new RealVal(LMath.Swap4(a[0].AsLong), a[0].FormatHint), "Reverses the order of each 4 bytes.");
        public static readonly FuncDef swap8 = new FuncDef("swap8(x*)", (e, a) => new RealVal(LMath.Swap8(a[0].AsLong), a[0].FormatHint), "Reverses the order of each 8 bytes.");
        public static readonly FuncDef reverseBits = new FuncDef("reverseBits(b, x*)", (e, a) => new RealVal(LMath.Reverse(a[0].AsInt, a[1].AsLong), a[1].FormatHint), "Reverses the lower b bits of x.");
        public static readonly FuncDef reverseBytewise = new FuncDef("reverseBytewise(b, x*)", (e, a) => new RealVal(LMath.ReverseBytes(a[0].AsLong), a[0].FormatHint), "Reverses the order of bits of each byte.");
        public static readonly FuncDef rotateL = new FuncDef("rotateL(b, x*)", (e, a) => new RealVal(LMath.RotateLeft(a[0].AsInt, a[1].AsLong), a[1].FormatHint), "Rotates left the lower b bits of a.");
        public static readonly FuncDef rotateR = new FuncDef("rotateR(b, x*)", (e, a) => new RealVal(LMath.RotateRight(a[0].AsInt, a[1].AsLong), a[1].FormatHint), "Rotates right the lower b bits of a.");
        public static readonly FuncDef count1 = new FuncDef("count1(x*)", (e, a) => new RealVal(LMath.CountOnes(a[0].AsLong)).FormatInt(), "Number of bits that have the value 1.");

        public static readonly FuncDef xorReduce = new FuncDef("xorReduce(x*)", (e, a) => new RealVal(LMath.XorReduce(a[0].AsLong)).FormatInt(), "Reduction XOR (Same as even parity).");
        public static readonly FuncDef oddParity = new FuncDef("oddParity(x*)", (e, a) => new RealVal(LMath.OddParity(a[0].AsLong)).FormatInt(), "Odd parity.");

        public static readonly FuncDef eccWidth = new FuncDef("eccWidth(b*)", (e, a) => new RealVal(LMath.EccWidth(a[0].AsInt)).FormatInt(), "Width of ECC for b-bit data.");
        public static readonly FuncDef eccEnc = new FuncDef("eccEnc(b, x*)", (e, a) => new RealVal(LMath.EccEncode(a[0].AsInt, a[1].AsLong)).FormatHex(), "Generate ECC code (b: data width, x: data)");
        public static readonly FuncDef eccDec = new FuncDef("eccDec(b, ecc, x)", (e, a) => new RealVal(LMath.EccDecode(a[0].AsInt, a[1].AsInt, a[2].AsLong)).FormatInt(), "Check ECC code (b: data width, ecc: ECC code, x: data)");

        public static readonly FuncDef toGray = new FuncDef("toGray(x*)", (e, a) => new RealVal(LMath.ToGray(a[0].AsLong), a[0].FormatHint), "Converts the value from binary to gray-code.");
        public static readonly FuncDef fromGray = new FuncDef("fromGray(x*)", (e, a) => new RealVal(LMath.FromGray(a[0].AsLong), a[0].FormatHint), "Converts the value from gray-code to binary.");

        public static readonly FuncDef now = new FuncDef("now()", (e, a) => new RealVal(UnixTime.FromLocalTime(DateTime.Now)).FormatDateTime(), "Current epoch time");

        public static readonly FuncDef toDays = new FuncDef("toDays(x*)", (e, a) => a[0].Div(e, new RealVal(24 * 60 * 60)).FormatReal(), "Converts from epoch time to days.");
        public static readonly FuncDef toHours = new FuncDef("toHours(x*)", (e, a) => a[0].Div(e, new RealVal(60 * 60)).FormatReal(), "Converts from epoch time to hours.");
        public static readonly FuncDef toMinutes = new FuncDef("toMinutes(x*)", (e, a) => a[0].Div(e, new RealVal(60)).FormatReal(), "Converts from epoch time to minutes.");
        public static readonly FuncDef toSeconds = new FuncDef("toSeconds(x*)", (e, a) => a[0].FormatReal(), "Converts from epoch time to seconds.");

        public static readonly FuncDef fromDays = new FuncDef("fromDays(x*)", (e, a) => a[0].Mul(e, new RealVal(24 * 60 * 60)).FormatDateTime(), "Converts from days to epoch time.");
        public static readonly FuncDef fromHours = new FuncDef("fromHours(x*)", (e, a) => a[0].Mul(e, new RealVal(60 * 60)).FormatDateTime(), "Converts from hours to epoch time.");
        public static readonly FuncDef fromMinutes = new FuncDef("fromMinutes(x*)", (e, a) => a[0].Mul(e, new RealVal(60)).FormatDateTime(), "Converts from minutes to epoch time.");
        public static readonly FuncDef fromSeconds = new FuncDef("fromSeconds(x*)", (e, a) => a[0].FormatDateTime(), "Converts from seconds to epoch time.");

        public static readonly FuncDef e3Floor = new FuncDef("e3Floor(x*)", (e, a) => new RealVal(PreferredNumbers.Floor(Eseries.E3, a[0].AsReal), a[0].FormatHint), "E3 series floor");
        public static readonly FuncDef e3Ceil = new FuncDef("e3Ceil(x*)", (e, a) => new RealVal(PreferredNumbers.Ceiling(Eseries.E3, a[0].AsReal), a[0].FormatHint), "E3 series ceiling");
        public static readonly FuncDef e3Round = new FuncDef("e3Round(x*)", (e, a) => new RealVal(PreferredNumbers.Round(Eseries.E3, a[0].AsReal), a[0].FormatHint), "E3 series round");
        public static readonly FuncDef e3Ratio = new FuncDef("e3Ratio(x*)", (e, a) => new ArrayVal(PreferredNumbers.FindSplitPair(Eseries.E3, a[0].AsReal)), "E3 series value of divider resistor");

        public static readonly FuncDef e6Floor = new FuncDef("e6Floor(x*)", (e, a) => new RealVal(PreferredNumbers.Floor(Eseries.E6, a[0].AsReal), a[0].FormatHint), "E6 series floor");
        public static readonly FuncDef e6Ceil = new FuncDef("e6Ceil(x*)", (e, a) => new RealVal(PreferredNumbers.Ceiling(Eseries.E6, a[0].AsReal), a[0].FormatHint), "E6 series ceiling");
        public static readonly FuncDef e6Round = new FuncDef("e6Round(x*)", (e, a) => new RealVal(PreferredNumbers.Round(Eseries.E6, a[0].AsReal), a[0].FormatHint), "E6 series round");
        public static readonly FuncDef e6Ratio = new FuncDef("e6Ratio(x*)", (e, a) => new ArrayVal(PreferredNumbers.FindSplitPair(Eseries.E6, a[0].AsReal)), "E6 series value of divider resistor");

        public static readonly FuncDef e12Floor = new FuncDef("e12Floor(x*)", (e, a) => new RealVal(PreferredNumbers.Floor(Eseries.E12, a[0].AsReal), a[0].FormatHint), "E12 series floor");
        public static readonly FuncDef e12Ceil = new FuncDef("e12Ceil(x*)", (e, a) => new RealVal(PreferredNumbers.Ceiling(Eseries.E12, a[0].AsReal), a[0].FormatHint), "E12 series ceiling");
        public static readonly FuncDef e12Round = new FuncDef("e12Round(x*)", (e, a) => new RealVal(PreferredNumbers.Round(Eseries.E12, a[0].AsReal), a[0].FormatHint), "E12 series round");
        public static readonly FuncDef e12Ratio = new FuncDef("e12Ratio(x*)", (e, a) => new ArrayVal(PreferredNumbers.FindSplitPair(Eseries.E12, a[0].AsReal)), "E12 series value of divider resistor");

        public static readonly FuncDef e24Floor = new FuncDef("e24Floor(x*)", (e, a) => new RealVal(PreferredNumbers.Floor(Eseries.E24, a[0].AsReal), a[0].FormatHint), "E24 series floor");
        public static readonly FuncDef e24Ceil = new FuncDef("e24Ceil(x*)", (e, a) => new RealVal(PreferredNumbers.Ceiling(Eseries.E24, a[0].AsReal), a[0].FormatHint), "E24 series ceiling");
        public static readonly FuncDef e24Round = new FuncDef("e24Round(x*)", (e, a) => new RealVal(PreferredNumbers.Round(Eseries.E24, a[0].AsReal), a[0].FormatHint), "E24 series round");
        public static readonly FuncDef e24Ratio = new FuncDef("e24Ratio(x*)", (e, a) => new ArrayVal(PreferredNumbers.FindSplitPair(Eseries.E24, a[0].AsReal)), "E24 series value of divider resistor");

        public static readonly FuncDef e48Floor = new FuncDef("e48Floor(x*)", (e, a) => new RealVal(PreferredNumbers.Floor(Eseries.E48, a[0].AsReal), a[0].FormatHint), "E48 series floor");
        public static readonly FuncDef e48Ceil = new FuncDef("e48Ceil(x*)", (e, a) => new RealVal(PreferredNumbers.Ceiling(Eseries.E48, a[0].AsReal), a[0].FormatHint), "E48 series ceiling");
        public static readonly FuncDef e48Round = new FuncDef("e48Round(x*)", (e, a) => new RealVal(PreferredNumbers.Round(Eseries.E48, a[0].AsReal), a[0].FormatHint), "E48 series round");
        public static readonly FuncDef e48Ratio = new FuncDef("e48Ratio(x*)", (e, a) => new ArrayVal(PreferredNumbers.FindSplitPair(Eseries.E48, a[0].AsReal)), "E48 series value of divider resistor");

        public static readonly FuncDef e96Floor = new FuncDef("e96Floor(x*)", (e, a) => new RealVal(PreferredNumbers.Floor(Eseries.E96, a[0].AsReal), a[0].FormatHint), "E96 series floor");
        public static readonly FuncDef e96Ceil = new FuncDef("e96Ceil(x*)", (e, a) => new RealVal(PreferredNumbers.Ceiling(Eseries.E96, a[0].AsReal), a[0].FormatHint), "E96 series ceiling");
        public static readonly FuncDef e96Round = new FuncDef("e96Round(x*)", (e, a) => new RealVal(PreferredNumbers.Round(Eseries.E96, a[0].AsReal), a[0].FormatHint), "E96 series round");
        public static readonly FuncDef e96Ratio = new FuncDef("e96Ratio(x*)", (e, a) => new ArrayVal(PreferredNumbers.FindSplitPair(Eseries.E96, a[0].AsReal)), "E96 series value of divider resistor");

        public static readonly FuncDef e192Floor = new FuncDef("e192Floor(x*)", (e, a) => new RealVal(PreferredNumbers.Floor(Eseries.E192, a[0].AsReal), a[0].FormatHint), "E192 series floor");
        public static readonly FuncDef e192Ceil = new FuncDef("e192Ceil(x*)", (e, a) => new RealVal(PreferredNumbers.Ceiling(Eseries.E192, a[0].AsReal), a[0].FormatHint), "E192 series ceiling");
        public static readonly FuncDef e192Round = new FuncDef("e192Round(x*)", (e, a) => new RealVal(PreferredNumbers.Round(Eseries.E192, a[0].AsReal), a[0].FormatHint), "E192 series round");
        public static readonly FuncDef e192Ratio = new FuncDef("e192Ratio(x*)", (e, a) => new ArrayVal(PreferredNumbers.FindSplitPair(Eseries.E192, a[0].AsReal)), "E192 series value of divider resistor");

        public static readonly FuncDef rgb_3 = new FuncDef("rgb(r, g, b)", (e, a) => new RealVal(ColorSpace.SatPack(a[0].AsReal, a[1].AsReal, a[2].AsReal)).FormatWebColor(), "Generates 24 bit color value from R, G, B.");
        public static readonly FuncDef rgb_1 = new FuncDef("rgb(rgb*)", (e, a) => a[0].FormatWebColor(), "Converts the value to web-color representation.");

        public static readonly FuncDef hsv2rgb = new FuncDef("hsv2rgb(h, s, v)", (e, a) => new RealVal(ColorSpace.HsvToRgb(a[0].AsReal, a[1].AsReal, a[2].AsReal)).FormatWebColor(), "Converts from H, S, V to 24 bit RGB color value.");
        public static readonly FuncDef rgb2hsv = new FuncDef("rgb2hsv(rgb*)", (e, a) => new ArrayVal(ColorSpace.RgbToHsv(a[0].AsReal)), "Converts the 24 bit RGB color value to HSV.");

        public static readonly FuncDef hsl2rgb = new FuncDef("hsl2rgb(h, s, l)", (e, a) => new RealVal(ColorSpace.HslToRgb(a[0].AsReal, a[1].AsReal, a[2].AsReal)).FormatWebColor(), "Convert from H, S, L to 24 bit color RGB value.");
        public static readonly FuncDef rgb2hsl = new FuncDef("rgb2hsl(rgb*)", (e, a) => new ArrayVal(ColorSpace.RgbToHsl(a[0].AsReal)), "Converts the 24 bit RGB color value to HSL.");

        public static readonly FuncDef yuv2rgb_3 = new FuncDef("yuv2rgb(y, u, v)", (e, a) => new RealVal(ColorSpace.YuvToRgb(a[0].AsReal, a[1].AsReal, a[2].AsReal)).FormatWebColor(), "Converts Y, U, V to 24 bit RGB color.");
        public static readonly FuncDef yuv2rgb_1 = new FuncDef("yuv2rgb(yuv*)", (e, a) => new RealVal(ColorSpace.YuvToRgb(a[0].AsReal)).FormatWebColor(), "Converts the 24 bit YUV color to 24 bit RGB.");

        public static readonly FuncDef rgb2yuv_3 = new FuncDef("rgb2yuv(r, g, b)", (e, a) => new RealVal(ColorSpace.RgbToYuv(a[0].AsReal, a[1].AsReal, a[2].AsReal)).FormatHex(), "Converts R, G, B to 24 bit YUV color.");
        public static readonly FuncDef rgb2yuv_1 = new FuncDef("rgb2yuv(rgb*)", (e, a) => new RealVal(ColorSpace.RgbToYuv(a[0].AsReal)).FormatHex(), "Converts 24bit RGB color to 24 bit YUV.");

        public static readonly FuncDef rgbto565 = new FuncDef("rgbto565(rgb*)", (e, a) => new RealVal(ColorSpace.Rgb888To565(a[0].AsInt)).FormatHex(), "Downconverts RGB888 color to RGB565.");
        public static readonly FuncDef rgbfrom565 = new FuncDef("rgbfrom565(rgb*)", (e, a) => new RealVal(ColorSpace.Rgb565To888(a[0].AsInt)).FormatWebColor(), "Upconverts RGB565 color to RGB888.");

        public static readonly FuncDef pack565 = new FuncDef("pack565(x, y, z)", (e, a) => new RealVal(ColorSpace.Pack565(a[0].AsInt, a[1].AsInt, a[2].AsInt)).FormatHex(), "Packs the 3 values to an RGB565 color.");
        public static readonly FuncDef unpack565 = new FuncDef("unpack565(x*)", (e, a) => new ArrayVal(ColorSpace.Unpack565(a[0].AsInt)), "Unpacks the RGB565 color to 3 values.");

        public static readonly FuncDef prime = new FuncDef("prime(x*)", (e, a) => new RealVal(RMath.Prime(a[0].AsInt)), "Returns x-th prime number.");
        public static readonly FuncDef isPrime = new FuncDef("isPrime(x*)", (e, a) => new BoolVal(RMath.IsPrime(a[0].AsReal)), "Returns whether the value is prime or not.");
        public static readonly FuncDef primeFact = new FuncDef("primeFact(x*)", (e, a) => new ArrayVal(RMath.PrimeFactors(a[0].AsReal), a[0].FormatHint), "Returns prime factors.");

        public static readonly FuncDef rand = new FuncDef("rand()", (e, a) => new RealVal((real)rng.NextDouble()), "Generates a random value between 0.0 and 1.0.");
        public static readonly FuncDef rand_2 = new FuncDef("rand(min, max)", (e, a) => {
            var min = a[0].AsReal;
            var max = a[1].AsReal;
            return new RealVal(min + (real)rng.NextDouble() * (max - min));
        }, "Generates a random value between min and max.");
        public static readonly FuncDef rand32 = new FuncDef("rand32()", (e, a) => new RealVal(rng.Next()), "Generates a 32bit random integer.");
        public static readonly FuncDef rand64 = new FuncDef("rand64()", (e, a) => new RealVal((((long)rng.Next()) << 32) | ((long)rng.Next())), "Generates a 64bit random integer.");

        public static readonly FuncDef len = new FuncDef("len(array)", (e, a) => new RealVal(((ArrayVal)a[0]).Length), "Length of array");
        public static readonly FuncDef range_2 = new FuncDef("range(start, stop)", (e, a) => new ArrayVal(RMath.Range(a[0].AsReal, a[1].AsReal, 0, false)), "Generate number sequence.");
        public static readonly FuncDef range_3 = new FuncDef("range(start, stop, step)", (e, a) => new ArrayVal(RMath.Range(a[0].AsReal, a[1].AsReal, a[2].AsReal, false)), "Generate number sequence.");
        public static readonly FuncDef rangeInclusive_2 = new FuncDef("rangeInclusive(start, stop)", (e, a) => new ArrayVal(RMath.Range(a[0].AsReal, a[1].AsReal, 0, true)), "Generate number sequence.");
        public static readonly FuncDef rangeInclusive_3 = new FuncDef("rangeInclusive(start, stop, step)", (e, a) => new ArrayVal(RMath.Range(a[0].AsReal, a[1].AsReal, a[2].AsReal, true)), "Generate number sequence.");
        public static readonly FuncDef reverseArray = new FuncDef("reverseArray(array)", (e, a) => {
            var array = (Val[])((ArrayVal)a[0]).Raw;
            Array.Reverse(array);
            return new ArrayVal(array, a[0].FormatHint);
        }, "Reverses the order of array elements");

        public static readonly FuncDef utf8Enc = new FuncDef("utf8Enc(str)", (e, a) => new ArrayVal(Encoding.UTF8.GetBytes(a[0].AsString)), "Encode string to UTF8 byte sequence.");
        public static readonly FuncDef utf8Dec = new FuncDef("utf8Dec(bytes[]...)", (e, a) => new ArrayVal(Encoding.UTF8.GetString(a[0].AsByteArray)), "Decode UTF8 byte sequence.");

        public static readonly FuncDef urlEnc = new FuncDef("urlEnc(str)", (e, a) => new ArrayVal(System.Web.HttpUtility.UrlEncode(a[0].AsString)), "Escape URL string.");
        public static readonly FuncDef urlDec = new FuncDef("urlDec(str)", (e, a) => new ArrayVal(System.Web.HttpUtility.UrlDecode(a[0].AsString)), "Decode URL string.");

        public static readonly FuncDef base64Enc = new FuncDef("base64Enc(str)", (e, a) => new ArrayVal(Convert.ToBase64String(Encoding.UTF8.GetBytes(a[0].AsString))), "Encode string to Base64.");
        public static readonly FuncDef base64Dec = new FuncDef("base64Dec(str)", (e, a) => new ArrayVal(Encoding.UTF8.GetString(Convert.FromBase64String(a[0].AsString))), "Decode Base64 to string.");
        public static readonly FuncDef base64EncBytes = new FuncDef("base64EncBytes(bytes[]...)", (e, a) => new ArrayVal(Convert.ToBase64String(a[0].AsByteArray)), "Encode byte-array to Base64.");
        public static readonly FuncDef base64DecBytes = new FuncDef("base64DecBytes(str)", (e, a) => new ArrayVal(Convert.FromBase64String(a[0].AsString)), "Decode Base64 to byte-array.");

        public static readonly FuncDef assert = new FuncDef("assert(x)", (e, a) => {
            if (!a[0].AsBool) {
                throw new CalctusError("Assertion failed.");
            }
            return a[0];
        }, "Highlights the expression if the argument is false.");

        /// <summary>ネイティブ関数の一覧</summary>
        public static FuncDef[] NativeFunctions = EnumNativeFunctions().ToArray();
        private static IEnumerable<FuncDef> EnumNativeFunctions() {
            return
                typeof(FuncDef)
                .GetFields()
                .Where(p => p.IsStatic && (p.FieldType == typeof(FuncDef)))
                .Select(p => (FuncDef)p.GetValue(null));
        }

        public static IEnumerable<FuncDef> EnumAllFunctions() {
            if (Settings.Instance.Script_Enable) {
                return ExtFuncDef.ExternalFunctions.Concat(NativeFunctions);
            }
            else {
                return NativeFunctions;
            }
        }

        /// <summary>指定された条件にマッチするネイティブ関数を返す</summary>
        public static FuncDef Match(Token tok, Val[] args, bool allowExtermals) {
            var funcs = allowExtermals ? EnumAllFunctions() : NativeFunctions;
            var f = funcs.FirstOrDefault(p => p.Match(tok.Text, args));
            if (f == null) {
                throw new LexerError(tok.Position, "function " + tok + "(" + args.Length + ") was not found.");
            }
            return f;
        }

    }
}
