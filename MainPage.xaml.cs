using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Networking;
using System.Text;
using Microsoft.Maui.Platform;
using System;
using System.Globalization;
using System.Windows.Input;
using Newtonsoft.Json;
using static System.Formats.Asn1.AsnWriter;

namespace Kursova
{
    public class NewsItem
    {
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
    }

    public partial class MainPage : ContentPage, INotifyPropertyChanged
    {
        private ObservableCollection<NewsItem> _newsHeadlines = new ObservableCollection<NewsItem>();
        public ObservableCollection<RouteInfo> DisplayedRoutes { get; set; } = new ObservableCollection<RouteInfo>();
        private Dictionary<string, List<RouteInfo>> Routes = new();
        private int _currentNewsIndex = 0;
        private string _currentTime = string.Empty;
        private NewsItem _currentNews = new NewsItem();
        private bool _isOnline = true;

        private bool _isAdminLoggedIn = false;

        public ObservableCollection<NewsItem> NewsHeadlines
        {
            get => _newsHeadlines;
            set
            {
                _newsHeadlines = value;
                OnPropertyChanged(nameof(NewsHeadlines));
            }
        }

        public int NewsCount => _newsHeadlines.Count;

        public NewsItem CurrentNews
        {
            get => _currentNews;
            set
            {
                _currentNews = value;
                OnPropertyChanged(nameof(CurrentNews));
            }
        }

        public int CurrentNewsIndex
        {
            get => _currentNewsIndex;
            set
            {
                if (_currentNewsIndex != value)
                {
                    _currentNewsIndex = value;
                    OnPropertyChanged(nameof(CurrentNewsIndex));
                    UpdateDisplayedNews();
                }
            }
        }

        public string CurrentTime
        {
            get => _currentTime;
            set
            {
                _currentTime = value;
                OnPropertyChanged(nameof(CurrentTime));
            }
        }

        public new event PropertyChangedEventHandler? PropertyChanged;

        public MainPage()
        {
            InitializeComponent();
            BindingContext = this;

            UpdateTime();
            StartTimers();
            CheckConnectivity();
            Microsoft.Maui.Networking.Connectivity.ConnectivityChanged += ConnectivityChanged;

            StartClock();
            InitializeMap();

            CityPicker.ItemsSource = new List<string> { "Всі", "Київ", "Львів", "Херсон" };
            CityPicker.SelectedItem = "Всі";
            LoadRoutesData();
        }

        public bool IsAdminLoggedIn
        {
            get => _isAdminLoggedIn;
            set
            {
                if (_isAdminLoggedIn != value)
                {
                    _isAdminLoggedIn = value;
                    OnPropertyChanged(nameof(IsAdminLoggedIn));
                }
            }
        }

        private void LoadRoutesData()
        {
            string filePath = Path.Combine(AppContext.BaseDirectory, "Data", "routes.json");
            Routes = RouteService.LoadRoutes(filePath);
        }

        private void StartClock()
        {
            Dispatcher.StartTimer(TimeSpan.FromSeconds(1), () =>
            {
                UpdateTime();
                return true; 
            });
        }


        private void UpdateTime()
        {
            CurrentTime = DateTime.Now.ToString("HH:mm");
        }

        private async void CheckConnectivity()
        {
            var current = Microsoft.Maui.Networking.Connectivity.Current.NetworkAccess;
            _isOnline = current == Microsoft.Maui.Networking.NetworkAccess.Internet;

            if (_isOnline)
            {
                await FetchNews();
            }
            else
            {
                LoadOfflineData();
            }
        }

        private async void ConnectivityChanged(object? sender, Microsoft.Maui.Networking.ConnectivityChangedEventArgs e)
        {
            _isOnline = e.NetworkAccess == Microsoft.Maui.Networking.NetworkAccess.Internet;

            if (_isOnline)
            {
                await FetchNews();
            }
            else
            {
                LoadOfflineData();
            }
        }

        private void LoadOfflineData()
        {
            CurrentNews = new NewsItem
            {
                Title = "Офлайн режим",
                Description = "Зараз ви працюєте в офлайн режимі. З'єднання з інтернетом відсутнє.",
                Source = "Локальні дані"
            };
        }

        private void UpdateDisplayedNews()
        {
            if (_newsHeadlines != null && _newsHeadlines.Count > 0 && _currentNewsIndex >= 0 && _currentNewsIndex < _newsHeadlines.Count)
            {
                CurrentNews = _newsHeadlines[_currentNewsIndex];
            }
        }

        private async Task FetchNews()
        {
            if (!_isOnline)
            {
                return;
            }
                try
                {
                    _newsHeadlines.Clear();

                    var pravdaNews = await FetchNewsFromRSS("https://www.pravda.com.ua/rss/view_news/", "Українська Правда") ?? new List<NewsItem>();
                    var bbcNews = await FetchNewsFromRSS("https://feeds.bbci.co.uk/ukrainian/rss.xml", "BBC Україна") ?? new List<NewsItem>();

                    var combinedNews = new List<NewsItem>();

                    int maxNewsCount = Math.Max(pravdaNews.Count, bbcNews.Count);
                    for (int i = 0; i < maxNewsCount; i++)
                    {
                        if (i < pravdaNews.Count)
                        {
                            combinedNews.Add(pravdaNews[i]);
                        }
                        if (i < bbcNews.Count)
                        {
                            combinedNews.Add(bbcNews[i]);
                        }
                    }

                    int totalIndicators = 10;
                    for (int i = 0; i < totalIndicators; i++)
                    {
                        _newsHeadlines.Add(combinedNews[i % combinedNews.Count]);
                    }

                    UpdateDisplayedNews();
                    OnPropertyChanged(nameof(NewsCount));
                }
                catch (Exception ex)
                {
                    CurrentNews = new NewsItem
                    {
                        Title = "Помилка завантаження новин",
                        Description = ex.Message,
                        Source = "Помилка"
                    };
                }

                await Task.Delay(TimeSpan.FromMinutes(30));
        }

        private async Task<List<NewsItem>?> FetchNewsFromRSS(string url, string sourceName)
        {
            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

                using var client = new HttpClient();
                var response = await client.GetAsync(url);
                var contentType = response.Content.Headers.ContentType;

                if (contentType != null && contentType.CharSet != null && !contentType.CharSet.ToLower().Contains("utf-8"))
                {
                    var contentStream = await response.Content.ReadAsStreamAsync();
                    using var reader = new System.IO.StreamReader(contentStream, Encoding.GetEncoding(contentType.CharSet ?? "utf-8"));
                    var content = await reader.ReadToEndAsync();
                    return ParseNews(content, sourceName);
                }
                else
                {
                    var contentStream = await response.Content.ReadAsStreamAsync();
                    using var reader = new System.IO.StreamReader(contentStream, Encoding.UTF8);
                    var content = await reader.ReadToEndAsync();
                    return ParseNews(content, sourceName);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Помилка завантаження новин з {sourceName}: {ex.Message}");
                return new List<NewsItem>();
            }
        }

