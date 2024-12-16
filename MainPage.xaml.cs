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
            //MapWebView.Navigated += MapWebView_Navigated;
            //_ = CopyHtmlToAppDataDirectory();
            CityPicker.ItemsSource = new List<string> { "Всі", "Київ", "Львів", "Херсон" };
            CityPicker.SelectedItem = "Всі"; // Встановлюємо значення за замовчуванням
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
            string filePath = Path.Combine(FileSystem.AppDataDirectory, "routes.json");
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

                // Завантаження маршрутів
                string filePath = Path.Combine(FileSystem.AppDataDirectory, "routes.json");
                Routes = RouteService.LoadRoutes(filePath);

                // Оновлення списку маршрутів для обраного міста (наприклад, Київ)
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

            string filePath = Path.Combine(FileSystem.AppDataDirectory, "routes.json");
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
                            // Якщо CityName порожній, заповнюємо його назвою міста
                            if (string.IsNullOrWhiteSpace(route.CityName))
                            {
                                route.CityName = city;
                            }

                            // Перевірка і заповнення зупинок, якщо їх кількість менша за StopCount
                            if (route.Stops == null)
                            {
                                route.Stops = new List<(string StopName, double Latitude, double Longitude)>();
                            }

                            while (route.Stops.Count < route.StopCount)
                            {
                                route.Stops.Add((string.Empty, 0.0, 0.0)); // Додаємо "порожню" зупинку
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
                    var json = JsonConvert.SerializeObject(routes, Formatting.Indented);

                    File.WriteAllText(filePath, json);
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



        //private void OnAddRouteClicked(object sender, EventArgs e)
        //{
        //    // Отримуємо вибране місто
        //    string selectedCity = CityEntry?.Text?.Trim() ?? string.Empty;

        //    if (string.IsNullOrEmpty(selectedCity))
        //    {
        //        DisplayAlert("Помилка", "Будь ласка, оберіть місто для додавання маршруту.", "OK");
        //        return;
        //    }

        //    // Новий маршрут
        //    var newRoute = new RouteInfo
        //    {
        //        RouteNumber = "Новий номер",
        //        Description = "Новий опис",
        //        Details = "Деталі маршруту",
        //        RouteLength = 0,
        //        StopCount = 0,
        //        VehicleCount = 0,
        //        Interval = "0 хв",
        //        TransportType = "Тип транспорту",
        //        Stops = new List<(string StopName, double Latitude, double Longitude)>()
        //    };

        //    // Додавання маршруту до вибраного міста
        //    if (Routes.ContainsKey(selectedCity))
        //    {
        //        Routes[selectedCity].Add(newRoute);
        //    }
        //    else
        //    {
        //        Routes[selectedCity] = new List<RouteInfo> { newRoute };
        //    }

        //    // Оновлення списку відображення
        //    if (DisplayedRoutes != null && selectedCity == CurrentSelectedCity)
        //    {
        //        DisplayedRoutes.Add(newRoute);
        //    }

        //    //SaveChangesButton.IsEnabled = true;

        //    DisplayAlert("Успіх", $"Маршрут додано до міста: {selectedCity}.", "OK");
        //}

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

                string filePath = Path.Combine(FileSystem.AppDataDirectory, "routes.json");
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
            AdminInterface.IsVisible = false;
            InitialInterface.IsVisible = true;
            IsAdminLoggedIn = false;

            AdminLoginEntry.Text = string.Empty;
            AdminPasswordEntry.Text = string.Empty;
        }

        private void OnCityFilterChanged(object? sender, EventArgs e)
        {
            if (CityPicker.SelectedItem == null)
            {
                CityPicker.SelectedItem = "Всі";
            }

            // Гарантія, що значення SelectedItem ніколи не буде null
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
                // Якщо значення все одно відсутнє, встановлюємо "Всі"
                CurrentSelectedCity = "Всі";
            }
        }

        private readonly List<string> ValidCities = new List<string> { "Київ", "Львів", "Херсон" };
        private readonly List<string> ValidTransportTypes = new List<string> { "автобус", "трамвай", "тролейбус" };

        private void OnAddOrUpdateRouteClicked(object sender, EventArgs e)
        {
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
            var errors = new List<string>();

            if (!ValidCities.Contains(cityName))
            {
                errors.Add($"Місто '{cityName}' немає в базі.");
            }

            if (!ValidTransportTypes.Contains(transportType))
            {
                errors.Add($"Тип транспорту '{transportType}' не підтримується. Доступні варіанти: автобус, трамвай, тролейбус.");
            }

            if (!double.TryParse(RouteLengthEntry.Text.Trim(), out double routeLength))
            {
                errors.Add("Довжина маршруту має бути числовим значенням.");
            }

            if (!int.TryParse(StopCountEntry.Text.Trim(), out int stopCount))
            {
                errors.Add("Кількість зупинок має бути цілим числом.");
            }

            if (!int.TryParse(VehicleCountEntry.Text.Trim(), out int vehicleCount))
            {
                errors.Add("Кількість транспорту має бути цілим числом.");
            }

            if (!int.TryParse(RouteNumberEntry.Text.Trim(), out int routeNumber))
            {
                errors.Add("Номер маршруту має бути цілим числом.");
            }

            if (errors.Any())
            {
                DisplayAlert("Помилка", string.Join("\n", errors), "OK");
                return;
            }

            if (Routes.ContainsKey(cityName))
            {
                var existingRoute = Routes[cityName]
                    .FirstOrDefault(r => r.RouteNumber == routeNumber.ToString() &&
                                         r.TransportType.Equals(transportType, StringComparison.OrdinalIgnoreCase) &&
                                         r.CityName.Equals(cityName, StringComparison.OrdinalIgnoreCase));

                if (existingRoute != null)
                {
                    DisplayAlert("Помилка", "Такий маршрут вже існує. Ви можете відредагувати його за необхідності.", "OK");
                    return;
                }
            }

            var stops = new List<(string StopName, double Latitude, double Longitude)>();
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

            if (Routes.ContainsKey(cityName))
            {
                var existingRoute = Routes[cityName].FirstOrDefault(r => r.RouteNumber == newRoute.RouteNumber);
                if (existingRoute != null)
                {
                    Routes[cityName].Remove(existingRoute);
                }
                Routes[cityName].Add(newRoute);
            }
            else
            {
                Routes[cityName] = new List<RouteInfo> { newRoute };
            }

            string filePath = Path.Combine(FileSystem.AppDataDirectory, "routes.json");
            RouteService.SaveRoutes(Routes, filePath);

            OnCityFilterUpdatedWithValidation("Всі");

            StopPicker.ItemsSource = Enumerable.Range(1, stopCount).Select(i => $"Зупинка {i}").ToList();
            StopPicker.SelectedIndex = 0;

            StopNameEntry.IsEnabled = true;
            LatitudeEntry.IsEnabled = true;
            LongitudeEntry.IsEnabled = true;
            SaveCoordinatesButton.IsEnabled = true;

            DisplayAlert("Успіх", $"Маршрут для міста '{cityName}' успішно додано або оновлено.", "OK");
            ClearInputFields();
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
                        // Перевірка на наявність неповних даних
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
                        // Перевірка на наявність неповних даних
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

            if (isFormValid && int.TryParse(StopCountEntry.Text, out int stopCount))
            {
                StopPicker.ItemsSource = Enumerable.Range(1, stopCount).Select(i => $"Зупинка {i}").ToList();
            }
        }
        private void OnFieldTextChanged(object sender, TextChangedEventArgs e)
        {
            ValidateFieldsForCoordinates();
        }




        //private void OnSaveChangesClicked(object sender, EventArgs e)
        //{
        //    string filePath = Path.Combine(FileSystem.AppDataDirectory, "routes.json");

        //    RouteService.SaveRoutes(Routes, filePath);

        //    DisplayAlert("Збережено", "Зміни успішно збережені.", "OK");

        //    SaveChangesButton.IsEnabled = false;
        //}

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
            // Перевірка, чи обрано маршрут
            if (RoutesCollectionView.SelectedItem is not RouteInfo selectedRoute)
            {
                DisplayAlert("Помилка", "Спочатку виберіть маршрут.", "OK");
                return;
            }

            // Перевірка, чи обрано зупинку у Picker
            if (StopPicker.SelectedItem is null)
            {
                DisplayAlert("Помилка", "Будь ласка, оберіть зупинку.", "OK");
                HighlightEmptyStopFields();
                return;
            }

            // Перевірка валідності координат
            if (!double.TryParse(LatitudeEntry.Text.Trim(), out double latitude) ||
                !double.TryParse(LongitudeEntry.Text.Trim(), out double longitude))
            {
                DisplayAlert("Помилка", "Будь ласка, введіть коректні координати (широта та довгота).", "OK");
                HighlightEmptyStopFields();
                return;
            }

            // Перевірка назви зупинки
            if (string.IsNullOrWhiteSpace(StopNameEntry.Text))
            {
                DisplayAlert("Помилка", "Будь ласка, введіть назву зупинки.", "OK");
                HighlightEmptyStopFields();
                return;
            }

            int stopIndex = StopPicker.SelectedIndex;

            // Перевірка, чи індекс зупинки валідний
            if (stopIndex >= 0 && stopIndex < selectedRoute.Stops.Count)
            {
                // Оновлюємо координати та назву зупинки
                var updatedStop = (StopName: StopNameEntry.Text.Trim(), Latitude: latitude, Longitude: longitude);
                selectedRoute.Stops[stopIndex] = updatedStop;

                // Зберігаємо зміни у Routes
                string cityName = selectedRoute.CityName;
                string routeNumber = selectedRoute.RouteNumber;

                if (Routes.ContainsKey(cityName))
                {
                    var routeToUpdate = Routes[cityName].FirstOrDefault(r => r.RouteNumber == routeNumber);
                    if (routeToUpdate != null)
                    {
                        routeToUpdate.Stops[stopIndex] = updatedStop;

                        // Зберігаємо оновлений файл
                        string filePath = Path.Combine(FileSystem.AppDataDirectory, "routes.json");
                        RouteService.SaveRoutes(Routes, filePath);

                        DisplayAlert("Успіх", $"Координати та назва для '{updatedStop.StopName}' оновлено.", "OK");
                        return;
                    }
                }
            }
            else
            {
                DisplayAlert("Помилка", "Індекс зупинки поза межами.", "OK");
                HighlightEmptyStopFields();
            }
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
            public const decimal LvivFare = 10.00m;     // Ціна проїзду у Львові
            public const decimal KhersonFare = 6.00m;   // Ціна проїзду в Херсоні
        }


        //private void ProcessJavaScriptMessage(string message)
        //{
        //    Console.WriteLine($"Received JavaScript message: {message}");
        //    if (message.StartsWith("js://AddManualMarker|"))
        //    {
        //        var parts = message.Replace("js://AddManualMarker|", "").Split('|');
        //        if (parts.Length == 2)
        //        {
        //            if (double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) &&
        //                double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var lng))
        //            {
        //                Console.WriteLine($"Parsed coordinates: Latitude={lat}, Longitude={lng}");
        //                _ = AddManualMarker(lat.ToString(CultureInfo.InvariantCulture), lng.ToString(CultureInfo.InvariantCulture));
        //            }
        //        }
        //    }
        //    else
        //    {
        //        Console.WriteLine("Message format not recognized.");
        //    }
        //}

        //private async Task CopyHtmlToAppDataDirectory()
        //{
        //    var sourcePath = Path.Combine(FileSystem.Current.AppDataDirectory, "map.html");

        //    try
        //    {
        //        if (File.Exists(sourcePath))
        //        {
        //            File.Delete(sourcePath);
        //            Console.WriteLine("Старий файл map.html видалено.");
        //        }

        //        using var stream = await FileSystem.OpenAppPackageFileAsync("map.html");
        //        using var destinationStream = File.Create(sourcePath);
        //        await stream.CopyToAsync(destinationStream);
        //        Console.WriteLine("Новий файл map.html успішно скопійовано.");
        //    }
        //    catch (Exception ex)
        //    {
        //        Console.WriteLine($"Помилка копіювання map.html: {ex.Message}");
        //    }
        //}

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

            // Очищення списку фільтрованих міст
            FilteredCities.Clear();

            // Фільтруємо доступні міста
            var filtered = _allCities
                .Where(c => c.ToLower().StartsWith(searchText))
                .ToList();

            foreach (var city in filtered)
            {
                FilteredCities.Add(city);
            }

            Console.WriteLine($"Filtered cities count: {FilteredCities.Count}");

            // Динамічний розмір підказок
            const int itemHeight = 50;
            const int maxHeight = 300;
            int calculatedHeight = FilteredCities.Count > 0
                ? Math.Min(FilteredCities.Count * itemHeight, maxHeight)
                : 0;

            AbsoluteLayout.SetLayoutBounds(CitySuggestionsParent, new Rect(0.5, 155, 300, calculatedHeight));
            CitySuggestions.IsVisible = FilteredCities.Count > 0;

            // Оновлюємо статус вибору міста
            IsCitySelected = !string.IsNullOrEmpty(e.NewTextValue) && _allCities.Contains(e.NewTextValue);

            // Додатково: Оновлення списку маршрутів для введеного міста
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
                RoutesListView.ItemsSource = null; // Очищаємо список, якщо місто не знайдено
            }
        }

        private string? CurrentSelectedCity = "Київ";

        private async void OnCitySelected(object sender, SelectionChangedEventArgs e)
        {
            if (e.CurrentSelection.FirstOrDefault() is string selectedCity)
            {
                // Оновлення вибраного міста
                CityEntry.Text = selectedCity;
                CitySuggestions.SelectedItem = null;
                FilteredCities.Clear();

                IsCitySelected = true;

                Console.WriteLine($"[OnCitySelected] Selected city: {selectedCity}");

                await ClearFieldsAndMarkers();
                await MoveMapToCity(selectedCity);

                DisplayedRoutes.Clear();

                if (Routes.ContainsKey(selectedCity))
                {
                    foreach (var route in Routes[selectedCity])
                    {
                        DisplayedRoutes.Add(route);
                    }
                }

                Console.WriteLine($"[OnCitySelected] Updated routes for city: {selectedCity}");
            }
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
            Console.WriteLine($"Moving map to city: {cityName}");
            if (CityCoordinates.TryGetValue(cityName, out var coordinates))
            {
                if (MapWebView != null)
                {
                    string script = $"setMapCenter({coordinates.Latitude.ToString(CultureInfo.InvariantCulture)}, {coordinates.Longitude.ToString(CultureInfo.InvariantCulture)}, 12)";
                    Console.WriteLine($"Executing JavaScript to center map: {script}");
                    await MapWebView.EvaluateJavaScriptAsync(script);
                }
                else
                {
                    Console.WriteLine("MapWebView is null. Cannot move map to city.");
                }
            }
            else
            {
                Console.WriteLine($"City {cityName} not found in coordinates dictionary.");
                await DisplayAlert("Місто не знайдено", "Цього міста наразі немає в базі.", "ОК");
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

        //private async Task AddManualMarker(string lat, string lng)
        //{
        //    double latitude = double.Parse(lat, CultureInfo.InvariantCulture);
        //    double longitude = double.Parse(lng, CultureInfo.InvariantCulture);

        //    Console.WriteLine($"Adding manual marker at Latitude={latitude}, Longitude={longitude}");

        //    string? address = await GetAddressFromCoordinates(latitude, longitude);
        //    if (!string.IsNullOrEmpty(address))
        //    {
        //        Console.WriteLine($"Retrieved address: {address}");
        //        if (string.IsNullOrEmpty(StartPointEntry.Text))
        //        {
        //            StartPointEntry.Text = address;
        //        }
        //        else
        //        {
        //            DestinationPointEntry.Text = address;
        //        }
        //    }

        //    await AddMarkerToMap(latitude, longitude, "manualMarker");
        //}

        //private void MapWebView_Navigated(object? sender, WebNavigatedEventArgs? e)
        //{
        //    if (e?.Url == null)
        //    {
        //        Console.WriteLine("URL is null in MapWebView_Navigated.");
        //        return;
        //    }

        //    if (MapWebView != null)
        //    {
        //        MapWebView.Navigated -= MapWebView_Navigated;

        //        try
        //        {
        //            MapWebView.Eval("addClickMarker()");
        //            Console.WriteLine("addClickMarker() successfully invoked.");
        //        }
        //        catch (Exception ex)
        //        {
        //            Console.WriteLine($"Error invoking addClickMarker(): {ex.Message}");
        //        }

        //        if (e.Url.Contains("AddManualMarker"))
        //        {
        //            try
        //            {
        //                var script = e.Url.Split('|');
        //                if (script.Length == 3 && script[0] == "AddManualMarker")
        //                {
        //                    _ = AddManualMarker(script[1], script[2]);
        //                }
        //            }
        //            catch (Exception ex)
        //            {
        //                Console.WriteLine($"Error processing AddManualMarker: {ex.Message}");
        //            }
        //        }

        //        MapWebView.Navigated += MapWebView_Navigated;
        //    }
        //    else
        //    {
        //        Console.WriteLine("MapWebView is null in MapWebView_Navigated.");
        //    }
        //}

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

                // Логування статусу відповіді
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

        private void OnBuildRouteClicked(object sender, EventArgs e)
        {
            DisplayAlert("Побудова маршруту", "Логіка побудови маршруту буде додана пізніше.", "OK");
        }

        private void OnMapLoaded(object sender, WebNavigatedEventArgs e)
        {
            // Карта успішно завантажена
        }
    }
}