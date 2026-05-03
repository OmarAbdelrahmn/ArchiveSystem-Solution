using System.Windows;
using ArchiveSystem.Core.Models;

namespace ArchiveSystem.Views.Dialogs
{
    public partial class EditDossierDialog : Window
    {
        // Output properties
        public int HijriMonth { get; private set; }
        public int HijriYear { get; private set; }
        public int? ExpectedCount { get; private set; }
        public int Hallway { get; private set; }
        public int Cabinet { get; private set; }
        public int Shelf { get; private set; }

        public EditDossierDialog(Dossier dossier)
        {
            InitializeComponent();

            // Pre-fill with current values
            HijriMonthBox.Text = dossier.HijriMonth.ToString();
            HijriYearBox.Text = dossier.HijriYear.ToString();
            if (dossier.ExpectedFileCount.HasValue)
                ExpectedCountBox.Text = dossier.ExpectedFileCount.Value.ToString();

            if (dossier.CurrentLocation != null)
            {
                HallwayBox.Text = dossier.CurrentLocation.HallwayNumber.ToString();
                CabinetBox.Text = dossier.CurrentLocation.CabinetNumber.ToString();
                ShelfBox.Text = dossier.CurrentLocation.ShelfNumber.ToString();
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            ErrorBorder.Visibility = Visibility.Collapsed;

            if (!int.TryParse(HijriMonthBox.Text, out int hMonth) || hMonth < 1 || hMonth > 12)
            { ShowError("الشهر الهجري يجب أن يكون بين 1 و 12."); return; }

            if (!int.TryParse(HijriYearBox.Text, out int hYear) || hYear < 1400 || hYear > 1600)
            { ShowError("السنة الهجرية غير صحيحة (1400-1600)."); return; }

            if (!int.TryParse(HallwayBox.Text, out int hall) || hall <= 0 ||
                !int.TryParse(CabinetBox.Text, out int cab) || cab <= 0 ||
                !int.TryParse(ShelfBox.Text, out int shelf) || shelf <= 0)
            { ShowError("يرجى إدخال أرقام الموقع بشكل صحيح."); return; }

            int? expected = null;
            if (!string.IsNullOrWhiteSpace(ExpectedCountBox.Text))
            {
                if (!int.TryParse(ExpectedCountBox.Text, out int ec) || ec < 0)
                { ShowError("عدد الملفات المتوقع غير صحيح."); return; }
                expected = ec;
            }

            HijriMonth = hMonth;
            HijriYear = hYear;
            ExpectedCount = expected;
            Hallway = hall;
            Cabinet = cab;
            Shelf = shelf;

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void ShowError(string msg)
        {
            ErrorText.Text = msg;
            ErrorBorder.Visibility = Visibility.Visible;
        }
    }
}