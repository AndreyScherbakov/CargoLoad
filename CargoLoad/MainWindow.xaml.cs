using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;

namespace CargoLoad
{
    /// <summary>Строка таблицы грузов задания (с номером типа).</summary>
    public sealed class ShipmentRow
    {
        public int TypeNo { get; set; }
        public int CargoTypeId { get; set; }
        public string Name { get; set; } = "";
        public string Article { get; set; } = "";
        public int Quantity { get; set; }
        public int LengthMm { get; set; }
        public int WidthMm { get; set; }
        public int HeightMm { get; set; }
        public double UnitWeight { get; set; }
        public double TotalWeight => Math.Round(Quantity * UnitWeight, 2);
        public string StackableText { get; set; } = "";
        public string RotateText { get; set; } = "";
        public string MaxLoadText { get; set; } = "";
    }

    /// <summary>Выбранная машина в ручном режиме.</summary>
    public sealed class TruckPick
    {
        public int VehicleId { get; set; }
        public int Count { get; set; } = 1;
        public string Title { get; set; } = "";
    }

    public partial class MainWindow : Window
    {
        private readonly Database _db;
        private List<Vehicle> _vehicles = new();
        private List<Shipment> _shipments = new();
        private List<TruckPick> _truckPicks = new();
        private List<ShipmentRow> _rows = new();
        private Dictionary<int, int> _typeNoByCargo = new();

        private MultiResult? _multi;
        private int _currentTruck = 0;
        private int _selectedVehicleId = -1;

        private static readonly Color[] Palette =
        {
            Color.FromRgb(0x5B, 0x9D, 0xFF),
            Color.FromRgb(0x4C, 0xC3, 0x8A),
            Color.FromRgb(0xF2, 0xA5, 0x4B),
            Color.FromRgb(0xE8, 0x6A, 0x8A),
            Color.FromRgb(0x9A, 0x7B, 0xF5),
            Color.FromRgb(0x56, 0xC8, 0xD8),
            Color.FromRgb(0xD8, 0xC8, 0x56),
            Color.FromRgb(0xC8, 0x8A, 0x56),
        };

        private static Database OpenDatabase()
        {
            string profileDir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CargoLoad");

            // 1) профиль пользователя, 2) папка программы (portable / ограниченная среда)
            foreach (string dir in new[] { profileDir, AppContext.BaseDirectory })
            {
                try
                {
                    Directory.CreateDirectory(dir);
                    var db = new Database(System.IO.Path.Combine(dir, "cargoload.db"));
                    return db;
                }
                catch (Exception)
                {
                    // недоступно на запись — пробуем следующее место
                }
            }

            throw new InvalidOperationException("Не удалось открыть базу данных ни в профиле, ни рядом с программой.");
        }

        public MainWindow()
        {
            InitializeComponent();

            _db = OpenDatabase();

            Loaded += (_, __) => OnLoaded();
        }

        private void OnLoaded()
        {
            string theme = _db.GetSetting("theme") ?? "dark";
            ApplyTheme(theme != "light");

            RefreshVehicles();
            RefreshTruckTypes();
            RefreshShipments();
            PackModeSelect.SelectedIndex = 0;
            PackRotCombo.SelectedIndex = 0;

            HandleCommandLine();
        }

        // ---------------- Тема ----------------

        private void ApplyTheme(bool dark)
        {
            Theme.Apply(dark);
            ThemeButton.Content = dark ? "Тема: тёмная" : "Тема: светлая";
            if (_multi != null) DrawSelectedTruck();
        }

        private void ThemeButton_Click(object sender, RoutedEventArgs e)
        {
            bool dark = !Theme.IsDark;
            ApplyTheme(dark);
            _db.SetSetting("theme", dark ? "dark" : "light");
        }

        // ---------------- ТС ----------------

        private void RefreshVehicles()
        {
            _vehicles = _db.GetVehicles();
            VehicleTable.ItemsSource = null;
            VehicleTable.ItemsSource = _vehicles;
            if (_vehicles.Count > 0 && VehicleTable.SelectedIndex < 0)
                VehicleTable.SelectedIndex = 0;
            RefreshTruckTypes();
        }

