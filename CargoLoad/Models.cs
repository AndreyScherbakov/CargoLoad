using System;

namespace CargoLoad
{
    [Flags]
    public enum LoadSide
    {
        None = 0,
        Rear = 1,
        Side = 2,
        Top = 4,
        RearSide = Rear | Side,
        SideTop = Side | Top,
    }

    public enum RotationMode
    {
        Fixed = 0,   // длинной стороной вдоль фургона
        Rotate90 = 1, // дополнительно поворот на 90° в плане
        Free = 2,     // все 6 ориентаций (кантование)
    }

    public sealed class Vehicle
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int LengthMm { get; set; }
        public int WidthMm { get; set; }
        public int HeightMm { get; set; }
        public int PayloadKg { get; set; }
        public LoadSide Load { get; set; }

        public bool IsTop => (Load & LoadSide.Top) != 0;
        public bool IsSide => (Load & LoadSide.Side) != 0;
        public bool IsRear => (Load & LoadSide.Rear) != 0;

        public double VolumeM3 => LengthMm / 1000.0 * WidthMm / 1000.0 * HeightMm / 1000.0;
    }

    public sealed class CargoType
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Article { get; set; } = "";
        public int LengthMm { get; set; }
        public int WidthMm { get; set; }
        public int HeightMm { get; set; }
        public double WeightKg { get; set; }       // вес одной единицы
        public bool Stackable { get; set; } = true;
        public bool CanRotate { get; set; }
        public int MaxLoadTopKg { get; set; }       // 0 = не ограничено

        public double VolumeM3 => LengthMm / 1000.0 * WidthMm / 1000.0 * HeightMm / 1000.0;
    }

    public sealed class Shipment
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public int VehicleId { get; set; } = -1;
    }

    public sealed class ShipmentItem
    {
        public int Id { get; set; }
        public int ShipmentId { get; set; }
        public int CargoTypeId { get; set; }
        public int Quantity { get; set; }

        public CargoType? Cargo { get; set; }

        public double TotalWeightKg => Cargo == null ? 0 : Quantity * Cargo.WeightKg;
    }

    public sealed class Placement
    {
        public int Seq { get; set; }
        public int CargoTypeId { get; set; }
        public string Name { get; set; } = "";
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double Len { get; set; }
        public double Wid { get; set; }
        public double Hei { get; set; }
        public int Tier { get; set; }
        public double WeightKg { get; set; }
    }
}
