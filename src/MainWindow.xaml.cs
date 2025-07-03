using Microsoft.Win32;
using PyChunks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Xml.Serialization;

namespace PyChunks
{
    public class chunkSideBar
    {
        public string name = "";
        public chunkSideBar next;
        public ChunkWindow window;
        public double Left { get; set; }
        public double Top { get; set; }

        public chunkSideBar(string name, chunkSideBar next)
        {
            this.name = name;
            this.next = next;
        }
    }

    public partial class MainWindow : Window
    {
        private bool isMaximized = false;
        private Rect restoreBounds;
        public chunkSideBar head = new chunkSideBar("", null);
        public List<ChunkWindow> openWindows = new List<ChunkWindow>();
        private DispatcherTimer menuHideTimer;
        private StackPanel currentHoveredMenu;

        public MainWindow()
        {
            InitializeComponent();

            // Initialize timer for menu hiding
            menuHideTimer = new DispatcherTimer();
            menuHideTimer.Interval = TimeSpan.FromMilliseconds(300);
            menuHideTimer.Tick += MenuHideTimer_Tick;
        }

        private void MenuHideTimer_Tick(object sender, EventArgs e)
        {
            menuHideTimer.Stop();
            if (currentHoveredMenu != null)
            {
                AnimateMenuClose(currentHoveredMenu);
                currentHoveredMenu = null;
            }
        }

        private void OpenPopupButton_Click(object sender, RoutedEventArgs e)
        {
            ChunkWindow popup = new ChunkWindow(this, head);
            popup.Show();
            openWindows.Add(popup);
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                MaximizeRestoreButton_Click(sender, e);
            }
            else
            {
                this.DragMove();
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
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
                isMaximized = false;
            }
            else
            {
                restoreBounds = new Rect(this.Left, this.Top, this.Width, this.Height);
                this.WindowState = WindowState.Maximized;
                isMaximized = true;
            }
        }

