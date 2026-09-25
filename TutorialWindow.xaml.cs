using System.Windows;
using System.Windows.Input;
using VarIntCalculator.Controls;
using VarIntCalculator.Services;

namespace VarIntCalculator
{
    /// <summary>
    /// The varint tutorial. It stays open beside the calculator (it is not modal), and its
    /// "Try it" links put each example on the calculator's display.
    /// </summary>
    public partial class TutorialWindow : Window
    {
        private readonly IReadOnlyList<TutorialStep> _steps;
        private int _index;

        public TutorialWindow(MainWindow calculator)
        {
            InitializeComponent();
            _steps = TutorialContent.Build(calculator.LoadExample);
            _index = Math.Clamp(App.Settings.TutorialStep, 0, _steps.Count - 1);
            FitToWorkArea();
            ShowStep();
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
            else if (e.Key == Key.Right || e.Key == Key.PageDown)
            {
                Move(+1);
                e.Handled = true;
            }
            else if (e.Key == Key.Left || e.Key == Key.PageUp)
            {
                Move(-1);
                e.Handled = true;
            }
        }

        private void ShowStep()
        {
            TutorialStep step = _steps[_index];
            StepText.Text = Tracking.Apply($"STEP {_index + 1} OF {_steps.Count}");
            TitleText.Text = step.Title;
            StepBody.Content = step.Body();
            ProgressDone.Width = new GridLength(_index + 1, GridUnitType.Star);
            ProgressLeft.Width = new GridLength(_steps.Count - _index - 1, GridUnitType.Star);
            PreviousButton.IsEnabled = _index > 0;
            RestartButton.Visibility = _index > 0 ? Visibility.Visible : Visibility.Hidden;
            bool last = _index == _steps.Count - 1;
            NextText.Text = last ? "Finish" : "Next";
            NextIcon.Kind = last ? MaterialDesignThemes.Wpf.PackIconKind.Check : MaterialDesignThemes.Wpf.PackIconKind.ChevronRight;
            BodyScroll.ScrollToTop();

            App.Settings.TutorialStep = _index;
            SettingsStore.SaveSettings(App.Settings);
        }

        private void Move(int delta)
        {
            int next = Math.Clamp(_index + delta, 0, _steps.Count - 1);
            if (next == _index)
                return;
            _index = next;
            ShowStep();
        }

        private void Next_Click(object sender, RoutedEventArgs e)
        {
            if (_index == _steps.Count - 1)
            {
                // Finished: the next visit starts from the beginning.
                App.Settings.TutorialStep = 0;
                SettingsStore.SaveSettings(App.Settings);
                Close();
                return;
            }
            Move(+1);
        }

        private void Previous_Click(object sender, RoutedEventArgs e) => Move(-1);

        private void Restart_Click(object sender, RoutedEventArgs e)
        {
            _index = 0;
            ShowStep();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void FitToWorkArea()
        {
            double room = SystemParameters.WorkArea.Height - 16;
            if (Height > room)
                Height = Math.Max(MinHeight, room);
        }
    }
}