        private List<NewsItem> ParseNews(string content, string sourceName)
        {
            var newsItems = new List<NewsItem>(); 

            try
            {
                var doc = XDocument.Parse(content);
                newsItems = doc.Descendants("item")
                    .Select(x => new NewsItem
                    {
                        Title = x.Element("title")?.Value ?? "Без заголовку",
                        Description = x.Element("description") is XElement descElement && !string.IsNullOrWhiteSpace(descElement.Value)
                                    ? descElement.Value
                                    : "Опис новини відсутній.",
                        Source = sourceName
                    })
                    .ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Помилка парсингу новин з {sourceName}: {ex.Message}");
            }

            return newsItems;
        }

        private void StartTimers()
        {
            Dispatcher.StartTimer(TimeSpan.FromSeconds(15), () =>
            {
                if (_newsHeadlines.Count > 0)
                {
                    CurrentNewsIndex = (CurrentNewsIndex + 1) % _newsHeadlines.Count;
                }
                return true;
            });

            Dispatcher.StartTimer(TimeSpan.FromMinutes(30), () =>
            {
                Task.Run(async () =>
                {
                    try
                    {
                        await FetchNews();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error fetching news: {ex.Message}");
                    }
                });
                return true;
            });
        }

        protected override void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void OnStartButtonClicked(object sender, EventArgs e)
        {
            InitialInterface.IsVisible = false;
            MapInterface.IsVisible = true;
        }

        private void OnAdminButtonClicked(object sender, EventArgs e)
        {
            AdminLoginPopup.IsVisible = true;
        }

        private void OnAdminLoginClicked(object sender, EventArgs e)
        {
            string login = AdminLoginEntry.Text?.Trim() ?? string.Empty;
            string password = AdminPasswordEntry.Text?.Trim() ?? string.Empty;

            if (login == "developer" && password == "Programmer773")
            {
                IsAdminLoggedIn = true;
                AdminLoginPopup.IsVisible = false;

                string filePath = Path.Combine(AppContext.BaseDirectory, "Data", "routes.json");
                Routes = RouteService.LoadRoutes(filePath);

                ShowAdminInterface();

                DisplayAlert("Успіх", "Вхід виконано успішно!", "OK");
            }
            else
            {
                DisplayAlert("Помилка", "Невірний логін або пароль!", "OK");
            }
        }

        private void ShowAdminInterface()
        {
            InitialInterface.IsVisible = false;
            AdminInterface.IsVisible = true;

            DisplayedRoutes.Clear();

            string filePath = Path.Combine(AppContext.BaseDirectory, "Data", "routes.json");
            Routes = RouteService.LoadRoutes(filePath);

            foreach (var city in Routes.Keys)
            {
                foreach (var route in Routes[city])
                {
                    route.HasMissingData = route.CheckForMissingData();
                    DisplayedRoutes.Add(route);
                }
            }

            RoutesCollectionView.ItemsSource = DisplayedRoutes;
        }

        private void OnAdminCancelClicked(object sender, EventArgs e)
        {
            AdminLoginPopup.IsVisible = false;
            AdminLoginEntry.Text = string.Empty;
            AdminPasswordEntry.Text = string.Empty;
        }

        public class RouteInfo
        {
            public string CityName { get; set; } = string.Empty;
            public string RouteNumber { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public string Details { get; set; } = string.Empty;
            public double RouteLength { get; set; }
            public int StopCount { get; set; }
            public int VehicleCount { get; set; }
            public string Interval { get; set; } = string.Empty;
            public string TransportType { get; set; } = string.Empty;

            public List<(string StopName, double Latitude, double Longitude)> Stops { get; set; } = new();

            public string Fare => CityName switch
            {
                "Київ" => $"{TransportFare.KyivFare} грн",
                "Львів" => $"{TransportFare.LvivFare} грн",
                "Херсон" => $"{TransportFare.KhersonFare} грн",
                _ => "Ціна не вказана"
            };

            [JsonIgnore]
            public bool HasMissingData { get; set; } = false;

            public bool CheckForMissingData()
            {
                bool isGeneralMissing = IsGeneralDataMissing();
                bool isStopsMissing = AreStopsDataMissing();

                return isGeneralMissing || isStopsMissing;
            }

            private bool IsGeneralDataMissing()
            {
                return string.IsNullOrWhiteSpace(CityName) ||
                       string.IsNullOrWhiteSpace(RouteNumber) ||
                       string.IsNullOrWhiteSpace(Description) ||
                       string.IsNullOrWhiteSpace(Details) ||
                       RouteLength <= 0 ||
                       StopCount <= 0 ||
                       VehicleCount <= 0 ||
                       string.IsNullOrWhiteSpace(Interval) ||
                       string.IsNullOrWhiteSpace(TransportType);
            }

            private bool AreStopsDataMissing()
            {
                return Stops.Any(stop =>
                    string.IsNullOrWhiteSpace(stop.StopName) ||
                    stop.Latitude == 0.0 ||
                    stop.Longitude == 0.0);
            }

            public List<(string StopName, double Latitude, double Longitude)> GetStopsWithMissingData()
            {
                return Stops
                    .Where(stop => string.IsNullOrWhiteSpace(stop.StopName) ||
                                   stop.Latitude == 0.0 ||
                                   stop.Longitude == 0.0)
                    .ToList();
            }
        }

        public static class RouteService
        {
            public static Dictionary<string, List<RouteInfo>> LoadRoutes(string filePath)
            {
                if (!File.Exists(filePath))
                {
                    File.WriteAllText(filePath, "{}");
                }

                var json = File.ReadAllText(filePath);
                try
                {
                    var routes = JsonConvert.DeserializeObject<Dictionary<string, List<RouteInfo>>>(json)
                                 ?? new Dictionary<string, List<RouteInfo>>();

                    foreach (var city in routes.Keys)
                    {
                        foreach (var route in routes[city])
                        {
                            if (string.IsNullOrWhiteSpace(route.CityName))
                            {
                                route.CityName = city;
                            }

                            if (route.Stops == null)
                            {
                                route.Stops = new List<(string StopName, double Latitude, double Longitude)>();
                            }

                            while (route.Stops.Count < route.StopCount)
                            {
                                route.Stops.Add((string.Empty, 0.0, 0.0));
                            }
                        }
                    }

                    return routes;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Помилка при завантаженні маршрутів: {ex.Message}");
                    return new Dictionary<string, List<RouteInfo>>();
                }
            }

            public static void SaveRoutes(Dictionary<string, List<RouteInfo>> routes, string filePath)
            {
                try
                {
                    string newFilePath = Path.Combine(AppContext.BaseDirectory, "Data", "routes.json");
                    var json = JsonConvert.SerializeObject(routes, Formatting.Indented);

                    File.WriteAllText(newFilePath, json);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Помилка при збереженні маршрутів: {ex.Message}");
                }
            }
        }

