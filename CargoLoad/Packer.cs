using System;
using System.Collections.Generic;
using System.Linq;

namespace CargoLoad
{
    public sealed class PackItem
    {
        public int CargoTypeId;
        public string Name = "";
        public double L, W, H, Weight; // мм, мм, мм, кг
        public bool Stackable = true;
        public bool CanRotate;
    }

    public sealed class Packed
    {
        public PackItem Item = null!;
        public double X, Y, Z, L, W, H;
        public int Level; // 0 = на полу, 1 = второй ярус
    }

    public sealed class PackResult
    {
        public List<Packed> Placed = new();
        public List<PackItem> Unplaced = new();
        public double UsedVolumeM3;
        public double TotalWeightKg;
        public double TruckVolumeM3;
        public int PayloadKg;
        public Vehicle Vehicle = null!;
    }

    /// <summary>Одна машина в плане перевозки.</summary>
    public sealed class TruckPlan
    {
        public Vehicle Vehicle = null!;
        public int Count = 1;
    }

    /// <summary>Результат раскладки по одной машине.</summary>
    public sealed class TruckResult
    {
        public Vehicle Vehicle = null!;
        public int Index;
        public PackResult Result = null!;
    }

    /// <summary>Результат раскладки по нескольким машинам.</summary>
    public sealed class MultiResult
    {
        public List<TruckResult> Trucks = new();
        public List<PackItem> Unplaced = new();

        public bool FitsAll => Unplaced.Count == 0;
        public double UsedVolumeM3 => Trucks.Sum(t => t.Result.UsedVolumeM3);
        public double TruckVolumeM3 => Trucks.Sum(t => t.Result.TruckVolumeM3);
        public double TotalWeightKg => Trucks.Sum(t => t.Result.TotalWeightKg);
    }

    public static class Packer
    {
        public static PackResult Pack(List<PackItem> items, Vehicle vehicle, RotationMode mode)
        {
            double L = vehicle.LengthMm, W = vehicle.WidthMm, H = vehicle.HeightMm;
            int payload = vehicle.PayloadKg;

            var result = new PackResult
            {
                TruckVolumeM3 = L / 1000.0 * W / 1000.0 * H / 1000.0,
                PayloadKg = payload,
                Vehicle = vehicle,
            };

            // сортировка: высокое и тяжёлое — вниз (нижний ярус)
            var sorted = items
                .OrderByDescending(i => i.H)
                .ThenByDescending(i => i.Weight)
                .ThenByDescending(i => i.L * i.W * i.H)
                .ToList();

            var placed = new List<Packed>();
            double placedWeight = 0;

            double maxDim = sorted.Count > 0 ? sorted.Max(i => Math.Max(i.L, Math.Max(i.W, i.H))) : 1;
            bool hasSideRule = !vehicle.IsTop && (vehicle.IsSide || vehicle.IsRear);
            double farDepth = 2 * maxDim; // последние ~2 ряда

            foreach (var item in sorted)
            {
                if (!TryPlace(item, L, W, H, payload, mode, placed, placedWeight, hasSideRule, farDepth, vehicle, out var box))
                {
                    result.Unplaced.Add(item);
                }
                else
                {
                    placed.Add(box);
                    placedWeight += item.Weight;
                }
            }

            result.Placed = placed;
            double usedM3 = 0, totalKg = 0;
            foreach (var p in placed)
            {
                usedM3 += p.L / 1000.0 * p.W / 1000.0 * p.H / 1000.0;
                totalKg += p.Item.Weight;
            }
            result.UsedVolumeM3 = usedM3;
            result.TotalWeightKg = totalKg;
            return result;
        }

        private static bool TryPlace(PackItem item, double L, double W, double H, int payload,
            RotationMode mode, List<Packed> placed, double placedWeight,
            bool widthRule, double farDepth, Vehicle vehicle, out Packed box)
        {
            box = null!;

            foreach (var (lx, ly, lz) in Orientations(item, mode))
            {
                if (lx > L || ly > W || lz > H) continue;

                int tries = 0;
                foreach (double z in ZLevels(item, lz, H, placed))
                {
                    foreach (var (x, y) in CandidateXY(lx, ly, placed))
                    {
                        if (++tries > 600) break;
                        if (CanPlace(item, lx, ly, lz, x, y, z, L, W, H, payload, placed, placedWeight, widthRule, farDepth, vehicle))
                        {
                            box = new Packed
                            {
                                Item = item, X = x, Y = y, Z = z,
                                L = lx, W = ly, H = lz,
                                Level = z < 0.5 ? 0 : 1,
                            };
                            return true;
                        }
                    }
                    if (tries > 600) break;
                }
            }
            return false;
        }

