using ArchiveSystem.Core.Helpers;
using ArchiveSystem.Core.Models;
using ArchiveSystem.Core.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ArchiveSystem.Views.Pages
{
    public partial class ManagementPage : Page
    {
        private readonly ManagementService _service;
        private Management? _selectedManagement;
        private System.Windows.Threading.DispatcherTimer? _debounce;

        // ── THEME HELPERS ─────────────────────────────────────────────────────

        private static readonly Color MidnightBase = (Color)ColorConverter.ConvertFromString("#0A1628");
        private static readonly Color NavyPanel = (Color)ColorConverter.ConvertFromString("#0D1F3C");
        private static readonly Color BorderColor = (Color)ColorConverter.ConvertFromString("#1E3050");
        private static readonly Color EmeraldMid = (Color)ColorConverter.ConvertFromString("#1a7a60");
        private static readonly Color RoseGold = (Color)ColorConverter.ConvertFromString("#C9966E");
        private static readonly Color DangerRed = (Color)ColorConverter.ConvertFromString("#C62828");
        private static readonly Color TextSecondary = (Color)ColorConverter.ConvertFromString("#8A9BB5");
        private static readonly Color SubText = (Color)ColorConverter.ConvertFromString("#4A5A7A");

        private static SolidColorBrush Brush(Color c, double opacity = 1)
            => new SolidColorBrush(c) { Opacity = opacity };

        private static SolidColorBrush Brush(string hex, double opacity = 1)
            => Brush((Color)ColorConverter.ConvertFromString(hex), opacity);

        /// <summary>Returns a themed Window shell with dark navy background and border.</summary>
        private Window MakeThemedWindow(string title, double width, double height)
        {
            return new Window
            {
                Title = title,
                Width = width,
                Height = height,
                MinWidth = width * 0.8,
                ResizeMode = ResizeMode.CanResizeWithGrip,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this),
                FlowDirection = FlowDirection.RightToLeft,
                Background = Brush(MidnightBase),
                BorderBrush = Brush(BorderColor, 0.80),
                BorderThickness = new Thickness(1),
                FontFamily = new FontFamily("Noto Kufi Arabic, Segoe UI"),
            };
        }

        /// <summary>Themed header strip inside a popup.</summary>
        private Border MakeDialogHeader(string title, string? subtitle = null)
        {
            var panel = new StackPanel { Margin = new Thickness(20, 14, 20, 14) };

            panel.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 17,
                FontWeight = FontWeights.Bold,
                Foreground = Brush(RoseGold),
            });

            if (!string.IsNullOrWhiteSpace(subtitle))
                panel.Children.Add(new TextBlock
                {
                    Text = subtitle,
                    FontSize = 11,
                    Foreground = Brush(SubText),
                    Margin = new Thickness(0, 3, 0, 0),
                });

            return new Border
            {
                Background = Brush(NavyPanel, 0.90),
                BorderBrush = Brush(BorderColor, 0.80),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = panel,
            };
        }

        /// <summary>Thin separator line between header and content.</summary>
        private static Border MakeSeparator()
            => new Border
            {
                Height = 1,
                Background = Brush((Color)ColorConverter.ConvertFromString("#1E3050"), 0.60),
                Margin = new Thickness(0),
            };

        // ── INIT ──────────────────────────────────────────────────────────────

        public ManagementPage()
        {
            InitializeComponent();
            _service = new ManagementService(App.Database);
            MonthCombo.SelectedIndex = 0;
            Loaded += (s, e) => Initialize();
        }

        private void Initialize()
        {
            if (PermissionHelper.DenyPage(this, Permissions.ManageManagements)) return;
            LoadManagements();
            ApplyPermissions();
        }

        private void ApplyPermissions()
        {
            bool canManage = PermissionHelper.Can(Permissions.ManageManagements);
            AddRootManagementBtn.Visibility = canManage ? Visibility.Visible : Visibility.Collapsed;
            AddSubManagementBtn.Visibility = canManage ? Visibility.Visible : Visibility.Collapsed;
            EditManagementBtn.Visibility = canManage ? Visibility.Visible : Visibility.Collapsed;
            DeleteManagementBtn.Visibility = canManage ? Visibility.Visible : Visibility.Collapsed;
            AddDossierBtn.Visibility = canManage ? Visibility.Visible : Visibility.Collapsed;
            EditDossierBtn.Visibility = canManage ? Visibility.Visible : Visibility.Collapsed;
            DeleteDossierBtn.Visibility = canManage ? Visibility.Visible : Visibility.Collapsed;
            ManageTypesBtn.Visibility = canManage ? Visibility.Visible : Visibility.Collapsed;
        }

        // ── MANAGEMENT TREE ───────────────────────────────────────────────────

        private void LoadManagements()
        {
            var all = _service.GetAllManagements();
            ManagementsList.ItemsSource = all;
        }

        private void ManagementsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedManagement = ManagementsList.SelectedItem as Management;
            bool hasSelection = _selectedManagement != null;
            bool isRoot = _selectedManagement?.ParentManagementId == null;

            AddSubManagementBtn.IsEnabled = hasSelection && isRoot;
            EditManagementBtn.IsEnabled = hasSelection;
            DeleteManagementBtn.IsEnabled = hasSelection;
            AddDossierBtn.IsEnabled = hasSelection;
            EditDossierBtn.IsEnabled = false;
            DeleteDossierBtn.IsEnabled = false;

            if (hasSelection)
            {
                SelectedManagementName.Text = _selectedManagement!.Name;
                if (!string.IsNullOrWhiteSpace(_selectedManagement.Description))
                {
                    SelectedManagementDesc.Text = _selectedManagement.Description;
                    SelectedManagementDesc.Visibility = Visibility.Visible;
                }
                else
                {
                    SelectedManagementDesc.Visibility = Visibility.Collapsed;
                }

                ManagementInfoBar.Visibility = Visibility.Visible;
                FiltersBar.Visibility = Visibility.Visible;
                ResultBar.Visibility = Visibility.Visible;
                NoManagementHint.Visibility = Visibility.Collapsed;

                LoadYearFilter();
                LoadTypeFilter();
                LoadDossiers();
            }
            else
            {
                ManagementInfoBar.Visibility = Visibility.Collapsed;
                FiltersBar.Visibility = Visibility.Collapsed;
                ResultBar.Visibility = Visibility.Collapsed;
                DossiersGrid.ItemsSource = null;
                EmptyHint.Visibility = Visibility.Collapsed;
                NoManagementHint.Visibility = Visibility.Visible;
            }
        }

        // ── MANAGEMENT CRUD ───────────────────────────────────────────────────

        private void AddRootManagement_Click(object sender, RoutedEventArgs e)
        {
            string? name = ShowInputDialog("أدخل اسم الإدارات الجديدة:", "إضافة إدارة");
            if (string.IsNullOrWhiteSpace(name)) return;

            string? desc = ShowInputDialog("وصف الإدارات (اختياري)", "وصف الإدارات");

            var err = _service.CreateManagement(name, null, desc);
            if (err != null) { ShowError(err); return; }

            LoadManagements();
        }

        private void AddSubManagement_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedManagement == null) return;

            string? name = ShowInputDialog(
                $"أدخل اسم الشعبة أو القسم تحت '{_selectedManagement.Name}':",
                "إضافة شعبة أو قسم");
            if (string.IsNullOrWhiteSpace(name)) return;

            var err = _service.CreateManagement(name, _selectedManagement.ManagementId, null);
            if (err != null) { ShowError(err); return; }

            LoadManagements();
        }

        private void EditManagement_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedManagement == null) return;

            string? name = ShowInputDialog("تعديل اسم الإدارات:", "تعديل الإدارات",
                defaultValue: _selectedManagement.Name);
            if (string.IsNullOrWhiteSpace(name)) return;

            string? desc = ShowInputDialog("وصف الإدارات (اختياري)", "وصف الإدارات",
                defaultValue: _selectedManagement.Description ?? "");

            var err = _service.UpdateManagement(_selectedManagement.ManagementId, name, desc);
            if (err != null) { ShowError(err); return; }

            LoadManagements();
        }

        private void DeleteManagement_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedManagement == null) return;

            var confirm = MessageBox.Show(
                $"هل تريد حذف الإدارات '{_selectedManagement.Name}'؟\n\nلا يمكن حذفها إذا كانت تحتوي على شعبة أو قسم أو دوسيات.",
                "تأكيد الحذف", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            var err = _service.DeleteManagement(_selectedManagement.ManagementId);
            if (err != null) { ShowError(err); return; }

            _selectedManagement = null;
            LoadManagements();
        }

        private void DeleteDossier_Click(object sender, RoutedEventArgs e)
        {
            if (DossiersGrid.SelectedItem is not ManagementDossier dossier) return;

            string? reason = ShowInputDialog(
                $"أدخل سبب حذف الدوسية رقم {dossier.DossierNumber}:",
                "حذف الدوسية");

            if (string.IsNullOrWhiteSpace(reason))
            {
                MessageBox.Show("سبب الحذف مطلوب.", "تنبيه",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var err = _service.DeleteDossier(dossier.ManagementDossierId, reason);
            if (err != null) { ShowError(err); return; }

            LoadDossiers();
        }

        // ── DOSSIER TYPES DIALOG ──────────────────────────────────────────────

        private void ManageTypes_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedManagement == null) return;
            ShowTypesDialog(_selectedManagement);
        }

        private void ShowTypesDialog(Management management)
        {
            var win = MakeThemedWindow($"أنواع دوسيات — {management.Name}", 420, 480);

            // ── Root layout ──────────────────────────────────────────────────
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });          // header
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });          // add row
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // list
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });          // footer

            // Header
            Grid.SetRow(MakeDialogHeader("🏷️ أنواع الدوسيات", management.Name), 0);
            root.Children.Add(MakeDialogHeader("🏷️ أنواع الدوسيات", management.Name));

            // ── Add-type row ─────────────────────────────────────────────────
            var addBorder = new Border
            {
                Background = Brush(NavyPanel, 0.60),
                BorderBrush = Brush(BorderColor, 0.60),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(16, 10, 16, 10),
            };
            Grid.SetRow(addBorder, 1);

            var addGrid = new Grid();
            addGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            addGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var newTypeBox = new TextBox { Height = 50, VerticalContentAlignment = VerticalAlignment.Center };
            MaterialDesignThemes.Wpf.HintAssist.SetHint(newTypeBox, "اسم النوع الجديد");
            newTypeBox.Style = (Style)FindResource("MaterialDesignOutlinedTextBox");
            newTypeBox.Foreground = Brush(TextSecondary);
            Grid.SetColumn(newTypeBox, 0);

            var addBtn = new Button
            {
                Content = "➕ إضافة",
                Height = 40,
                Width = 100,
                Margin = new Thickness(10, 0, 0, 0),
                Style = (Style)FindResource("MaterialDesignRaisedButton"),
                Background = Brush(EmeraldMid),
                Foreground = Brushes.White,
            };
            Grid.SetColumn(addBtn, 1);

            addGrid.Children.Add(newTypeBox);
            addGrid.Children.Add(addBtn);
            addBorder.Child = addGrid;
            root.Children.Add(addBorder);

            // ── Types list ───────────────────────────────────────────────────
            var listBorder = new Border
            {
                Background = Brush(NavyPanel, 0.40),
                Padding = new Thickness(10, 6, 10, 6),
            };
            Grid.SetRow(listBorder, 2);

            var typesListBox = new ListBox
            {
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Foreground = Brush(TextSecondary),
            };
            listBorder.Child = typesListBox;
            root.Children.Add(listBorder);

            void RefreshTypes()
            {
                var types = _service.GetDossierTypes(management.ManagementId);
                typesListBox.Items.Clear();
                foreach (var t in types)
                {
                    // Row border with subtle hover feel
                    var rowBorder = new Border
                    {
                        CornerRadius = new CornerRadius(4),
                        Background = Brush(NavyPanel, 0.70),
                        BorderBrush = Brush(BorderColor, 0.50),
                        BorderThickness = new Thickness(1),
                        Margin = new Thickness(0, 3, 0, 3),
                        Padding = new Thickness(12, 6, 6, 6),
                    };

                    var rowGrid = new Grid();
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition());
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    var label = new TextBlock
                    {
                        Text = t.TypeName,
                        FontSize = 13,
                        Foreground = Brush(TextSecondary),
                        VerticalAlignment = VerticalAlignment.Center,
                    };

                    var deleteBtn = new Button
                    {
                        Content = "✕",
                        Style = (Style)FindResource("MaterialDesignFlatButton"),
                        Foreground = Brush(DangerRed),
                        Height = 28,
                        Width = 32,
                        Padding = new Thickness(0),
                        Tag = t.TypeId,
                    };

                    int capturedTypeId = t.TypeId;
                    deleteBtn.Click += (_, _) =>
                    {
                        var confirmDelete = MessageBox.Show(
                            $"حذف نوع '{t.TypeName}'؟",
                            "تأكيد", MessageBoxButton.YesNo, MessageBoxImage.Question);
                        if (confirmDelete != MessageBoxResult.Yes) return;
                        _service.DeleteDossierType(capturedTypeId);
                        RefreshTypes();
                        LoadTypeFilter();
                        LoadDossiers();
                    };

                    Grid.SetColumn(label, 0);
                    Grid.SetColumn(deleteBtn, 1);
                    rowGrid.Children.Add(label);
                    rowGrid.Children.Add(deleteBtn);
                    rowBorder.Child = rowGrid;

                    typesListBox.Items.Add(new ListBoxItem
                    {
                        Content = rowBorder,
                        HorizontalContentAlignment = HorizontalAlignment.Stretch,
                        Background = Brushes.Transparent,
                        Padding = new Thickness(0),
                    });
                }
            }

            RefreshTypes();

            addBtn.Click += (_, _) =>
            {
                string typeName = newTypeBox.Text.Trim();
                if (string.IsNullOrWhiteSpace(typeName)) return;
                var err = _service.AddDossierType(management.ManagementId, typeName);
                if (err != null)
                {
                    MessageBox.Show(err, "خطأ", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                newTypeBox.Text = string.Empty;
                RefreshTypes();
                LoadTypeFilter();
            };

            // ── Footer ───────────────────────────────────────────────────────
            var footerBorder = new Border
            {
                Background = Brush(NavyPanel, 0.90),
                BorderBrush = Brush(BorderColor, 0.80),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(16, 10, 16, 10),
            };
            Grid.SetRow(footerBorder, 3);

            var closeBtn = new Button
            {
                Content = "إغلاق",
                Height = 36,
                Width = 110,
                HorizontalAlignment = HorizontalAlignment.Left,
                Style = (Style)FindResource("MaterialDesignOutlinedButton"),
                Foreground = Brush(TextSecondary),
                BorderBrush = Brush(BorderColor),
            };
            closeBtn.Click += (_, _) => win.Close();
            footerBorder.Child = closeBtn;
            root.Children.Add(footerBorder);

            win.Content = root;
            win.ShowDialog();
        }

        // ── FILTERS ───────────────────────────────────────────────────────────

        private void LoadYearFilter()
        {
            if (_selectedManagement == null) return;
            var years = _service.GetDistinctYears(_selectedManagement.ManagementId);
            YearCombo.Items.Clear();
            YearCombo.Items.Add(new ComboBoxItem { Content = "الكل", Tag = 0 });
            foreach (var y in years)
                YearCombo.Items.Add(new ComboBoxItem { Content = y.ToString(), Tag = y });
            YearCombo.SelectedIndex = 0;
        }

        private void LoadTypeFilter()
        {
            if (_selectedManagement == null) return;
            var types = _service.GetDossierTypes(_selectedManagement.ManagementId);
            TypeFilterCombo.Items.Clear();
            TypeFilterCombo.Items.Add(new ComboBoxItem { Content = "كل الأنواع", Tag = null });
            foreach (var t in types)
                TypeFilterCombo.Items.Add(t);
            TypeFilterCombo.SelectedIndex = 0;
        }

        private void Filter_Changed(object sender, RoutedEventArgs e) => StartDebounce();

        private void StartDebounce()
        {
            _debounce?.Stop();
            _debounce = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(300)
            };
            _debounce.Tick += (_, _) => { _debounce.Stop(); LoadDossiers(); };
            _debounce.Start();
        }

        private void ResetFilter_Click(object sender, RoutedEventArgs e)
        {
            DossierNumBox.Text = string.Empty;
            YearCombo.SelectedIndex = 0;
            MonthCombo.SelectedIndex = 0;
            TypeFilterCombo.SelectedIndex = 0;
            LoadDossiers();
        }

        // ── DOSSIERS LOAD ─────────────────────────────────────────────────────

        private void LoadDossiers()
        {
            if (_selectedManagement == null) return;

            int? year = YearCombo.SelectedItem is ComboBoxItem yi && yi.Tag is int y && y > 0 ? y : null;
            int? month = MonthCombo.SelectedItem is ComboBoxItem mi && mi.Tag is int m && m > 0 ? m : null;
            int? typeId = TypeFilterCombo.SelectedItem is ManagementDossierType t ? t.TypeId : null;
            string? numSearch = string.IsNullOrWhiteSpace(DossierNumBox.Text) ? null : DossierNumBox.Text;

            var dossiers = _service.GetDossiers(
                _selectedManagement.ManagementId, year, month, typeId, numSearch);

            DossiersGrid.ItemsSource = dossiers;
            EmptyHint.Visibility = dossiers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            ResultCountText.Text = dossiers.Count == 0
                ? "لا توجد دوسيات تطابق شروط الفرز."
                : $"{dossiers.Count} دوسية";

            // Refresh sidebar count WITHOUT triggering SelectionChanged
            int selectedId = _selectedManagement.ManagementId;
            ManagementsList.SelectionChanged -= ManagementsList_SelectionChanged;
            var all = _service.GetAllManagements();
            ManagementsList.ItemsSource = all;
            foreach (Management mg in ManagementsList.Items)
            {
                if (mg.ManagementId == selectedId)
                {
                    ManagementsList.SelectedItem = mg;
                    _selectedManagement = mg;
                    break;
                }
            }
            ManagementsList.SelectionChanged += ManagementsList_SelectionChanged;
        }

        private void DossiersGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            bool hasSelection = DossiersGrid.SelectedItem is ManagementDossier;
            bool canManage = PermissionHelper.Can(Permissions.ManageManagements);
            EditDossierBtn.IsEnabled = hasSelection && canManage;
            DeleteDossierBtn.IsEnabled = hasSelection && canManage;
        }

        private void DossiersGrid_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DossiersGrid.SelectedItem is ManagementDossier)
                EditDossier_Click(sender, e);
        }

        // ── DOSSIER CRUD ──────────────────────────────────────────────────────

        private void AddDossier_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedManagement == null) return;
            ShowDossierDialog(null);
        }

        private void EditDossier_Click(object sender, RoutedEventArgs e)
        {
            if (DossiersGrid.SelectedItem is not ManagementDossier dossier) return;
            ShowDossierDialog(dossier);
        }

        private void ShowDossierDialog(ManagementDossier? existing)
        {
            if (_selectedManagement == null) return;

            var types = _service.GetDossierTypes(_selectedManagement.ManagementId);
            bool isEdit = existing != null;

            var win = MakeThemedWindow(
                isEdit ? "تعديل دوسية" : "إضافة دوسية جديدة",
                460, 500);

            // ── Root layout ──────────────────────────────────────────────────
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });           // header
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // form
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });           // footer

            // Header
            var header = MakeDialogHeader(
                isEdit ? "✏️ تعديل بيانات الدوسية" : "➕ إضافة دوسية جديدة",
                _selectedManagement.Name);
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            // ── Form (scrollable) ────────────────────────────────────────────
            var form = new StackPanel { Margin = new Thickness(20, 16, 20, 8) };

            // Dossier number
            var numBox = MakeThemedTextBox("رقم الدوسية *");
            if (isEdit) numBox.Text = existing!.DossierNumber.ToString();
            form.Children.Add(numBox);

            // Month / Year row
            var dateGrid = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            dateGrid.ColumnDefinitions.Add(new ColumnDefinition());
            dateGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            dateGrid.ColumnDefinitions.Add(new ColumnDefinition());

            var monthBox = MakeThemedTextBox("الشهر (1-12) *");
            monthBox.Margin = new Thickness(0);
            if (isEdit) monthBox.Text = existing!.HijriMonth.ToString();

            var yearBox = MakeThemedTextBox("السنة الهجرية *");
            yearBox.Margin = new Thickness(0);
            if (isEdit) yearBox.Text = existing!.HijriYear.ToString();

            Grid.SetColumn(monthBox, 0);
            Grid.SetColumn(yearBox, 2);
            dateGrid.Children.Add(monthBox);
            dateGrid.Children.Add(yearBox);
            form.Children.Add(dateGrid);

            // Type combo
            var typeCombo = new ComboBox { Margin = new Thickness(0, 0, 0, 14) };
            MaterialDesignThemes.Wpf.HintAssist.SetHint(typeCombo, "(النوع (اختياري");
            typeCombo.Style = (Style)FindResource("MaterialDesignOutlinedComboBox");
            typeCombo.Foreground = Brush(TextSecondary);
            typeCombo.DisplayMemberPath = "TypeName";
            typeCombo.Items.Add(new ComboBoxItem { Content = "-- بدون نوع --", Tag = null });
            foreach (var t in types)
                typeCombo.Items.Add(t);
            typeCombo.SelectedIndex = 0;

            if (isEdit && existing!.TypeId.HasValue)
            {
                foreach (var item in typeCombo.Items)
                    if (item is ManagementDossierType mdt && mdt.TypeId == existing.TypeId)
                    { typeCombo.SelectedItem = item; break; }
            }
            form.Children.Add(typeCombo);

            // Notes
            var notesBox = MakeThemedTextBox("(ملاحظات (اختياري");
            notesBox.AcceptsReturn = true;
            notesBox.Height = 90;
            notesBox.TextWrapping = TextWrapping.Wrap;
            notesBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            if (isEdit) notesBox.Text = existing!.Notes ?? string.Empty;
            form.Children.Add(notesBox);

            // Error block
            var errorBlock = new TextBlock
            {
                Foreground = Brush(DangerRed),
                FontSize = 12,
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(0, 4, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            };
            form.Children.Add(errorBlock);

            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = form,
            };
            Grid.SetRow(scrollViewer, 1);
            root.Children.Add(scrollViewer);

            // ── Footer ───────────────────────────────────────────────────────
            var footerBorder = new Border
            {
                Background = Brush(NavyPanel, 0.90),
                BorderBrush = Brush(BorderColor, 0.80),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(20, 12, 20, 12),
            };
            Grid.SetRow(footerBorder, 2);

            var btnRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                FlowDirection = FlowDirection.RightToLeft,
            };

            var saveBtn = new Button
            {
                Content = "💾 حفظ",
                Width = 110,
                Height = 38,
                Margin = new Thickness(0, 0, 10, 0),
                Style = (Style)FindResource("MaterialDesignRaisedButton"),
                Background = Brush(EmeraldMid),
                Foreground = Brushes.White,
            };

            var cancelBtn = new Button
            {
                Content = "إلغاء",
                Width = 100,
                Height = 38,
                Style = (Style)FindResource("MaterialDesignOutlinedButton"),
                Foreground = Brush(TextSecondary),
                BorderBrush = Brush(BorderColor),
            };
            cancelBtn.Click += (_, _) => win.Close();

            saveBtn.Click += (_, _) =>
            {
                errorBlock.Visibility = Visibility.Collapsed;

                if (!int.TryParse(numBox.Text, out int dNum) || dNum <= 0)
                { errorBlock.Text = "رقم الدوسية غير صحيح."; errorBlock.Visibility = Visibility.Visible; return; }

                if (!int.TryParse(monthBox.Text, out int hMonth) || hMonth < 1 || hMonth > 12)
                { errorBlock.Text = "الشهر يجب أن يكون بين 1 و 12."; errorBlock.Visibility = Visibility.Visible; return; }

                if (!int.TryParse(yearBox.Text, out int hYear) || hYear < 1400 || hYear > 1600)
                { errorBlock.Text = "السنة الهجرية غير صحيحة."; errorBlock.Visibility = Visibility.Visible; return; }

                int? selectedTypeId = typeCombo.SelectedItem is ManagementDossierType mt ? mt.TypeId : null;
                string? notes = string.IsNullOrWhiteSpace(notesBox.Text) ? null : notesBox.Text.Trim();

                string? err;
                if (isEdit)
                    err = _service.UpdateDossier(existing!.ManagementDossierId,
                              dNum, hMonth, hYear, selectedTypeId, notes);
                else
                {
                    var (createErr, _) = _service.CreateDossier(
                        _selectedManagement!.ManagementId, dNum, hMonth, hYear, selectedTypeId, notes);
                    err = createErr;
                }

                if (err != null)
                { errorBlock.Text = err; errorBlock.Visibility = Visibility.Visible; return; }

                win.Close();
                LoadDossiers();
                LoadYearFilter();
            };

            btnRow.Children.Add(saveBtn);
            btnRow.Children.Add(cancelBtn);
            footerBorder.Child = btnRow;
            root.Children.Add(footerBorder);

            win.Content = root;
            win.ShowDialog();
        }

        // ── HELPERS ───────────────────────────────────────────────────────────

        /// <summary>
        /// Themed replacement for InputDialog.Show().
        /// Returns the entered text, or null if cancelled.
        /// </summary>
        private string? ShowInputDialog(string prompt, string title, string defaultValue = "")
        {
            string? result = null;

            var win = MakeThemedWindow(title, 420, 320);
            win.ResizeMode = ResizeMode.NoResize;

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // header
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // body
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // footer

            // Header
            var header = MakeDialogHeader(title);
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            // Body
            var bodyBorder = new Border
            {
                Background = Brush(NavyPanel, 0.50),
                Padding = new Thickness(20, 16, 20, 12),
            };
            Grid.SetRow(bodyBorder, 1);

            var body = new StackPanel();

            body.Children.Add(new TextBlock
            {
                Text = prompt,
                FontSize = 13,
                Foreground = Brush(TextSecondary),
                Margin = new Thickness(0, 0, 0, 10),
                TextWrapping = TextWrapping.Wrap,
            });

            var inputBox = new TextBox
            {
                Text = defaultValue,
                Height = 50,
                VerticalContentAlignment = VerticalAlignment.Center,
                Foreground = Brush(TextSecondary),
                CaretBrush = Brush(EmeraldMid),
            };
            MaterialDesignThemes.Wpf.HintAssist.SetHint(inputBox, prompt);
            MaterialDesignThemes.Wpf.HintAssist.SetForeground(inputBox, Brush(SubText));
            inputBox.Style = (Style)FindResource("MaterialDesignOutlinedTextBox");
            // Select all text on focus for easy replacement
            inputBox.GotFocus += (_, _) => inputBox.SelectAll();
            body.Children.Add(inputBox);

            bodyBorder.Child = body;
            root.Children.Add(bodyBorder);

            // Footer
            var footerBorder = new Border
            {
                Background = Brush(NavyPanel, 0.90),
                BorderBrush = Brush(BorderColor, 0.80),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(20, 10, 20, 10),
            };
            Grid.SetRow(footerBorder, 2);

            var btnRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                FlowDirection = FlowDirection.RightToLeft,
            };

            var okBtn = new Button
            {
                Content = "موافق",
                Width = 100,
                Height = 36,
                Margin = new Thickness(0, 0, 10, 0),
                Style = (Style)FindResource("MaterialDesignRaisedButton"),
                Background = Brush(EmeraldMid),
                Foreground = Brushes.White,
                IsDefault = true,
            };
            okBtn.Click += (_, _) =>
            {
                result = inputBox.Text.Trim();
                win.Close();
            };

            var cancelBtn = new Button
            {
                Content = "إلغاء",
                Width = 90,
                Height = 36,
                Style = (Style)FindResource("MaterialDesignOutlinedButton"),
                Foreground = Brush(TextSecondary),
                BorderBrush = Brush(BorderColor),
                IsCancel = true,
            };
            cancelBtn.Click += (_, _) => win.Close();

            btnRow.Children.Add(okBtn);
            btnRow.Children.Add(cancelBtn);
            footerBorder.Child = btnRow;
            root.Children.Add(footerBorder);

            win.Content = root;

            // Focus the input after the window loads
            win.Loaded += (_, _) =>
            {
                inputBox.Focus();
                if (!string.IsNullOrEmpty(defaultValue))
                    inputBox.SelectAll();
            };

            win.ShowDialog();
            return string.IsNullOrWhiteSpace(result) ? null : result;
        }

        /// <summary>Outlined TextBox styled for the dark theme.</summary>
        private TextBox MakeThemedTextBox(string hint)
        {
            var tb = new TextBox
            {
                Margin = new Thickness(0, 0, 0, 14),
                Foreground = Brush(TextSecondary),
                CaretBrush = Brush(EmeraldMid),
            };
            MaterialDesignThemes.Wpf.HintAssist.SetHint(tb, hint);
            MaterialDesignThemes.Wpf.HintAssist.SetForeground(tb, Brush(SubText));
            tb.Style = (Style)FindResource("MaterialDesignOutlinedTextBox");
            return tb;
        }

        // Keep the original helper name so nothing else breaks
        private TextBox MakeTextBox(string hint) => MakeThemedTextBox(hint);

        private void ShowError(string msg) =>
            MessageBox.Show(msg, "خطأ", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}