        private void OnRouteSelected(object sender, SelectionChangedEventArgs e)
        {
            if (e.CurrentSelection.FirstOrDefault() is RouteInfo selectedRoute)
            {
                DisplayAlert("Маршрут вибрано", $"Вибраний маршрут: {selectedRoute.Description}", "OK");
            }
            else
            {
                DisplayAlert("Помилка", "Будь ласка, виберіть маршрут.", "OK");
            }
        }

        private void OnEditRouteClicked(object sender, EventArgs e)
        {
            if (RoutesCollectionView.SelectedItem is not RouteInfo selectedRoute)
            {
                DisplayAlert("Помилка", "Будь ласка, оберіть маршрут для редагування.", "OK");
                return;
            }

            CityNameEntry.Text = selectedRoute.CityName;
            RouteNumberEntry.Text = selectedRoute.RouteNumber;
            DescriptionEntry.Text = selectedRoute.Description;
            DetailsEntry.Text = selectedRoute.Details;
            RouteLengthEntry.Text = selectedRoute.RouteLength.ToString();
            StopCountEntry.Text = selectedRoute.StopCount.ToString();
            VehicleCountEntry.Text = selectedRoute.VehicleCount.ToString();
            IntervalEntry.Text = selectedRoute.Interval;
            TransportTypeEntry.Text = selectedRoute.TransportType;

            ValidateRouteFields(selectedRoute);

            EnsureStopCountConsistency(selectedRoute);

            StopPicker.ItemsSource = Enumerable.Range(1, selectedRoute.Stops.Count)
                                               .Select(i => $"Зупинка {i}")
                                               .ToList();
            StopPicker.SelectedIndex = 0;

            OnStopPickerSelectionChanged(StopPicker, EventArgs.Empty);
        }

        private void EnsureStopCountConsistency(RouteInfo route)
        {
            if (route.Stops == null)
            {
                route.Stops = new List<(string StopName, double Latitude, double Longitude)>();
            }

            while (route.Stops.Count < route.StopCount)
            {
                route.Stops.Add((string.Empty, 0.0, 0.0));
            }
        }

        private void OnStopPickerSelectionChanged(object sender, EventArgs e)
        {
            if (RoutesCollectionView.SelectedItem is not RouteInfo selectedRoute)
                return;

            int stopIndex = StopPicker.SelectedIndex;

            if (stopIndex >= 0 && stopIndex >= selectedRoute.Stops.Count)
            {
                selectedRoute.Stops.Add(("Нова зупинка", 0.0, 0.0));
            }

            if (stopIndex >= 0 && stopIndex < selectedRoute.Stops.Count)
            {
                var selectedStop = selectedRoute.Stops[stopIndex];

                StopNameEntry.Text = selectedStop.StopName ?? string.Empty;
                LatitudeEntry.Text = selectedStop.Latitude != 0.0 ? selectedStop.Latitude.ToString() : string.Empty;
                LongitudeEntry.Text = selectedStop.Longitude != 0.0 ? selectedStop.Longitude.ToString() : string.Empty;

                ToggleStopFields(true);

                bool isStopIncomplete = string.IsNullOrWhiteSpace(selectedStop.StopName) ||
                                        selectedStop.Latitude == 0.0 ||
                                        selectedStop.Longitude == 0.0;

                SetBorderColor(StopPickerFrame, isStopIncomplete);
                SetBorderColor(StopNameFrame, string.IsNullOrWhiteSpace(selectedStop.StopName));
                SetBorderColor(LatitudeFrame, selectedStop.Latitude == 0.0);
                SetBorderColor(LongitudeFrame, selectedStop.Longitude == 0.0);
            }
            else
            {
                ToggleStopFields(false);

                StopNameEntry.Text = string.Empty;
                LatitudeEntry.Text = string.Empty;
                LongitudeEntry.Text = string.Empty;

                SetBorderColor(StopPickerFrame, true);
                SetBorderColor(StopNameFrame, true);
                SetBorderColor(LatitudeFrame, true);
                SetBorderColor(LongitudeFrame, true);
            }
        }

        private void ValidateRouteFields(RouteInfo route)
        {
            SetBorderColor(CityNameFrame, string.IsNullOrWhiteSpace(route.CityName));
            SetBorderColor(RouteNumberFrame, string.IsNullOrWhiteSpace(route.RouteNumber));
            SetBorderColor(DescriptionFrame, string.IsNullOrWhiteSpace(route.Description));
            SetBorderColor(DetailsFrame, string.IsNullOrWhiteSpace(route.Details));
            SetBorderColor(RouteLengthFrame, route.RouteLength <= 0);
            SetBorderColor(StopCountFrame, route.StopCount <= 0);
            SetBorderColor(VehicleCountFrame, route.VehicleCount <= 0);
            SetBorderColor(IntervalFrame, string.IsNullOrWhiteSpace(route.Interval));
            SetBorderColor(TransportTypeFrame, string.IsNullOrWhiteSpace(route.TransportType));

            if (StopPicker.SelectedIndex >= 0 && StopPicker.SelectedIndex < route.Stops.Count)
            {
                var selectedStop = route.Stops[StopPicker.SelectedIndex];
                bool isIncompleteStop = string.IsNullOrWhiteSpace(selectedStop.StopName) ||
                                        selectedStop.Latitude == 0.0 ||
                                        selectedStop.Longitude == 0.0;

                SetBorderColor(StopPickerFrame, isIncompleteStop);
                ValidateStopFields(selectedStop);
            }
            else
            {
                SetBorderColor(StopPickerFrame, true);
            }
        }

        private void ValidateStopFields((string StopName, double Latitude, double Longitude) stop)
        {
            SetBorderColor(StopNameFrame, string.IsNullOrWhiteSpace(stop.StopName));
            SetBorderColor(LatitudeFrame, stop.Latitude == 0.0);
            SetBorderColor(LongitudeFrame, stop.Longitude == 0.0);
        }

        private void ToggleStopFields(bool isEnabled)
        {
            StopNameEntry.IsEnabled = isEnabled;
            LatitudeEntry.IsEnabled = isEnabled;
            LongitudeEntry.IsEnabled = isEnabled;
            SaveCoordinatesButton.IsEnabled = isEnabled;
        }

        private void SetBorderColor(Frame frame, bool isMissing)
        {
            frame.BackgroundColor = isMissing ? Colors.LightCoral : Colors.Transparent;
        }

        private void OnDeleteRouteClicked(object sender, EventArgs e)
        {
            if (RoutesCollectionView.SelectedItem is not RouteInfo selectedRoute)
            {
                DisplayAlert("Помилка", "Будь ласка, оберіть маршрут для видалення.", "OK");
                return;
            }

            string cityName = selectedRoute.CityName;

            if (Routes.ContainsKey(cityName))
            {
                var routesInCity = Routes[cityName];
                routesInCity.Remove(selectedRoute);

                if (!routesInCity.Any())
                {
                    Routes.Remove(cityName);
                }

                string filePath = Path.Combine(AppContext.BaseDirectory, "Data", "routes.json");
                RouteService.SaveRoutes(Routes, filePath);

                OnCityFilterChanged(this, EventArgs.Empty);

                DisplayAlert("Успіх", $"Маршрут {selectedRoute.Description} успішно видалено.", "OK");
            }
            else
            {
                DisplayAlert("Помилка", "Маршрут не знайдено у файлі.", "OK");
            }
            ClearInputFields();
        }

