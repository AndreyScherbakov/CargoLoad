using System;
using System.Collections.Generic;

namespace CargoLoad;

/// <summary>
/// Баланс груза в кузове: распределение веса между осями (перед/зад)
/// и между бортами (лево/право), плюс положение центра тяжести.
/// </summary>
public sealed class TruckBalance
{
    public double TotalWeightKg { get; private set; }

    /// <summary>Центр тяжести по длине, мм от передней стенки кузова.</summary>
    public double CgXMm { get; private set; }

    /// <summary>Центр тяжести по ширине, мм от левого борта.</summary>
    public double CgYMm { get; private set; }

    public double FrontKg { get; private set; }
    public double RearKg { get; private set; }
    public double LeftKg { get; private set; }
    public double RightKg { get; private set; }

    public double BodyLengthMm { get; private set; }
    public double BodyWidthMm { get; private set; }

    public double FrontPct => TotalWeightKg > 0 ? 100.0 * FrontKg / TotalWeightKg : 50.0;
    public double RearPct => TotalWeightKg > 0 ? 100.0 * RearKg / TotalWeightKg : 50.0;
    public double LeftPct => TotalWeightKg > 0 ? 100.0 * LeftKg / TotalWeightKg : 50.0;
    public double RightPct => TotalWeightKg > 0 ? 100.0 * RightKg / TotalWeightKg : 50.0;

    /// <summary>Перекос по бортам, % от массы груза в машине.</summary>
    public double SideImbalancePct =>
        TotalWeightKg > 0 ? 100.0 * Math.Abs(LeftKg - RightKg) / TotalWeightKg : 0;

    /// <summary>Перекос перед/зад, % от массы груза в машине.</summary>
    public double EndImbalancePct =>
        TotalWeightKg > 0 ? 100.0 * Math.Abs(FrontKg - RearKg) / TotalWeightKg : 0;

    /// <summary>Смещение ЦМ от геометрического центра кузова по длине (+ — к задней стенке).</summary>
    public double CgOffsetXMm => BodyLengthMm > 0 ? CgXMm - BodyLengthMm / 2.0 : 0;

    /// <summary>Смещение ЦМ от осевой линии по ширине (+ — к правому борту).</summary>
    public double CgOffsetYMm => BodyWidthMm > 0 ? CgYMm - BodyWidthMm / 2.0 : 0;

    public bool IsSideBalanced(double tolerancePct) => SideImbalancePct <= tolerancePct;

    public static TruckBalance Compute(IReadOnlyList<Packed> placed, double bodyL, double bodyW)
    {
        var b = new TruckBalance { BodyLengthMm = bodyL, BodyWidthMm = bodyW };
        if (placed.Count == 0 || bodyL <= 0 || bodyW <= 0) return b;

        double midX = bodyL / 2.0, midY = bodyW / 2.0;
        double sumXW = 0, sumYW = 0, total = 0;

        foreach (var p in placed)
        {
            double w = p.Item.Weight;
            if (w <= 0) continue;

            // доля габарита, попадающая в переднюю половину / к левому борту
            double frontShare = OverlapShare(p.X, p.L, midX);
            double leftShare = OverlapShare(p.Y, p.W, midY);

            b.FrontKg += w * frontShare;
            b.RearKg += w * (1 - frontShare);
            b.LeftKg += w * leftShare;
            b.RightKg += w * (1 - leftShare);

            sumXW += (p.X + p.L / 2.0) * w;
            sumYW += (p.Y + p.W / 2.0) * w;
            total += w;
        }

        b.TotalWeightKg = total;
        if (total > 0)
        {
            b.CgXMm = sumXW / total;
            b.CgYMm = sumYW / total;
        }
        return b;
    }

    /// <summary>Доля отрезка [start, start+len], лежащая левее (или раньше) границы mid.</summary>
    private static double OverlapShare(double start, double len, double mid)
    {
        if (len <= 0) return 0;
        double overlap = Math.Min(start + len, mid) - Math.Max(start, 0);
        if (overlap <= 0) return 0;
        if (overlap >= len) return 1;
        return overlap / len;
    }
}