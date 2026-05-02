using System.Windows;
using System.Windows.Controls;
using ArchiveSystem.Core.Services;

namespace ArchiveSystem.Views.Pages
{
    public partial class DossierDetailsPage : Page
    {
        private readonly DossierService _dossierService;
        private readonly RecordService _recordService;
        private readonly int _dossierId;

        public DossierDetailsPage(int dossierId)
        {
            InitializeComponent();
            _dossierService = new DossierService(App.Database);
            _recordService = new RecordService(App.Database);
            _dossierId = dossierId;
            Loaded += (s, e) => LoadData();
        }

        private void LoadData()
        {
            var dossier = _dossierService.GetDossierById(_dossierId);
            if (dossier == null) return;

            var records = _recordService.GetRecordsByDossier(_dossierId);

            // Header
            HeaderTitle.Text = $"📁  دوسية رقم {dossier.DossierNumber}";
            DossierNumberText.Text = dossier.DossierNumber.ToString();

            // Location
            LocationText.Text = dossier.CurrentLocation?.DisplayName ?? "غير محدد";

            // File count
            int expected = dossier.ExpectedFileCount ?? 0;
            int actual = records.Count;
            FileCountText.Text = expected > 0
                ? $"{actual} / {expected}"
                : $"{actual} ملف";

            // Records table
            RecordsGrid.ItemsSource = records;
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            NavigationService?.GoBack();
        }
    }
}