        private void OnExitAdminModeClicked(object sender, EventArgs e)
        {
            DisplayAlert(
            "Увага",
            "Якщо ви вносили якісь зміни, треба перезапустити застосунок.",
            "OK"
            );
            ClearAllFields();
            ClearStopFieldWarnings();
            ValidateFieldsForCoordinates();
            AdminInterface.IsVisible = false;
            InitialInterface.IsVisible = true;
            IsAdminLoggedIn = false;

            AdminLoginEntry.Text = string.Empty;
            AdminPasswordEntry.Text = string.Empty;
        }

        private bool isValidationEnabled = true;

        private void ClearAllFields()
        {
            bool previousValidationState = isValidationEnabled;
            isValidationEnabled = false;

            CityNameEntry.Text = string.Empty;
            RouteNumberEntry.Text = string.Empty;
            DescriptionEntry.Text = string.Empty;
            DetailsEntry.Text = string.Empty;
            RouteLengthEntry.Text = string.Empty;
            StopCountEntry.Text = string.Empty;
            VehicleCountEntry.Text = string.Empty;
            IntervalEntry.Text = string.Empty;
            TransportTypeEntry.Text = string.Empty;

            StopPicker.SelectedIndex = -1;
            StopPicker.ItemsSource = null;
            StopNameEntry.Text = string.Empty;
            LatitudeEntry.Text = string.Empty;
            LongitudeEntry.Text = string.Empty;

            ResetFieldBorders();

            StopPicker.IsEnabled = false;
            StopNameEntry.IsEnabled = false;
            LatitudeEntry.IsEnabled = false;
            LongitudeEntry.IsEnabled = false;
            SaveCoordinatesButton.IsEnabled = false;

            isValidationEnabled = previousValidationState;
        }

        private void ClearStopFieldWarnings()
        {
            StopNameEntry.BackgroundColor = Colors.Transparent;
            LatitudeEntry.BackgroundColor = Colors.Transparent;
            LongitudeEntry.BackgroundColor = Colors.Transparent;
            StopPicker.BackgroundColor = Colors.Transparent;
        }


        private void OnCityFilterChanged(object? sender, EventArgs e)
        {
            if (CityPicker.SelectedItem == null)
            {
                CityPicker.SelectedItem = "Всі";
            }

            if (CityPicker.SelectedItem is string selectedCity && !string.IsNullOrWhiteSpace(selectedCity))
            {
                CurrentSelectedCity = selectedCity;

                DisplayedRoutes.Clear();

                if (selectedCity == "Всі")
                {
                    foreach (var city in Routes.Keys)
                    {
                        foreach (var route in Routes[city])
                        {
                            DisplayedRoutes.Add(route);
                        }
                    }
                }
                else
                {
                    if (Routes.ContainsKey(selectedCity))
                    {
                        foreach (var route in Routes[selectedCity])
                        {
                            DisplayedRoutes.Add(route);
                        }
                    }
                }
            }
            else
            {
                CurrentSelectedCity = "Всі";
            }
        }

        private readonly List<string> ValidCities = new List<string> { "Київ", "Львів", "Херсон" };
        private readonly List<string> ValidTransportTypes = new List<string> { "автобус", "трамвай", "тролейбус" };

        private void OnAddOrUpdateRouteClicked(object sender, EventArgs e)
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(CityNameEntry.Text) ||
                string.IsNullOrWhiteSpace(RouteNumberEntry.Text) ||
                string.IsNullOrWhiteSpace(DescriptionEntry.Text) ||
                string.IsNullOrWhiteSpace(DetailsEntry.Text) ||
                string.IsNullOrWhiteSpace(RouteLengthEntry.Text) ||
                string.IsNullOrWhiteSpace(StopCountEntry.Text) ||
                string.IsNullOrWhiteSpace(VehicleCountEntry.Text) ||
                string.IsNullOrWhiteSpace(IntervalEntry.Text) ||
                string.IsNullOrWhiteSpace(TransportTypeEntry.Text))
            {
                DisplayAlert("Помилка", "Будь ласка, заповніть усі поля маршруту.", "OK");
                return;
            }

            var cityName = CityNameEntry.Text.Trim();
            var transportType = TransportTypeEntry.Text.Trim().ToLower();

            if (!ValidCities.Contains(cityName))
                errors.Add($"Місто '{cityName}' немає в базі.");
            if (!ValidTransportTypes.Contains(transportType))
                errors.Add($"Тип транспорту '{transportType}' не підтримується. Доступні варіанти: Автобус, Трамвай, Тролейбус.");

            if (!double.TryParse(RouteLengthEntry.Text.Trim(), out double routeLength))
                errors.Add("Довжина маршруту має бути числовим значенням.");
            if (!int.TryParse(StopCountEntry.Text.Trim(), out int stopCount))
                errors.Add("Кількість зупинок має бути цілим числом.");
            if (!int.TryParse(VehicleCountEntry.Text.Trim(), out int vehicleCount))
                errors.Add("Кількість транспорту має бути цілим числом.");
            if (!int.TryParse(RouteNumberEntry.Text.Trim(), out int routeNumber))
                errors.Add("Номер маршруту має бути цілим числом.");

            if (errors.Any())
            {
                DisplayAlert("Помилка", string.Join("\n", errors), "OK");
                return;
            }

            bool isEditMode = Routes.ContainsKey(cityName) &&
                              Routes[cityName].Any(r => r.RouteNumber == routeNumber.ToString());

            var stops = new List<(string StopName, double Latitude, double Longitude)>();