        private static IEnumerable<(double, double, double)> Orientations(PackItem item, RotationMode mode)
        {
            double a = item.L, b = item.W, c = item.H;
            if (mode == RotationMode.Fixed || !item.CanRotate)
            {
                yield return (a, b, c);
                yield break;
            }
            if (mode == RotationMode.Rotate90)
            {
                yield return (a, b, c);
                yield return (b, a, c);
                yield break;
            }
            // Free: все 6 перестановок
            var perms = new[]
            {
                (a, b, c), (a, c, b), (b, a, c),
                (b, c, a), (c, a, b), (c, b, a),
            };
            foreach (var p in perms.Distinct()) yield return p;
        }

        private static IEnumerable<double> ZLevels(PackItem item, double itemH, double truckH, List<Packed> placed)
        {
            var set = new SortedSet<double> { 0 };
            if (!item.Stackable) return set;

            foreach (var p in placed)
            {
                if (p.Level != 0) continue;
                if (!p.Item.Stackable) continue;
                if (p.Item.Weight < item.Weight) continue; // тяжёлое снизу
                double z = p.Z + p.H;
                if (z + itemH <= truckH + 0.5) set.Add(z);
            }
            return set;
        }

        private static IEnumerable<(double, double)> CandidateXY(double lx, double ly, List<Packed> placed)
        {
            var xs = new SortedSet<double> { 0 };
            var ys = new SortedSet<double> { 0 };
            foreach (var p in placed)
            {
                xs.Add(p.X + p.L);
                ys.Add(p.Y + p.W);
            }
            foreach (double y in ys)
                foreach (double x in xs)
                    yield return (x, y);
        }

        private static bool CanPlace(PackItem item, double lx, double ly, double lz,
            double x, double y, double z, double L, double W, double H, int payload,
            List<Packed> placed, double placedWeight, bool widthRule, double farDepth, Vehicle vehicle)
        {
            if (x < 0 || y < 0 || z < 0) return false;
            if (x + lx > L + 0.5 || y + ly > W + 0.5 || z + lz > H + 0.5) return false;

            if (placedWeight + item.Weight > payload + 0.5) return false;

            // поддержка и пересечения
            foreach (var p in placed)
            {
                if (Overlap3D(x, y, z, lx, ly, lz, p))
                    return false;
            }

            if (z >= 0.5)
            {
                // нужен опорный ящик снизу, полностью покрывающий след
                bool supported = false;
                foreach (var p in placed)
                {
                    if (p.Level != 0) continue;
                    if (Math.Abs(p.Z + p.H - z) > 0.5) continue;
                    if (x >= p.X - 0.5 && y >= p.Y - 0.5 &&
                        x + lx <= p.X + p.L + 0.5 && y + ly <= p.Y + p.W + 0.5)
                    {
                        supported = true;
                        break;
                    }
                }
                if (!supported) return false;
            }

            // правило ширины: в дальней от борта зоне по ширине не более 2 ящиков
            if (widthRule)
            {
                bool far;
                if (vehicle.IsRear && !vehicle.IsSide)
                    far = x < farDepth;                 // передний конец, дальше от задней двери
                else
                    far = y > (W - farDepth);            // дальний борт
                if (far)
                {
                    int same = 0;
                    int level = z < 0.5 ? 0 : 1;
                    foreach (var p in placed)
                    {
                        if (p.Level != level) continue;
                        if (OverlapX(x, lx, p)) same++;
                    }
                    if (same >= 2) return false;
                }
            }

            return true;
        }

        private static bool Overlap3D(double x, double y, double z, double lx, double ly, double lz, Packed p)
        {
            return x < p.X + p.L && x + lx > p.X &&
                   y < p.Y + p.W && y + ly > p.Y &&
                   z < p.Z + p.H && z + lz > p.Z;
        }

        private static bool OverlapX(double x, double lx, Packed p)
        {
            return x < p.X + p.L && x + lx > p.X;
        }

        public static List<Placement> ToPlacements(PackResult result)
        {
            var list = new List<Placement>();
            int seq = 1;
            foreach (var p in result.Placed)
            {
                list.Add(new Placement
                {
                    Seq = seq++,
                    CargoTypeId = p.Item.CargoTypeId,
                    Name = p.Item.Name,
                    X = Math.Round(p.X),
                    Y = Math.Round(p.Y),
                    Z = Math.Round(p.Z),
                    Len = Math.Round(p.L),
                    Wid = Math.Round(p.W),
                    Hei = Math.Round(p.H),
                    Tier = p.Level,
                    WeightKg = p.Item.Weight,
                });
            }
            return list;
        }

        // ---------------- Несколько ТС ----------------

        /// <summary>
        /// Раскладывает груз последовательно по машинам из плана: каждая машина
        /// забирает столько, сколько помещается, остаток уходит в следующую.
        /// </summary>
        public static MultiResult PackMulti(List<PackItem> items, List<TruckPlan> plan, RotationMode mode)
        {
            var multi = new MultiResult();
            var remaining = new List<PackItem>(items);
            int index = 0;

            foreach (var truck in plan)
            {
                for (int n = 0; n < Math.Max(1, truck.Count) && remaining.Count > 0; n++)
                {
                    var r = Pack(remaining, truck.Vehicle, mode);
                    multi.Trucks.Add(new TruckResult
                    {
                        Vehicle = truck.Vehicle,
                        Index = index++,
                        Result = r,
                    });
                    remaining = new List<PackItem>(r.Unplaced);
                }
            }

            multi.Unplaced = remaining;
            return multi;
        }