        public void updateSideBar()
        {
            Console.WriteLine("Updating sidebar...");

            var sidebarStackPanel = this.FindName("SidebarStackPanel") as StackPanel;
            if (sidebarStackPanel == null)
            {
                MessageBox.Show("SidebarStackPanel not found", "Error");
                return;
            }

            while (sidebarStackPanel.Children.Count > 2)
            {
                sidebarStackPanel.Children.RemoveAt(2);
            }

            int totalChunks = 0;
            chunkSideBar countNode = head;
            while (countNode != null)
            {
                totalChunks++;
                countNode = countNode.next;
            }

            Console.WriteLine($"Found {totalChunks} chunks in linked list");

            chunkSideBar node = head.next;
            int buttonCount = 0;

            while (node != null)
            {
                string chunkName = node.name;
                Console.WriteLine($"Creating button for chunk: {chunkName}");

                // Create the main container for the button and menu
                StackPanel chunkContainer = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    Margin = new Thickness(0, 5, 0, 0)
                };

                // Create the chunk button with sliding text animation
                Button chunkButton = new Button
                {
                    Content = chunkName,
                    Style = (Style)this.FindResource("SidebarButtonStyle"),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Top,
                    Height = 30,
                    Width = 101,
                    RenderTransformOrigin = new Point(0.5, 0.5),
                    RenderTransform = new TranslateTransform(),
                    Tag = node // Store the chunkSideBar reference in the button's Tag
                };

                // Initialize render transform for sliding animation
                chunkButton.Loaded += (s, e) => {
                    var textBlock = FindVisualChild<TextBlock>(chunkButton, "ContentText");
                    if (textBlock != null)
                    {
                        textBlock.RenderTransform = new TranslateTransform();
                        textBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

                        if (textBlock.DesiredSize.Width > 80)
                        {
                            DoubleAnimation slideAnimation = new DoubleAnimation
                            {
                                From = 0, // מתחיל מההתחלה ללא מרווח
                                To = -(textBlock.DesiredSize.Width - 70),
                                Duration = TimeSpan.FromSeconds(3),
                                AutoReverse = true,
                                RepeatBehavior = RepeatBehavior.Forever,
                                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
                            };

                            textBlock.Tag = slideAnimation;
                        }
                    }
                };

                chunkButton.MouseEnter += (s, e) => {
                    var textBlock = FindVisualChild<TextBlock>(chunkButton, "ContentText");
                    if (textBlock != null && textBlock.Tag is DoubleAnimation animation)
                    {
                        textBlock.TextTrimming = TextTrimming.None;
                        textBlock.RenderTransform.BeginAnimation(TranslateTransform.XProperty, animation);
                    }
                };

                chunkButton.MouseLeave += (s, e) => {
                    var textBlock = FindVisualChild<TextBlock>(chunkButton, "ContentText");
                    if (textBlock != null)
                    {
                        textBlock.RenderTransform.BeginAnimation(TranslateTransform.XProperty, null);
                        textBlock.RenderTransform = new TranslateTransform();
                        textBlock.TextTrimming = TextTrimming.CharacterEllipsis;
                    }
                };

                chunkButton.Click += ChunkButton_Click;
                chunkButton.MouseEnter += ChunkButton_MouseEnter;
                chunkButton.MouseLeave += ChunkButton_MouseLeave;

                // Create the menu panel (initially hidden)
                StackPanel menuPanel = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 5, 0, 0),
                    Visibility = Visibility.Collapsed,
                    Opacity = 0,
                    Background = new SolidColorBrush(Color.FromArgb(220, 45, 45, 48)),
                    Tag = node // Store the chunkSideBar reference
                };

                // Apply rounded corners and shadow effect to menu
                menuPanel.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black,
                    Direction = 270,
                    ShadowDepth = 2,
                    BlurRadius = 8,
                    Opacity = 0.3
                };

                Border menuBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(220, 45, 45, 48)),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(8, 4, 8, 4)
                };

                StackPanel menuContent = new StackPanel
                {
                    Orientation = Orientation.Vertical
                };

                // Focus button with icon and text
                Button focusButton = new Button
                {
                    Height = 28,
                    Background = Brushes.Transparent,
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    Margin = new Thickness(0, 0, 0, 2),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(8, 4, 8, 4),
                    Tag = node,
                    Style = null
                };

                // Create content for focus button with icon and text
                StackPanel focusContent = new StackPanel
                {
                    Orientation = Orientation.Horizontal
                };

                // Focus/search icon using Path
                System.Windows.Shapes.Path focusIcon = new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse("M15.5 14h-.79l-.28-.27C15.41 12.59 16 11.11 16 9.5 16 5.91 13.09 3 9.5 3S3 5.91 3 9.5 5.91 16 9.5 16c1.61 0 3.09-.59 4.23-1.57l.27.28v.79l5 4.99L20.49 19l-4.99-5zm-6 0C7.01 14 5 11.99 5 9.5S7.01 5 9.5 5 14 7.01 14 9.5 11.99 14 9.5 14z"),
                    Fill = Brushes.White,
                    Width = 14,
                    Height = 14,
                    Stretch = Stretch.Uniform,
                    Margin = new Thickness(0, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };

                TextBlock focusText = new TextBlock
                {
                    Text = "Focus",
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center
                };

                focusContent.Children.Add(focusIcon);
                focusContent.Children.Add(focusText);
                focusButton.Content = focusContent;

                focusButton.Template = CreateSimpleButtonTemplate();
                focusButton.Click += FocusButton_Click;
                focusButton.MouseEnter += MenuButton_MouseEnter;
                focusButton.MouseLeave += MenuButton_MouseLeave;

                // Delete button with icon and text
                Button deleteButton = new Button
                {
                    Height = 28,
                    Background = Brushes.Transparent,
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(8, 4, 8, 4),
                    Tag = node,
                    Style = null
                };

                // Create content for delete button with icon and text
                StackPanel deleteContent = new StackPanel
                {
                    Orientation = Orientation.Horizontal
                };

                // Delete/trash icon using Path
                System.Windows.Shapes.Path deleteIcon = new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse("M6 19c0 1.1.9 2 2 2h8c1.1 0 2-.9 2-2V7H6v12zM19 4h-3.5l-1-1h-5l-1 1H5v2h14V4z"),
                    Fill = Brushes.White,
                    Width = 14,
                    Height = 14,
                    Stretch = Stretch.Uniform,
                    Margin = new Thickness(0, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };

                TextBlock deleteText = new TextBlock
                {
                    Text = "Delete",
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center
                };

                deleteContent.Children.Add(deleteIcon);
                deleteContent.Children.Add(deleteText);
                deleteButton.Content = deleteContent;

                deleteButton.Template = CreateSimpleButtonTemplate();
                deleteButton.Click += DeleteButton_Click;
                deleteButton.MouseEnter += MenuButton_MouseEnter;
                deleteButton.MouseLeave += MenuButton_MouseLeave;

                menuContent.Children.Add(focusButton);
                menuContent.Children.Add(deleteButton);
                menuBorder.Child = menuContent;
                menuPanel.Children.Add(menuBorder);

                // Add mouse enter/leave for menu panel itself
                menuPanel.MouseEnter += MenuPanel_MouseEnter;
                menuPanel.MouseLeave += MenuPanel_MouseLeave;

                chunkContainer.Children.Add(chunkButton);
                chunkContainer.Children.Add(menuPanel);

                sidebarStackPanel.Children.Add(chunkContainer);
                AnimateButton(chunkContainer, buttonCount);

                buttonCount++;
                node = node.next;
            }

            Console.WriteLine($"Added {buttonCount} buttons to sidebar");
            sidebarStackPanel.UpdateLayout();
        }

        // Helper method to find visual child
        private T FindVisualChild<T>(DependencyObject parent, string childName) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T && ((FrameworkElement)child).Name == childName)
                {
                    return (T)child;
                }
                else
                {
                    var result = FindVisualChild<T>(child, childName);
                    if (result != null)
                        return result;
                }
            }
            return null;
        }

        private ControlTemplate CreateSimpleButtonTemplate()
        {
            ControlTemplate template = new ControlTemplate(typeof(Button));

            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.Name = "border";
            border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(0));

            FrameworkElementFactory contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
            contentPresenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            contentPresenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);

            border.AppendChild(contentPresenter);
            template.VisualTree = border;

            // Add triggers for hover effects
            Trigger mouseOverTrigger = new Trigger();
            mouseOverTrigger.Property = Button.IsMouseOverProperty;
            mouseOverTrigger.Value = true;
            mouseOverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)), "border"));
            template.Triggers.Add(mouseOverTrigger);

            return template;
        }

        private void MenuButton_MouseEnter(object sender, MouseEventArgs e)
        {
            Button button = sender as Button;
            if (button != null)
            {
                ScaleTransform scaleTransform = new ScaleTransform(1.1, 1.1);
                button.RenderTransform = scaleTransform;
                button.RenderTransformOrigin = new Point(0.5, 0.5);

                DoubleAnimation scaleAnimation = new DoubleAnimation
                {
                    To = 1.1,
                    Duration = TimeSpan.FromMilliseconds(150),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };

                scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnimation);
                scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimation);
            }
        }

        private void MenuButton_MouseLeave(object sender, MouseEventArgs e)
        {
            Button button = sender as Button;
            if (button != null)
            {
                ScaleTransform scaleTransform = button.RenderTransform as ScaleTransform;
                if (scaleTransform != null)
                {
                    DoubleAnimation scaleAnimation = new DoubleAnimation
                    {
                        To = 1.0,
                        Duration = TimeSpan.FromMilliseconds(150),
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };

                    scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnimation);
                    scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimation);
                }
            }
        }

        private void ChunkButton_MouseEnter(object sender, MouseEventArgs e)
        {
            menuHideTimer.Stop();

            Button button = sender as Button;
            if (button != null)
            {
                StackPanel parent = button.Parent as StackPanel;
                if (parent != null && parent.Children.Count > 1)
                {
                    StackPanel menuPanel = parent.Children[1] as StackPanel;
                    if (menuPanel != null)
                    {
                        if (currentHoveredMenu != null && currentHoveredMenu != menuPanel)
                        {
                            AnimateMenuClose(currentHoveredMenu);
                        }

                        currentHoveredMenu = menuPanel;
                        AnimateMenuOpen(menuPanel);
                    }
                }
            }
        }

        private void ChunkButton_MouseLeave(object sender, MouseEventArgs e)
        {
            // Start timer to potentially hide menu
            menuHideTimer.Stop();
            menuHideTimer.Start();
        }

        private void MenuPanel_MouseEnter(object sender, MouseEventArgs e)
        {
            // Cancel hide timer when mouse enters menu
            menuHideTimer.Stop();
        }

        private void MenuPanel_MouseLeave(object sender, MouseEventArgs e)
        {
            // Start timer to hide menu when mouse leaves
            menuHideTimer.Stop();
            menuHideTimer.Start();
        }

        private void AnimateMenuOpen(StackPanel menuPanel)
        {
            menuPanel.Visibility = Visibility.Visible;

            DoubleAnimation opacityAnimation = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            DoubleAnimation scaleXAnimation = new DoubleAnimation
            {
                From = 0.8,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 }
            };

            DoubleAnimation scaleYAnimation = new DoubleAnimation
            {
                From = 0.8,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 }
            };

            ScaleTransform scaleTransform = new ScaleTransform();
            menuPanel.RenderTransform = scaleTransform;
            menuPanel.RenderTransformOrigin = new Point(0.5, 0);

            menuPanel.BeginAnimation(UIElement.OpacityProperty, opacityAnimation);
            scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleXAnimation);
            scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleYAnimation);
        }

        private void AnimateMenuClose(StackPanel menuPanel)
        {
            DoubleAnimation opacityAnimation = new DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(150),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };

            DoubleAnimation scaleAnimation = new DoubleAnimation
            {
                From = 1.0,
                To = 0.8,
                Duration = TimeSpan.FromMilliseconds(150),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };

            opacityAnimation.Completed += (s, ev) =>
            {
                menuPanel.Visibility = Visibility.Collapsed;
            };

            ScaleTransform scaleTransform = menuPanel.RenderTransform as ScaleTransform ?? new ScaleTransform();
            menuPanel.RenderTransform = scaleTransform;
            menuPanel.RenderTransformOrigin = new Point(0.5, 0);

            menuPanel.BeginAnimation(UIElement.OpacityProperty, opacityAnimation);
            scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnimation);
            scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimation);
        }

        private void FocusButton_Click(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            if (button != null && button.Tag is chunkSideBar chunk)
            {
                if (chunk.window != null)
                {
                    chunk.window.WindowState = WindowState.Maximized;
                    BringChunkWindowToFront(chunk.window);
                }
            }
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            if (button != null && button.Tag is chunkSideBar chunk)
            {
                if (chunk.window != null)
                {
                    chunk.window.CloseButton_Click(sender, new RoutedEventArgs());
                }
            }
        }

        private void ChunkButton_Click(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            if (button != null && button.Tag is chunkSideBar chunk)
            {
                if (chunk.window != null)
                {
                    // Bring the window to front of all other chunk windows
                    BringChunkWindowToFront(chunk.window);
                }
            }
        }

        private void BringChunkWindowToFront(ChunkWindow targetWindow)
        {
            // First, set all other chunk windows to normal topmost state
            foreach (ChunkWindow window in openWindows)
            {
                if (window != targetWindow && window.IsLoaded)
                {
                    window.Topmost = false;
                }
            }

            // Then bring the target window to front
            targetWindow.Activate();
            targetWindow.Topmost = true;

            // Use a small delay to ensure proper window ordering
            Dispatcher.BeginInvoke(new Action(() =>
            {
                targetWindow.Topmost = false; // Reset topmost to allow normal window interaction
                targetWindow.Focus();
            }), DispatcherPriority.Background);
        }

        private void AnimateButtonRemoval(StackPanel container, StackPanel parentPanel)
        {
            DoubleAnimation opacityAnimation = new DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };

            DoubleAnimation translateAnimation = new DoubleAnimation
            {
                From = 0,
                To = -30,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };

            opacityAnimation.Completed += (s, ev) =>
            {
                parentPanel.Children.Remove(container);
            };

            container.BeginAnimation(UIElement.OpacityProperty, opacityAnimation);
            (container.RenderTransform as TranslateTransform).BeginAnimation(TranslateTransform.XProperty, translateAnimation);
        }

        private void AnimateButton(StackPanel container, int index)
        {
            container.Opacity = 0;
            container.RenderTransform = new TranslateTransform(-20, 0);

            DoubleAnimation opacityAnimation = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(400),
                BeginTime = TimeSpan.FromMilliseconds(30 * index),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            DoubleAnimation translateAnimation = new DoubleAnimation
            {
                From = -20,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(450),
                BeginTime = TimeSpan.FromMilliseconds(30 * index),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            container.BeginAnimation(UIElement.OpacityProperty, opacityAnimation);
            (container.RenderTransform as TranslateTransform).BeginAnimation(TranslateTransform.XProperty, translateAnimation);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void SaveProject_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog();
            saveFileDialog.Filter = "PyChunks Project files (*.pycproj)|*.pycproj";
            if (saveFileDialog.ShowDialog() == true)
            {
                ProjectData projectData = new ProjectData();
                chunkSideBar currentNode = head.next;
                while (currentNode != null)
                {
                    if (currentNode.window != null)
                    {
                        projectData.Chunks.Add(new ChunkData
                        {
                            Name = currentNode.name,
                            Code = currentNode.window.PopupTextBox.Text,
                            Left = currentNode.window.Left,
                            Top = currentNode.window.Top
                        });
                    }
                    currentNode = currentNode.next;
                }

                XmlSerializer serializer = new XmlSerializer(typeof(ProjectData));
                using (FileStream fs = new FileStream(saveFileDialog.FileName, FileMode.Create))
                {
                    serializer.Serialize(fs, projectData);
                }
                ShowCustomMessageBox("Space saved successfully!", "Save Space", true);
            }
        }

        private void ClearProject_Click(object sender, RoutedEventArgs e)
        {
            
            var childrenToRemove = SidebarStackPanel.Children
                .OfType<FrameworkElement>()
                .Where(c => c.Name != "CreateChunkButton" && c.Name != "ExistingChunksText")
                .ToList();

            foreach (var child in childrenToRemove)
            {
                SidebarStackPanel.Children.Remove(child);
            }

            chunkSideBar current = head.next;
            while (current != null)
            {
                if (current.window != null)
                {
                    current.window.Close();
                }
                current = current.next;
            }

            head.next = null;

            ShowCustomMessageBox("All chunks have been cleared.", "Clear Space", true);
        }

        private void ShowCustomMessageBox(string message, string title, bool isSuccess)
        {
            Window messageDialog = new Window
            {
                Title = title,
                Width = 350,
                Height = 170,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent
            };

            Border mainBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(51, 51, 51)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8)
            };

            Grid mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(50) });

            // Title bar
            Border titleBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(37, 37, 38)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(51, 51, 51)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                CornerRadius = new CornerRadius(8, 8, 0, 0)
            };

            Grid titleGrid = new Grid();
            TextBlock titleText = new TextBlock
            {
                Text = title,
                Foreground = Brushes.White,
                Margin = new Thickness(15, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.Medium
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
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 5, 0)
            };

            closeButton.Click += (s, args) => messageDialog.Close();

            titleGrid.Children.Add(titleText);
            titleGrid.Children.Add(closeButton);
            titleBorder.Child = titleGrid;
            Grid.SetRow(titleBorder, 0);
            mainGrid.Children.Add(titleBorder);

            // Message content
            StackPanel messageContent = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(20)
            };

            // Icon
            System.Windows.Shapes.Path iconPath = new System.Windows.Shapes.Path
            {
                Width = 24,
                Height = 24,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(0, 0, 15, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            if (isSuccess)
            {
                iconPath.Data = Geometry.Parse("M9,20.42L2.79,14.21L5.62,11.38L9,14.77L18.88,4.88L21.71,7.71L9,20.42Z");
                iconPath.Fill = new SolidColorBrush(Color.FromRgb(76, 175, 80));
            }
            else
            {
                iconPath.Data = Geometry.Parse("M13,13H11V7H13M13,17H11V15H13M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2Z");
                iconPath.Fill = new SolidColorBrush(Color.FromRgb(244, 67, 54));
            }

            TextBlock messageText = new TextBlock
            {
                Text = message,
                Foreground = new SolidColorBrush(Color.FromRgb(212, 212, 212)),
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 14
            };

            messageContent.Children.Add(iconPath);
            messageContent.Children.Add(messageText);
            Grid.SetRow(messageContent, 1);
            mainGrid.Children.Add(messageContent);

            // OK Button
            Button okButton = new Button
            {
                Content = "OK",
                Width = 80,
                Height = 30,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(Color.FromRgb(76, 175, 80)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 0, 10)
            };

            okButton.Template = CreateRoundedButtonTemplate(new SolidColorBrush(Color.FromRgb(76, 175, 80)));
            okButton.Click += (s, args) => messageDialog.Close();

            Grid.SetRow(okButton, 2);
            mainGrid.Children.Add(okButton);

            mainBorder.Child = mainGrid;
            messageDialog.Content = mainBorder;
            messageDialog.ShowDialog();
        }

        private ControlTemplate CreateRoundedButtonTemplate(SolidColorBrush backgroundColor)
        {
            ControlTemplate template = new ControlTemplate(typeof(Button));

            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, backgroundColor);
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(0));

            FrameworkElementFactory contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
            contentPresenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            contentPresenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);

            border.AppendChild(contentPresenter);
            template.VisualTree = border;

            // Add triggers for hover effects
            Trigger mouseOverTrigger = new Trigger();
            mouseOverTrigger.Property = Button.IsMouseOverProperty;
            mouseOverTrigger.Value = true;
            mouseOverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(69, 160, 73))));
            template.Triggers.Add(mouseOverTrigger);

            return template;
        }




        private void LoadProject_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "PyChunks Project files (*.pycproj)|*.pycproj";
            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    XmlSerializer serializer = new XmlSerializer(typeof(ProjectData));
                    ProjectData projectData;
                    using (FileStream fs = new FileStream(openFileDialog.FileName, FileMode.Open))
                    {
                        projectData = (ProjectData)serializer.Deserialize(fs);
                    }

                    // Clear existing chunks
                    chunkSideBar currentNode = head.next;
                    while (currentNode != null)
                    {
                        if (currentNode.window != null)
                        {
                            currentNode.window.Close();
                        }
                        currentNode = currentNode.next;
                    }
                    head.next = null;
                    openWindows.Clear();

                    // Load new chunks
                    foreach (ChunkData chunk in projectData.Chunks)
                    {
                        ChunkWindow popup = new ChunkWindow(this, head);
                        popup.TitleTextBlock.Text = chunk.Name;
                        popup.TitleTextBox.Text = chunk.Name;
                        popup.PopupTextBox.Text = chunk.Code;
                        popup.Left = chunk.Left;
                        popup.Top = chunk.Top;
                        popup.Show();
                        openWindows.Add(popup);

                        // Update the chunkSideBar node with the correct window reference and position
                        chunkSideBar newChunkNode = head;
                        while (newChunkNode.next != null) newChunkNode = newChunkNode.next;
                        newChunkNode.window = popup;
                        newChunkNode.name = chunk.Name; // Update the name of the chunkSideBar node
                        newChunkNode.Left = chunk.Left;
                        newChunkNode.Top = chunk.Top;
                    }
                    updateSideBar();
                    ShowCustomMessageBox("Space loaded successfully!", "Load Space", true);
                }
                catch (Exception ex)
                {
                    ShowCustomMessageBox($"Error loading space: {ex.Message}", "Load Space Error", false);
                }
            }
        }
    }
}




