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
    public partial class MainWindow : Window
    {
        private readonly Database _db;
        private List<Vehicle> _vehicles = new();
        private List<Shipment> _shipments = new();
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

        public MainWindow()
        {
            InitializeComponent();

            string dbPath = System.IO.Path.Combine(AppContext.BaseDirectory, "cargoload.db");
            _db = new Database(dbPath);

            Loaded += (_, __) => OnLoaded();
        }

        private void OnLoaded()
        {
            RefreshVehicles();
            RefreshShipments();
            PackModeCombo.SelectedIndex = 0;

            // демо-режим для проверки: CargoLoad.exe "путь.xlsx" [--pack]
            string[] args = Environment.GetCommandLineArgs();
            string? file = args.Skip(1).FirstOrDefault(a => a.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
                                                           || a.EndsWith(".csv", StringComparison.OrdinalIgnoreCase));
            if (file != null)
            {
                int sid = _db.CreateShipment("Импорт: " + System.IO.Path.GetFileName(file), -1);
                ImportFile(file, sid);
                RefreshShipments();
                SelectShipment(sid);
                if (args.Contains("--pack"))
                {
                    var euro = _vehicles.FirstOrDefault(v => v.Name.Contains("13,6"));
                    if (euro != null)
                        PackVehicleCombo.SelectedIndex = _vehicles.IndexOf(euro);
                    PackShipmentCombo.SelectedIndex = _shipments.IndexOf(_shipments.First(s => s.Id == sid));
                    RunPack();
                    MainTab.SelectedIndex = 2;
                }
            }
        }

        // ---------------- ТС ----------------

        private void RefreshVehicles()
        {
            _vehicles = _db.GetVehicles();
            VehicleList.ItemsSource = _vehicles.Select(v =>
                $"{v.Name}  ·  {v.LengthMm / 1000.0:0.##}×{v.WidthMm / 1000.0:0.##}×{v.HeightMm / 1000.0:0.##} м  ·  {v.PayloadKg / 1000.0:0.##} т").ToList();
            PackVehicleCombo.ItemsSource = _vehicles.Select(v =>
                $"{v.Name} ({v.LengthMm / 1000.0:0.##}×{v.WidthMm / 1000.0:0.##}×{v.HeightMm / 1000.0:0.##} м, {v.PayloadKg / 1000.0:0.##} т)").ToList();

            if (VehicleList.Items.Count > 0 && VehicleList.SelectedIndex < 0)
                VehicleList.SelectedIndex = 0;
            if (PackVehicleCombo.Items.Count > 0 && PackVehicleCombo.SelectedIndex < 0)
                PackVehicleCombo.SelectedIndex = 0;
        }

        private void VehicleList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            int i = VehicleList.SelectedIndex;
            if (i < 0 || i >= _vehicles.Count) return;
            var v = _vehicles[i];
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
            if ((load & LoadSide.Rear) != 0 && (load & LoadSide.Side) != 0) return 0; // зад/бок
            if ((load & LoadSide.Side) != 0 && (load & LoadSide.Top) != 0) return 1;   // бок/верх
            if ((load & LoadSide.Top) != 0) return 2;
            if ((load & LoadSide.Rear) != 0) return 3;
            return 4; // бок
        }

        private void BtnVehAdd_Click(object sender, RoutedEventArgs e)
        {
            _selectedVehicleId = -1;
            VName.Text = ""; VLen.Text = ""; VWid.Text = ""; VHei.Text = ""; VPayload.Text = "";
            VLoad.SelectedIndex = 1;
            VehicleList.SelectedIndex = -1;
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
                if (idx >= 0) VehicleList.SelectedIndex = idx;
                VehStatus.Text = "Сохранено.";
            }
            catch (Exception ex)
            {
                VehStatus.Text = "Ошибка: " + ex.Message;
            }
        }

        private void BtnVehDelete_Click(object sender, RoutedEventArgs e)
        {
            int i = VehicleList.SelectedIndex;
            if (i < 0 || i >= _vehicles.Count) return;
            _db.DeleteVehicle(_vehicles[i].Id);
            _selectedVehicleId = -1;
            RefreshVehicles();
            VehStatus.Text = "Удалено.";
        }

        private static LoadSide IndexToLoad(int index)
        {
            return index switch
            {
                0 => LoadSide.RearSide,
                1 => LoadSide.SideTop,
                2 => LoadSide.Top,
                3 => LoadSide.Rear,
                _ => LoadSide.Side,
            };
        }

        // ---------------- Задания ----------------

        private void RefreshShipments()
        {
            _shipments = _db.GetShipments();
            var disp = _shipments.Select(s =>
                $"{s.Name}  ({s.CreatedAt:dd.MM.yyyy HH:mm})").ToList();
            ShipmentCombo.ItemsSource = disp;
            PackShipmentCombo.ItemsSource = disp;
            if (ShipmentCombo.Items.Count > 0 && ShipmentCombo.SelectedIndex < 0)
                ShipmentCombo.SelectedIndex = 0;
            if (PackShipmentCombo.Items.Count > 0 && PackShipmentCombo.SelectedIndex < 0)
                PackShipmentCombo.SelectedIndex = 0;
            RefreshItems();
        }

        private void SelectShipment(int id)
        {
            int i = _shipments.FindIndex(s => s.Id == id);
            if (i >= 0)
            {
                ShipmentCombo.SelectedIndex = i;
                PackShipmentCombo.SelectedIndex = i;
            }
        }

        private void ShipmentCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshItems();

        private Shipment? CurrentShipment()
        {
            int i = ShipmentCombo.SelectedIndex;
            if (i < 0 || i >= _shipments.Count) return null;
            return _shipments[i];
        }

        private void RefreshItems()
        {
            var sh = CurrentShipment();
            ItemsGrid.ItemsSource = sh == null ? null : _db.GetShipmentItems(sh.Id);
        }

        private void BtnNewShipment_Click(object sender, RoutedEventArgs e)
        {
            var sh = CurrentShipment();
            int vid = sh?.VehicleId ?? -1;
            int id = _db.CreateShipment("Задание " + DateTime.Now.ToString("dd.MM HH:mm:ss"), vid);
            RefreshShipments();
            SelectShipment(id);
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

            int count = ImportFile(dlg.FileName, sh.Id);
            RefreshItems();
            MessageBox.Show($"Импортировано строк: {count}", "Импорт", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private int ImportFile(string path, int shipmentId)
        {
            SheetReader.Sheet sheet = path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)
                ? SheetReader.ReadCsv(path)
                : SheetReader.ReadXlsx(path);

            if (sheet.RowCount < 2) return 0;

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

            int count = 0;
            for (int r = sheet.HeaderIndex + 1; r < sheet.RowCount; r++)
            {
                string name = Str(sheet.Cell(r, cName));
                if (string.IsNullOrWhiteSpace(name)) continue;
                int qty = Math.Max(1, (int)Math.Round(Num(sheet.Cell(r, cQty))));
                int l = (int)Math.Round(Num(sheet.Cell(r, cL)));
                int w = (int)Math.Round(Num(sheet.Cell(r, cW)));
                int h = (int)Math.Round(Num(sheet.Cell(r, cH)));
                if (l <= 0 || w <= 0 || h <= 0) continue;

                // «Вес (кг)» в файле — суммарный по строке; делим на количество
                double totalWt = Num(sheet.Cell(r, cWt));
                double unitWt = qty > 0 ? totalWt / qty : 0;

                var cargo = new CargoType
                {
                    Name = name.Trim(),
                    Article = cArt >= 0 ? Str(sheet.Cell(r, cArt)) : "",
                    LengthMm = l, WidthMm = w, HeightMm = h,
                    WeightKg = Math.Round(unitWt, 2),
                    Stackable = IsYes(sheet.Cell(r, cSt)),
                    CanRotate = cRot >= 0 ? IsYes(sheet.Cell(r, cRot)) : true,
                    MaxLoadTopKg = cMax >= 0 ? (int)Math.Round(Num(sheet.Cell(r, cMax))) : 0,
                };
                int ctId = _db.AddCargoType(cargo);
                _db.AddShipmentItem(shipmentId, ctId, qty);
                count++;
            }
            return count;
        }

        private void BtnDeleteShipment_Click(object sender, RoutedEventArgs e)
        {
            var sh = CurrentShipment();
            if (sh == null) return;
            _db.DeleteShipment(sh.Id);
            RefreshShipments();
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
                MessageBox.Show("Проверьте габариты груза (мм).", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            int ctId = _db.AddCargoType(cargo);
            _db.AddShipmentItem(sh.Id, ctId, qty);
            RefreshItems();
        }

        // ---------------- Раскладка ----------------

        private void BtnPack_Click(object sender, RoutedEventArgs e) => RunPack();

        private void RunPack()
        {
            int si = PackShipmentCombo.SelectedIndex;
            int vi = PackVehicleCombo.SelectedIndex;
            if (si < 0 || si >= _shipments.Count || vi < 0 || vi >= _vehicles.Count)
            {
                ReportText.Text = "Выберите задание и транспортное средство.";
                return;
            }

            var shipment = _shipments[si];
            var vehicle = _vehicles[vi];
            var mode = (RotationMode)PackModeCombo.SelectedIndex;

            var items = new List<PackItem>();
            foreach (var it in _db.GetShipmentItems(shipment.Id))
            {
                if (it.Cargo == null) continue;
                for (int k = 0; k < it.Quantity; k++)
                {
                    items.Add(new PackItem
                    {
                        CargoTypeId = it.CargoTypeId,
                        Name = it.Cargo.Name,
                        L = it.Cargo.LengthMm,
                        W = it.Cargo.WidthMm,
                        H = it.Cargo.HeightMm,
                        Weight = it.Cargo.WeightKg,
                        Stackable = it.Cargo.Stackable,
                        CanRotate = it.Cargo.CanRotate,
                    });
                }
            }

            var result = Packer.Pack(items, vehicle, mode);
            var placements = Packer.ToPlacements(result);
            _db.SavePlacements(shipment.Id, placements);

            DrawPlan(result, vehicle);
            FillReport(result, vehicle, placements, items.Count);
        }

        private void DrawPlan(PackResult result, Vehicle vehicle)
        {
            PackCanvas.Children.Clear();
            LegendPanel.Children.Clear();

            double maxLen = vehicle.LengthMm, maxWid = vehicle.WidthMm;
            double drawW = 900;
            double scale = drawW / maxLen;
            double drawH = Math.Max(10, maxWid * scale);
            PackCanvas.Width = drawW;
            PackCanvas.Height = drawH;

            var border = new Rectangle
            {
                Width = drawW,
                Height = drawH,
                Stroke = new SolidColorBrush(Color.FromRgb(0x8D, 0x96, 0xA6)),
                StrokeThickness = 2,
                Fill = new SolidColorBrush(Color.FromRgb(0x14, 0x17, 0x1C)),
            };
            PackCanvas.Children.Add(border);
            Canvas.SetLeft(border, 0);
            Canvas.SetTop(border, 0);

            var colorIndex = new Dictionary<int, int>();
            int nextColor = 0;
            foreach (var p in result.Placed)
            {
                if (!colorIndex.ContainsKey(p.Item.CargoTypeId))
                    colorIndex[p.Item.CargoTypeId] = nextColor++ % Palette.Length;
            }

            int seq = 1;

            foreach (var p in result.Placed)
            {
                int ci = colorIndex[p.Item.CargoTypeId];
                var c = Palette[ci];

                var rect = new Rectangle
                {
                    Width = Math.Max(2, p.L * scale),
                    Height = Math.Max(2, p.W * scale),
                    Fill = new SolidColorBrush(Color.FromArgb(p.Level == 0 ? (byte)230 : (byte)150, c.R, c.G, c.B)),
                    Stroke = new SolidColorBrush(Color.FromRgb(0x0E, 0x10, 0x14)),
                    StrokeThickness = 1,
                };
                Canvas.SetLeft(rect, p.X * scale);
                Canvas.SetTop(rect, p.Y * scale);
                PackCanvas.Children.Add(rect);

                if (p.L * scale > 30 && p.W * scale > 18)
                {
                    var txt = new TextBlock
                    {
                        Text = seq.ToString(),
                        FontSize = 10,
                        Foreground = Brushes.White,
                    };
                    Canvas.SetLeft(txt, p.X * scale + 3);
                    Canvas.SetTop(txt, p.Y * scale + 2);
                    PackCanvas.Children.Add(txt);
                }
                seq++;
            }

            foreach (var kv in colorIndex.OrderBy(k => k.Value))
            {
                var ct = result.Placed.First(p => p.Item.CargoTypeId == kv.Key).Item;
                var swatch = new Border
                {
                    Width = 12,
                    Height = 12,
                    CornerRadius = new CornerRadius(3),
                    Background = new SolidColorBrush(Palette[kv.Value % Palette.Length]),
                    Margin = new Thickness(0, 0, 8, 0),
                };
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
                row.Children.Add(swatch);
                row.Children.Add(new TextBlock
                {
                    Text = $"{ct.Name}  {ct.L:0}×{ct.W:0}×{ct.H:0} мм, {ct.Weight:0.#} кг",
                    Foreground = new SolidColorBrush(Color.FromRgb(0xC7, 0xCD, 0xD8)),
                    FontSize = 12,
                });
                LegendPanel.Children.Add(row);
            }
        }

        private void FillReport(PackResult result, Vehicle vehicle, List<Placement> placements, int totalItems)
        {
            double fillVol = result.TruckVolumeM3 > 0 ? result.UsedVolumeM3 / result.TruckVolumeM3 * 100 : 0;
            double payloadKg = vehicle.PayloadKg;
            double fillWt = payloadKg > 0 ? result.TotalWeightKg / payloadKg * 100 : 0;

            double mx = 0, my = 0, mz = 0, tw = 0;
            foreach (var p in placements)
            {
                double w = p.WeightKg;
                mx += (p.X + p.Len / 2) * w;
                my += (p.Y + p.Wid / 2) * w;
                mz += (p.Z + p.Hei / 2) * w;
                tw += w;
            }
            string cg = tw > 0
                ? $"{mx / tw / 1000:0.00} × {my / tw / 1000:0.00} × {mz / tw / 1000:0.00} м (Д×Ш×В)"
                : "—";

            ReportText.Text =
                $"ТС: {vehicle.Name}\n" +
                $"Кузов: {vehicle.LengthMm / 1000.0:0.##} × {vehicle.WidthMm / 1000.0:0.##} × {vehicle.HeightMm / 1000.0:0.##} м ({result.TruckVolumeM3:0.0} м³)\n" +
                $"Грузоподъёмность: {payloadKg / 1000.0:0.#} т\n\n" +
                $"Размещено: {result.Placed.Count} из {totalItems} единиц\n" +
                $"Заполнение объёма: {fillVol:0.0} %\n" +
                $"Вес груза: {result.TotalWeightKg / 1000.0:0.###} т ({fillWt:0.0} %)\n" +
                $"Центр тяжести: {cg}";

            if (result.Unplaced.Count == 0)
                UnplacedText.Text = "Всё размещено.";
            else
            {
                var groups = result.Unplaced.GroupBy(u => u.Name)
                    .Select(g => $"{g.Key} × {g.Count()}").ToList();
                UnplacedText.Text = string.Join("\n", groups);
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
        {
            return v == null ? "" : Convert.ToString(v, CultureInfo.InvariantCulture)?.Trim() ?? "";
        }

        private static bool IsYes(object? v)
        {
            if (v == null) return false;
            string s = Convert.ToString(v, CultureInfo.InvariantCulture)?.Trim().ToLowerInvariant() ?? "";
            return s == "да" || s == "yes" || s == "y" || s == "1" || s == "true" || s == "+";
        }
    }
}
