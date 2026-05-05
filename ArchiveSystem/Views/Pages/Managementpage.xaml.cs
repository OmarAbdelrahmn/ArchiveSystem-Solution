using ArchiveSystem.Core.Helpers;
using ArchiveSystem.Core.Models;
using ArchiveSystem.Core.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ArchiveSystem.Views.Pages
{
    public partial class ManagementPage : Page
    {
        private readonly ManagementService _service;
        private Management? _selectedManagement;
        private System.Windows.Threading.DispatcherTimer? _debounce;

        public ManagementPage()
        {
            InitializeComponent();
            _service = new ManagementService(App.Database);
            MonthCombo.SelectedIndex = 0;
            Loaded += (s, e) => Initialize();
        }

        // ── INIT ──────────────────────────────────────────────────────────────

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
            string name = Microsoft.VisualBasic.Interaction.InputBox(
                "أدخل اسم الإدارة الرئيسية الجديدة:",
                "إضافة إدارة رئيسية", "");
            if (string.IsNullOrWhiteSpace(name)) return;

            string? desc = Microsoft.VisualBasic.Interaction.InputBox(
                "وصف الإدارة (اختياري):", "وصف الإدارة", "");

            var err = _service.CreateManagement(name, null, desc);
            if (err != null) { ShowError(err); return; }

            LoadManagements();
        }

        private void AddSubManagement_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedManagement == null) return;

            string name = Microsoft.VisualBasic.Interaction.InputBox(
                $"أدخل اسم الإدارة الفرعية تحت '{_selectedManagement.Name}':",
                "إضافة إدارة فرعية", "");
            if (string.IsNullOrWhiteSpace(name)) return;

            var err = _service.CreateManagement(name, _selectedManagement.ManagementId, null);
            if (err != null) { ShowError(err); return; }

            LoadManagements();
        }

        private void EditManagement_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedManagement == null) return;

            string name = Microsoft.VisualBasic.Interaction.InputBox(
                "تعديل اسم الإدارة:",
                "تعديل الإدارة", _selectedManagement.Name);
            if (string.IsNullOrWhiteSpace(name)) return;

            string? desc = Microsoft.VisualBasic.Interaction.InputBox(
                "وصف الإدارة (اختياري):",
                "وصف الإدارة", _selectedManagement.Description ?? "");

            var err = _service.UpdateManagement(_selectedManagement.ManagementId, name, desc);
            if (err != null) { ShowError(err); return; }

            LoadManagements();
        }

        private void DeleteManagement_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedManagement == null) return;

            var confirm = MessageBox.Show(
                $"هل تريد حذف الإدارة '{_selectedManagement.Name}'؟\n\nلا يمكن حذفها إذا كانت تحتوي على إدارات فرعية أو دوسيات.",
                "تأكيد الحذف", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            var err = _service.DeleteManagement(_selectedManagement.ManagementId);
            if (err != null) { ShowError(err); return; }

            _selectedManagement = null;
            LoadManagements();
        }

        // ── DOSSIER TYPES DIALOG ──────────────────────────────────────────────

        private void ManageTypes_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedManagement == null) return;
            ShowTypesDialog(_selectedManagement);
        }

        private void ShowTypesDialog(Management management)
        {
            // Simple inline dialog using a Window
            var win = new Window
            {
                Title = $"أنواع دوسيات — {management.Name}",
                Width = 400,
                Height = 450,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this),
                FlowDirection = FlowDirection.RightToLeft,
                Background = System.Windows.Media.Brushes.WhiteSmoke
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.Margin = new Thickness(16);

            // Add row
            var addGrid = new Grid();
            addGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            addGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var newTypeBox = new TextBox
            {
                Margin = new Thickness(0, 0, 8, 12),
                Height = 40
            };
            MaterialDesignThemes.Wpf.HintAssist.SetHint(newTypeBox, "اسم النوع الجديد");
            newTypeBox.Style = (Style)FindResource("MaterialDesignOutlinedTextBox");
            Grid.SetColumn(newTypeBox, 0);

            var addBtn = new Button
            {
                Content = "➕ إضافة",
                Height = 40,
                Width = 90,
                Margin = new Thickness(0, 0, 0, 12)
            };
            addBtn.Style = (Style)FindResource("MaterialDesignRaisedButton");
            addBtn.Background = (System.Windows.Media.Brush)
                new System.Windows.Media.BrushConverter().ConvertFromString("#1a7a60")!;
            addBtn.Foreground = System.Windows.Media.Brushes.White;
            Grid.SetColumn(addBtn, 1);

            addGrid.Children.Add(newTypeBox);
            addGrid.Children.Add(addBtn);
            Grid.SetRow(addGrid, 0);

            // Types list
            var typesListBox = new ListBox
            {
                BorderThickness = new Thickness(1),
                Background = System.Windows.Media.Brushes.White
            };
            Grid.SetRow(typesListBox, 1);

            void RefreshTypes()
            {
                var types = _service.GetDossierTypes(management.ManagementId);
                typesListBox.Items.Clear();
                foreach (var t in types)
                {
                    var itemGrid = new Grid();
                    itemGrid.ColumnDefinitions.Add(new ColumnDefinition());
                    itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    var label = new TextBlock
                    {
                        Text = t.TypeName,
                        FontSize = 13,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(8, 4, 0, 4)
                    };

                    var deleteBtn = new Button
                    {
                        Content = "✕",
                        Style = (Style)FindResource("MaterialDesignFlatButton"),
                        Foreground = new System.Windows.Media.SolidColorBrush(
                            (System.Windows.Media.Color)System.Windows.Media.ColorConverter
                                .ConvertFromString("#C62828")),
                        Height = 28,
                        Width = 32,
                        Padding = new Thickness(0),
                        Tag = t.TypeId
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

                    Grid.SetColumn(deleteBtn, 1);
                    itemGrid.Children.Add(label);
                    itemGrid.Children.Add(deleteBtn);

                    typesListBox.Items.Add(new ListBoxItem
                    {
                        Content = itemGrid,
                        HorizontalContentAlignment = HorizontalAlignment.Stretch
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

            var closeBtn = new Button
            {
                Content = "إغلاق",
                Height = 36,
                Width = 100,
                Margin = new Thickness(0, 12, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            closeBtn.Style = (Style)FindResource("MaterialDesignOutlinedButton");
            closeBtn.Click += (_, _) => win.Close();
            Grid.SetRow(closeBtn, 2);

            grid.Children.Add(addGrid);
            grid.Children.Add(typesListBox);
            grid.Children.Add(closeBtn);

            win.Content = grid;
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

            // ── Refresh sidebar count WITHOUT triggering SelectionChanged ──
            int selectedId = _selectedManagement.ManagementId;
            ManagementsList.SelectionChanged -= ManagementsList_SelectionChanged; // detach
            var all = _service.GetAllManagements();
            ManagementsList.ItemsSource = all;
            foreach (Management mg in ManagementsList.Items)
            {
                if (mg.ManagementId == selectedId)
                {
                    ManagementsList.SelectedItem = mg;
                    _selectedManagement = mg; // update reference
                    break;
                }
            }
            ManagementsList.SelectionChanged += ManagementsList_SelectionChanged; // reattach
        }


        private void DossiersGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            bool hasSelection = DossiersGrid.SelectedItem is ManagementDossier;
            EditDossierBtn.IsEnabled = hasSelection && PermissionHelper.Can(Permissions.ManageManagements);
            DeleteDossierBtn.IsEnabled = hasSelection && PermissionHelper.Can(Permissions.ManageManagements);
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

            var win = new Window
            {
                Title = isEdit ? "تعديل دوسية" : "إضافة دوسية جديدة",
                Width = 440,
                Height = 420,
                ResizeMode = ResizeMode.CanResizeWithGrip,          // ← allow resize
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this),
                FlowDirection = FlowDirection.RightToLeft,
                Background = System.Windows.Media.Brushes.WhiteSmoke
            };

            var panel = new StackPanel { Margin = new Thickness(24) };

            // ... all existing panel.Children.Add(...) code stays exactly the same ...
            panel.Children.Add(new TextBlock
            {
                Text = isEdit ? "تعديل بيانات الدوسية" : "إضافة دوسية جديدة",
                FontSize = 17,
                FontWeight = FontWeights.Bold,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter
                        .ConvertFromString("#1a7a60")),
                Margin = new Thickness(0, 0, 0, 20)
            });

            var numBox = MakeTextBox("رقم الدوسية *");
            if (isEdit) numBox.Text = existing!.DossierNumber.ToString();
            panel.Children.Add(numBox);

            var dateGrid = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            dateGrid.ColumnDefinitions.Add(new ColumnDefinition());
            dateGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            dateGrid.ColumnDefinitions.Add(new ColumnDefinition());

            var monthBox = MakeTextBox("الشهر (1-12) *");
            monthBox.Margin = new Thickness(0);
            if (isEdit) monthBox.Text = existing!.HijriMonth.ToString();

            var yearBox = MakeTextBox("السنة الهجرية *");
            yearBox.Margin = new Thickness(0);
            if (isEdit) yearBox.Text = existing!.HijriYear.ToString();

            Grid.SetColumn(monthBox, 0);
            Grid.SetColumn(yearBox, 2);
            dateGrid.Children.Add(monthBox);
            dateGrid.Children.Add(yearBox);
            panel.Children.Add(dateGrid);

            var typeCombo = new ComboBox { Margin = new Thickness(0, 0, 0, 14) };
            MaterialDesignThemes.Wpf.HintAssist.SetHint(typeCombo, "النوع (اختياري)");
            typeCombo.Style = (Style)FindResource("MaterialDesignOutlinedComboBox");
            typeCombo.Items.Add(new ComboBoxItem { Content = "-- بدون نوع --", Tag = null });
            foreach (var t in types)
                typeCombo.Items.Add(t);
            typeCombo.SelectedIndex = 0;
            typeCombo.DisplayMemberPath = "TypeName";

            if (isEdit && existing!.TypeId.HasValue)
            {
                foreach (var item in typeCombo.Items)
                {
                    if (item is ManagementDossierType mdt && mdt.TypeId == existing.TypeId)
                    { typeCombo.SelectedItem = item; break; }
                }
            }
            panel.Children.Add(typeCombo);

            var notesBox = MakeTextBox("ملاحظات (اختياري)");
            notesBox.AcceptsReturn = true;
            notesBox.Height = 80;
            notesBox.TextWrapping = TextWrapping.Wrap;
            notesBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            if (isEdit) notesBox.Text = existing!.Notes ?? string.Empty;
            panel.Children.Add(notesBox);

            var errorBlock = new TextBlock
            {
                Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter
                        .ConvertFromString("#C62828")),
                FontSize = 12,
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(0, 6, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };
            panel.Children.Add(errorBlock);

            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 14, 0, 0)
            };

            var saveBtn = new Button
            {
                Content = "حفظ",
                Width = 100,
                Height = 36,
                Margin = new Thickness(0, 0, 10, 0)
            };
            saveBtn.Style = (Style)FindResource("MaterialDesignRaisedButton");
            saveBtn.Background = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter
                    .ConvertFromString("#1a7a60"));
            saveBtn.Foreground = System.Windows.Media.Brushes.White;

            var cancelBtn = new Button { Content = "إلغاء", Width = 100, Height = 36 };
            cancelBtn.Style = (Style)FindResource("MaterialDesignOutlinedButton");
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
                {
                    err = _service.UpdateDossier(existing!.ManagementDossierId,
                        dNum, hMonth, hYear, selectedTypeId, notes);
                }
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

            btnPanel.Children.Add(saveBtn);
            btnPanel.Children.Add(cancelBtn);
            panel.Children.Add(btnPanel);

            // ── Wrap panel in a Grid with ScrollViewer ────────────────────────────
            var outerGrid = new Grid();
            outerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            outerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = panel
            };
            Grid.SetRow(scrollViewer, 0);
            outerGrid.Children.Add(scrollViewer);

            win.Content = outerGrid;
            win.ShowDialog();
        }

        private void DeleteDossier_Click(object sender, RoutedEventArgs e)
        {
            if (DossiersGrid.SelectedItem is not ManagementDossier dossier) return;

            string reason = Microsoft.VisualBasic.Interaction.InputBox(
                $"أدخل سبب حذف الدوسية رقم {dossier.DossierNumber}:",
                "حذف الدوسية", "");

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

        // ── HELPERS ───────────────────────────────────────────────────────────

        private TextBox MakeTextBox(string hint)
        {
            var tb = new TextBox { Margin = new Thickness(0, 0, 0, 14) };
            MaterialDesignThemes.Wpf.HintAssist.SetHint(tb, hint);
            tb.Style = (Style)FindResource("MaterialDesignOutlinedTextBox");
            return tb;
        }

        private void ShowError(string msg) =>
            MessageBox.Show(msg, "خطأ", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}