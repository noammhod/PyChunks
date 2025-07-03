using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Xml;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.ProgressBar;

namespace PyChunks
{
    public partial class ChunkWindow : Window
    {
        private bool isMaximized = false;
        private Rect restoreBounds;
        private bool isEditing = false;
        private bool isCollapsed = false;
        private double normalHeight;
        private Button minimizeButtonRef;
        private bool isAnimationInProgress = false;
        public chunkSideBar current;
        public chunkSideBar head;
        private static bool isPythonInitialized = false;
        private static readonly object pythonLock = new object();
        private bool isInstalling = false;

        public ChunkWindow(Window owner, chunkSideBar head)
        {
            InitializeComponent();

            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("PyChunks.python-syntax-highlighting.xml"))
            {
                using (XmlReader reader = new XmlTextReader(s))
                {
                    PopupTextBox.SyntaxHighlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);
                }
            }

            this.Owner = owner;
            this.head = head;

            TitleTextBlock.Text = "New Chunk";
            TitleTextBox.Text = "New Chunk";

            current = new chunkSideBar(TitleTextBox.Text, null);
            current.window = this; // Set the window reference in the chunkSideBar
            current.name = TitleTextBox.Text; // Ensure name is set on creation
            chunkSideBar lastNode = head;
            while (lastNode.next != null) lastNode = lastNode.next;
            lastNode.next = current;

            if (this.Owner is MainWindow mainWindow) mainWindow.updateSideBar();

            if (owner != null)
            {
                this.Left = owner.Left + 50;
                this.Top = owner.Top + 50;
            }

            Loaded += (s, e) =>
            {
                normalHeight = this.Height;
                InitializeScrollSync();
            };

