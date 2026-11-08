using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using QuorumAPI;
using COP = QuorumAPI.COP;
using DColor = System.Drawing.Color;

namespace Legend
{
    public partial class MainWindow : Window
    {
        public enum Page { Home, Editor, Hub, Settings }
        Page currentPage = Page.Home;

        static readonly Color AccentC = Color.FromRgb(0xF0, 0xF1, 0xF3);
        static readonly Color MutedC = Color.FromRgb(0x6E, 0x6E, 0x6E);
        static readonly Color TabBgC = Color.FromRgb(0x14, 0x14, 0x14);
        static readonly Color TabBgActiveC = Color.FromRgb(0x1A, 0x1A, 0x1A);
        static readonly Color TextC = Color.FromRgb(0xE6, 0xE6, 0xE6);
        static readonly Color SoftC = Color.FromRgb(0xBD, 0xBD, 0xBD);
        static readonly Color PositiveC = Color.FromRgb(0xE4, 0xE6, 0xEA);
        static readonly Color WarningC = Color.FromRgb(0xE8, 0xA3, 0x3D);
        static readonly Color ErrorC = Color.FromRgb(0xE5, 0x4B, 0x4B);

        readonly QuorumModule quorum = new QuorumModule();
        bool quorumReady;
        bool busy;

        class Doc
        {
            public string Name;
            public string Text = "";
            public Button Header;
            public TextBlock Label;
            public SolidColorBrush Bg, Fg;
        }

        readonly List<Doc> docs = new List<Doc>();
        Doc current;
        int counter;

        bool alwaysOnTop = false;
        bool autoAttach = false;
        bool showTerminal = true;
        bool cpuLimiter = false;
        bool hardwareAccel = true;

        private static readonly HttpClient httpClient = CreateHttpClient();
        private static int hubRequestId;

        static HttpClient CreateHttpClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Legend/1.0 (Legend catalogue)");
            return client;
        }