        private void RefreshTruckTypes()
        {
            TruckTypeCombo.ItemsSource = null;
            TruckTypeCombo.ItemsSource = _vehicles
                .Select(v => $"{v.Name} · {v.DimsText} м · {v.PayloadText} т")
                .ToList();
            if (_vehicles.Count > 0 && TruckTypeCombo.SelectedIndex < 0)
                TruckTypeCombo.SelectedIndex = 0;
        }

        private void VehicleTable_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (VehicleTable.SelectedItem is not Vehicle v) return;
            _selectedVehicleId = v.Id;
            VName.Text = v.Name;
            VLen.Text = (v.LengthMm / 1000.0).ToString("0.##", CultureInfo.InvariantCulture);
            VWid.Text = (v.WidthMm / 1000.0).ToString("0.##", CultureInfo.InvariantCulture);
            VHei.Text = (v.HeightMm / 1000.0).ToString("0.##", CultureInfo.InvariantCulture);
            VPayload.Text = (v.PayloadKg / 1000.0).ToString("0.##", CultureInfo.InvariantCulture);
            VLoad.SelectedIndex = LoadToIndex(v.Load);
        }

        private static int LoadToIndex(LoadSide load)
        {
            if ((load & LoadSide.Rear) != 0 && (load & LoadSide.Side) != 0) return 0;
            if ((load & LoadSide.Side) != 0 && (load & LoadSide.Top) != 0) return 1;
            if ((load & LoadSide.Top) != 0) return 2;
            if ((load & LoadSide.Rear) != 0) return 3;
            return 4;
        }

        private static LoadSide IndexToLoad(int index) => index switch
        {
            0 => LoadSide.RearSide,
            1 => LoadSide.SideTop,
            2 => LoadSide.Top,
            3 => LoadSide.Rear,
            _ => LoadSide.Side,
        };

        private void BtnVehAdd_Click(object sender, RoutedEventArgs e)
        {
            _selectedVehicleId = -1;
            VName.Text = ""; VLen.Text = ""; VWid.Text = ""; VHei.Text = ""; VPayload.Text = "";
            VLoad.SelectedIndex = 1;
            VehicleTable.SelectedIndex = -1;
            VehStatus.Text = "Новое ТС: заполните параметры и нажмите «Сохранить».";
        }

        private void BtnVehSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var v = new Vehicle
                {
                    Id = _selectedVehicleId,
                    Name = string.IsNullOrWhiteSpace(VName.Text) ? "ТС" : VName.Text.Trim(),
                    LengthMm = (int)Math.Round(ParseNum(VLen.Text) * 1000),
                    WidthMm = (int)Math.Round(ParseNum(VWid.Text) * 1000),
                    HeightMm = (int)Math.Round(ParseNum(VHei.Text) * 1000),
                    PayloadKg = (int)Math.Round(ParseNum(VPayload.Text) * 1000),
                    Load = IndexToLoad(VLoad.SelectedIndex),
                };
                if (v.LengthMm <= 0 || v.WidthMm <= 0 || v.HeightMm <= 0)
                {
                    VehStatus.Text = "Проверьте размеры кузова (в метрах).";
                    return;
                }

                if (v.Id < 0) _db.AddVehicle(v.Name, v.LengthMm, v.WidthMm, v.HeightMm, v.PayloadKg, v.Load);
                else _db.UpdateVehicle(v);

