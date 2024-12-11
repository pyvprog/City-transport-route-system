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
        private int _currentNewsIndex = 0;
        private string _currentTime = string.Empty;
        private NewsItem _currentNews = new NewsItem();
        private bool _isOnline = true;

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
            MapWebView.Navigated += MapWebView_Navigated;
            //_ = CopyHtmlToAppDataDirectory();
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

        private void ProcessJavaScriptMessage(string message)
        {
            Console.WriteLine($"Received JavaScript message: {message}");
            if (message.StartsWith("js://AddManualMarker|"))
            {
                var parts = message.Replace("js://AddManualMarker|", "").Split('|');
                if (parts.Length == 2)
                {
                    if (double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) &&
                        double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var lng))
                    {
                        Console.WriteLine($"Parsed coordinates: Latitude={lat}, Longitude={lng}");
                        _ = AddManualMarker(lat.ToString(CultureInfo.InvariantCulture), lng.ToString(CultureInfo.InvariantCulture));
                    }
                }
            }
            else
            {
                Console.WriteLine("Message format not recognized.");
            }
        }

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


        //private async void OnCitySelected(object sender, SelectionChangedEventArgs e)
        //{
        // Перевіряємо, чи є вибране місто
        //    if (e.CurrentSelection?.FirstOrDefault() is string selectedCity)
        //   {
        //       CityEntry.Text = selectedCity; // Заповнюємо поле введення обраним містом
        //        CitySuggestions.IsVisible = false; // Ховаємо список підказок
        //
        //        // Викликаємо метод переміщення карти до вибраного міста
        //        await MoveMapToCity(selectedCity);
        //    }
        //}

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
            string searchText = e.NewTextValue?.ToLower() ?? string.Empty;
            Console.WriteLine($"City search text changed: {searchText}");

            FilteredCities.Clear();
            var filtered = _allCities.Where(c => c.ToLower().StartsWith(searchText)).ToList();

            foreach (var city in filtered)
            {
                FilteredCities.Add(city);
            }
            Console.WriteLine($"Filtered cities count: {FilteredCities.Count}");

            const int itemHeight = 50;
            const int maxHeight = 300;
            int calculatedHeight = FilteredCities.Count > 0 ? Math.Min(FilteredCities.Count * itemHeight, maxHeight) : 0;

            AbsoluteLayout.SetLayoutBounds(CitySuggestionsParent, new Rect(0.5, 155, 300, calculatedHeight));

            CitySuggestions.IsVisible = FilteredCities.Count > 0;

            IsCitySelected = !string.IsNullOrEmpty(e.NewTextValue) && _allCities.Contains(e.NewTextValue);
        }

        private async void OnCitySelected(object sender, SelectionChangedEventArgs e)
        {
            if (e.CurrentSelection.FirstOrDefault() is string selectedCity)
            {
                CityEntry.Text = selectedCity;
                CitySuggestions.SelectedItem = null;
                FilteredCities.Clear();

                IsCitySelected = true;

                Console.WriteLine($"[OnCitySelected] Selected city: {selectedCity}");

                await ClearFieldsAndMarkers();
                await MoveMapToCity(selectedCity);
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


        private void OnBuildRouteClicked(object sender, EventArgs e)
        {
            DisplayAlert("Побудова маршруту", "Логіка побудови маршруту буде додана пізніше.", "OK");
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

        private async Task AddManualMarker(string lat, string lng)
        {
            double latitude = double.Parse(lat, CultureInfo.InvariantCulture);
            double longitude = double.Parse(lng, CultureInfo.InvariantCulture);

            Console.WriteLine($"Adding manual marker at Latitude={latitude}, Longitude={longitude}");

            string? address = await GetAddressFromCoordinates(latitude, longitude);
            if (!string.IsNullOrEmpty(address))
            {
                Console.WriteLine($"Retrieved address: {address}");
                if (string.IsNullOrEmpty(StartPointEntry.Text))
                {
                    StartPointEntry.Text = address;
                }
                else
                {
                    DestinationPointEntry.Text = address;
                }
            }

            await AddMarkerToMap(latitude, longitude, "manualMarker");
        }

        private void MapWebView_Navigated(object? sender, WebNavigatedEventArgs? e)
        {
            if (e?.Url == null)
            {
                Console.WriteLine("URL is null in MapWebView_Navigated.");
                return;
            }

            if (MapWebView != null)
            {
                MapWebView.Navigated -= MapWebView_Navigated;

                try
                {
                    MapWebView.Eval("addClickMarker()");
                    Console.WriteLine("addClickMarker() successfully invoked.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error invoking addClickMarker(): {ex.Message}");
                }

                if (e.Url.Contains("AddManualMarker"))
                {
                    try
                    {
                        var script = e.Url.Split('|');
                        if (script.Length == 3 && script[0] == "AddManualMarker")
                        {
                            _ = AddManualMarker(script[1], script[2]);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error processing AddManualMarker: {ex.Message}");
                    }
                }

                MapWebView.Navigated += MapWebView_Navigated;
            }
            else
            {
                Console.WriteLine("MapWebView is null in MapWebView_Navigated.");
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

        //private async Task ClearAllMarkers()
        //{
        //    if (MapWebView == null)
        //    {
        //        Console.WriteLine("MapWebView is null. Cannot clear markers.");
        //        return;
        //    }
        //
        //    try
        //    {
        //        string script = "clearAllMarkers()"; // Метод JavaScript для очищення міток
        //        Console.WriteLine("[ClearAllMarkers] Executing JavaScript Command: clearAllMarkers()");
        //        await MapWebView.EvaluateJavaScriptAsync(script);
        //        Console.WriteLine("[ClearAllMarkers] All markers cleared successfully.");
        //    }
        //    catch (Exception ex)
        //    {
        //        Console.WriteLine($"[ClearAllMarkers] Error clearing markers: {ex.Message}");
        //    }
        //}

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

        private void OnMapLoaded(object sender, WebNavigatedEventArgs e)
        {
            // Карта успішно завантажена
        }
    }
}