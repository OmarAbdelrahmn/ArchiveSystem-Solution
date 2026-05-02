using System.Windows;
using ArchiveSystem.Core.Services;

namespace ArchiveSystem.Views.Dialogs
{
    public partial class EditStagingDossierDialog : Window
    {
        // Output properties read by caller
        public int DossierNumber { get; private set; }
        public int HijriMonth { get; private set; }
        public int HijriYear { get; private set; }
        public int? ExpectedCount { get; private set; }

        private readonly StagedDossierView _staged;

        public EditStagingDossierDialog(StagedDossierView staged)
        {
            InitializeComponent();
            _staged = staged;

            SheetNameText.Text = $"الشيت: {staged.SheetName}  |  الصفوف الفعلية: {staged.ActualRowCount}";
            ActualCountText.Text = $"الصفوف الفعلية في الشيت: {staged.ActualRowCount}";

            // Pre-fill current values if available
            if (staged.DossierNumber.HasValue)
                DossierNumberBox.Text = staged.DossierNumber.Value.ToString();
            if (staged.HijriMonth.HasValue)
                HijriMonthBox.Text = staged.HijriMonth.Value.ToString();
            if (staged.HijriYear.HasValue)
                HijriYearBox.Text = staged.HijriYear.Value.ToString();
            if (staged.ExpectedCount.HasValue)
                ExpectedCountBox.Text = staged.ExpectedCount.Value.ToString();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            ErrorBorder.Visibility = Visibility.Collapsed;

            if (!int.TryParse(DossierNumberBox.Text, out int dNum) || dNum <= 0)
            {
                ShowError("يرجى إدخال رقم دوسية صحيح."); return;
            }

            if (!int.TryParse(HijriMonthBox.Text, out int hMonth) || hMonth < 1 || hMonth > 12)
            {
                ShowError("الشهر الهجري يجب أن يكون بين 1 و 12."); return;
            }

            if (!int.TryParse(HijriYearBox.Text, out int hYear) || hYear < 1400 || hYear > 1600)
            {
                ShowError("السنة الهجرية غير صحيحة (يجب أن تكون بين 1400 و 1600)."); return;
            }

            int? expectedCount = null;
            if (!string.IsNullOrWhiteSpace(ExpectedCountBox.Text))
            {
                if (!int.TryParse(ExpectedCountBox.Text, out int ec) || ec < 0)
                {
                    ShowError("عدد الملفات المتوقع غير صحيح."); return;
                }
                expectedCount = ec;
            }

            DossierNumber = dNum;
            HijriMonth = hMonth;
            HijriYear = hYear;
            ExpectedCount = expectedCount;

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