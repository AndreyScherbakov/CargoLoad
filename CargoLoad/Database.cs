using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace CargoLoad
{
    public sealed class Database
    {
        private readonly Sqlite _db;

        public Database(string path)
        {
            _db = new Sqlite(path);
            InitSchema();
            Seed();
        }

        private void InitSchema()
        {
            _db.Execute(@"
CREATE TABLE IF NOT EXISTS vehicles (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  name TEXT NOT NULL,
  length_mm INTEGER NOT NULL,
  width_mm INTEGER NOT NULL,
  height_mm INTEGER NOT NULL,
  payload_kg INTEGER NOT NULL,
  load_side INTEGER NOT NULL
);");
            _db.Execute(@"
CREATE TABLE IF NOT EXISTS cargo_types (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  name TEXT NOT NULL,
  article TEXT NOT NULL,
  length_mm INTEGER NOT NULL,
  width_mm INTEGER NOT NULL,
  height_mm INTEGER NOT NULL,
  weight_kg REAL NOT NULL,
  stackable INTEGER NOT NULL,
  can_rotate INTEGER NOT NULL,
  max_load_top_kg INTEGER NOT NULL
);");
            _db.Execute(@"
CREATE TABLE IF NOT EXISTS shipments (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  name TEXT NOT NULL,
  created_at TEXT NOT NULL,
  vehicle_id INTEGER NOT NULL
);");
            _db.Execute(@"
CREATE TABLE IF NOT EXISTS shipment_items (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  shipment_id INTEGER NOT NULL,
  cargo_type_id INTEGER NOT NULL,
  quantity INTEGER NOT NULL
);");
            _db.Execute(@"
CREATE TABLE IF NOT EXISTS placements (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  shipment_id INTEGER NOT NULL,
  vehicle_index INTEGER NOT NULL DEFAULT 0,
  seq INTEGER NOT NULL,
  cargo_type_id INTEGER NOT NULL,
  x REAL NOT NULL, y REAL NOT NULL, z REAL NOT NULL,
  len REAL NOT NULL, wid REAL NOT NULL, hei REAL NOT NULL,
  tier INTEGER NOT NULL
);");
            _db.Execute(@"
CREATE TABLE IF NOT EXISTS plan_trucks (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  shipment_id INTEGER NOT NULL,
  idx INTEGER NOT NULL,
  vehicle_id INTEGER NOT NULL
);");
            _db.Execute(@"
CREATE TABLE IF NOT EXISTS settings (
  key TEXT PRIMARY KEY,
  value TEXT NOT NULL
);");

            // миграция: в старых базах нет колонки vehicle_index
            try { _db.Execute("ALTER TABLE placements ADD COLUMN vehicle_index INTEGER NOT NULL DEFAULT 0"); }
            catch (SqliteException) { /* колонка уже есть */ }
        }

        private void Seed()
        {
            if (CountVehicles() == 0)
            {
                AddVehicle("Газель", 4200, 2000, 2050, 2000, LoadSide.RearSide);
                AddVehicle("Бычок", 6100, 2450, 2300, 4000, LoadSide.SideTop);
                AddVehicle("Тент", 7200, 2450, 2550, 4000, LoadSide.SideTop);
                AddVehicle("Еврофура", 13600, 2450, 2550, 20000, LoadSide.SideTop);
                AddVehicle("Фура", 16500, 2450, 2550, 24000, LoadSide.SideTop);
            }

            // миграция старых названий с длиной в скобках
            MigrateVehicleNames();
        }

        private void MigrateVehicleNames()
        {
            var renames = new (string Old, string New)[]
            {
                ("Газель (4,2м)", "Газель"),
                ("Бычок (6,1м)", "Бычок"),
                ("Тент (7,2м)", "Тент"),
                ("Еврофура (13,6м)", "Еврофура"),
                ("Фура 16,5м", "Фура"),
            };
            foreach (var (oldName, newName) in renames)
            {
                using var s = _db.Prepare("UPDATE vehicles SET name = ? WHERE name = ?");
                s.Bind(1, newName);
                s.Bind(2, oldName);
                s.Step();
            }
        }

        // ---------- Settings ----------

        public string? GetSetting(string key)
        {
            using var s = _db.Prepare("SELECT value FROM settings WHERE key = ?");
            s.Bind(1, key);
            return s.Step() ? s.GetString(0) : null;
        }

        public void SetSetting(string key, string value)
        {
            using var s = _db.Prepare(@"
INSERT INTO settings(key, value) VALUES(?, ?)
ON CONFLICT(key) DO UPDATE SET value = excluded.value");
            s.Bind(1, key);
            s.Bind(2, value);
            s.Step();
        }

        // ---------- Vehicles ----------

        public int CountVehicles()
        {
            using var s = _db.Prepare("SELECT COUNT(*) FROM vehicles");
            s.Step();
            return (int)s.GetInt64(0);
        }

        public int AddVehicle(string name, int l, int w, int h, int payload, LoadSide load)
        {
            using var s = _db.Prepare("INSERT INTO vehicles(name,length_mm,width_mm,height_mm,payload_kg,load_side) VALUES(?,?,?,?,?,?)");
            s.Bind(1, name); s.Bind(2, (long)l); s.Bind(3, (long)w); s.Bind(4, (long)h);
            s.Bind(5, (long)payload); s.Bind(6, (long)load);
            s.Step();
            return (int)_db.LastRowId();
        }

        public List<Vehicle> GetVehicles()
        {
            var list = new List<Vehicle>();
            using var s = _db.Prepare("SELECT id,name,length_mm,width_mm,height_mm,payload_kg,load_side FROM vehicles ORDER BY id");
            while (s.Step())
            {
                list.Add(new Vehicle
                {
                    Id = (int)s.GetInt64(0),
                    Name = s.GetString(1) ?? "",
                    LengthMm = (int)s.GetInt64(2),
                    WidthMm = (int)s.GetInt64(3),
                    HeightMm = (int)s.GetInt64(4),
                    PayloadKg = (int)s.GetInt64(5),
                    Load = (LoadSide)s.GetInt64(6),
                });
            }
            return list;
        }

        public void UpdateVehicle(Vehicle v)
        {
            using var s = _db.Prepare("UPDATE vehicles SET name=?,length_mm=?,width_mm=?,height_mm=?,payload_kg=?,load_side=? WHERE id=?");
            s.Bind(1, v.Name); s.Bind(2, (long)v.LengthMm); s.Bind(3, (long)v.WidthMm); s.Bind(4, (long)v.HeightMm);
            s.Bind(5, (long)v.PayloadKg); s.Bind(6, (long)v.Load); s.Bind(7, (long)v.Id);
            s.Step();
        }

        public void DeleteVehicle(int id)
        {
            using var s = _db.Prepare("DELETE FROM vehicles WHERE id=?");
            s.Bind(1, (long)id);
            s.Step();
        }

        // ---------- Shipments ----------

        public int CreateShipment(string name, int vehicleId)
        {
            using var s = _db.Prepare("INSERT INTO shipments(name,created_at,vehicle_id) VALUES(?,?,?)");
            s.Bind(1, name);
            s.Bind(2, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            s.Bind(3, (long)vehicleId);
            s.Step();
            return (int)_db.LastRowId();
        }

        public List<Shipment> GetShipments()
        {
            var list = new List<Shipment>();
            using var s = _db.Prepare("SELECT id,name,created_at,vehicle_id FROM shipments ORDER BY id DESC");
            while (s.Step())
            {
                list.Add(new Shipment
                {
                    Id = (int)s.GetInt64(0),
                    Name = s.GetString(1) ?? "",
                    CreatedAt = ParseDate(s.GetString(2)),
                    VehicleId = (int)s.GetInt64(3),
                });
            }
            return list;
        }

        public void DeleteShipment(int id)
        {
            foreach (var sql in new[]
            {
                "DELETE FROM shipment_items WHERE shipment_id=?",
                "DELETE FROM placements WHERE shipment_id=?",
                "DELETE FROM shipments WHERE id=?",
            })
            {
                using var s = _db.Prepare(sql);
                s.Bind(1, (long)id);
                s.Step();
            }
        }

        // ---------- Cargo types & items ----------

        public int AddCargoType(CargoType c)
        {
            using var s = _db.Prepare(@"
INSERT INTO cargo_types(name,article,length_mm,width_mm,height_mm,weight_kg,stackable,can_rotate,max_load_top_kg)
VALUES(?,?,?,?,?,?,?,?,?)");
            s.Bind(1, c.Name); s.Bind(2, c.Article);
            s.Bind(3, (long)c.LengthMm); s.Bind(4, (long)c.WidthMm); s.Bind(5, (long)c.HeightMm);
            s.Bind(6, c.WeightKg);
            s.Bind(7, (long)(c.Stackable ? 1 : 0)); s.Bind(8, (long)(c.CanRotate ? 1 : 0));
            s.Bind(9, (long)c.MaxLoadTopKg);
            s.Step();
            return (int)_db.LastRowId();
        }

        public void AddShipmentItem(int shipmentId, int cargoTypeId, int qty)
        {
            using var s = _db.Prepare("INSERT INTO shipment_items(shipment_id,cargo_type_id,quantity) VALUES(?,?,?)");
            s.Bind(1, (long)shipmentId); s.Bind(2, (long)cargoTypeId); s.Bind(3, (long)qty);
            s.Step();
        }

        public List<ShipmentItem> GetShipmentItems(int shipmentId)
        {
            var list = new List<ShipmentItem>();
            using var s = _db.Prepare(@"
SELECT si.id, si.shipment_id, si.cargo_type_id, si.quantity,
       ct.name, ct.article, ct.length_mm, ct.width_mm, ct.height_mm,
       ct.weight_kg, ct.stackable, ct.can_rotate, ct.max_load_top_kg
FROM shipment_items si
JOIN cargo_types ct ON ct.id = si.cargo_type_id
WHERE si.shipment_id = ?
ORDER BY si.id");
            s.Bind(1, (long)shipmentId);
            while (s.Step())
            {
                var item = new ShipmentItem
                {
                    Id = (int)s.GetInt64(0),
                    ShipmentId = (int)s.GetInt64(1),
                    CargoTypeId = (int)s.GetInt64(2),
                    Quantity = (int)s.GetInt64(3),
                    Cargo = new CargoType
                    {
                        Id = (int)s.GetInt64(2),
                        Name = s.GetString(4) ?? "",
                        Article = s.GetString(5) ?? "",
                        LengthMm = (int)s.GetInt64(6),
                        WidthMm = (int)s.GetInt64(7),
                        HeightMm = (int)s.GetInt64(8),
                        WeightKg = s.GetDouble(9),
                        Stackable = s.GetInt64(10) != 0,
                        CanRotate = s.GetInt64(11) != 0,
                        MaxLoadTopKg = (int)s.GetInt64(12),
                    },
                };
                list.Add(item);
            }
            return list;
        }

        // ---------- Placements ----------

        public void ClearPlacements(int shipmentId)
        {
            using var s = _db.Prepare("DELETE FROM placements WHERE shipment_id=?");
            s.Bind(1, (long)shipmentId);
            s.Step();
        }

        public void SavePlacements(int shipmentId, List<Placement> placements)
        {
            ClearPlacements(shipmentId);
            foreach (var p in placements)
            {
                using var s = _db.Prepare(@"
INSERT INTO placements(shipment_id,vehicle_index,seq,cargo_type_id,x,y,z,len,wid,hei,tier)
VALUES(?,?,?,?,?,?,?,?,?,?,?)");
                s.Bind(1, (long)shipmentId);
                s.Bind(2, (long)p.VehicleIndex);
                s.Bind(3, (long)p.Seq);
                s.Bind(4, (long)p.CargoTypeId);
                s.Bind(5, p.X); s.Bind(6, p.Y); s.Bind(7, p.Z);
                s.Bind(8, p.Len); s.Bind(9, p.Wid); s.Bind(10, p.Hei);
                s.Bind(11, (long)p.Tier);
                s.Step();
            }
        }

        public void SavePlanTrucks(int shipmentId, List<int> vehicleIds)
        {
            using (var d = _db.Prepare("DELETE FROM plan_trucks WHERE shipment_id=?"))
            {
                d.Bind(1, (long)shipmentId);
                d.Step();
            }
            for (int i = 0; i < vehicleIds.Count; i++)
            {
                using var s = _db.Prepare("INSERT INTO plan_trucks(shipment_id,idx,vehicle_id) VALUES(?,?,?)");
                s.Bind(1, (long)shipmentId);
                s.Bind(2, (long)i);
                s.Bind(3, (long)vehicleIds[i]);
                s.Step();
            }
        }

        // ---------- helpers ----------

        public long LastRowId() => _db.LastRowId();

        public static string? ReadCellString(object? v)
        {
            return v == null ? null : Convert.ToString(v, CultureInfo.InvariantCulture)?.Trim();
        }

        private static DateTime ParseDate(string? s)
        {
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                return d;
            return DateTime.MinValue;
        }
    }
}