                RefreshVehicles();
                int idx = _vehicles.FindIndex(x => x.Id == v.Id);
                if (idx >= 0) VehicleTable.SelectedIndex = idx;
                VehStatus.Text = "Сохранено.";
            }
            catch (Exception ex)
            {
                VehStatus.Text = "Ошибка: " + ex.Message;
            }
        }

        private void BtnVehDelete_Click(object sender, RoutedEventArgs e)
        {
            if (VehicleTable.SelectedItem is not Vehicle v) return;
            _db.DeleteVehicle(v.Id);
            _selectedVehicleId = -1;
            RefreshVehicles();
            VehStatus.Text = "Удалено.";
        }

        // ---------------- Задания ----------------

        private void RefreshShipments()
        {
            _shipments = _db.GetShipments();
            var disp = _shipments
                .Select(s => $"{s.Name}  ({s.CreatedAt:dd.MM.yyyy HH:mm})")
                .ToList();
            ShipmentCombo.ItemsSource = null;
            ShipmentCombo.ItemsSource = disp;
            PackShipmentCombo.ItemsSource = null;
            PackShipmentCombo.ItemsSource = disp;

            if (disp.Count > 0 && ShipmentCombo.SelectedIndex < 0) ShipmentCombo.SelectedIndex = 0;
            if (disp.Count > 0 && PackShipmentCombo.SelectedIndex < 0) PackShipmentCombo.SelectedIndex = 0;

            RefreshItems();
        }

        private void SelectShipment(int id)
        {
            int i = _shipments.FindIndex(s => s.Id == id);
            if (i < 0) return;
            ShipmentCombo.SelectedIndex = i;
            PackShipmentCombo.SelectedIndex = i;
        }

        private void ShipmentCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshItems();

        private Shipment? CurrentShipment()
        {
            int i = ShipmentCombo.SelectedIndex;
            return i >= 0 && i < _shipments.Count ? _shipments[i] : null;
        }

        /// <summary>Строки задания с номерами типов (нумерация по типам, а не по ящикам).</summary>
        private void RefreshItems()
        {
            var sh = CurrentShipment();
            _rows = new List<ShipmentRow>();
            _typeNoByCargo = new Dictionary<int, int>();

            if (sh != null)
            {
                int typeNo = 1;
                foreach (var it in _db.GetShipmentItems(sh.Id))
                {
                    var c = it.Cargo;
                    if (c == null) continue;

                    if (!_typeNoByCargo.TryGetValue(c.Id, out int no))
                    {
                        no = typeNo++;
                        _typeNoByCargo[c.Id] = no;
                    }

                    _rows.Add(new ShipmentRow
                    {
                        TypeNo = no,
                        CargoTypeId = c.Id,
                        Name = c.Name,
                        Article = c.Article,
                        Quantity = it.Quantity,
                        LengthMm = c.LengthMm,
                        WidthMm = c.WidthMm,
                        HeightMm = c.HeightMm,
                        UnitWeight = c.WeightKg,
                        StackableText = c.Stackable ? "да" : "нет",
                        RotateText = c.CanRotate ? "да" : "нет",
                        MaxLoadText = c.MaxLoadTopKg > 0 ? c.MaxLoadTopKg.ToString() : "—",
                    });
                }
            }

            ItemsGrid.ItemsSource = null;
            ItemsGrid.ItemsSource = _rows;
        }

        private void BtnNewShipment_Click(object sender, RoutedEventArgs e)
        {
            int id = _db.CreateShipment("Задание " + DateTime.Now.ToString("dd.MM HH:mm:ss"), -1);
            RefreshShipments();
            SelectShipment(id);
        }

        private void BtnDeleteShipment_Click(object sender, RoutedEventArgs e)
        {
            var sh = CurrentShipment();
            if (sh == null) return;
            _db.DeleteShipment(sh.Id);
            RefreshShipments();
        }

        private void BtnImport_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Выберите файл задания на погрузку",
                Filter = "Excel/CSV (*.xlsx;*.csv)|*.xlsx;*.csv|Все файлы (*.*)|*.*",
            };
            if (dlg.ShowDialog() != true) return;

            var sh = CurrentShipment();
            if (sh == null)
            {
                int id = _db.CreateShipment("Импорт: " + System.IO.Path.GetFileName(dlg.FileName), -1);
                RefreshShipments();
                SelectShipment(id);
                sh = CurrentShipment()!;
            }

            try
            {
                var (lines, units) = ImportFile(dlg.FileName, sh.Id);
                RefreshItems();
                MessageBox.Show($"Импортировано позиций: {lines} (единиц груза: {units})",
                    "Импорт", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка импорта: " + ex.Message, "Импорт",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private sealed class ImportLine
        {
            public string Name = "";
            public string Article = "";
            public int Qty;
            public int L, W, H;
            public double UnitWeight;
            public bool Stackable = true;
            public bool CanRotate = true;
            public int MaxLoadTop;
        }

        /// <summary>Импорт с объединением одинаковых позиций (название + габариты + вес единицы).</summary>
        private (int Lines, int Units) ImportFile(string path, int shipmentId)
        {
            SheetReader.Sheet sheet = path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)
                ? SheetReader.ReadCsv(path)
                : SheetReader.ReadXlsx(path);

            if (sheet.RowCount < 2) return (0, 0);

            int cName = sheet.ColumnByName(new[] { "Название", "Наименование", "Name", "Груз" });
            int cArt = sheet.ColumnByName(new[] { "Артикул", "Article" });
            int cQty = sheet.ColumnByName(new[] { "Количество", "Кол-во", "Quantity", "Qty" });
            int cL = sheet.ColumnByName(new[] { "Длина (мм)", "Длина, мм", "Длина", "Length" });
            int cW = sheet.ColumnByName(new[] { "Ширина (мм)", "Ширина, мм", "Ширина", "Width" });
            int cH = sheet.ColumnByName(new[] { "Высота (мм)", "Высота, мм", "Высота", "Height" });
            int cWt = sheet.ColumnByName(new[] { "Вес (кг)", "Вес, кг", "Вес", "Weight" });
            int cSt = sheet.ColumnByName(new[] { "Штабелирование", "Stackable" });
            int cRot = sheet.ColumnByName(new[] { "Кантование", "Rotate" });
            int cMax = sheet.ColumnByName(new[] { "Допустимая нагрузка на груз (кг)", "Допустимая нагрузка", "MaxLoad" });

            if (cName < 0 || cQty < 0 || cL < 0 || cW < 0 || cH < 0)
                throw new InvalidDataException("Не найдены обязательные колонки: Название, Количество, Длина, Ширина, Высота.");

            var lines = new List<ImportLine>();

            for (int r = sheet.HeaderIndex + 1; r < sheet.RowCount; r++)
            {
                string name = Str(sheet.Cell(r, cName));
                if (string.IsNullOrWhiteSpace(name)) continue;

                int qty = Math.Max(1, (int)Math.Round(Num(sheet.Cell(r, cQty))));
                int l = (int)Math.Round(Num(sheet.Cell(r, cL)));
                int w = (int)Math.Round(Num(sheet.Cell(r, cW)));
                int h = (int)Math.Round(Num(sheet.Cell(r, cH)));
                if (l <= 0 || w <= 0 || h <= 0) continue;

                double totalWt = Num(sheet.Cell(r, cWt));
                lines.Add(new ImportLine
                {
                    Name = name.Trim(),
                    Article = cArt >= 0 ? Str(sheet.Cell(r, cArt)) : "",
                    Qty = qty,
                    L = l, W = w, H = h,
                    UnitWeight = Math.Round(qty > 0 ? totalWt / qty : 0, 2),
                    Stackable = IsYes(sheet.Cell(r, cSt)),
                    CanRotate = cRot >= 0 ? IsYes(sheet.Cell(r, cRot)) : true,
                    MaxLoadTop = cMax >= 0 ? (int)Math.Round(Num(sheet.Cell(r, cMax))) : 0,
                });
            }

            // объединяем одинаковые позиции: название + габариты + вес единицы
            var merged = lines
                .GroupBy(x => (
                    x.Name,
                    x.L, x.W, x.H,
                    Weight: Math.Round(x.UnitWeight, 2)))
                .Select(g => new ImportLine
                {
                    Name = g.Key.Name,
                    Article = g.Select(x => x.Article).FirstOrDefault(a => !string.IsNullOrWhiteSpace(a)) ?? "",
                    Qty = g.Sum(x => x.Qty),
                    L = g.Key.L, W = g.Key.W, H = g.Key.H,
                    UnitWeight = g.Key.Weight,
                    Stackable = g.All(x => x.Stackable),
                    CanRotate = g.Any(x => x.CanRotate),
                    MaxLoadTop = g.Max(x => x.MaxLoadTop),
                })
                .OrderByDescending(x => x.Qty)
                .ToList();

            int units = 0;
            foreach (var line in merged)
            {
                var cargo = new CargoType
                {
                    Name = line.Name,
                    Article = line.Article,
                    LengthMm = line.L,
                    WidthMm = line.W,
                    HeightMm = line.H,
                    WeightKg = line.UnitWeight,
                    Stackable = line.Stackable,
                    CanRotate = line.CanRotate,
                    MaxLoadTopKg = line.MaxLoadTop,
                };
                int ctId = _db.AddCargoType(cargo);
                _db.AddShipmentItem(shipmentId, ctId, line.Qty);
                units += line.Qty;
            }

            return (merged.Count, units);
        }

        private void BtnAddItem_Click(object sender, RoutedEventArgs e)
        {
            var sh = CurrentShipment();
            if (sh == null)
            {
                int id = _db.CreateShipment("Задание " + DateTime.Now.ToString("dd.MM HH:mm:ss"), -1);
                RefreshShipments();
                SelectShipment(id);
                sh = CurrentShipment()!;
            }

            int qty = Math.Max(1, (int)Math.Round(ParseNum(MQty.Text)));
            var cargo = new CargoType
            {
                Name = string.IsNullOrWhiteSpace(MName.Text) ? "Груз" : MName.Text.Trim(),
                LengthMm = (int)Math.Round(ParseNum(ML.Text)),
                WidthMm = (int)Math.Round(ParseNum(MW.Text)),
                HeightMm = (int)Math.Round(ParseNum(MH.Text)),
                WeightKg = Math.Round(ParseNum(MWeight.Text), 2),
                Stackable = true,
                CanRotate = true,
            };

            if (cargo.LengthMm <= 0 || cargo.WidthMm <= 0 || cargo.HeightMm <= 0)
            {
                MessageBox.Show("Проверьте габариты груза (мм).", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int ctId = _db.AddCargoType(cargo);
            _db.AddShipmentItem(sh.Id, ctId, qty);
            RefreshItems();
        }

        // ---------------- Раскладка ----------------

        private void PackModeSelect_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ManualPanel == null) return;
            bool auto = PackModeSelect.SelectedIndex == 1;
            ManualPanel.Visibility = auto ? Visibility.Collapsed : Visibility.Visible;
        }

        private void BtnAddTruck_Click(object sender, RoutedEventArgs e)
        {
            int vi = TruckTypeCombo.SelectedIndex;
            if (vi < 0 || vi >= _vehicles.Count) return;

            var v = _vehicles[vi];
            int qty = Math.Max(1, (int)Math.Round(ParseNum(TruckQty.Text)));

            var pick = _truckPicks.FirstOrDefault(p => p.VehicleId == v.Id);
            if (pick != null) pick.Count += qty;
            else _truckPicks.Add(new TruckPick
            {
                VehicleId = v.Id,
                Count = qty,
                Title = $"{v.Name} · {v.DimsText} м · {v.PayloadText} т",
            });

            RefreshTruckPicks();
        }

        private void BtnRemoveTruck_Click(object sender, RoutedEventArgs e)
        {
            int i = TruckList.SelectedIndex;
            if (i < 0 || i >= _truckPicks.Count) return;
            _truckPicks.RemoveAt(i);
            RefreshTruckPicks();
        }

        private void RefreshTruckPicks()
        {
            TruckList.ItemsSource = null;
            TruckList.ItemsSource = _truckPicks
                .Select(p => $"{p.Count} × {p.Title}")
                .ToList();
        }

        private void BtnPack_Click(object sender, RoutedEventArgs e) => RunPack();

        private List<PackItem> BuildPackItems()
        {
            var items = new List<PackItem>();
            var sh = CurrentShipment();
            if (sh == null) return items;

            foreach (var it in _db.GetShipmentItems(sh.Id))
            {
                var c = it.Cargo;
                if (c == null) continue;
                for (int k = 0; k < it.Quantity; k++)
                {
                    items.Add(new PackItem
                    {
                        CargoTypeId = c.Id,
                        Name = c.Name,
                        L = c.LengthMm,
                        W = c.WidthMm,
                        H = c.HeightMm,
                        Weight = c.WeightKg,
                        Stackable = c.Stackable,
                        CanRotate = c.CanRotate,
                    });
                }
            }
            return items;
        }

        private void RunPack()
        {
            int si = PackShipmentCombo.SelectedIndex;
            if (si < 0 || si >= _shipments.Count)
            {
                SummaryText.Text = "Выберите задание.";
                return;
            }

            var shipment = _shipments[si];
            var mode = (RotationMode)Math.Max(0, PackRotCombo.SelectedIndex);
            var items = BuildPackItems();
            if (items.Count == 0)
            {
                SummaryText.Text = "В задании нет груза.";
                return;
            }

            bool auto = PackModeSelect.SelectedIndex == 1;
            MultiResult result;

            if (auto)
            {
                result = Packer.PlanAuto(items, _vehicles, mode);
            }
            else
            {
                if (_truckPicks.Count == 0)
                {
                    SummaryText.Text = "Добавьте машины или выберите автоматический режим.";
                    return;
                }

                var plan = new List<TruckPlan>();
                foreach (var pick in _truckPicks)
                {
                    var v = _vehicles.FirstOrDefault(x => x.Id == pick.VehicleId);
                    if (v == null) continue;
                    plan.Add(new TruckPlan { Vehicle = v, Count = pick.Count });
                }
                result = Packer.PackMulti(items, plan, mode);
            }

            _multi = result;
            _db.SavePlacements(shipment.Id, Packer.ToPlacements(result));
            _db.SavePlanTrucks(shipment.Id, result.Trucks.Select(t => t.Vehicle.Id).ToList());

            FillMachineList();
            FillSummary(items.Count);
            if (result.Trucks.Count > 0) MachineList.SelectedIndex = 0;
            else
            {
                PackCanvas.Children.Clear();
                LegendPanel.Children.Clear();
                ReportText.Text = "Раскладка не выполнена.";
                UnplacedText.Text = "";
            }
        }

        private void FillMachineList()
        {
            if (_multi == null) return;

            MachineList.ItemsSource = _multi.Trucks
                .Select(t =>
                {
                    double fill = t.Result.TruckVolumeM3 > 0
                        ? t.Result.UsedVolumeM3 / t.Result.TruckVolumeM3 * 100
                        : 0;
                    return $"Машина {t.Index + 1}: {t.Vehicle.Name} — {fill:0}%";
                })
                .ToList();
        }

        private void FillSummary(int totalItems)
        {
            if (_multi == null) return;

            double fill = _multi.TruckVolumeM3 > 0
                ? _multi.UsedVolumeM3 / _multi.TruckVolumeM3 * 100
                : 0;

            string unplaced = _multi.Unplaced.Count == 0
                ? ""
                : $"  ·  не размещено: {_multi.Unplaced.Count}";

            SummaryText.Text =
                $"Машин: {_multi.Trucks.Count}  ·  размещено: {totalItems - _multi.Unplaced.Count} из {totalItems}" +
                $"  ·  заполнение объёма: {fill:0.0}%  ·  общий вес: {_multi.TotalWeightKg / 1000.0:0.###} т{unplaced}";

            if (_multi.Unplaced.Count == 0)
            {
                UnplacedText.Text = "Всё размещено.";
            }
            else
            {
                var groups = _multi.Unplaced
                    .GroupBy(u => u.Name)
                    .Select(g =>
                    {
                        string no = _typeNoByCargo.TryGetValue(g.First().CargoTypeId, out int n) ? $"№{n} " : "";
                        return $"{no}{g.Key} × {g.Count()}";
                    });
                UnplacedText.Text = string.Join("\n", groups);
            }
        }

        private void MachineList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MachineList.SelectedIndex >= 0)
            {
                _currentTruck = MachineList.SelectedIndex;
                DrawSelectedTruck();
            }
        }

        private void DrawSelectedTruck()
        {
            if (_multi == null || _currentTruck < 0 || _currentTruck >= _multi.Trucks.Count) return;

            var truck = _multi.Trucks[_currentTruck];
            var result = truck.Result;
            var vehicle = truck.Vehicle;

            PackCanvas.Children.Clear();
            LegendPanel.Children.Clear();
            CanvasTitle.Text = $"Вид сверху — машина {truck.Index + 1}: {vehicle.Name}";

            double maxLen = vehicle.LengthMm, maxWid = vehicle.WidthMm;
            double drawW = 860;
            double scale = drawW / maxLen;
            double drawH = Math.Max(10, maxWid * scale);
            PackCanvas.Width = drawW;
            PackCanvas.Height = drawH;

            var border = new Rectangle
            {
                Width = drawW,
                Height = drawH,
                Stroke = Theme.GetBrush("Truck"),
                StrokeThickness = 2,
                Fill = Theme.GetBrush("CanvasBox"),
            };
            PackCanvas.Children.Add(border);
            Canvas.SetLeft(border, 0);
            Canvas.SetTop(border, 0);

            foreach (var p in result.Placed)
            {
                int typeNo = _typeNoByCargo.TryGetValue(p.Item.CargoTypeId, out int n) ? n : 0;
                var color = Palette[(typeNo - 1 + Palette.Length) % Palette.Length];

                var rect = new Rectangle
                {
                    Width = Math.Max(2, p.L * scale),
                    Height = Math.Max(2, p.W * scale),
                    Fill = new SolidColorBrush(p.Level == 0
                        ? Color.FromArgb(235, color.R, color.G, color.B)
                        : Color.FromArgb(150, color.R, color.G, color.B)),
                    Stroke = p.Level == 0
                        ? Theme.GetBrush("TierInk")
                        : new SolidColorBrush(Color.FromRgb(0xE8, 0xA0, 0x40)),
                    StrokeThickness = p.Level == 0 ? 1 : 1.5,
                    ToolTip = $"№ типа {typeNo}\n{p.Item.Name}\n" +
                              $"X={p.X:0}, Y={p.Y:0}, Z={p.Z:0} мм\n" +
                              $"{p.L:0}×{p.W:0}×{p.H:0} мм, {p.Item.Weight:0.#} кг\n" +
                              $"ярус: {p.Level + 1}",
                };
                if (p.Level > 0)
                    rect.StrokeDashArray = new DoubleCollection { 4, 2 };

                Canvas.SetLeft(rect, p.X * scale);
                Canvas.SetTop(rect, p.Y * scale);
                PackCanvas.Children.Add(rect);

                double w = p.L * scale, h = p.W * scale;
                if (w > 18 && h > 14)
                {
                    var txt = new TextBlock
                    {
                        Text = typeNo.ToString(),
                        FontSize = 11,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = Brushes.White,
                    };

                    // 1-й ярус — левый верхний угол, 2-й — правый верхний
                    if (p.Level == 0)
                    {
                        Canvas.SetLeft(txt, p.X * scale + 3);
                        Canvas.SetTop(txt, p.Y * scale + 1);
                    }
                    else
                    {
                        Canvas.SetLeft(txt, p.X * scale + Math.Max(0, w - 14));
                        Canvas.SetTop(txt, p.Y * scale + 1);
                    }
                    PackCanvas.Children.Add(txt);
                }
            }

            FillLegend(result);
            FillTruckReport(truck);
        }

        private void FillLegend(PackResult result)
        {
            var types = result.Placed
                .Select(p => p.Item.CargoTypeId)
                .Distinct()
                .OrderBy(id => _typeNoByCargo.TryGetValue(id, out int n) ? n : int.MaxValue);

            foreach (int cargoId in types)
            {
                int typeNo = _typeNoByCargo.TryGetValue(cargoId, out int n) ? n : 0;
                var color = Palette[(typeNo - 1 + Palette.Length) % Palette.Length];
                var item = result.Placed.First(p => p.Item.CargoTypeId == cargoId).Item;

                var row = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 0, 0, 6),
                };
                row.Children.Add(new Border
                {
                    Width = 12,
                    Height = 12,
                    CornerRadius = new CornerRadius(3),
                    Background = new SolidColorBrush(color),
                    Margin = new Thickness(0, 0, 8, 0),
                });
                row.Children.Add(new TextBlock
                {
                    Text = $"№{typeNo} · {item.Name} · {item.L:0}×{item.W:0}×{item.H:0} мм · {item.Weight:0.#} кг",
                    Foreground = Theme.GetBrush("Text"),
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                });
                LegendPanel.Children.Add(row);
            }
        }

        private void FillTruckReport(TruckResult truck)
        {
            var r = truck.Result;
            var v = truck.Vehicle;

            double fillVol = r.TruckVolumeM3 > 0 ? r.UsedVolumeM3 / r.TruckVolumeM3 * 100 : 0;
            double fillWt = v.PayloadKg > 0 ? r.TotalWeightKg / v.PayloadKg * 100 : 0;

            int tier0 = r.Placed.Count(p => p.Level == 0);
            int tier1 = r.Placed.Count(p => p.Level == 1);

            double mx = 0, my = 0, mz = 0, tw = 0;
            foreach (var p in r.Placed)
            {
                double w = p.Item.Weight;
                mx += (p.X + p.L / 2) * w;
                my += (p.Y + p.W / 2) * w;
                mz += (p.Z + p.H / 2) * w;
                tw += w;
            }
            string cg = tw > 0
                ? $"{mx / tw / 1000:0.00} × {my / tw / 1000:0.00} × {mz / tw / 1000:0.00} м"
                : "—";

            ReportText.Text =
                $"Машина {truck.Index + 1}: {v.Name}\n" +
                $"Кузов: {v.DimsText} м ({r.TruckVolumeM3:0.0} м³), {v.PayloadText} т\n\n" +
                $"Мест: {r.Placed.Count} (1-й ярус: {tier0}, 2-й ярус: {tier1})\n" +
                $"Заполнение объёма: {fillVol:0.0} %\n" +
                $"Вес: {r.TotalWeightKg / 1000.0:0.###} т ({fillWt:0.0} %)\n" +
                $"Центр тяжести: {cg}";
        }

        // ---------------- Демо-режим / CLI ----------------

        private void HandleCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            string? file = args.Skip(1).FirstOrDefault(a =>
                a.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ||
                a.EndsWith(".csv", StringComparison.OrdinalIgnoreCase));
            if (file == null || !File.Exists(file)) return;

            try
            {
                int sid = _db.CreateShipment("Импорт: " + System.IO.Path.GetFileName(file), -1);
                ImportFile(file, sid);
                RefreshShipments();
                SelectShipment(sid);

                if (args.Contains("--pack"))
                {
                    PackModeSelect.SelectedIndex = 1; // авто
                    RunPack();
                    MainTab.SelectedIndex = 2;
                }
            }
            catch (Exception ex)
            {
                // демо-режим не должен ронять приложение, но ошибку показываем
                SummaryText.Text = "Ошибка импорта: " + ex.Message;
            }
        }

        // ---------------- helpers ----------------

        private static double Num(object? v)
        {
            if (v is double d) return d;
            if (v is long l) return l;
            if (v is int i) return i;
            return ParseNum(Convert.ToString(v, CultureInfo.InvariantCulture) ?? "0");
        }

        private static double ParseNum(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0;
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return d;
            if (double.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
            return 0;
        }

        private static string Str(object? v)
            => v == null ? "" : Convert.ToString(v, CultureInfo.InvariantCulture)?.Trim() ?? "";

        private static bool IsYes(object? v)
        {
            if (v == null) return false;
            string s = Convert.ToString(v, CultureInfo.InvariantCulture)?.Trim().ToLowerInvariant() ?? "";
            return s == "да" || s == "yes" || s == "y" || s == "1" || s == "true" || s == "+";
        }
    }
}