            if (isEditMode)
            {
                var existingRoute = Routes[cityName].FirstOrDefault(r => r.RouteNumber == routeNumber.ToString());
                if (existingRoute != null)
                {
                    existingRoute.Description = DescriptionEntry.Text.Trim();
                    existingRoute.Details = DetailsEntry.Text.Trim();
                    existingRoute.RouteLength = routeLength;
                    existingRoute.StopCount = stopCount;
                    existingRoute.VehicleCount = vehicleCount;
                    existingRoute.Interval = IntervalEntry.Text.Trim();
                    existingRoute.TransportType = transportType;

                    while (existingRoute.Stops.Count < stopCount)
                    {
                        existingRoute.Stops.Add(("Нова зупинка", 0, 0));
                    }
                    while (existingRoute.Stops.Count > stopCount)
                    {
                        existingRoute.Stops.RemoveAt(existingRoute.Stops.Count - 1);
                    }

                    stops = existingRoute.Stops;

                    foreach (var stop in existingRoute.Stops)
                    {
                        if (string.IsNullOrWhiteSpace(stop.StopName) || stop.Latitude == 0 || stop.Longitude == 0)
                        {
                            errors.Add($"Зупинка '{stop.StopName}' має некоректні або неповні дані.");
                        }
                    }

                    if (errors.Any())
                    {
                        DisplayAlert("Помилка", string.Join("\n", errors), "OK");
                        return;
                    }
                }
            }
            else
            {
                if (Routes.ContainsKey(cityName))
                {
                    var duplicateRoute = Routes[cityName]
                        .FirstOrDefault(r => r.RouteNumber == routeNumber.ToString() &&
                                             r.TransportType.Equals(transportType, StringComparison.OrdinalIgnoreCase) &&
                                             r.CityName.Equals(cityName, StringComparison.OrdinalIgnoreCase));

                    if (duplicateRoute != null)
                    {
                        DisplayAlert("Помилка", "Такий маршрут вже існує. Ви можете відредагувати його за необхідності.", "OK");
                        return;
                    }
                }

                for (int i = 0; i < stopCount; i++)
                {
                    stops.Add(($"Зупинка {i + 1}", 0, 0));
                }

                var newRoute = new RouteInfo
                {
                    CityName = cityName,
                    RouteNumber = routeNumber.ToString(),
                    Description = DescriptionEntry.Text.Trim(),
                    Details = DetailsEntry.Text.Trim(),
                    RouteLength = routeLength,
                    StopCount = stopCount,
                    VehicleCount = vehicleCount,
                    Interval = IntervalEntry.Text.Trim(),
                    TransportType = transportType,
                    Stops = stops
                };

                if (!Routes.ContainsKey(cityName))
                {
                    Routes[cityName] = new List<RouteInfo>();
                }

                Routes[cityName].Add(newRoute);
            }

            string filePath = Path.Combine(AppContext.BaseDirectory, "Data", "routes.json");
            RouteService.SaveRoutes(Routes, filePath);

            OnCityFilterUpdatedWithValidation("Всі");

            StopPicker.ItemsSource = Enumerable.Range(1, stopCount).Select(i => $"Зупинка {i}").ToList();
            StopPicker.SelectedIndex = stopCount > 0 ? stopCount - 1 : 0;

            StopNameEntry.IsEnabled = true;
            LatitudeEntry.IsEnabled = true;
            LongitudeEntry.IsEnabled = true;
            SaveCoordinatesButton.IsEnabled = true;

            DisplayAlert("Успіх", $"Маршрут для міста '{cityName}' успішно додано або оновлено.", "OK");
            ClearInputFields();
            ValidateFieldsForCoordinates();
        }

        private void ClearInputFields()
        {
            if (CityNameEntry != null) CityNameEntry.Text = string.Empty;
            if (RouteNumberEntry != null) RouteNumberEntry.Text = string.Empty;
            if (DescriptionEntry != null) DescriptionEntry.Text = string.Empty;
            if (DetailsEntry != null) DetailsEntry.Text = string.Empty;
            if (RouteLengthEntry != null) RouteLengthEntry.Text = string.Empty;
            if (StopCountEntry != null) StopCountEntry.Text = string.Empty;
            if (VehicleCountEntry != null) VehicleCountEntry.Text = string.Empty;
            if (IntervalEntry != null) IntervalEntry.Text = string.Empty;
            if (TransportTypeEntry != null) TransportTypeEntry.Text = string.Empty;

            if (RoutesCollectionView != null) RoutesCollectionView.SelectedItem = null;
            if (StopPicker != null) StopPicker.SelectedIndex = -1;
            if (LatitudeEntry != null) LatitudeEntry.Text = string.Empty;
            if (LongitudeEntry != null) LongitudeEntry.Text = string.Empty;

            ResetFieldBorders();
        }

        private void ResetFieldBorders()
        {
            SetBorderColor(CityNameFrame, false);
            SetBorderColor(RouteNumberFrame, false);
            SetBorderColor(DescriptionFrame, false);
            SetBorderColor(DetailsFrame, false);
            SetBorderColor(RouteLengthFrame, false);
            SetBorderColor(StopCountFrame, false);
            SetBorderColor(VehicleCountFrame, false);
            SetBorderColor(IntervalFrame, false);
            SetBorderColor(TransportTypeFrame, false);

            SetBorderColor(StopPickerFrame, false);
            SetBorderColor(StopNameFrame, false);
            SetBorderColor(LatitudeFrame, false);
            SetBorderColor(LongitudeFrame, false);
        }

        private void OnCityFilterUpdatedWithValidation(string selectedCity)
        {
            DisplayedRoutes.Clear();

            if (selectedCity == "Всі")
            {
                foreach (var city in Routes.Keys)
                {
                    foreach (var route in Routes[city])
                    {
                        route.HasMissingData = route.CheckForMissingData();
                        DisplayedRoutes.Add(route);
                    }
                }
            }
            else
            {
                if (Routes.ContainsKey(selectedCity))
                {
                    foreach (var route in Routes[selectedCity])
                    {
                        route.HasMissingData = route.CheckForMissingData();
                        DisplayedRoutes.Add(route);
                    }
                }
            }
        }


        private void OnCityFilterUpdated(string selectedCity)
        {
            DisplayedRoutes.Clear();

            if (selectedCity == "Всі")
            {
                foreach (var city in Routes.Keys)
                {
                    foreach (var route in Routes[city])
                    {
                        DisplayedRoutes.Add(route);
                    }
                }
            }
            else
            {
                if (Routes.ContainsKey(selectedCity))
                {
                    foreach (var route in Routes[selectedCity])
                    {
                        DisplayedRoutes.Add(route);
                    }
                }
            }
        }

        private void ValidateFieldsForCoordinates()
        {
            if (!isValidationEnabled) return;

            bool isFormValid = !string.IsNullOrWhiteSpace(CityNameEntry.Text) &&
                               !string.IsNullOrWhiteSpace(RouteNumberEntry.Text) &&
                               !string.IsNullOrWhiteSpace(DescriptionEntry.Text) &&
                               !string.IsNullOrWhiteSpace(DetailsEntry.Text) &&
                               !string.IsNullOrWhiteSpace(RouteLengthEntry.Text) &&
                               !string.IsNullOrWhiteSpace(StopCountEntry.Text) &&
                               !string.IsNullOrWhiteSpace(VehicleCountEntry.Text) &&
                               !string.IsNullOrWhiteSpace(IntervalEntry.Text) &&
                               !string.IsNullOrWhiteSpace(TransportTypeEntry.Text);

            StopPicker.IsEnabled = isFormValid;
            StopNameEntry.IsEnabled = isFormValid;
            LatitudeEntry.IsEnabled = isFormValid;
            LongitudeEntry.IsEnabled = isFormValid;
            SaveCoordinatesButton.IsEnabled = isFormValid;

            if (isFormValid)
            {
                if (int.TryParse(StopCountEntry.Text.Trim(), out int stopCount) && stopCount > 0)
                {
                    StopPicker.ItemsSource = Enumerable.Range(1, stopCount)
                                                       .Select(i => $"Зупинка {i}")
                                                       .ToList();

                    if (StopPicker.SelectedIndex >= stopCount)
                        StopPicker.SelectedIndex = stopCount - 1;
                    else if (StopPicker.SelectedIndex == -1 && stopCount > 0)
                        StopPicker.SelectedIndex = 0;
                }
                else
                {
                    StopPicker.ItemsSource = null;
                    StopPicker.SelectedIndex = -1;
                }
            }
            else
            {
                StopPicker.ItemsSource = null;
                StopPicker.SelectedIndex = -1;
            }

            if (!isFormValid)
            {
                StopPicker.BackgroundColor = Colors.Transparent;
                StopNameEntry.BackgroundColor = Colors.Transparent;
                LatitudeEntry.BackgroundColor = Colors.Transparent;
                LongitudeEntry.BackgroundColor = Colors.Transparent;
            }
        }


