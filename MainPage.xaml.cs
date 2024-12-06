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
            BindingContext = this;
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
            MapWebView.Source = "map.html";
        }

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

        private void OnCityEntryTextChanged(object sender, TextChangedEventArgs e)
        {
            string searchText = e.NewTextValue?.ToLower() ?? string.Empty;

            FilteredCities.Clear();
            var filtered = _allCities.Where(c => c.ToLower().StartsWith(searchText)).ToList();

            foreach (var city in filtered)
            {
                FilteredCities.Add(city);
            }

            const int itemHeight = 50;
            const int maxHeight = 300;
            int calculatedHeight = FilteredCities.Count > 0 ? Math.Min(FilteredCities.Count * itemHeight, maxHeight) : 0;

            AbsoluteLayout.SetLayoutBounds(CitySuggestionsParent, new Rect(0.5, 155, 300, calculatedHeight));

            CitySuggestions.IsVisible = FilteredCities.Count > 0;
        }

        private async void OnCitySelected(object sender, SelectionChangedEventArgs e)
        {
            if (e.CurrentSelection.FirstOrDefault() is string selectedCity)
            {
                CityEntry.Text = selectedCity;
                CitySuggestions.SelectedItem = null;
                FilteredCities.Clear();
                await MoveMapToCity(selectedCity);
            }
        }

        private async void OnCityEntryCompleted(object sender, EventArgs e)
        {
            string? enteredCity = CityEntry?.Text?.Trim();
            if (!string.IsNullOrEmpty(enteredCity))
            {
                await MoveMapToCity(enteredCity);
            }
        }

        private async Task MoveMapToCity(string cityName)
        {
            if (CityCoordinates.TryGetValue(cityName, out var coordinates))
            {
                Console.WriteLine($"MoveMapToCity called with: Latitude={coordinates.Latitude}, Longitude={coordinates.Longitude}");
                string script = $"setMapCenter({coordinates.Latitude.ToString(CultureInfo.InvariantCulture)}, {coordinates.Longitude.ToString(CultureInfo.InvariantCulture)}, 12)";
                await MapWebView.EvaluateJavaScriptAsync(script);
            }
            else
            {
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

        private void OnMapLoaded(object sender, WebNavigatedEventArgs e)
        {
            // Карта успішно завантажена
        }
    }
}