            InitializeLineNumbers();
            InitializeMinimizeState();
            PopupTextBox.TextChanged += PopupTextBox_TextChanged;
        }

        private ScrollViewer GetScrollViewer(DependencyObject dep)
        {
            if (dep == null) return null;

            if (dep is ScrollViewer) return (ScrollViewer)dep;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(dep); i++)
            {
                var child = VisualTreeHelper.GetChild(dep, i);
                var result = GetScrollViewer(child);
                if (result != null) return result;
            }
            return null;
        }

        private void InitializeScrollSync()
        {
            var popupScrollViewer = GetScrollViewer(PopupTextBox);
            var lineNumbersScrollViewer = GetScrollViewer(LineNumbersTextBox);

            if (popupScrollViewer != null && lineNumbersScrollViewer != null)
            {
                popupScrollViewer.ScrollChanged += (s, e) =>
                {
                    lineNumbersScrollViewer.ScrollToVerticalOffset(e.VerticalOffset);
                };
            }
        }


        private void InitializeLineNumbers()
        {
            PopupTextBox.TextChanged += (sender, e) => UpdateLineNumbers();
            PopupTextBox.Loaded += (sender, e) => UpdateLineNumbers();
        }

        private void UpdateLineNumbers()
        {
            string lineNumbers = "";
            for (int i = 1; i <= PopupTextBox.LineCount; i++)
                lineNumbers += i + "\n";
            LineNumbersTextBox.Text = lineNumbers;
        }

        private void PopupTextBox_TextChanged(object sender, EventArgs e)
        {
            UpdateLineNumbers();

            Dispatcher.InvokeAsync(() =>
            {
                var textView = PopupTextBox.TextArea.TextView;
                textView.EnsureVisualLines();

                int caretLine = PopupTextBox.TextArea.Caret.Line;

                int firstVisibleLine = textView.VisualLines.FirstOrDefault()?.FirstDocumentLine.LineNumber ?? 0;
                int lastVisibleLine = textView.VisualLines.LastOrDefault()?.LastDocumentLine.LineNumber ?? 0;

                if (caretLine > lastVisibleLine - 1)
                {
                    PopupTextBox.ScrollToLine(caretLine);
                }
            }, DispatcherPriority.Background);
        }

        private async void RunButton_Click(object sender, RoutedEventArgs e)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string pythonExe = System.IO.Path.Combine(baseDir, "python", "python.exe");

            if (!File.Exists(pythonExe))
            {
                MessageBox.Show("Embedded Python not found in /python folder.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string script = PopupTextBox.Text;
            await RunPythonScriptAsync(pythonExe, script);
        }

        private Window CreateProgressWindow(string libName)
        {
            var win = new Window()
            {
                Width = 300,
                Height = 100,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                Background = Brushes.Black,
                Foreground = Brushes.White,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Topmost = true,
                AllowsTransparency = true,
                ShowInTaskbar = false,
                Opacity = 0.9,
                Content = new StackPanel
                {
                    Margin = new Thickness(20),
                    Children =
            {
                new TextBlock
                {
                    Text = $"Installing: {libName}",
                    FontSize = 14,
                    Margin = new Thickness(0, 0, 0, 10),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextAlignment = TextAlignment.Center
                },
                new ProgressBar
                {
                    IsIndeterminate = true,
                    Height = 20
                }
            }
                }
            };
            return win;
        }

        private static string[] ExtractRequiredLibraries(string script)
        {
            var libraries = new HashSet<string>();

            // פיצול כל השורות ואפילו פקודות בשורה אחת
            var lines = script
                .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .SelectMany(line => line.Split(';'))  // תומך בקווים עם ; ביניהם
                .Select(line => line.Trim());

            foreach (var line in lines)
            {
                var trimmed = line;

                // התעלם מהערות או שורות ריקות
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#"))
                    continue;

                // רק שורות שמתחילות ב-import או from
                if (trimmed.StartsWith("import ", StringComparison.OrdinalIgnoreCase))
                {
                    // דוגמה: import os, sys as system
                    var imports = trimmed.Substring(7).Split(',');
                    foreach (var imp in imports)
                    {
                        var parts = imp.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length > 0)
                        {
                            var lib = parts[0].Split('.')[0];
                            libraries.Add(lib);
                        }
                    }
                }
                else if (trimmed.StartsWith("from ", StringComparison.OrdinalIgnoreCase))
                {
                    // דוגמה: from numpy.linalg import inv
                    var match = Regex.Match(trimmed, @"^from\s+([a-zA-Z0-9_\.]+)\s+import", RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        var lib = match.Groups[1].Value.Split('.')[0];
                        libraries.Add(lib);
                    }
                }
            }

            return libraries.ToArray();
        }



        private static readonly HashSet<string> builtInModules = new HashSet<string>
        {
            "abc", "argparse", "array", "asyncio", "base64", "binascii", "bisect", "builtins", "calendar",
            "cmath", "collections", "contextlib", "copy", "csv", "ctypes", "datetime", "decimal", "enum",
            "errno", "functools", "gc", "getopt", "getpass", "gettext", "glob", "gzip", "hashlib", "heapq",
            "hmac", "imp", "importlib", "inspect", "io", "itertools", "json", "locale", "logging", "math",
            "numbers", "operator", "os", "pathlib", "pickle", "platform", "plistlib", "pprint", "queue",
            "random", "re", "select", "shutil", "signal", "socket", "sqlite3", "ssl", "stat", "string",
            "struct", "subprocess", "sys", "tempfile", "textwrap", "threading", "time", "timeit", "traceback",
            "types", "typing", "unicodedata", "unittest", "uuid", "warnings", "weakref", "xml", "zipfile", "zipimport"
        };

        private static bool IsBuiltInLibrary(string libName)
        {
            return builtInModules.Contains(libName);
        }


        private static bool IsLibraryInstalled(string libraryName, string sitePackagesPath)
        {
            if (string.IsNullOrEmpty(libraryName))
                return false;

            if (IsBuiltInLibrary(libraryName))
                return true;

            string folderPath = System.IO.Path.Combine(sitePackagesPath, libraryName);
            if (Directory.Exists(folderPath)) return true;

            return Directory.GetDirectories(sitePackagesPath, libraryName + "*").Any();
        }

        private static async Task<bool> InstallLibraryAsync(string pythonExePath, string libraryName, string sitePackagesPath, Action<string> showProgress, Action hideProgress)
        {
            var psi = new ProcessStartInfo()
            {
                FileName = pythonExePath,
                Arguments = $"-m pip install {libraryName} --target \"{sitePackagesPath}\" --disable-pip-version-check --quiet",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            try
            {
                showProgress?.Invoke(libraryName);
                using (var process = Process.Start(psi))
                {
                    await process.WaitForExitAsync();

                    if (process.ExitCode != 0)
                    {
                        hideProgress?.Invoke();
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            MessageBox.Show($"Failed to install {libraryName}.", "Pip Install Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        });
                        return false;
                    }
                }
            }
            finally
            {
                hideProgress?.Invoke();
            }
            return true;
        }





        private async Task RunPythonScriptAsync(string pythonExePath, string scriptCode)
        {
            // הגדרת תיקיית הסקריפטים הקבועה
            string scriptsDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PyChunks", "Scripts");
            Directory.CreateDirectory(scriptsDir);

            CleanupOldScripts(scriptsDir);


            string chunkId = SanitizeFileName(TitleTextBlock.Text);

            string tempScriptPath = System.IO.Path.Combine(scriptsDir, $"{chunkId}_{DateTime.Now:yyyyMMdd_HHmmss}.py");

            string wrappedScript = "import sys\n" +
                                   "sys.stdout.reconfigure(encoding='utf-8')\n" +
                                   "sys.stderr.reconfigure(encoding='utf-8')\n" +
                                   scriptCode;

            File.WriteAllText(tempScriptPath, wrappedScript, new UTF8Encoding(false));

            var libs = ExtractRequiredLibraries(scriptCode);

            Window progressWin = null;
            Action<string> showProgress = (lib) =>
            {
                progressWin = CreateProgressWindow(lib);
                progressWin.Show();
            };
            Action hideProgress = () =>
            {
                if (progressWin != null) progressWin.Close();
            };

            foreach (var lib in libs)
            {
                if (!IsLibraryInstalled(lib, System.IO.Path.Combine(System.IO.Path.GetDirectoryName(pythonExePath), "Lib", "site-packages")))
                {
                    bool installed = await InstallLibraryAsync(pythonExePath, lib, System.IO.Path.Combine(System.IO.Path.GetDirectoryName(pythonExePath), "Lib", "site-packages"), showProgress, hideProgress);
                    if (!installed) return;
                }
            }

            var psi = new ProcessStartInfo()
            {
                FileName = pythonExePath,
                Arguments = $"-u \"{tempScriptPath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using (var process = new Process { StartInfo = psi, EnableRaisingEvents = true })
            {
                var outputWindow = new OutputWindow(this, "", TitleTextBlock.Text);
                outputWindow.Show();

                var buffer = new char[1024];
                int read;
                bool isWaitingInput = false;

                process.Start();
                outputWindow.SetAttachedProcess(process);

                var stdOut = process.StandardOutput;
                var stdErr = process.StandardError;
                var stdIn = process.StandardInput;

                var readOutputTask = Task.Run(async () =>
                {
                    while ((read = await stdOut.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        string text = new string(buffer, 0, read);
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            outputWindow.AppendOutput(text);

                            if (!text.EndsWith("\n") && !isWaitingInput)
                            {
                                isWaitingInput = true;
                                outputWindow.WaitForInput();
                            }
                            else if (text.EndsWith("\n") && isWaitingInput)
                            {
                                isWaitingInput = false;
                                outputWindow.DisableInput();
                            }
                        });
                    }
                });

                var readErrorTask = Task.Run(async () =>
                {
                    while ((read = await stdErr.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        string text = new string(buffer, 0, read);
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            outputWindow.AppendOutput("[Error] " + text);
                        });
                    }
                });

                outputWindow.SetProcessInputHandler(input =>
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            stdIn.WriteLine(input);
                            stdIn.Flush();
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                outputWindow.AppendOutput(input + "\n");
                            });
                        }
                    }
                    catch { }
                });

                await Task.WhenAll(readOutputTask, readErrorTask);

                await Task.Run(() => process.WaitForExit());

                Application.Current.Dispatcher.Invoke(() =>
                {
                    outputWindow.DisableInput();
                    outputWindow.UpdateStopButtonToClose(); // שינוי הכפתור ל־Close
                });
            }
        }

        private string SanitizeFileName(string name)
        {
            foreach (char c in System.IO.Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name;
        }


        private void CleanupOldScripts(string scriptsDir, int daysToKeep = 7)
        {
            try
            {
                var dirInfo = new DirectoryInfo(scriptsDir);
                if (!dirInfo.Exists) return; // אם התיקייה לא קיימת - אין מה למחוק

                DateTime cutoffDate = DateTime.Now.AddDays(-daysToKeep);

                var oldFiles = dirInfo.GetFiles("*.py")
                                     .Where(f => f.CreationTime < cutoffDate)
                                     .ToList();

                foreach (var file in oldFiles)
                {
                    try
                    {
                        file.Delete();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed to delete {file.FullName}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during CleanupOldScripts: {ex.Message}");
            }
        }






        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2) MaximizeRestoreButton_Click(sender, e);
            else this.DragMove();
        }

        private void MaximizeRestoreButton_Click(object sender, RoutedEventArgs e)
        {
            if (isMaximized)
            {
                this.WindowState = WindowState.Normal;
                this.Left = restoreBounds.Left;
                this.Top = restoreBounds.Top;
                this.Width = restoreBounds.Width;
                this.Height = restoreBounds.Height;
            }
            else
            {
                restoreBounds = new Rect(this.Left, this.Top, this.Width, this.Height);
                this.WindowState = WindowState.Maximized;
            }
            isMaximized = !isMaximized;
        }

        public void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            // Stylish confirmation dialog
            Window confirmDialog = new Window
            {
                Title = "Confirm Close",
                Width = 350,
                Height = 170,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.None,
                Background = new SolidColorBrush(Color.FromRgb(45, 45, 48))
            };

            Border mainBorder = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(28, 28, 30)),
                BorderThickness = new Thickness(1)
            };

            Grid mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(50) });

            // Title bar
            Border titleBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(45, 45, 48)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(28, 28, 30)),
                BorderThickness = new Thickness(0, 0, 0, 1)
            };

            Grid titleGrid = new Grid();
            TextBlock titleText = new TextBlock
            {
                Text = "Confirm Close",
                Foreground = Brushes.White,
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            Button closeButton = new Button
            {
                Content = "×",
                FontSize = 16,
                Width = 30,
                Height = 30,
                Foreground = Brushes.White,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Right
            };

            closeButton.Click += (s, args) => confirmDialog.Close();

            titleGrid.Children.Add(titleText);
            titleGrid.Children.Add(closeButton);
            titleBorder.Child = titleGrid;
            Grid.SetRow(titleBorder, 0);
            mainGrid.Children.Add(titleBorder);

            // Message
            TextBlock messageText = new TextBlock
            {
                Text = "Are you sure you want to close this chunk?\nYou will not be able to restore it later.",
                Foreground = Brushes.White,
                Margin = new Thickness(20),
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Grid.SetRow(messageText, 1);
            mainGrid.Children.Add(messageText);

            // Buttons
            StackPanel buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10)
            };

            Button yesButton = new Button
            {
                Content = "Yes",
                Width = 80,
                Height = 30,
                Margin = new Thickness(5),
                Background = new SolidColorBrush(Color.FromRgb(92, 184, 92)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Template = CreateRoundedButtonTemplate(new SolidColorBrush(Color.FromRgb(92, 184, 92)))
            };

            Button noButton = new Button
            {
                Content = "No",
                Width = 80,
                Height = 30,
                Margin = new Thickness(5),
                Background = new SolidColorBrush(Color.FromRgb(60, 60, 65)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Template = CreateRoundedButtonTemplate(new SolidColorBrush(Color.FromRgb(60, 60, 65)))
            };

            yesButton.Click += (s, args) => { confirmDialog.DialogResult = true; confirmDialog.Close(); };
            noButton.Click += (s, args) => { confirmDialog.DialogResult = false; confirmDialog.Close(); };

            buttonPanel.Children.Add(yesButton);
            buttonPanel.Children.Add(noButton);
            Grid.SetRow(buttonPanel, 2);
            mainGrid.Children.Add(buttonPanel);

            mainBorder.Child = mainGrid;
            confirmDialog.Content = mainBorder;
            titleBorder.MouseLeftButtonDown += (s, args) => confirmDialog.DragMove();

            if (confirmDialog.ShowDialog() == true)
            {
                chunkSideBar help = head;
                if (help.next == null) head.name = "";
                else if (help.name == current.name) head = head.next;
                else
                {
                    while (help.next != null && help.next.name != current.name) help = help.next;
                    if (help.next != null) help.next = help.next.next;
                }

                if (this.Owner is MainWindow mainWindow) mainWindow.updateSideBar();
                this.Close();
            }
        }

        private ControlTemplate CreateRoundedButtonTemplate(Brush defaultBackground)
        {
            ControlTemplate template = new ControlTemplate(typeof(Button));
            FrameworkElementFactory borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.Name = "border";
            borderFactory.SetValue(Border.BackgroundProperty, defaultBackground);
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));

            FrameworkElementFactory contentPresenterFactory = new FrameworkElementFactory(typeof(ContentPresenter));
            contentPresenterFactory.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            contentPresenterFactory.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);

            borderFactory.AppendChild(contentPresenterFactory);
            template.VisualTree = borderFactory;

            Trigger mouseOverTrigger = new Trigger();
            mouseOverTrigger.Property = UIElement.IsMouseOverProperty;
            mouseOverTrigger.Value = true;
            Color baseColor = ((SolidColorBrush)defaultBackground).Color;
            mouseOverTrigger.Setters.Add(new Setter(Border.BackgroundProperty,
                new SolidColorBrush(Color.FromRgb(
                    (byte)Math.Max(0, baseColor.R - 20),
                    (byte)Math.Max(0, baseColor.G - 20),
                    (byte)Math.Max(0, baseColor.B - 20)))));
            template.Triggers.Add(mouseOverTrigger);

            return template;
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            this.Owner = null;
        }

        private void EditTitle_Click(object sender, RoutedEventArgs e)
        {
            isEditing = !isEditing;
            TitleTextBlock.Visibility = isEditing ? Visibility.Collapsed : Visibility.Visible;
            TitleTextBox.Visibility = isEditing ? Visibility.Visible : Visibility.Collapsed;

            if (isEditing)
            {
                TitleTextBox.Focus();
                TitleTextBox.SelectAll();
            }
            else
            {
                TitleTextBlock.Text = TitleTextBox.Text;
                current.name = TitleTextBlock.Text;
                if (this.Owner is MainWindow mainWindow) mainWindow.updateSideBar();
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (isAnimationInProgress) return;

            // Store reference to the minimize button
            minimizeButtonRef = sender as Button;

            // Stop any ongoing animations to prevent conflicts
            this.BeginAnimation(HeightProperty, null);
            ContentGrid.BeginAnimation(OpacityProperty, null);

            // Toggle collapsed state
            isCollapsed = !isCollapsed;

            // Update button icon immediately
            UpdateMinimizeButtonIcon(isCollapsed);

            // Perform the minimize/restore action
            if (isCollapsed)
            {
                MinimizeWindowWithAnimation();
            }
            else
            {
                RestoreWindowWithAnimation();
            }
        }

        private void MinimizeWindowWithAnimation()
        {
            if (isAnimationInProgress) return;

            isAnimationInProgress = true;

            // Store current height if not already stored
            if (normalHeight <= 0 || normalHeight == 40)
            {
                normalHeight = this.ActualHeight > 40 ? this.ActualHeight : 300; // Fallback to 300 if something's wrong
            }

            // First fade out the content
            var contentFadeOut = new DoubleAnimation(ContentGrid.Opacity, 0, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };

            contentFadeOut.Completed += (s, e) =>
            {
                // Hide content after fade out
                ContentGrid.Visibility = Visibility.Collapsed;

                // Then animate height reduction
                var heightAnim = new DoubleAnimation(this.ActualHeight, 40, TimeSpan.FromMilliseconds(250))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };

                heightAnim.Completed += (s2, e2) =>
                {
                    isAnimationInProgress = false;
                    // Ensure final state is correct
                    this.Height = 40;
                    this.MinHeight = 40;
                };

                this.BeginAnimation(HeightProperty, heightAnim);
            };

            ContentGrid.BeginAnimation(OpacityProperty, contentFadeOut);
        }

        private void RestoreWindowWithAnimation()
        {
            if (isAnimationInProgress) return;

            isAnimationInProgress = true;

            // Ensure we have a valid normal height
            if (normalHeight <= 40)
            {
                normalHeight = 400; // Default fallback height
            }

            // Reset MinHeight to allow expansion
            this.MinHeight = 0;

            // First expand the window
            var heightAnim = new DoubleAnimation(this.ActualHeight, normalHeight, TimeSpan.FromMilliseconds(250))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            heightAnim.Completed += (s, e) =>
            {
                // Show content and fade it in
                ContentGrid.Visibility = Visibility.Visible;
                ContentGrid.Opacity = 0;

                var contentFadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
                };

                contentFadeIn.Completed += (s2, e2) =>
                {
                    isAnimationInProgress = false;
                    // Ensure final state is correct
                    ContentGrid.Opacity = 1;
                    this.Height = normalHeight;
                };

                ContentGrid.BeginAnimation(OpacityProperty, contentFadeIn);
            };

            this.BeginAnimation(HeightProperty, heightAnim);
        }

        private void UpdateMinimizeButtonIcon(bool isMinimized)
        {
            if (minimizeButtonRef == null) return;

            try
            {
                // Create the path geometry for the minimize/restore icon
                var pathGeometry = isMinimized ? "M0,8 L5,3 L10,8" : "M0,3 L5,8 L10,3";

                var iconPath = new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse(pathGeometry),
                    Stroke = Brushes.White,
                    StrokeThickness = 1.5,
                    Width = 10,
                    Height = 8,
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };

                minimizeButtonRef.Content = iconPath;
            }
            catch (Exception ex)
            {
                // Fallback to text if path creation fails
                minimizeButtonRef.Content = isMinimized ? "▲" : "▼";
                System.Diagnostics.Debug.WriteLine($"Icon update failed: {ex.Message}");
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog { Filter = "Python Files (*.py)|*.py", DefaultExt = ".py" };
            if (dialog.ShowDialog() == true) File.WriteAllText(dialog.FileName, PopupTextBox.Text);
        }

        private void LoadButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Filter = "Python Files (*.py)|*.py", DefaultExt = ".py" };
            if (dialog.ShowDialog() == true) PopupTextBox.Text = File.ReadAllText(dialog.FileName);
        }

        private void PopupTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!(sender is TextBox textBox)) return;
            int caretIndex = textBox.CaretIndex;

            if (e.Key == Key.Tab)
            {
                e.Handled = true;
                textBox.Text = textBox.Text.Insert(caretIndex, "    ");
                textBox.CaretIndex = caretIndex + 4;
            }
            else if (e.Key == Key.Back && caretIndex >= 4 && textBox.Text.Substring(caretIndex - 4, 4) == "    ")
            {
                e.Handled = true;
                textBox.Text = textBox.Text.Remove(caretIndex - 4, 4);
                textBox.CaretIndex = caretIndex - 4;
            }
        }

        protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnPreviewMouseLeftButtonDown(e);

            if (isCollapsed && !isAnimationInProgress)
            {
                // Check if the click is not on the minimize button itself
                var hitElement = e.OriginalSource as FrameworkElement;

                // More robust check for minimize button
                bool isMinimizeButtonClick = false;

                if (hitElement != null)
                {
                    // Check if we clicked on the minimize button or any of its children
                    var parent = hitElement;
                    while (parent != null)
                    {
                        if (parent.Name == "MinimizeButton" ||
                            (parent is Button btn && btn.Name == "MinimizeButton") ||
                            parent == minimizeButtonRef)
                        {
                            isMinimizeButtonClick = true;
                            break;
                        }
                        parent = parent.Parent as FrameworkElement ??
                                (parent.TemplatedParent as FrameworkElement);
                    }
                }

                // Only restore if we didn't click the minimize button
                if (!isMinimizeButtonClick)
                {
                    isCollapsed = false;
                    UpdateMinimizeButtonIcon(false);
                    RestoreWindowWithAnimation();
                }
            }
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);

            // Only update normalHeight if we're not collapsed and not animating
            if (!isCollapsed && !isAnimationInProgress && sizeInfo.HeightChanged)
            {
                // Only store height if it's a reasonable size (not the minimized height)
                if (this.ActualHeight > 40)
                {
                    normalHeight = this.ActualHeight;
                }
            }
        }

        private void EnsureMinimizeState()
        {
            if (isCollapsed)
            {
                ContentGrid.Visibility = Visibility.Collapsed;
                this.Height = 40;
                this.MinHeight = 40;
                UpdateMinimizeButtonIcon(true);
            }
            else
            {
                ContentGrid.Visibility = Visibility.Visible;
                ContentGrid.Opacity = 1;
                this.MinHeight = 0;
                if (normalHeight > 40)
                {
                    this.Height = normalHeight;
                }
                UpdateMinimizeButtonIcon(false);
            }
        }

        private void InitializeMinimizeState()
        {
            // Ensure we have a valid normal height from the start
            this.Loaded += (s, e) =>
            {
                if (normalHeight <= 0)
                {
                    normalHeight = this.ActualHeight;
                }
                EnsureMinimizeState();
            };
        }

        protected override void OnLocationChanged(EventArgs e)
        {
            base.OnLocationChanged(e);
            if (current != null)
            {
                current.Left = this.Left;
                current.Top = this.Top;
            }
        }
    }
}