        private void OnFieldTextChanged(object sender, TextChangedEventArgs e)
        {
            ValidateFieldsForCoordinates();
        }

        private void OnResetChangesClicked(object sender, EventArgs e)
        {
            ClearInputFields();

            OnCityFilterChanged(this, EventArgs.Empty);

            if (StopPicker != null)
            {
                StopPicker.SelectedIndex = -1;
                StopPicker.ItemsSource = null;
            }

            if (LatitudeEntry != null) LatitudeEntry.Text = string.Empty;
            if (LongitudeEntry != null) LongitudeEntry.Text = string.Empty;

            DisplayAlert("Скинуто", "Всі зміни скасовані. Поля та вибори очищено.", "OK");
        }

        private void OnSaveCoordinatesClicked(object sender, EventArgs e)
        {
            if (RoutesCollectionView.SelectedItem is not RouteInfo selectedRoute)
            {
                DisplayAlert("Помилка", "Спочатку виберіть маршрут.", "OK");
                HighlightEmptyStopFields();
                return;
            }

            if (StopPicker.SelectedItem is null)
            {
                DisplayAlert("Помилка", "Будь ласка, оберіть зупинку.", "OK");
                HighlightEmptyStopFields();
                return;
            }

            if (!double.TryParse(LatitudeEntry.Text.Trim(), out double latitude) ||
                !double.TryParse(LongitudeEntry.Text.Trim(), out double longitude))
            {
                DisplayAlert("Помилка", "Будь ласка, введіть коректні координати (широта та довгота).", "OK");
                HighlightEmptyStopFields();
                return;
            }

            if (string.IsNullOrWhiteSpace(StopNameEntry.Text))
            {
                DisplayAlert("Помилка", "Будь ласка, введіть назву зупинки.", "OK");
                HighlightEmptyStopFields();
                return;
            }

            int stopIndex = StopPicker.SelectedIndex;

            if (stopIndex < 0 || stopIndex >= selectedRoute.Stops.Count)
            {
                DisplayAlert("Помилка", "Індекс зупинки поза межами. Будь ласка, оберіть коректну зупинку.", "OK");
                HighlightEmptyStopFields();
                return;
            }

            var updatedStop = (StopName: StopNameEntry.Text.Trim(), Latitude: latitude, Longitude: longitude);
            selectedRoute.Stops[stopIndex] = updatedStop;

            string cityName = selectedRoute.CityName;
            string routeNumber = selectedRoute.RouteNumber;

            if (Routes.ContainsKey(cityName))
            {
                var routeToUpdate = Routes[cityName].FirstOrDefault(r => r.RouteNumber == routeNumber);
                if (routeToUpdate != null)
                {
                    routeToUpdate.Stops[stopIndex] = updatedStop;

                    string filePath = Path.Combine(AppContext.BaseDirectory, "Data", "routes.json");
                    RouteService.SaveRoutes(Routes, filePath);

                    ClearStopFieldWarnings();

                    DisplayAlert("Успіх", $"Координати та назва для '{updatedStop.StopName}' оновлено.", "OK");
                }
            }

            StopPicker.ItemsSource = Enumerable.Range(1, selectedRoute.Stops.Count)
                                               .Select(i => $"Зупинка {i}")
                                               .ToList();
            StopPicker.SelectedIndex = Math.Min(stopIndex, selectedRoute.Stops.Count - 1);
        }

        private void HighlightEmptyStopFields()
        {
            StopNameEntry.BackgroundColor = string.IsNullOrWhiteSpace(StopNameEntry.Text)
                ? Colors.Red
                : Colors.Transparent;

            LatitudeEntry.BackgroundColor = string.IsNullOrWhiteSpace(LatitudeEntry.Text)
                ? Colors.Red
                : Colors.Transparent;

            LongitudeEntry.BackgroundColor = string.IsNullOrWhiteSpace(LongitudeEntry.Text)
                ? Colors.Red
                : Colors.Transparent;

            StopPicker.BackgroundColor = StopPicker.SelectedItem == null
                ? Colors.Red
                : Colors.Transparent;
        }

        public static class TransportFare
        {
            public const decimal KyivFare = 8.00m;      // Ціна проїзду в Києві
            public const decimal LvivFare = 15.00m;     // Ціна проїзду у Львові
            public const decimal KhersonFare = 6.00m;   // Ціна проїзду в Херсоні
        }

