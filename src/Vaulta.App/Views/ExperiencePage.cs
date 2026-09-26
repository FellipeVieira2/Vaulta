using Vaulta.App.ViewModels;
using Microsoft.Maui.Storage;
using Microsoft.Maui.Controls.Shapes;

namespace Vaulta.App.Views;

public sealed class ExperiencePage : ContentPage, IQueryAttributable
{
    private readonly ExperienceViewModel _viewModel;
    private readonly VerticalStackLayout _content;

    public ExperiencePage(ExperienceViewModel viewModel)
    {
        _viewModel = viewModel;
        BindingContext = viewModel;
        _content = new VerticalStackLayout { Padding = new Thickness(24, 28), Spacing = 18 };
        Content = new ScrollView { Content = _content };
        BackgroundColor = ColorResource("BackgroundPrimary");
        Shell.SetNavBarIsVisible(this, false);
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        BuildContent();
    }

    public void ConfigureScreen(string screenId)
    {
        _viewModel.ScreenId = screenId;
        BuildContent();
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("screen", out var screen))
            _viewModel.ScreenId = Uri.UnescapeDataString(screen.ToString() ?? "home");
        BuildContent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        BuildContent();
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(ExperienceViewModel.ScreenId) or nameof(ExperienceViewModel.StatusMessage))
            MainThread.BeginInvokeOnMainThread(BuildContent);
    }

    private void BuildContent()
    {
        var screen = _viewModel.Screen;
        Title = screen.Title;
        if (screen.Id == "onboarding-1")
        {
            BuildOnboardingOne();
            return;
        }

        if (screen.Id == "onboarding-2")
        {
            BuildOnboardingTwo();
            return;
        }

        _content.Children.Clear();

        if (!string.IsNullOrWhiteSpace(screen.Eyebrow))
            _content.Children.Add(CreateLabel(screen.Eyebrow, "CaptionTextStyle", "BrandPrimary"));

        _content.Children.Add(CreateLabel(screen.Id is "home" or "collection" or "market" or "profile" ? _viewModel.DisplayGreeting : screen.Title, "H1TextStyle"));

        if (!string.IsNullOrWhiteSpace(screen.Subtitle))
            _content.Children.Add(CreateLabel(screen.Subtitle, "BodyTextStyle", "TextSecondary"));

        if (screen.Id.StartsWith("scanner", StringComparison.Ordinal) || screen.Id.StartsWith("scan-", StringComparison.Ordinal))
            AddScannerVisual(screen);

        if (screen.Notice is not null)
            _content.Children.Add(CreateSurface(CreateLabel(screen.Notice, "BodySmallTextStyle")));

        if (screen.Id is "login" or "signup")
            AddAuthenticationFields(screen.Id == "signup");

        if (screen.InputHint is not null)
            AddSearchField(screen);

        if (screen.Id is "add-to-collection" or "sell")
            AddItemFields(screen, screen.Id == "sell");

        if (screen.Options is not null)
            AddOptions(screen);

        if (screen.Metrics is not null)
            AddMetrics(screen);

        if (screen.Cards is not null)
            AddCards(screen);

        if (!string.IsNullOrWhiteSpace(_viewModel.StatusMessage))
            _content.Children.Add(CreateLabel(_viewModel.StatusMessage, "BodySmallTextStyle", "StatusWarning"));

        if (screen.Actions is null) return;
        foreach (var action in screen.Actions)
        {
            var isGoogleSignIn = action.Title == "Continuar com Google";
            var button = isGoogleSignIn ? CreateGoogleSignInButton() : CreateButton(action.Title, action.IsPrimary);
            button.Clicked += async (_, _) => await _viewModel.NavigateCommand.ExecuteAsync(action.Route);
            _content.Children.Add(button);
        }
    }

    private void BuildOnboardingOne()
    {
        Shell.SetNavBarIsVisible(this, false);

        var layout = new Grid
        {
            Padding = new Thickness(24, 8, 24, 8),
            RowDefinitions =
            {
                new RowDefinition(new GridLength(36)),
                new RowDefinition(new GridLength(36)),
                new RowDefinition(new GridLength(360)),
                new RowDefinition(GridLength.Star),
                new RowDefinition(new GridLength(132))
            }
        };

        var skip = new Button
        {
            Text = "Pular",
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            Padding = new Thickness(0),
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Center,
            BackgroundColor = Colors.Transparent,
            TextColor = ColorResource("TextSecondary")
        };
        skip.Clicked += async (_, _) => await _viewModel.NavigateCommand.ExecuteAsync("login");
        layout.Add(skip, 0, 1);

        var cardHeader = new Grid
        {
            Padding = new Thickness(8, 6),
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            BackgroundColor = Color.FromArgb("#1C1D23"),
            HeightRequest = 36
        };
        var headerLine = new Border
        {
            WidthRequest = 50,
            HeightRequest = 6,
            VerticalOptions = LayoutOptions.Center,
            StrokeThickness = 0,
            BackgroundColor = Color.FromArgb("#484C5B"),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(4) }
        };
        var badge = new Border
        {
            Padding = new Thickness(6, 2),
            BackgroundColor = Color.FromArgb("#2B2161"),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(4) },
            Content = new Label { Text = "10", FontSize = 9, FontAttributes = FontAttributes.Bold, TextColor = ColorResource("BrandPrimary") }
        };
        cardHeader.Add(headerLine, 0, 0);
        cardHeader.Add(badge, 1, 0);

        var cardArtwork = new Grid
        {
            HeightRequest = 190,
            Background = new LinearGradientBrush(
            [
                new GradientStop(Color.FromArgb("#1C1D23"), 0),
                new GradientStop(Color.FromArgb("#2A264D"), 0.5f),
                new GradientStop(Color.FromArgb("#1C1535"), 1)
            ], new Point(0, 0), new Point(1, 1)),
            RowDefinitions = { new RowDefinition(GridLength.Star) },
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star) }
        };
        var mark = new Border
        {
            WidthRequest = 56,
            HeightRequest = 56,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            BackgroundColor = Color.FromArgb("#08FFFFFF"),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(28) },
            Content = new Polygon
            {
                Points = new PointCollection
                {
                    new Point(0, 0),
                    new Point(28, 0),
                    new Point(14, 24)
                },
                Fill = new SolidColorBrush(ColorResource("BrandPrimary")),
                WidthRequest = 28,
                HeightRequest = 24,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            }
        };
        cardArtwork.Add(mark);

        var artworkBorder = new Border
        {
            Margin = new Thickness(0, 0, 0, 0),
            StrokeThickness = 1,
            Stroke = new SolidColorBrush(Color.FromArgb("#23242E")),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10) },
            Content = cardArtwork
        };

        var cardInner = new Border
        {
            Margin = new Thickness(0),
            Padding = new Thickness(12),
            StrokeThickness = 1,
            BackgroundColor = Color.FromArgb("#121217"),
            Stroke = new SolidColorBrush(Color.FromArgb("#333442")),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(20) },
            Content = new VerticalStackLayout { Spacing = 0, Children = { cardHeader, artworkBorder } }
        };
        var cardFrame = new Border
        {
            WidthRequest = 210,
            HeightRequest = 300,
            Padding = new Thickness(12),
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            StrokeThickness = 1.5,
            BackgroundColor = Color.FromArgb("#121217"),
            Stroke = new SolidColorBrush(Color.FromArgb("#333442")),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(20) },
            Content = cardInner
        };
        layout.Add(cardFrame, 0, 2);

        var copy = new VerticalStackLayout { Spacing = 12, VerticalOptions = LayoutOptions.End };
        copy.Children.Add(new Label
        {
            Text = "Organize sua coleção",
            FontSize = 30,
            FontAttributes = FontAttributes.Bold,
            LineHeight = 1.2,
            TextColor = ColorResource("TextPrimary")
        });
        copy.Children.Add(new Label
        {
            Text = "Catalogue every card. Know exactly what you have in real-time.",
            FontSize = 16,
            LineHeight = 1.5,
            TextColor = ColorResource("TextSecondary")
        });
        layout.Add(copy, 0, 3);

        var footer = new Grid { RowDefinitions = { new RowDefinition(new GridLength(24)), new RowDefinition(new GridLength(76)) } };
        var progress = new HorizontalStackLayout { Spacing = 8, VerticalOptions = LayoutOptions.Center };
        var active = new Border
        {
            WidthRequest = 24,
            HeightRequest = 6,
            BackgroundColor = ColorResource("BrandPrimary"),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(4) }
        };
        progress.Children.Add(active);
        progress.Children.Add(CreateProgressDot());
        progress.Children.Add(CreateProgressDot());
        footer.Add(progress, 0, 0);

        var next = new Button
        {
            Text = "Próximo",
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            CornerRadius = 14,
            HeightRequest = 52,
            Margin = new Thickness(0, 24, 0, 0),
            TextColor = ColorResource("TextInverse"),
            BackgroundColor = ColorResource("BrandPrimary")
        };
        next.Clicked += async (_, _) => await _viewModel.NavigateCommand.ExecuteAsync("onboarding-2");
        footer.Add(next, 0, 1);
        layout.Add(footer, 0, 4);

        Content = layout;
    }

    private void BuildOnboardingTwo()
    {
        Shell.SetNavBarIsVisible(this, false);

        var layout = new Grid
        {
            Padding = new Thickness(24, 8, 24, 8),
            RowDefinitions =
            {
                new RowDefinition(new GridLength(36)),
                new RowDefinition(new GridLength(36)),
                new RowDefinition(new GridLength(400)),
                new RowDefinition(GridLength.Star),
                new RowDefinition(new GridLength(132))
            }
        };

        var skip = new Button
        {
            Text = "Pular",
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            Padding = new Thickness(0),
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Center,
            BackgroundColor = Colors.Transparent,
            TextColor = ColorResource("TextSecondary")
        };
        skip.Clicked += async (_, _) => await _viewModel.NavigateCommand.ExecuteAsync("login");
        layout.Add(skip, 0, 1);

        var valueDetails = new VerticalStackLayout
        {
            Spacing = 4,
            Children =
            {
                new Label
                {
                    Text = "VALOR DA COLEÇÃO",
                    FontSize = 11,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = ColorResource("TextSecondary")
                },
                new Label
                {
                    Text = "R$ 24.850,00",
                    FontSize = 22,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = ColorResource("TextPrimary")
                },
                new HorizontalStackLayout
                {
                    Spacing = 4,
                    Children =
                    {
                        new Label { Text = "+14.8%", FontSize = 12, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#10B981") },
                        new Label { Text = "este mês", FontSize = 11, TextColor = Color.FromArgb("#4E4E5F") }
                    }
                }
            }
        };

        var chart = new Grid
        {
            HeightRequest = 100,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 8,
            VerticalOptions = LayoutOptions.End
        };
        var barHeights = new double[] { 38, 56, 44, 75, 100 };
        for (var index = 0; index < barHeights.Length; index++)
        {
            var bar = new Border
            {
                WidthRequest = 18,
                HeightRequest = barHeights[index],
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.End,
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(4) },
                Background = index == barHeights.Length - 1
                    ? new LinearGradientBrush(
                        [new GradientStop(Color.FromArgb("#9B8DF5"), 0), new GradientStop(Color.FromArgb("#7D6BF0"), 1)],
                        new Point(0, 0), new Point(0, 1))
                    : new SolidColorBrush(index == 3 ? Color.FromArgb("#2A264D") : Color.FromArgb("#1C1D23"))
            };
            chart.Add(bar, index, 0);
        }

        var valuePanel = new Border
        {
            WidthRequest = 260,
            HeightRequest = 260,
            Padding = new Thickness(24),
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            StrokeThickness = 1.5,
            BackgroundColor = Color.FromArgb("#121217"),
            Stroke = new SolidColorBrush(Color.FromArgb("#333442")),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(28) },
            Content = new Grid
            {
                RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) },
                Children = { valueDetails, chart }
            }
        };
        Grid.SetRow(chart, 1);
        layout.Add(valuePanel, 0, 2);

        var copy = new VerticalStackLayout { Spacing = 12, VerticalOptions = LayoutOptions.End };
        copy.Children.Add(new Label
        {
            Text = "Descubra quanto ela vale",
            FontSize = 28,
            FontAttributes = FontAttributes.Bold,
            LineHeight = 1.2,
            TextColor = ColorResource("TextPrimary")
        });
        copy.Children.Add(new Label
        {
            Text = "Track real-time values, analyze price history, and watch your portfolio grow.",
            FontSize = 16,
            LineHeight = 1.5,
            TextColor = ColorResource("TextSecondary")
        });
        layout.Add(copy, 0, 3);

        var footer = new Grid { RowDefinitions = { new RowDefinition(new GridLength(24)), new RowDefinition(new GridLength(76)) } };
        var progress = new HorizontalStackLayout { Spacing = 8, VerticalOptions = LayoutOptions.Center };
        progress.Children.Add(CreateProgressDot());
        progress.Children.Add(new Border
        {
            WidthRequest = 24,
            HeightRequest = 6,
            BackgroundColor = ColorResource("BrandPrimary"),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(4) }
        });
        progress.Children.Add(CreateProgressDot());
        footer.Add(progress, 0, 0);

        var next = new Button
        {
            Text = "Próximo",
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            CornerRadius = 14,
            HeightRequest = 52,
            Margin = new Thickness(0, 24, 0, 0),
            TextColor = ColorResource("TextInverse"),
            BackgroundColor = ColorResource("BrandPrimary")
        };
        next.Clicked += async (_, _) => await _viewModel.NavigateCommand.ExecuteAsync("signup");
        footer.Add(next, 0, 1);
        layout.Add(footer, 0, 4);

        Content = layout;
    }

    private static Border CreateProgressDot() => new()
    {
        WidthRequest = 6,
        HeightRequest = 6,
        BackgroundColor = Color.FromArgb("#393944"),
        StrokeThickness = 0,
        StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(3) }
    };

    private void AddScannerVisual(ScreenDefinition screen)
    {
        if (screen.Id == "scanner")
        {
            var cardFrame = new Border
            {
                HeightRequest = 350,
                WidthRequest = 250,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                StrokeThickness = 2,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(22) },
                Content = new VerticalStackLayout
                {
                    VerticalOptions = LayoutOptions.Center,
                    HorizontalOptions = LayoutOptions.Center,
                    Spacing = 12,
                    Children =
                    {
                        CreateLabel("✦", "H1TextStyle", "BrandPrimary"),
                        CreateLabel("Área de captura", "BodySmallTextStyle")
                    }
                }
            };
            cardFrame.SetDynamicResource(VisualElement.BackgroundColorProperty, "SurfaceDefault");
            cardFrame.SetDynamicResource(Border.StrokeProperty, "BrandPrimary");
            _content.Children.Add(cardFrame);
            _content.Children.Add(CreateLabel("A câmera será ativada quando a integração de captura estiver disponível.", "CaptionTextStyle"));
            return;
        }

        if (screen.Id == "scanner-analyzing")
        {
            var analysis = new HorizontalStackLayout { Spacing = 12, HorizontalOptions = LayoutOptions.Center };
            analysis.Children.Add(new ActivityIndicator { IsRunning = true, Color = ColorResource("BrandPrimary"), VerticalOptions = LayoutOptions.Center });
            analysis.Children.Add(CreateLabel("Analisando imagem e detalhes...", "BodySmallTextStyle"));
            _content.Children.Add(CreateSurface(analysis));
            return;
        }

        if (screen.Id == "scan-result")
        {
            var match = CreateSurface(new VerticalStackLayout
            {
                Spacing = 10,
                Children =
                {
                    CreateLabel("98% DE PRECISÃO", "CaptionTextStyle", "StatusSuccess"),
                    CreateLabel("Match perfeito", "H3TextStyle"),
                    CreateLabel("Pikachu VMAX · Lost Origin · 029/196", "BodySmallTextStyle")
                }
            });
            _content.Children.Add(match);
        }

        if (screen.Id == "scan-confirmed")
            _content.Children.Add(CreateLabel("✓", "DisplayTextStyle", "StatusSuccess"));
    }

    private void AddAuthenticationFields(bool registration)
    {
        if (registration)
        {
            AddEntry("Nome", "Seu nome", value => _viewModel.DisplayName = value);
            AddEntry("Username", "seu_usuario", value => _viewModel.Username = value);
        }

        AddEntry("E-mail", "voce@exemplo.com", value => _viewModel.Email = value, Keyboard.Email);
        AddEntry("Senha", "Mínimo de 8 caracteres", value => _viewModel.Password = value, isPassword: true);
    }

    private void AddSearchField(ScreenDefinition screen)
    {
        var search = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 8 };
        var entry = new Entry { Placeholder = screen.InputHint, TextColor = ColorResource("TextPrimary"), PlaceholderColor = ColorResource("TextTertiary") };
        entry.SetDynamicResource(Entry.BackgroundColorProperty, "SurfaceDefault");
        entry.TextChanged += (_, args) => _viewModel.SearchText = args.NewTextValue ?? string.Empty;
        search.Add(entry, 0, 0);
        var searchButton = CreateButton("Buscar", true);
        searchButton.Clicked += async (_, _) => await _viewModel.NavigateCommand.ExecuteAsync("search-results");
        search.Add(searchButton, 1, 0);
        _content.Children.Add(search);

        if (screen.Id is "catalog" or "market")
        {
            var filters = new HorizontalStackLayout { Spacing = 8 };
            foreach (var game in screen.Options ?? [])
            {
                var filter = CreateButton(game, false);
                filter.Padding = new Thickness(12, 8);
                filter.Clicked += (_, _) => _viewModel.SearchText = game;
                filters.Children.Add(filter);
            }
            _content.Children.Add(new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = filters });
        }
    }

    private void AddItemFields(ScreenDefinition screen, bool includeListingFields)
    {
        _content.Children.Add(CreateLabel("CONDIÇÃO", "CaptionTextStyle"));
        foreach (var condition in screen.Options ?? [])
        {
            var choice = CreateButton(condition, _viewModel.SelectedCondition == condition);
            choice.Clicked += (_, _) =>
            {
                _viewModel.SelectConditionCommand.Execute(condition);
                BuildContent();
            };
            _content.Children.Add(choice);
        }

        AddEntry("Quantidade", "1", _ => { }, Keyboard.Numeric);
        AddEntry(includeListingFields ? "Preço de venda" : "Custo de aquisição", "R$ 0,00", _ => { }, Keyboard.Numeric);
        if (includeListingFields)
            AddEntry("Descrição do anúncio (opcional)", "Detalhes de envio, idioma ou estado...", _ => { });

        var photoButton = CreateButton("Adicionar foto real", false);
        photoButton.Clicked += (_, _) => _viewModel.NavigateCommand.Execute("unsupported");
        _content.Children.Add(photoButton);
    }

    private void AddEntry(string label, string placeholder, Action<string> update, Keyboard? keyboard = null, bool isPassword = false)
    {
        _content.Children.Add(CreateLabel(label.ToUpperInvariant(), "CaptionTextStyle"));
        var entry = new Entry
        {
            Placeholder = placeholder,
            TextColor = ColorResource("TextPrimary"),
            PlaceholderColor = ColorResource("TextTertiary"),
            Keyboard = keyboard ?? Keyboard.Default,
            IsPassword = isPassword
        };
        entry.SetDynamicResource(Entry.BackgroundColorProperty, "SurfaceDefault");
        entry.TextChanged += (_, args) => update(args.NewTextValue ?? string.Empty);
        _content.Children.Add(entry);
    }

    private void AddOptions(ScreenDefinition screen)
    {
        if (screen.Id is "catalog" or "market" or "add-to-collection" or "sell")
            return;

        foreach (var option in screen.Options!)
        {
            if (screen.Id == "preferences-tcg")
            {
                var row = new HorizontalStackLayout { Spacing = 12, VerticalOptions = LayoutOptions.Center };
                var checkBox = new CheckBox { IsChecked = _viewModel.SelectedTcgs.Contains(option), Color = ColorResource("BrandPrimary") };
                checkBox.CheckedChanged += (_, args) =>
                {
                    if (args.Value != _viewModel.SelectedTcgs.Contains(option))
                        _viewModel.ToggleTcgCommand.Execute(option);
                };
                row.Children.Add(checkBox);
                row.Children.Add(CreateLabel(option, "BodyTextStyle"));
                var tcgSurface = CreateSurface(row);
                tcgSurface.GestureRecognizers.Add(new TapGestureRecognizer
                {
                    Command = new Command(() =>
                    {
                        _viewModel.ToggleTcgCommand.Execute(option);
                        checkBox.IsChecked = _viewModel.SelectedTcgs.Contains(option);
                    })
                });
                _content.Children.Add(tcgSurface);
            }
            else
            {
                var chip = CreateButton(option, false);
                chip.Clicked += async (_, _) =>
                {
                    if (screen.Id == "settings" && option == "TCGs preferidos")
                        await _viewModel.NavigateCommand.ExecuteAsync("preferences-tcg");
                    else
                        await _viewModel.NavigateCommand.ExecuteAsync("unsupported");
                };
                _content.Children.Add(chip);
            }
        }
    }

    private void AddMetrics(ScreenDefinition screen)
    {
        var metrics = screen.Id == "portfolio"
            ? screen.Metrics!.Concat([new ScreenMetric("VARIAÇÃO 24H", "+ 2,4%"), new ScreenMetric("VARIAÇÃO 30D", "+ 8,7%")])
            : screen.Metrics!;

        foreach (var metric in metrics)
        {
            var block = new VerticalStackLayout { Spacing = 4 };
            block.Children.Add(CreateLabel(metric.Label, "CaptionTextStyle"));
            block.Children.Add(CreateLabel(metric.Value, "H2TextStyle"));
            if (metric.Detail is not null)
                block.Children.Add(CreateLabel(metric.Detail, "CaptionTextStyle"));
            _content.Children.Add(CreateSurface(block));
        }
    }

    private void AddCards(ScreenDefinition screen)
    {
        foreach (var card in screen.Cards!)
        {
            var row = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 12 };
            var details = new VerticalStackLayout { Spacing = 4 };
            details.Children.Add(CreateLabel(card.Game, "CaptionTextStyle", "BrandPrimary"));
            details.Children.Add(CreateLabel(card.Title, "TitleTextStyle"));
            details.Children.Add(CreateLabel(card.Detail, "BodySmallTextStyle"));
            row.Add(details, 0, 0);
            var price = new VerticalStackLayout { Spacing = 4, VerticalOptions = LayoutOptions.Center };
            price.Children.Add(CreateLabel(card.Price, "LabelTextStyle"));
            if (card.Change is not null)
                price.Children.Add(CreateLabel(card.Change, "CaptionTextStyle", card.Change.StartsWith('-') ? "StatusError" : "StatusSuccess"));
            row.Add(price, 1, 0);
            var cardSurface = CreateSurface(row);
            cardSurface.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(async () => await _viewModel.NavigateCommand.ExecuteAsync(screen.Id switch
                {
                    "collection" => "collection-item",
                    "scan-candidates" => "scan-confirmed",
                    _ => "card-detail"
                }))
            });
            _content.Children.Add(cardSurface);
        }
    }

    private Label CreateLabel(string? text, string style, string? colorResource = null)
    {
        var label = new Label { Text = text, Style = (Style)Application.Current!.Resources[style] };
        if (colorResource is not null)
            label.SetDynamicResource(Label.TextColorProperty, colorResource);
        return label;
    }

    private Border CreateSurface(View content)
    {
        var border = new Border
        {
            Padding = new Thickness(16),
            StrokeThickness = 1,
            Content = content
        };
        border.SetDynamicResource(VisualElement.BackgroundColorProperty, "SurfaceDefault");
        border.SetDynamicResource(Border.StrokeProperty, "BorderSubtle");
        border.StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(16) };
        return border;
    }

    private Button CreateButton(string text, bool primary)
    {
        var button = new Button { Text = text, CornerRadius = 14, Padding = new Thickness(18, 14), FontAttributes = FontAttributes.Bold };
        button.SetDynamicResource(Button.BackgroundColorProperty, primary ? "BrandPrimary" : "SurfaceElevated");
        button.SetDynamicResource(Button.TextColorProperty, "TextPrimary");
        return button;
    }

    private Button CreateGoogleSignInButton()
    {
        var button = new Button
        {
            ImageSource = "google-g.svg",
            WidthRequest = 52,
            HeightRequest = 52,
            Padding = new Thickness(14),
            CornerRadius = 14,
            HorizontalOptions = LayoutOptions.Center,
            BackgroundColor = ColorResource("SurfaceElevated")
        };
        SemanticProperties.SetDescription(button, "Continuar com Google");
        return button;
    }

    private static Color ColorResource(string key) => (Color)Application.Current!.Resources[key];
}