        /// <summary>
        /// Автоподбор состава: минимум машин, при равенстве — минимум суммарного
        /// объёма кузовов.
        /// </summary>
        public static MultiResult PlanAuto(List<PackItem> items, List<Vehicle> fleet, RotationMode mode, int maxTrucks = 8)
        {
            if (items.Count == 0 || fleet.Count == 0)
                return new MultiResult { Unplaced = new List<PackItem>(items) };

            double needVolume = items.Sum(i => i.L * i.W * i.H) / 1e9;
            double needWeight = items.Sum(i => i.Weight);

            // машины от больших к меньшим
            var types = fleet
                .OrderByDescending(v => v.VolumeM3)
                .ToList();

            int limit = Math.Min(maxTrucks, Math.Max(1, items.Count));

            // нижняя оценка по вместимости и грузоподъёмности
            double maxVolume = types[0].VolumeM3;
            int maxPayload = types[0].PayloadKg;
            int kStart = Math.Max(1, Math.Max(
                (int)Math.Ceiling(needVolume / Math.Max(0.001, maxVolume)),
                (int)Math.Ceiling(needWeight / Math.Max(1, maxPayload))));

            int attempts = 0;
            const int maxAttempts = 8;

            // Для крупных партий перебор вариантов слишком дорог: подбираем количество
            // самых вместительных машин по ёмкости с запасом на потери укладки,
            // и выполняем один проход укладки.
            if (items.Count > 150)
            {
                const double efficiency = 0.5; // ожидаемая доля полезного объёма
                int k = Math.Max(kStart, (int)Math.Ceiling(needVolume / Math.Max(0.001, maxVolume * efficiency)));
                k = Math.Max(k, (int)Math.Ceiling(needWeight / Math.Max(1, maxPayload)));
                k = Math.Min(k, limit);

                var fastPlan = Enumerable.Range(0, Math.Max(1, k))
                    .Select(_ => new TruckPlan { Vehicle = types[0], Count = 1 })
                    .ToList();

                return PackMulti(items, fastPlan, mode);
            }

            for (int k = kStart; k <= limit && attempts < maxAttempts; k++)
            {
                var candidates = Combinations(types, k)
                    .Select(combo => new
                    {
                        Combo = combo,
                        Volume = combo.Sum(v => v.VolumeM3),
                        Payload = combo.Sum(v => v.PayloadKg),
                    })
                    .Where(c => c.Volume >= needVolume * 0.85 && c.Payload >= needWeight)
                    .OrderBy(c => c.Volume)
                    .Take(2)   // самые компактные варианты
                    .ToList();

                foreach (var candidate in candidates)
                {
                    if (attempts++ >= maxAttempts) break;

                    // грузим сначала в более вместительные машины
                    var plan = candidate.Combo
                        .OrderByDescending(v => v.VolumeM3)
                        .Select(v => new TruckPlan { Vehicle = v, Count = 1 })
                        .ToList();

                    var result = PackMulti(items, plan, mode);
                    if (result.FitsAll)
                        return result;
                }
            }

            // ничего не подошло — вернуть лучший из максимального числа машин
            var fallbackPlan = Enumerable.Range(0, limit)
                .Select(_ => new TruckPlan { Vehicle = types[0], Count = 1 })
                .ToList();
            return PackMulti(items, fallbackPlan, mode);
        }

        /// <summary>Все сочетания k элементов с повторениями.</summary>
        private static IEnumerable<List<T>> Combinations<T>(List<T> source, int k)
        {
            var buffer = new int[k];

            IEnumerable<List<T>> Walk(int depth, int start)
            {
                if (depth == k)
                {
                    yield return buffer.Select(i => source[i]).ToList();
                    yield break;
                }
                for (int i = start; i < source.Count; i++)
                {
                    buffer[depth] = i;
                    foreach (var combo in Walk(depth + 1, i))
                        yield return combo;
                }
            }

            return Walk(0, 0);
        }

        /// <summary>Размещения по всем машинам плана.</summary>
        public static List<Placement> ToPlacements(MultiResult multi)
        {
            var list = new List<Placement>();
            foreach (var truck in multi.Trucks)
            {
                int seq = 1;
                foreach (var p in truck.Result.Placed)
                {
                    list.Add(new Placement
                    {
                        Seq = seq++,
                        CargoTypeId = p.Item.CargoTypeId,
                        Name = p.Item.Name,
                        VehicleIndex = truck.Index,
                        X = Math.Round(p.X),
                        Y = Math.Round(p.Y),
                        Z = Math.Round(p.Z),
                        Len = Math.Round(p.L),
                        Wid = Math.Round(p.W),
                        Hei = Math.Round(p.H),
                        Tier = p.Level,
                        WeightKg = p.Item.Weight,
                    });
                }
            }
            return list;
        }
    }
}