        public MainWindow()
        {
            try
            {
                InitializeComponent();

                var wa = SystemParameters.WorkArea;
                MaxWidth = wa.Width; MaxHeight = wa.Height;

                NavHome.Stroke = new SolidColorBrush(AccentC);
                NavCode.Stroke = new SolidColorBrush(MutedC);
                NavHub.Stroke = new SolidColorBrush(MutedC);
                NavSettings.Stroke = new SolidColorBrush(MutedC);

                Editor.TextChanged += (s, e) => UpdateLines();
                Editor.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((s, e) =>
                    Canvas.SetTop(LineNumbers, -e.VerticalOffset)));

                PreviewKeyDown += (s, e) =>
                {
                    if (e.Key == Key.F1) ShowPage(Page.Home);
                    else if (e.Key == Key.F2) ShowPage(Page.Editor);
                    else if (e.Key == Key.F3) ShowPage(Page.Hub);
                    else if (e.Key == Key.F4) ShowPage(Page.Settings);
                };
                TitleBar.MouseLeftButtonDown += (s, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };

                Loaded += (s, e) =>
                {
                    BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)));
                    Dispatcher.BeginInvoke(new Action(async () => {
                        await LoadScriptBloxScriptsAsync("");
                    }), DispatcherPriority.Loaded);
                };

                InitQuorum();

                AddTab();
                ShowPage(Page.Home, false);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not start Legend:\n" + ex.Message, "Legend", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
            }
        }

        void InitQuorum()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string workspace = Path.Combine(baseDir, "Workspace");
                string autoexec = Path.Combine(baseDir, "autoexec");
                Directory.CreateDirectory(workspace);
                Directory.CreateDirectory(autoexec);

                quorum.MsgBoxLogs = false;
                quorum.SetAutoAttach(autoAttach);

                try
                {
                    quorum.SetWorkspace(workspace);
                    quorum.SetAutoexecute(autoexec);
                    quorum.SetInfo("Legend", "1.0.0", "Legend/1.0.0", "Legend-Fingerprint");
                    quorum.SetAttachNotification("Legend", "Attached.");
                }
                catch (Exception) { }

                QuorumModule.UseOutput(true);
                var logger = QuorumModule.Logger;
                if (logger != null)
                {
                    logger.SetLogSource(COP.LogSource.All);
                    logger.SetTheme(new COP.LogTheme
                    {
                        Info = DColor.FromArgb(0xE6, 0xE6, 0xE6),
                        Success = DColor.FromArgb(0xE4, 0xE6, 0xEA),
                        Warning = DColor.FromArgb(0xE8, 0xA3, 0x3D),
                        Error = DColor.FromArgb(0xE5, 0x4B, 0x4B),
                        System = DColor.FromArgb(0x8A, 0x8A, 0x8A),
                        Log = DColor.FromArgb(0xF0, 0xF1, 0xF3),
                    });
                    logger.SetFormat(new COP.LogFormat
                    {
                        InfoTag = "[INFO]",
                        SuccessTag = "[OK]",
                        WarningTag = "[WARN]",
                        ErrorTag = "[ERR]",
                        SystemTag = "[SYS]",
                        LogTag = "[LOG]",
                    });
                    logger.OnLog += Logger_OnLog;
                    logger.StartRobloxLogWatcher(1000);
                }

                quorumReady = true;
                Log("Quorum module loaded. " + quorum.BinDirectory, MutedC);
                SetStatus(QuorumModule.IsRobloxOpen() ? "Not attached" : "Roblox closed", MutedC);
            }
            catch (Exception ex)
            {
                Log("Quorum module failed to load: " + ex.Message, ErrorC);
                SetStatus("Module error", ErrorC);
            }
        }

        void Logger_OnLog(string message, DColor color)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => AppendLog(message, color)));
                return;
            }
            AppendLog(message, color);
        }

        void AppendLog(string message, DColor color)
        {
            if (LogsBox == null) return;
            var run = new Run(message + Environment.NewLine)
            {
                Foreground = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B))
            };
            LogsBox.Document.Blocks.Add(new Paragraph(run) { Margin = new Thickness(0) });

            while (LogsBox.Document.Blocks.Count > 800)
                LogsBox.Document.Blocks.Remove(LogsBox.Document.Blocks.FirstBlock);

            LogsBox.ScrollToEnd();
        }

        void Log(string message, Color color) =>
            AppendLog(message, DColor.FromArgb(color.R, color.G, color.B));

        void SetStatus(string text, Color color)
        {
            StatusText.Text = text;
            StatusText.Foreground = new SolidColorBrush(color);
            StatusDot.Fill = new SolidColorBrush(color);
        }

        void ApplyState(QuorumStates state)
        {
            switch (state)
            {
                case QuorumStates.Attached:
                    SetStatus("Attached", PositiveC);
                    Log("Attached.", PositiveC);
                    break;
                case QuorumStates.Attaching:
                    SetStatus("Attaching", WarningC);
                    Log("Attaching...", WarningC);
                    break;
                case QuorumStates.Executed:
                    SetStatus("Attached", PositiveC);
                    Log("Script executed.", PositiveC);
                    break;
                case QuorumStates.NoProcessFound:
                    SetStatus("Roblox closed", WarningC);
                    Log("No Roblox client found.", WarningC);
                    break;
                case QuorumStates.TamperDetected:
                    SetStatus("Tamper detected", ErrorC);
                    Log("Module refused to start: tamper detected.", ErrorC);
                    break;
                case QuorumStates.Error:
                    SetStatus("Error", ErrorC);
                    Log("Operation failed.", ErrorC);
                    break;
                default:
                    SetStatus("Not attached", MutedC);
                    Log("Not attached.", MutedC);
                    break;
            }
        }

        async void BtnInject_Click(object s, RoutedEventArgs e)
        {
            if (!quorumReady || busy) { if (!quorumReady) Log("Quorum module is not loaded.", ErrorC); return; }
            busy = true;
            SetStatus("Attaching", WarningC);
            Log("Attaching to Roblox...", SoftC);
            try
            {
                var state = await Task.Run(() => quorum.AttachAPI(true));
                ApplyState(state);
            }
            catch (Exception ex)
            {
                SetStatus("Error", ErrorC);
                Log("Attach failed: " + ex.Message, ErrorC);
            }
            finally { busy = false; }
        }

        async void BtnExecute_Click(object s, RoutedEventArgs e)
        {
            string script = Editor.Text;
            if (string.IsNullOrWhiteSpace(script)) { Log("Nothing to execute.", WarningC); return; }
            if (!quorumReady) { Log("Quorum module is not loaded.", ErrorC); return; }
            if (busy) { Log("Another operation is still running.", WarningC); return; }

            busy = true;
            Log("Executing script...", SoftC);
            try
            {
                var state = await Task.Run(() => quorum.ExecuteScript(script));
                ApplyState(state);
            }
            catch (Exception ex)
            {
                SetStatus("Error", ErrorC);
                Log("Execute failed: " + ex.Message, ErrorC);
            }
            finally { busy = false; }
        }

        void BtnClearTerminal_Click(object s, RoutedEventArgs e)
        {
            LogsBox.Document.Blocks.Clear();
        }

        void BtnOpenScript_Click(object s, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Lua scripts (*.lua;*.txt)|*.lua;*.txt|All files (*.*)|*.*"
            };
            if (dlg.ShowDialog(this) != true) return;

            string text;
            try { text = File.ReadAllText(dlg.FileName); }
            catch (Exception ex) { Log("Open failed: " + ex.Message, ErrorC); return; }

            if (current == null) AddTab();
            current.Text = text;
            current.Name = Path.GetFileName(dlg.FileName);
            current.Label.Text = current.Name;
            Editor.Text = text;
            UpdateLines();
            ShowPage(Page.Editor);
            Log("Opened " + dlg.FileName, SoftC);
        }

        void BtnSaveScript_Click(object s, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Lua scripts (*.lua)|*.lua|Text files (*.txt)|*.txt|All files (*.*)|*.*",
                FileName = (current?.Name ?? "script") + ".lua"
            };
            if (dlg.ShowDialog(this) != true) return;

            try
            {
                File.WriteAllText(dlg.FileName, Editor.Text);
                if (current != null)
                {
                    current.Name = Path.GetFileName(dlg.FileName);
                    current.Label.Text = current.Name;
                }
                Log("Saved " + dlg.FileName, SoftC);
            }
            catch (Exception ex) { Log("Save failed: " + ex.Message, ErrorC); }
        }

        void BtnMin_Click(object s, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        void BtnMax_Click(object s, RoutedEventArgs e) =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        void BtnClose_Click(object s, RoutedEventArgs e) => Close();

        void BtnNavHome_Click(object s, RoutedEventArgs e) => ShowPage(Page.Home);
        void BtnNavCode_Click(object s, RoutedEventArgs e) => ShowPage(Page.Editor);
        void BtnNavHub_Click(object s, RoutedEventArgs e) => ShowPage(Page.Hub);
        void BtnNavSettings_Click(object s, RoutedEventArgs e) => ShowPage(Page.Settings);

        void ShowPage(Page page, bool animate = true)
        {
            bool changed = page != currentPage;
            currentPage = page;

            HomePage.Visibility = page == Page.Home ? Visibility.Visible : Visibility.Collapsed;
            EditorPage.Visibility = page == Page.Editor ? Visibility.Visible : Visibility.Collapsed;
            ScriptHubPage.Visibility = page == Page.Hub ? Visibility.Visible : Visibility.Collapsed;
            SettingsPage.Visibility = page == Page.Settings ? Visibility.Visible : Visibility.Collapsed;

            SetStroke(NavHome, page == Page.Home ? AccentC : MutedC, animate);
            SetStroke(NavCode, page == Page.Editor ? AccentC : MutedC, animate);
            SetStroke(NavHub, page == Page.Hub ? AccentC : MutedC, animate);
            SetStroke(NavSettings, page == Page.Settings ? AccentC : MutedC, animate);

            SetScale(HomeLineScale, page == Page.Home ? 1 : 0, animate);
            SetScale(CodeLineScale, page == Page.Editor ? 1 : 0, animate);
            SetScale(HubLineScale, page == Page.Hub ? 1 : 0, animate);
            SetScale(SettingsLineScale, page == Page.Settings ? 1 : 0, animate);

            if (animate && changed)
            {
                FrameworkElement el = null;
                if (page == Page.Home) el = HomePage;
                else if (page == Page.Editor) el = EditorPage;
                else if (page == Page.Hub) el = ScriptHubPage;
                else if (page == Page.Settings) el = SettingsPage;
                
                if (el != null) Fade(el);
            }

            if (page == Page.Editor) Dispatcher.BeginInvoke(new Action(() => Editor.Focus()), DispatcherPriority.Input);
        }

        static void Fade(FrameworkElement el)
        {
            var tt = new TranslateTransform(0, 10);
            el.RenderTransform = tt;
            el.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
            tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(280))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }

        static void SetStroke(Icon icon, Color to, bool animate)
        {
            var b = (SolidColorBrush)icon.Stroke;
            if (animate) Anim(b, to, 180);
            else { b.BeginAnimation(SolidColorBrush.ColorProperty, null); b.Color = to; }
        }

        static void SetScale(ScaleTransform t, double to, bool animate)
        {
            if (animate)
                t.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(200))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            else { t.BeginAnimation(ScaleTransform.ScaleXProperty, null); t.ScaleX = to; }
        }

        static void Anim(SolidColorBrush b, Color to, int ms) =>
            b.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(to, TimeSpan.FromMilliseconds(ms)));

        void BtnAddTab_Click(object s, RoutedEventArgs e) => AddTab();

        Doc AddTab()
        {
            counter++;
            var d = new Doc { Name = "Untitled " + counter };
            d.Bg = new SolidColorBrush(TabBgC);
            d.Fg = new SolidColorBrush(SoftC);
            d.Label = new TextBlock { Text = d.Name, FontSize = 11, Foreground = d.Fg };

            var close = new Button
            {
                Style = (Style)FindResource("IconBtn"),
                Width = 18, Height = 18, Margin = new Thickness(10, 0, 0, 0),
                Content = new Icon { Data = (Geometry)FindResource("I.X"), Width = 9, Height = 9, Thickness = 2.2 }
            };
            close.Click += (s, e) => { e.Handled = true; CloseTab(d); };

            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(d.Label);
            sp.Children.Add(close);

            var scale = new ScaleTransform(0.85, 0.85);
            d.Header = new Button
            {
                Style = (Style)FindResource("TabBtn"),
                Background = d.Bg,
                Content = sp,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = scale,
                Opacity = 0
            };
            d.Header.Click += (s, e) => Activate(d);

            docs.Add(d);
            TabPanel.Children.Add(d.Header);

            var ms = TimeSpan.FromMilliseconds(220);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            d.Header.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, ms));
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.85, 1, ms) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.85, 1, ms) { EasingFunction = ease });

            Activate(d);
            Dispatcher.BeginInvoke(new Action(() => d.Header.BringIntoView()), DispatcherPriority.Loaded);
            return d;
        }

        void Activate(Doc d)
        {
            if (current == d) { if (currentPage == Page.Editor) Editor.Focus(); return; }

            if (current != null)
            {
                current.Text = Editor.Text;
                Anim(current.Bg, TabBgC, 160);
                Anim(current.Fg, SoftC, 160);
            }
            current = d;
            Editor.Text = d.Text;
            Editor.CaretIndex = Editor.Text.Length;
            Anim(d.Bg, TabBgActiveC, 160);
            Anim(d.Fg, TextC, 160);
            UpdateLines();

            Editor.BeginAnimation(OpacityProperty, new DoubleAnimation(0.35, 1, TimeSpan.FromMilliseconds(160)));
            if (currentPage == Page.Editor) Editor.Focus();
        }

        void CloseTab(Doc d)
        {
            int i = docs.IndexOf(d);
            if (i < 0) return;
            bool wasCurrent = current == d;
            docs.RemoveAt(i);

            var h = d.Header;
            h.IsHitTestVisible = false;
            h.ClipToBounds = true;
            double w = h.ActualWidth;
            h.Width = w;

            var ms = TimeSpan.FromMilliseconds(170);
            var wa = new DoubleAnimation(w, 0, ms) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            wa.Completed += (s, e) => TabPanel.Children.Remove(h);
            h.BeginAnimation(OpacityProperty, new DoubleAnimation(0, ms));
            h.BeginAnimation(WidthProperty, wa);

            if (wasCurrent)
            {
                current = null;
                if (docs.Count > 0) Activate(docs[Math.Min(i, docs.Count - 1)]);
                else AddTab();
            }
        }

        void TabScroll_Wheel(object s, MouseWheelEventArgs e)
        {
            TabScroll.ScrollToHorizontalOffset(TabScroll.HorizontalOffset - e.Delta);
            e.Handled = true;
        }

        void UpdateLines()
        {
            int n = Editor.Text.Count(c => c == '\n') + 1;
            var sb = new StringBuilder();
            for (int i = 1; i <= n; i++) { if (i > 1) sb.Append('\n'); sb.Append(i); }
            LineNumbers.Text = sb.ToString();
        }

        void BtnClear_Click(object s, RoutedEventArgs e)
        {
            Editor.Clear();
            Editor.Focus();
        }

        async void HubSearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                string query = HubSearchBox.Text?.Trim() ?? "";
                await LoadScriptBloxScriptsAsync(query);
            }
        }

        async Task LoadScriptBloxScriptsAsync(string query)
        {
            int requestId = System.Threading.Interlocked.Increment(ref hubRequestId);
            HubCardsPanel.Children.Clear();
            SetHubStatus("");

            try
            {
                string url = string.IsNullOrWhiteSpace(query)
                    ? "https://scriptblox.com/api/script/fetch?page=1&max=20"
                    : $"https://scriptblox.com/api/script/search?q={Uri.EscapeDataString(query)}&page=1&max=20";

                using (var response = await httpClient.GetAsync(url))
                {
                    response.EnsureSuccessStatusCode();
                    string json = await response.Content.ReadAsStringAsync();
                    using (var doc = JsonDocument.Parse(json))
                    {
                        if (requestId != System.Threading.Volatile.Read(ref hubRequestId)) return;

                        var root = doc.RootElement;
                        if (!root.TryGetProperty("result", out var result)
                            || !result.TryGetProperty("scripts", out var scripts)
                            || scripts.ValueKind != JsonValueKind.Array)
                        {
                            string apiMessage = ReadString(root, "message", "Unexpected response from ScriptBlox.");
                            SetHubStatus(apiMessage);
                            return;
                        }

                        if (requestId != System.Threading.Volatile.Read(ref hubRequestId)) return;

                        int count = 0;
                        foreach (var scriptElem in scripts.EnumerateArray())
                        {
                            if (scriptElem.ValueKind != JsonValueKind.Object) continue;

                            string title = ReadString(scriptElem, "title", "Untitled Script");
                            string scriptCode = ReadString(scriptElem, "script", "");
                            string slug = ReadString(scriptElem, "slug", "");
                            string gameName = "Universal Script";
                            string imageUrl = ReadString(scriptElem, "image", "");

                            if (scriptElem.TryGetProperty("game", out var gameElem) && gameElem.ValueKind == JsonValueKind.Object)
                            {
                                gameName = ReadString(gameElem, "name", gameName);
                                if (string.IsNullOrWhiteSpace(imageUrl))
                                    imageUrl = ReadString(gameElem, "imageUrl", "");
                            }

                            CreateScriptCard(title, gameName, imageUrl, scriptCode, slug);
                            count++;
                        }

                        if (count == 0)
                        {
                            SetHubStatus("No scripts found.");
                        }
                        else
                        {
                            SetHubStatus("");
                        }
                    }
                }
            }
            catch (TaskCanceledException)
            {
                if (requestId == System.Threading.Volatile.Read(ref hubRequestId))
                    SetHubStatus("ScriptBlox request timed out. Press Enter to retry.");
            }
            catch (HttpRequestException ex)
            {
                if (requestId == System.Threading.Volatile.Read(ref hubRequestId))
                {
                    SetHubStatus(ex.StatusCode.HasValue
                        ? $"ScriptBlox returned HTTP {(int)ex.StatusCode.Value}. Press Enter to retry."
                        : "Could not reach ScriptBlox. Check the connection and press Enter to retry.");
                }
            }
            catch (JsonException)
            {
                if (requestId == System.Threading.Volatile.Read(ref hubRequestId))
                    SetHubStatus("ScriptBlox returned data that could not be read.");
            }
            catch
            {
                if (requestId == System.Threading.Volatile.Read(ref hubRequestId))
                    SetHubStatus("Could not display ScriptBlox results. Press Enter to retry.");
            }
        }

        void SetHubStatus(string message)
        {
            HubStatus.Text = message;
            HubStatus.Visibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;
        }

        static string ReadString(JsonElement element, string propertyName, string fallback)
        {
            return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? fallback
                : fallback;
        }

        void CreateScriptCard(string title, string gameInfo, string imageUrl, string scriptCode, string slug)
        {
            var cardBorder = new Border
            {
                Width = 420,
                Height = 240,
                Margin = new Thickness(0, 0, 12, 20),
                Background = (Brush)FindResource("Panel"),
                BorderBrush = (Brush)FindResource("Line"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8)
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(120) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var imgBorder = new Border
            {
                CornerRadius = new CornerRadius(8, 8, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22)),
                ClipToBounds = true
            };

            if (!string.IsNullOrEmpty(imageUrl))
            {
                try
                {
                    var img = new Image
                    {
                        Source = new BitmapImage(new Uri(imageUrl.StartsWith("http") ? imageUrl : "https://scriptblox.com" + imageUrl)),
                        Stretch = Stretch.UniformToFill
                    };
                    imgBorder.Child = img;
                }
                catch
                {
                    imgBorder.Child = new TextBlock { Text = gameInfo, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)) };
                }
            }
            else
            {
                imgBorder.Child = new TextBlock { Text = gameInfo, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)) };
            }
            Grid.SetRow(imgBorder, 0);
            grid.Children.Add(imgBorder);

            var contentGrid = new Grid { Margin = new Thickness(16, 12, 16, 12) };
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var titleBlock = new TextBlock
            {
                Text = title,
                FontSize = 13,
                Style = (Style)FindResource("Bold"),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetRow(titleBlock, 0);
            contentGrid.Children.Add(titleBlock);

            var infoPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            infoPanel.Children.Add(new TextBlock { Text = gameInfo, FontSize = 11, Foreground = (Brush)FindResource("Muted") });
            infoPanel.Children.Add(new TextBlock { Text = "✔", Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0x33, 0x3A)), FontSize = 11, Margin = new Thickness(4, 0, 0, 0) });
            Grid.SetRow(infoPanel, 1);
            contentGrid.Children.Add(infoPanel);

            var btnGrid = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var execBtn = new Button
            {
                Style = (Style)FindResource("ActionBtn"),
                Background = new SolidColorBrush(Color.FromRgb(0x15, 0x15, 0x15)),
                Margin = new Thickness(0),
                Height = 32,
                Content = new TextBlock { Text = "LOAD", Foreground = (Brush)FindResource("Text"), Style = (Style)FindResource("Bold"), FontSize = 11 }
            };
            execBtn.Click += (s, e) =>
            {
                if (current != null)
                {
                    current.Text = scriptCode;
                    Editor.Text = scriptCode;
                }
                else
                {
                    var doc = AddTab();
                    doc.Text = scriptCode;
                    Editor.Text = scriptCode;
                }
                ShowPage(Page.Editor);
            };
            Grid.SetColumn(execBtn, 0);
            btnGrid.Children.Add(execBtn);

            var extBtn = new Button
            {
                Style = (Style)FindResource("ActionBtn"),
                Width = 32,
                Padding = new Thickness(0),
                Margin = new Thickness(0),
                Height = 32,
                Content = new Icon { Data = (Geometry)FindResource("I.External"), Width = 14, Height = 14, Stroke = (Brush)FindResource("Text") }
            };
            extBtn.Click += (s, e) =>
            {
                if (!string.IsNullOrEmpty(slug))
                {
                    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo($"https://scriptblox.com/script/{Uri.EscapeDataString(slug)}") { UseShellExecute = true }); } catch { }
                }
            };
            Grid.SetColumn(extBtn, 2);
            btnGrid.Children.Add(extBtn);

            Grid.SetRow(btnGrid, 2);
            contentGrid.Children.Add(btnGrid);

            Grid.SetRow(contentGrid, 1);
            grid.Children.Add(contentGrid);

            cardBorder.Child = grid;
            HubCardsPanel.Children.Add(cardBorder);
        }

        void BtnAlwaysOnTop_Click(object sender, RoutedEventArgs e)
        {
            alwaysOnTop = !alwaysOnTop;
            Topmost = alwaysOnTop;
            TxtAlwaysOnTop.Text = alwaysOnTop ? "Enabled" : "Disabled";
            TxtAlwaysOnTop.Foreground = alwaysOnTop ? (Brush)FindResource("Positive") : (Brush)FindResource("Muted");
            TxtAlwaysOnTop.Style = alwaysOnTop ? (Style)FindResource("Bold") : null;
        }

        void BtnAutoAttach_Click(object sender, RoutedEventArgs e)
        {
            autoAttach = !autoAttach;
            TxtAutoAttach.Text = autoAttach ? "Enabled" : "Disabled";
            TxtAutoAttach.Foreground = autoAttach ? (Brush)FindResource("Positive") : (Brush)FindResource("Muted");
            TxtAutoAttach.Style = autoAttach ? (Style)FindResource("Bold") : null;

            if (quorumReady)
            {
                try { quorum.SetAutoAttach(autoAttach); }
                catch (Exception ex) { Log("Auto attach failed: " + ex.Message, ErrorC); }
            }
        }

        void BtnShowTerminal_Click(object sender, RoutedEventArgs e)
        {
            showTerminal = !showTerminal;
            TerminalPanel.Visibility = showTerminal ? Visibility.Visible : Visibility.Collapsed;
            TerminalRow.Height = showTerminal ? new GridLength(150) : new GridLength(0);
            
            TxtShowTerminal.Text = showTerminal ? "Enabled" : "Disabled";
            TxtShowTerminal.Foreground = showTerminal ? (Brush)FindResource("Positive") : (Brush)FindResource("Muted");
            TxtShowTerminal.Style = showTerminal ? (Style)FindResource("Bold") : null;
        }

        void SliderFontSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtFontSize == null) return;
            int val = (int)e.NewValue;
            TxtFontSize.Text = val + "px";
            if (Editor != null)
            {
                Editor.FontSize = val;
                LineNumbers.FontSize = val;
            }
        }

        void BtnCpuLimiter_Click(object sender, RoutedEventArgs e)
        {
            cpuLimiter = !cpuLimiter;
            SliderCpu.IsEnabled = cpuLimiter;

            TxtCpuLimiter.Text = cpuLimiter ? "Enabled" : "Disabled";
            TxtCpuLimiter.Foreground = cpuLimiter ? (Brush)FindResource("Positive") : (Brush)FindResource("Muted");
            TxtCpuLimiter.Style = cpuLimiter ? (Style)FindResource("Bold") : null;
        }

        void SliderCpu_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtCpuIntensity == null) return;
            TxtCpuIntensity.Text = (int)e.NewValue + "%";
        }

        void BtnHardwareAccel_Click(object sender, RoutedEventArgs e)
        {
            hardwareAccel = !hardwareAccel;
            TxtHardwareAccel.Text = hardwareAccel ? "Enabled" : "Disabled";
            TxtHardwareAccel.Foreground = hardwareAccel ? (Brush)FindResource("Positive") : (Brush)FindResource("Muted");
            TxtHardwareAccel.Style = hardwareAccel ? (Style)FindResource("Bold") : null;
        }

        void BtnResetSettings_Click(object sender, RoutedEventArgs e)
        {
            if (alwaysOnTop) BtnAlwaysOnTop_Click(null, null);
            if (autoAttach) BtnAutoAttach_Click(null, null);
            if (!showTerminal) BtnShowTerminal_Click(null, null);
            if (cpuLimiter) BtnCpuLimiter_Click(null, null);
            if (!hardwareAccel) BtnHardwareAccel_Click(null, null);
            SliderFontSize.Value = 11;
            SliderCpu.Value = 10;
        }

        protected override void OnClosed(EventArgs e)
        {
            try { if (quorumReady) quorum.Detach(); } catch (Exception) { }
            base.OnClosed(e);
        }
    }
}