        private void InitializeMap()
        {
            if (MapWebView != null)
            {
                Console.WriteLine("Ініціалізація MapWebView...");

                try
                {
                    var htmlSource = new HtmlWebViewSource
                    {
                        Html = File.ReadAllText("Resources/Raw/map.html")
                    };
                    MapWebView.Source = htmlSource;

                    Console.WriteLine("MapWebView успішно ініціалізовано.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Помилка ініціалізації MapWebView: {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine("MapWebView дорівнює null. Ініціалізація пропущена.");
            }
        }

        private Dictionary<string, (double Latitude, double Longitude)> CityCoordinates = new()
        {
            { "Київ", (50.4501, 30.5234) },
            { "Львів", (49.8397, 24.0297) },
            { "Херсон", (46.6356, 32.6164) }
        };

        private ObservableCollection<string> _allCities = new ObservableCollection<string>
        {
            "Київ", "Львів", "Херсон", "Одеса", "Дніпро", "Харків", "Запоріжжя", "Вінниця", "Миколаїв"
        };

        public ObservableCollection<string> FilteredCities { get; set; } = new ObservableCollection<string>();

        private bool _isCitySelected = false;

        public bool IsCitySelected
        {
            get => _isCitySelected;
            set
            {
                _isCitySelected = value;
                OnPropertyChanged(nameof(IsCitySelected));
            }
        }

        private void OnCityEntryTextChanged(object sender, TextChangedEventArgs e)
        {
            string searchText = e.NewTextValue?.Trim().ToLower() ?? string.Empty;
            Console.WriteLine($"City search text changed: {searchText}");

            FilteredCities.Clear();

            var filtered = _allCities
                .Where(c => c.ToLower().StartsWith(searchText))
                .ToList();

            foreach (var city in filtered)
            {
                FilteredCities.Add(city);
            }

            Console.WriteLine($"Filtered cities count: {FilteredCities.Count}");

            const int itemHeight = 50;
            const int maxHeight = 300;
            int calculatedHeight = FilteredCities.Count > 0
                ? Math.Min(FilteredCities.Count * itemHeight, maxHeight)
                : 0;

            AbsoluteLayout.SetLayoutBounds(CitySuggestionsParent, new Rect(0.5, 155, 300, calculatedHeight));
            CitySuggestions.IsVisible = FilteredCities.Count > 0;

            bool isCityValid = !string.IsNullOrEmpty(e.NewTextValue) && _allCities.Contains(e.NewTextValue);

            bool isCitySelectedFromDropdown = CitySuggestions.SelectedItem != null;

            IsCitySelected = isCityValid && isCitySelectedFromDropdown;

            StartPointEntry.IsEnabled = IsCitySelected;
            DestinationPointEntry.IsEnabled = IsCitySelected;

            if (IsCitySelected)
            {
                var filteredRoutes = Routes
                    .Where(city => city.Key.Equals(e.NewTextValue, StringComparison.OrdinalIgnoreCase))
                    .SelectMany(city => city.Value)
                    .ToList();

                RoutesListView.ItemsSource = filteredRoutes;
                Console.WriteLine($"Filtered routes count: {filteredRoutes.Count}");
            }
            else
            {
                RoutesListView.ItemsSource = null;
            }
        }

        private string? CurrentSelectedCity = "Київ";

        private async void OnCitySelected(object sender, SelectionChangedEventArgs e)
        {
            if (e.CurrentSelection.FirstOrDefault() is string selectedCity && !string.IsNullOrWhiteSpace(selectedCity))
            {
                Console.WriteLine($"[OnCitySelected] Selected city: {selectedCity}");

                CityEntry.Text = selectedCity;
                CitySuggestions.SelectedItem = null;
                FilteredCities.Clear();
                IsCitySelected = true;

                try
                {
                    await ClearFieldsAndMarkers();

                    if (CityCoordinates.TryGetValue(selectedCity, out var coordinates))
                    {
                        await MapWebView.EvaluateJavaScriptAsync("clearMarker('startPoint'); clearMarker('endPoint');");

                        string script = $"setMapCenter({coordinates.Latitude.ToString(CultureInfo.InvariantCulture)}, {coordinates.Longitude.ToString(CultureInfo.InvariantCulture)}, 12)";
                        Console.WriteLine($"[OnCitySelected] Executing JavaScript: {script}");

                        await MapWebView.EvaluateJavaScriptAsync("clearMarkersAndRoutes()");
                        await MapWebView.EvaluateJavaScriptAsync(script);
                    }
                    else
                    {
                        Console.WriteLine($"[OnCitySelected] City '{selectedCity}' not found in coordinates.");
                        await DisplayAlert("Помилка", "Цього міста немає у базі даних.", "OK");
                        IsCitySelected = false;
                        StartPointEntry.IsEnabled = false;
                        DestinationPointEntry.IsEnabled = false;
                        return;
                    }

                    string json = $"{{\"city\": \"{selectedCity}\"}}";
                    await MapWebView.EvaluateJavaScriptAsync($"receiveDataFromCSharp('{json}')");
                    Console.WriteLine($"[OnCitySelected] Data sent to JS: {json}");

                    UpdateDisplayedRoutes(selectedCity);

                    StartPointEntry.IsEnabled = true;
                    DestinationPointEntry.IsEnabled = true;

                    Console.WriteLine($"[OnCitySelected] Routes updated for city: {selectedCity}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[OnCitySelected] Error: {ex.Message}");
                    await DisplayAlert("Помилка", "Сталася помилка при обробці вибору міста.", "OK");

                    IsCitySelected = false;
                    StartPointEntry.IsEnabled = false;
                    DestinationPointEntry.IsEnabled = false;
                }
            }
            else
            {
                Console.WriteLine("[OnCitySelected] No city selected.");
                IsCitySelected = false;

                StartPointEntry.IsEnabled = false;
                DestinationPointEntry.IsEnabled = false;

                RoutesListView.ItemsSource = null;
            }
        }

        private void UpdateDisplayedRoutes(string city)
        {
            DisplayedRoutes.Clear();

            if (Routes.ContainsKey(city))
            {
                foreach (var route in Routes[city])
                {
                    DisplayedRoutes.Add(route);
                }
                Console.WriteLine($"Маршрути оновлено для міста: {city}");
            }
            else
            {
                Console.WriteLine($"Маршрути для міста \"{city}\" відсутні.");
            }
            RoutesListView.ItemsSource = DisplayedRoutes;
        }

        private async Task OnCityEntryCompleted(object sender, EventArgs e)
        {
            string? enteredCity = CityEntry?.Text?.Trim();
            if (!string.IsNullOrEmpty(enteredCity))
            {
                var coordinates = await GetCoordinatesFromAddress(enteredCity);
                if (coordinates != null)
                {
                    await AddMarkerToMap(coordinates.Value.lat, coordinates.Value.lng, "startPoint");
                }
            }
        }

        private async Task MoveMapToCity(string cityName)
        {
            Console.WriteLine($"[MoveMapToCity] Moving map to city: {cityName}");

            if (CityCoordinates.TryGetValue(cityName, out var coordinates))
            {
                if (MapWebView != null)
                {
                    try
                    {
                        string clearScript = "clearMarkersAndRoutes();";
                        Console.WriteLine("[MoveMapToCity] Clearing markers and routes...");
                        await MapWebView.EvaluateJavaScriptAsync(clearScript);

                        string setCenterScript = $"setMapCenter({coordinates.Latitude.ToString(CultureInfo.InvariantCulture)}, {coordinates.Longitude.ToString(CultureInfo.InvariantCulture)}, 12);";
                        Console.WriteLine($"[MoveMapToCity] Executing JavaScript to center map: {setCenterScript}");
                        await MapWebView.EvaluateJavaScriptAsync(setCenterScript);

                        Console.WriteLine($"[MoveMapToCity] Map successfully centered to city: {cityName}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[MoveMapToCity] JavaScript execution error: {ex.Message}");
                        await DisplayAlert("Помилка", "Не вдалося перемістити карту до обраного міста.", "OK");
                    }
                }
                else
                {
                    Console.WriteLine("[MoveMapToCity] MapWebView is null. Cannot move map.");
                    await DisplayAlert("Помилка", "Карта наразі недоступна.", "OK");
                }
            }
            else
            {
                Console.WriteLine($"[MoveMapToCity] City '{cityName}' not found in coordinates dictionary.");
                await DisplayAlert("Місто не знайдено", "Цього міста наразі немає в базі.", "OK");
            }
        }

        private void OnTapGestureRecognizerTapped(object sender, EventArgs e)
        {
            FilteredCities.Clear();
        }

        private void OnEntryFocused(object sender, FocusEventArgs e)
        {
            CitySuggestions.IsVisible = false;
        }

        private async Task AddMarkerToMap(double latitude, double longitude, string markerKey)
        {
            if (MapWebView == null)
            {
                Console.WriteLine("[AddMarkerToMap] MapWebView is null. Cannot add marker to map.");
                return;
            }

            try
            {
                string script = $"addMarker({latitude.ToString(CultureInfo.InvariantCulture)}, {longitude.ToString(CultureInfo.InvariantCulture)}, '{markerKey}')";
                Console.WriteLine($"[AddMarkerToMap] Executing JavaScript Command: {script}");

                await MapWebView.EvaluateJavaScriptAsync(script);

                Console.WriteLine($"[AddMarkerToMap] JavaScript executed successfully for marker {markerKey} at Latitude={latitude}, Longitude={longitude}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AddMarkerToMap] Error executing JavaScript: {ex.Message}");
            }
        }

        private async Task<string?> GetAddressFromCoordinates(double latitude, double longitude)
        {
            using var client = new HttpClient();
            string url = $"https://nominatim.openstreetmap.org/reverse?format=json&lat={latitude}&lon={longitude}";
            var response = await client.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var json = Newtonsoft.Json.Linq.JObject.Parse(content);
                return json["display_name"]?.ToString();
            }
            return null;
        }

        private async Task<(double lat, double lng)?> GetCoordinatesFromAddress(string address)
        {
            using var client = new HttpClient();
            string formattedAddress = Uri.EscapeDataString(address.Trim());
            string url = $"https://nominatim.openstreetmap.org/search?q={formattedAddress}&format=json&addressdetails=1";

            Console.WriteLine($"[GetCoordinatesFromAddress] Requesting coordinates for: {address}");
            Console.WriteLine($"[GetCoordinatesFromAddress] Generated URL: {url}");

            client.DefaultRequestHeaders.Add("User-Agent", "MyApp/1.0");

            try
            {
                var response = await client.GetAsync(url);

                Console.WriteLine($"[GetCoordinatesFromAddress] Response Status: {response.StatusCode}");

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();

                    Console.WriteLine($"[GetCoordinatesFromAddress] Response Content: {content}");

                    var json = Newtonsoft.Json.Linq.JArray.Parse(content);

                    if (json.Count > 0)
                    {
                        var firstResult = json.First;
                        Console.WriteLine($"[GetCoordinatesFromAddress] First JSON result: {firstResult}");

                        if (firstResult != null)
                        {
                            double lat = double.Parse(firstResult["lat"]?.ToString() ?? "0", CultureInfo.InvariantCulture);
                            double lng = double.Parse(firstResult["lon"]?.ToString() ?? "0", CultureInfo.InvariantCulture);
                            Console.WriteLine($"[GetCoordinatesFromAddress] Coordinates found: Latitude={lat}, Longitude={lng}");
                            return (lat, lng);
                        }
                    }
                    else
                    {
                        Console.WriteLine("[GetCoordinatesFromAddress] No results found in API response.");
                    }
                }
                else
                {
                    Console.WriteLine($"[GetCoordinatesFromAddress] API Error: {response.StatusCode}, Reason: {response.ReasonPhrase}");
                }
            }
            catch (Newtonsoft.Json.JsonException jsonEx)
            {
                Console.WriteLine($"[GetCoordinatesFromAddress] JSON Parsing Error: {jsonEx.Message}");
            }
            catch (HttpRequestException httpEx)
            {
                Console.WriteLine($"[GetCoordinatesFromAddress] HTTP Request Error: {httpEx.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GetCoordinatesFromAddress] General Error: {ex.Message}");
            }

            Console.WriteLine($"[GetCoordinatesFromAddress] Failed to fetch coordinates for: {address}");
            return null;
        }

