using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace PyChunks
{
    public partial class OutputWindow : Window
    {
        private bool isClosing = false;
        private Process attachedProcess = null;
        private Action<string> processInputHandler;

        public OutputWindow(Window owner, string outputText, string title)
        {
            InitializeComponent();
            this.Owner = owner;

            OutputTextBox.Text = outputText;
            Title.Text = title;

            if (owner != null)
            {
                this.Left = owner.Left + 50;
                this.Top = owner.Top + 50;
            }

            this.Closing += (s, e) =>
            {
                if (!isClosing)
                {
                    e.Cancel = true;
                    CloseWithAnimation();
                }
            };

            OpenWithAnimation();
        }

        private void OpenWithAnimation()
        {
            this.Opacity = 0;
            this.Height = 0;
            double targetHeight = 400;

            var heightAnimation = new DoubleAnimation
            {
                To = targetHeight,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            var opacityAnimation = new DoubleAnimation
            {
                To = 1,
                Duration = TimeSpan.FromMilliseconds(250),
                BeginTime = TimeSpan.FromMilliseconds(50)
            };

            this.BeginAnimation(HeightProperty, heightAnimation);
            this.BeginAnimation(OpacityProperty, opacityAnimation);
        }

        private void CloseWithAnimation()
        {
            var heightAnimation = new DoubleAnimation
            {
                To = 0,
                Duration = TimeSpan.FromMilliseconds(250),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };

            var opacityAnimation = new DoubleAnimation
            {
                To = 0,
                Duration = TimeSpan.FromMilliseconds(200)
            };

            heightAnimation.Completed += (s, e) =>
            {
                isClosing = true;
                this.Close();
            };

            this.BeginAnimation(HeightProperty, heightAnimation);
            this.BeginAnimation(OpacityProperty, opacityAnimation);
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            this.DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            CloseWithAnimation();
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(OutputTextBox.Text))
            {
                Clipboard.SetText(OutputTextBox.Text);
                string originalContent = (string)CopyButton.Content;
                CopyButton.Content = "Copied!";

                var timer = new System.Windows.Threading.DispatcherTimer();
                timer.Tick += (s, args) =>
                {
                    CopyButton.Content = originalContent;
                    timer.Stop();
                };
                timer.Interval = TimeSpan.FromSeconds(1.5);
                timer.Start();
            }
        }

        public void AppendOutput(string text)
        {
            OutputTextBox.AppendText(text);
            OutputTextBox.ScrollToEnd();
        }

        public void SetProcessInputHandler(Action<string> handler)
        {
            processInputHandler = handler;
        }

        private void OutputTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && processInputHandler != null)
            {
                e.Handled = true;
                string input = OutputTextBox.Text.Split('\n')[^1];
                processInputHandler(input);
            }
        }

        public void WaitForInput()
        {
            OutputTextBox.IsReadOnly = false;
            OutputTextBox.Focus();
        }

        public void DisableInput()
        {
            OutputTextBox.IsReadOnly = true;
        }

        public void SetAttachedProcess(Process process)
        {
            attachedProcess = process;
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            if (attachedProcess != null)
            {
                try
                {
                    if (!attachedProcess.HasExited)
                    {
                        attachedProcess.Kill();
                        AppendOutput("\n[Process stopped by user]\n");
                    }

                    attachedProcess = null;
                }
                catch (Exception ex)
                {
                    AppendOutput($"\n[Error stopping process: {ex.Message}]\n");
                }
                StopButton.Content = "Close";
                StopButton.Click -= StopButton_Click;
                StopButton.Click += (s2, e2) => CloseWithAnimation();
            }
            else
            {
                CloseWithAnimation();
            }
        }


        public void UpdateStopButtonToClose()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                StopButton.Content = "Close";
                StopButton.Click -= StopButton_Click;
                StopButton.Click += (s, e) => CloseWithAnimation();

                var copyStyle = (Style)FindResource("CopyButtonStyle");
                if (copyStyle != null)
                {
                    StopButton.Style = copyStyle;
                }
            });
        }
    }
}