        private async void OnAddStartPointMarkerClicked(object sender, EventArgs e)
        {
            string selectedCity = CityEntry?.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(selectedCity))
            {
                await DisplayAlert("Помилка", "Будь ласка, спочатку задайте місто.", "OK");
                return;
            }

            string address = StartPointEntry?.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(address))
            {
                await MapWebView.EvaluateJavaScriptAsync("clearMarker('startPoint')");
                await DisplayAlert("Помилка", "Будь ласка, введіть адресу місця відправлення.", "OK");
                return;
            }

            string[] possibleFormats = {
            $"{selectedCity}, {address}",
            $"{address}, {selectedCity}"
            };

            foreach (var format in possibleFormats)
            {
                Console.WriteLine($"[OnAddStartPointMarkerClicked] Trying format: {format}");
                var coordinates = await GetCoordinatesFromAddress(format);
                if (coordinates != null)
                {
                    Console.WriteLine($"[OnAddStartPointMarkerClicked] Coordinates found: Latitude={coordinates.Value.lat}, Longitude={coordinates.Value.lng}");
                    await AddMarkerToMap(coordinates.Value.lat, coordinates.Value.lng, "startPoint");
                    return;
                }
            }

            Console.WriteLine($"[OnAddStartPointMarkerClicked] Failed to find coordinates for address: {address}, {selectedCity}");
            await DisplayAlert("Помилка", $"Не вдалося знайти координати для адреси: {address}, {selectedCity}", "OK");
        }

        private async void OnAddDestinationPointMarkerClicked(object sender, EventArgs e)
        {
            string selectedCity = CityEntry?.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(selectedCity))
            {
                await DisplayAlert("Помилка", "Будь ласка, спочатку задайте місто.", "OK");
                return;
            }

            string address = DestinationPointEntry?.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(address))
            {
                await MapWebView.EvaluateJavaScriptAsync("clearMarker('endPoint')");
                await DisplayAlert("Помилка", "Будь ласка, введіть адресу місця прибуття.", "OK");
                return;
            }

            string[] possibleFormats = {
            $"{selectedCity}, {address}",
            $"{address}, {selectedCity}"
            };

            foreach (var format in possibleFormats)
            {
                Console.WriteLine($"[OnAddDestinationPointMarkerClicked] Trying format: {format}");
                var coordinates = await GetCoordinatesFromAddress(format);
                if (coordinates != null)
                {
                    Console.WriteLine($"[OnAddDestinationPointMarkerClicked] Coordinates found: Latitude={coordinates.Value.lat}, Longitude={coordinates.Value.lng}");
                    await AddMarkerToMap(coordinates.Value.lat, coordinates.Value.lng, "endPoint");
                    return;
                }
            }

            Console.WriteLine($"[OnAddDestinationPointMarkerClicked] Failed to find coordinates for address: {address}, {selectedCity}");
            await DisplayAlert("Помилка", $"Не вдалося знайти координати для адреси: {address}, {selectedCity}", "OK");
        }

        private async Task ClearFieldsAndMarkers()
        {
            StartPointEntry.Text = string.Empty;
            DestinationPointEntry.Text = string.Empty;

            if (MapWebView != null)
            {
                try
                {
                    Console.WriteLine("Clearing markers via JavaScript...");
                    await MapWebView.EvaluateJavaScriptAsync("clearMarkers()");
                    Console.WriteLine("Markers cleared from map.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Помилка очищення маркерів: {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine("MapWebView is null, cannot clear markers.");
            }

            Console.WriteLine("Поля та маркери очищено.");
        }

        private void OnBackButtonClicked(object sender, EventArgs e)
        {
            MapInterface.IsVisible = false;
            InitialInterface.IsVisible = true;
        }
    }
}