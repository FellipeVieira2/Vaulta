using Vaulta.App.ViewModels;
using Microsoft.Maui.Storage;
using Microsoft.Maui.Controls.Shapes;

namespace Vaulta.App.Views;

public sealed partial class ExperiencePage : ContentPage, IQueryAttributable
{
    private readonly ExperienceViewModel _viewModel;
    private readonly VerticalStackLayout _content;
    private readonly ScrollView _scroll;
    private string? _builtScreen;
    private bool _visible;

    public ExperiencePage(ExperienceViewModel viewModel)
    {
        _viewModel = viewModel;
        BindingContext = viewModel;
        _content = new VerticalStackLayout { Padding = new Thickness(24, 28), Spacing = 18 };
        _scroll = new ScrollView { Content = _content };
        Content = _scroll;
        BackgroundColor = ColorResource("BackgroundPrimary");
        Shell.SetNavBarIsVisible(this, false);
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
        _visible = true;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        if (_builtScreen != _viewModel.ScreenId) BuildContent();
        _viewModel.RefreshGreeting();
        _ = RevealAsync();
    }

    protected override void OnDisappearing()
    {
        _visible = false;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        Content?.CancelAnimations();
        base.OnDisappearing();
    }

    private async Task RevealAsync()
    {
        if (!_visible || Content is not VisualElement view) return;
        await UiMotion.RevealAsync(view);
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(ExperienceViewModel.ScreenId) or nameof(ExperienceViewModel.Screen))
            MainThread.BeginInvokeOnMainThread(BuildContent);
    }

    private void BuildContent()
    {
        var screen = _viewModel.Screen;
        _builtScreen = screen.Id;
        Title = screen.Title;
        Shell.SetTabBarIsVisible(this, screen.Id is "home" or "collection" or "market" or "profile");
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

        Content = _scroll;
        _content.Children.Clear();
        if (screen.Id is not ("home" or "collection" or "market" or "profile" or "login"))
        {
            var back = CreateButton("‹  Voltar", false);
            back.HorizontalOptions = LayoutOptions.Start;
            back.BackgroundColor = Colors.Transparent;
            back.Clicked += async (_, _) => await Shell.Current.GoToAsync("..");
            _content.Children.Add(back);
        }

        if (!string.IsNullOrWhiteSpace(screen.Eyebrow))
            _content.Children.Add(CreateLabel(screen.Eyebrow, "CaptionTextStyle", "BrandPrimary"));

        var heading = CreateLabel(screen.Title, "H1TextStyle");
        if (screen.Id == "home") heading.SetBinding(Label.TextProperty, nameof(ExperienceViewModel.DisplayGreeting));
        _content.Children.Add(heading);

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

        if (screen.Id == "checkout")
            AddCheckoutAddressFields();

        if (screen.Id == "wallet")
            AddWalletView();

        if (screen.Options is not null)
            AddOptions(screen);

        if (screen.Metrics is not null)
            AddMetrics(screen);

        if (screen.Id == "card-detail")
            _content.Children.Add(CreateCardArtwork(_viewModel.SelectedPrinting?.ArtworkUrl, screen.Title, 300));

        if (screen.Id == "collection-item")
        {
            AddCollectionItemFields(screen);
            var cardHistory = _viewModel.SelectedPrintingId.HasValue
                ? _viewModel.GetCardChartData(_viewModel.SelectedPrintingId.Value)
                : null;
            _content.Children.Add(new Vaulta.App.Views.Components.VaultaChart
            {
                ChartHeight = 120,
                ItemsSource = cardHistory
            });
        }

        if (screen.Id == "market")
        {
            _viewModel.LoadMarketplaceListingsCommand.Execute(CancellationToken.None);
            if (_viewModel.MarketplaceListings is { Items.Count: > 0 })
            {
                foreach (var listing in _viewModel.MarketplaceListings.Items)
                {
                    var details = new VerticalStackLayout
                    {
                        Spacing = 6,
                        Children =
                        {
                            CreateLabel($"R$ {listing.PriceBrl:N2}", "H3TextStyle", "BrandPrimary"),
                            CreateLabel(listing.Condition, "CaptionTextStyle", "TextSecondary"),
                            CreateLabel(listing.Status, "CaptionTextStyle", "StatusSuccess"),
                            CreateLabel($"Criado em: {listing.CreatedAt:dd/MM/yyyy}", "CaptionTextStyle", "TextTertiary")
                        }
                    };
                    if (!string.IsNullOrWhiteSpace(listing.Description))
                        details.Children.Add(CreateLabel(listing.Description, "BodySmallTextStyle"));
                    var viewBtn = CreateButton("Ver detalhes", false);
                    viewBtn.Clicked += async (_, _) =>
                    {
                        _viewModel.SelectCard("market", new ScreenCard(
                            Title: $"Listing {listing.Id:N}",
                            Game: "Marketplace",
                            Detail: listing.Condition,
                            Price: $"R$ {listing.PriceBrl:N2}",
                            ArtworkUrl: listing.Photos.FirstOrDefault()?.Url,
                            ItemId: listing.Id));
                        await _viewModel.NavigateCommand.ExecuteAsync("listing-detail");
                    };
                    details.Children.Add(viewBtn);
                    _content.Children.Add(CreateSurface(details));
                }
            }
            else if (!_viewModel.IsBusy)
            {
                _content.Children.Add(CreateLabel("Nenhum anúncio ativo no momento.", "BodyTextStyle", "TextSecondary"));
            }
        }
        else if (screen.Cards is not null)
        {
            AddCards(screen);
        }

        var status = CreateLabel(null, "BodySmallTextStyle", "StatusWarning");
        status.SetBinding(Label.TextProperty, nameof(ExperienceViewModel.StatusMessage));
        status.SetBinding(IsVisibleProperty, nameof(ExperienceViewModel.HasStatusMessage));
        _content.Children.Add(status);
        var busy = new ActivityIndicator { Color = ColorResource("BrandPrimary"), HeightRequest = 24 };
        busy.SetBinding(ActivityIndicator.IsRunningProperty, nameof(ExperienceViewModel.IsBusy));
        busy.SetBinding(IsVisibleProperty, nameof(ExperienceViewModel.IsBusy));
        _content.Children.Add(busy);

        if (screen.Actions is null || screen.Id is "checkout" or "wallet") return;
        foreach (var action in screen.Actions)
        {
            var isGoogleSignIn = action.Title == "Continuar com Google";
            var button = isGoogleSignIn ? CreateGoogleSignInButton() : CreateButton(action.Title, action.IsPrimary);
            if (screen.Id == "listing-detail" && action.Title == "Comprar agora")
            {
                button.Clicked += async (_, _) =>
                {
                    if (_viewModel.SelectedListingId is Guid listingId)
                        await _viewModel.BuyListingCommand.ExecuteAsync(listingId);
                    else
                        _viewModel.StatusMessage = "ID do listing não disponível para compra.";
                };
            }
            else
            {
                button.Clicked += async (_, _) => await _viewModel.NavigateCommand.ExecuteAsync(action.Route);
            }
            if (action.Route is "login-submit" or "signup-submit")
                button.Triggers.Add(new DataTrigger(typeof(Button))
                {
                    Binding = new Binding(nameof(ExperienceViewModel.IsBusy)), Value = true,
                    Setters = { new Setter { Property = Button.TextProperty, Value = "Aguarde…" } }
                });
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
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto)
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
            Text = "Catalogue suas cartas e encontre tudo o que você tem em um só lugar.",
            FontSize = 16,
            LineHeight = 1.5,
            TextColor = ColorResource("TextSecondary")
        });
        layout.Add(copy, 0, 3);

        var footer = new Grid { Padding = new Thickness(0, 12, 0, 8), RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto) } };
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
        footer.Add(progress, 0, 0);

        var next = new Button
        {
            Text = "Próximo",
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            CornerRadius = 14,
            MinimumHeightRequest = 52,
            Margin = new Thickness(0, 24, 0, 0),
            TextColor = ColorResource("TextInverse"),
            BackgroundColor = ColorResource("BrandPrimary")
        };
        next.Clicked += async (_, _) => await _viewModel.NavigateCommand.ExecuteAsync("onboarding-2");
        footer.Add(next, 0, 1);
        Content = ComposeOnboarding(layout, footer);
        UiMotion.AttachPress(next);
        BindInteraction(next);
        BindInteraction(skip);
        skip.MinimumHeightRequest = 48;
        skip.MinimumWidthRequest = 48;
    }

    private void BuildOnboardingTwo()
    {
        Shell.SetNavBarIsVisible(this, false);

        var layout = new Grid
        {
            Padding = new Thickness(24, 8, 24, 8),
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto)
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
                    Text = "EXEMPLO DE COLEÇÃO",
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
                        new Label { Text = "+14,8%", FontSize = 12, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#10B981") },
                        new Label { Text = "este mês", FontSize = 11, TextColor = ColorResource("TextSecondary") }
                    }
                }
            }
        };

        var portfolioHistory = _viewModel.GetPortfolioChartData();
        var chart = new Vaulta.App.Views.Components.VaultaChart
        {
            ChartHeight = 140,
            ItemsSource = portfolioHistory
        };

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
            Text = "Organize seu acervo e prepare sua coleção para acompanhar valores e tendências.",
            FontSize = 16,
            LineHeight = 1.5,
            TextColor = ColorResource("TextSecondary")
        });
        layout.Add(copy, 0, 3);

        var footer = new Grid { Padding = new Thickness(0, 12, 0, 8), RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto) } };
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
        footer.Add(progress, 0, 0);

        var next = new Button
        {
            Text = "Criar minha conta",
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            CornerRadius = 14,
            MinimumHeightRequest = 52,
            Margin = new Thickness(0, 24, 0, 0),
            TextColor = ColorResource("TextInverse"),
            BackgroundColor = ColorResource("BrandPrimary")
        };
        next.Clicked += async (_, _) => await _viewModel.NavigateCommand.ExecuteAsync("signup");
        footer.Add(next, 0, 1);
        Content = ComposeOnboarding(layout, footer);
        UiMotion.AttachPress(next);
        BindInteraction(next);
        BindInteraction(skip);
        skip.MinimumHeightRequest = 48;
        skip.MinimumWidthRequest = 48;
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
            var captureButton = CreateButton("📷  Escanear Carta", true);
            captureButton.MinimumHeightRequest = 64;
            captureButton.FontSize = 18;
            captureButton.Clicked += async (_, _) => await _viewModel.ScanCardCommand.ExecuteAsync(CancellationToken.None);
            SemanticProperties.SetDescription(captureButton, "Abrir câmera para escanear carta Pokémon");
            _content.Children.Add(captureButton);

            if (_viewModel.CapturedImageBytes is not null)
            {
                var preview = new Image
                {
                    Source = ImageSource.FromStream(() => new MemoryStream(_viewModel.CapturedImageBytes)),
                    Aspect = Aspect.AspectFit,
                    HeightRequest = 280,
                    Margin = new Thickness(0, 16, 0, 0)
                };
                SemanticProperties.SetDescription(preview, "Foto da carta capturada");
                _content.Children.Add(preview);
            }

            if (_viewModel.HasScanCandidates && _viewModel.ScanResult is not null)
            {
                _content.Children.Add(CreateLabel("Resultados encontrados:", "TitleTextStyle"));
                foreach (var candidate in _viewModel.ScanResult.Candidates)
                {
                    var details = new VerticalStackLayout
                    {
                        Spacing = 6,
                        Children =
                        {
                            CreateLabel(candidate.Name, "H3TextStyle"),
                            CreateLabel($"{candidate.SetName} · #{candidate.CollectorNumber}", "BodySmallTextStyle"),
                            CreateLabel($"Confiança: {candidate.ConfidenceScore:P0}", "CaptionTextStyle", "StatusSuccess")
                        }
                    };
                    if (candidate.EstimatedMarketValueBrl.HasValue)
                        details.Children.Add(CreateLabel($"Valor estimado: {candidate.EstimatedMarketValueBrl.Value:C2}", "LabelTextStyle", "BrandPrimary"));
                    else
                        details.Children.Add(CreateLabel("Valor de mercado indisponível", "CaptionTextStyle", "TextTertiary"));

                    var addBtn = CreateButton("Adicionar à Coleção", true);
                    addBtn.Clicked += async (_, _) =>
                    {
                        await _viewModel.AddScannedCardToCollectionCommand.ExecuteAsync(candidate);
                        if (_viewModel.StatusMessage?.Contains("adicionada", StringComparison.OrdinalIgnoreCase) == true)
                        {
                            _viewModel.SelectCard("scan-candidates", new ScreenCard(
                                Title: candidate.Name,
                                Game: "Pokémon",
                                Detail: $"{candidate.SetName} · #{candidate.CollectorNumber}",
                                Price: candidate.EstimatedMarketValueBrl?.ToString("C2") ?? "Valor indisponível",
                                ArtworkUrl: candidate.ArtworkUrl,
                                Change: null));
                            await _viewModel.NavigateCommand.ExecuteAsync("scan-confirmed");
                        }
                    };
                    details.Children.Add(addBtn);
                    _content.Children.Add(CreateSurface(details));
                }
            }
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

        if (screen.Id == "scan-confirmed")
        {
            var confirmed = new VerticalStackLayout
            {
                Spacing = 12,
                HorizontalOptions = LayoutOptions.Center,
                Children =
                {
                    CreateLabel("✓", "DisplayTextStyle", "StatusSuccess"),
                    CreateLabel("Carta adicionada com sucesso!", "H2TextStyle"),
                    CreateLabel("Sua coleção foi atualizada.", "BodyTextStyle", "TextSecondary")
                }
            };
            _content.Children.Add(CreateSurface(confirmed));
        }
    }

    private void AddAuthenticationFields(bool registration)
    {
        if (registration)
        {
            AddEntry("Nome", "Seu nome", nameof(ExperienceViewModel.DisplayName));
            AddEntry("Nome de usuário", "seu_usuario", nameof(ExperienceViewModel.Username));
        }

        AddEntry("E-mail", "voce@exemplo.com", nameof(ExperienceViewModel.Email), Keyboard.Email);
        AddEntry("Senha", registration ? "Crie uma senha forte" : "Sua senha", nameof(ExperienceViewModel.Password), isPassword: true);
        if (registration) _content.Children.Add(CreateLabel("Use 12 a 128 caracteres, com maiúscula, minúscula, número e símbolo.", "BodySmallTextStyle"));
    }

    private void AddSearchField(ScreenDefinition screen)
    {
        var search = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 8 };
        var entry = new Entry { Placeholder = screen.InputHint, TextColor = ColorResource("TextPrimary"), PlaceholderColor = ColorResource("TextTertiary") };
        entry.SetDynamicResource(Entry.BackgroundColorProperty, "SurfaceDefault");
        entry.SetBinding(Entry.TextProperty, nameof(ExperienceViewModel.SearchText), mode: BindingMode.TwoWay);
        entry.MinimumHeightRequest = 48;
        SemanticProperties.SetDescription(entry, "Buscar cartas");
        search.Add(entry, 0, 0);
        var searchButton = CreateButton("Buscar", true);
        searchButton.Clicked += async (_, _) => await _viewModel.SearchNowCommand.ExecuteAsync(null);
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

            // Filter-specific price variation chart (updates when a filter/search is applied)
            var filterChart = new Vaulta.App.Views.Components.VaultaChart
            {
                ChartHeight = 100,
                Margin = new Thickness(0, 12, 0, 0)
            };
            filterChart.SetBinding(Vaulta.App.Views.Components.VaultaChart.ItemsSourceProperty,
                new Binding(nameof(_viewModel.FilterChartData), source: _viewModel));
            _content.Children.Add(filterChart);
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
                UpdateConditionButtons();
            };
            choice.ClassId = condition;
            _content.Children.Add(choice);
        }

        AddEntry("Quantidade", "1", nameof(ExperienceViewModel.Quantity), Keyboard.Numeric);
        AddEntry(includeListingFields ? "Preço de venda" : "Custo de aquisição", "R$ 0,00", nameof(ExperienceViewModel.AcquisitionCost), Keyboard.Numeric);
        if (includeListingFields)
            AddEntry("Descrição do anúncio (opcional)", "Detalhes de envio, idioma ou estado...", nameof(ExperienceViewModel.ItemNotes));

        if (includeListingFields)
        {
            var photoButton = CreateButton("📷 Adicionar foto do card", false);
            photoButton.Clicked += async (_, _) => await _viewModel.PickSellPhotoCommand.ExecuteAsync(null);
            _content.Children.Add(photoButton);

            var submitButton = CreateButton("Publicar anúncio", true);
            submitButton.Clicked += async (_, _) => await _viewModel.SubmitListingCommand.ExecuteAsync(null);
            _content.Children.Add(submitButton);
        }
        else
        {
            var photoButton = CreateButton("Adicionar foto real", false);
            photoButton.Clicked += (_, _) => _viewModel.NavigateCommand.Execute("unsupported");
            _content.Children.Add(photoButton);
        }
    }

    private void AddCheckoutAddressFields()
    {
        AddEntry("Rua / Logradouro", "Ex: Rua das Flores, 123", nameof(ExperienceViewModel.ShippingStreet));
        AddEntry("Cidade", "Ex: São Paulo", nameof(ExperienceViewModel.ShippingCity));
        AddEntry("Estado", "Ex: SP", nameof(ExperienceViewModel.ShippingState));
        AddEntry("CEP", "00000-000", nameof(ExperienceViewModel.ShippingZipCode), Keyboard.Numeric);

        var submitButton = CreateButton("Confirmar e pagar", true);
        submitButton.Clicked += async (_, _) => await _viewModel.SubmitCheckoutCommand.ExecuteAsync(null);
        _content.Children.Add(submitButton);
    }

    private void AddWalletView()
    {
        var balanceLabel = CreateLabel("R$ 0,00", "TitleTextStyle", "BrandPrimary");
        balanceLabel.SetBinding(Label.TextProperty, new Binding(nameof(ExperienceViewModel.WalletBalance), stringFormat: "R$ {0:N2}"));
        _content.Children.Add(balanceLabel);

        _content.Children.Add(CreateLabel("HISTÓRICO DE TRANSAÇÕES", "CaptionTextStyle"));
        _content.Children.Add(CreateLabel("Carregando transações...", "BodySmallTextStyle", "TextSecondary"));

        AddEntry("Valor do saque", "Ex: 150,00", nameof(ExperienceViewModel.WithdrawAmount), Keyboard.Numeric);
        var withdrawButton = CreateButton("Solicitar saque", true);
        withdrawButton.Clicked += async (_, _) => await _viewModel.RequestWithdrawalCommand.ExecuteAsync(null);
        _content.Children.Add(withdrawButton);

        _ = _viewModel.LoadWalletCommand.ExecuteAsync(null);
    }

    private void AddEntry(string label, string placeholder, string propertyName, Keyboard? keyboard = null, bool isPassword = false)
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
        entry.SetBinding(Entry.TextProperty, propertyName, mode: BindingMode.TwoWay);
        BindInteraction(entry);
        entry.MinimumHeightRequest = 48;
        entry.FontSize = 16;
        SemanticProperties.SetDescription(entry, label);
        if (isPassword)
        {
            var row = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 8 };
            var toggle = CreateButton("Mostrar", false);
            toggle.Clicked += (_, _) => { entry.IsPassword = !entry.IsPassword; toggle.Text = entry.IsPassword ? "Mostrar" : "Ocultar"; };
            row.Add(entry); row.Add(toggle, 1);
            _content.Children.Add(row);
        }
        else _content.Children.Add(entry);
    }

    private void AddCollectionItemFields(ScreenDefinition screen)
    {
        _content.Children.Add(CreateLabel("CONDIÇÃO", "CaptionTextStyle"));
        foreach (var condition in screen.Options ?? [])
        {
            var choice = CreateButton(condition, _viewModel.SelectedCondition == condition);
            choice.Clicked += (_, _) =>
            {
                _viewModel.SelectConditionCommand.Execute(condition);
                UpdateConditionButtons();
            };
            choice.ClassId = condition;
            _content.Children.Add(choice);
        }

        AddEntry("Notas (opcional)", "Adicione observações sobre o item...", nameof(ExperienceViewModel.ItemNotes));
    }

    private void AddOptions(ScreenDefinition screen)
    {
        if (screen.Id is "catalog" or "market" or "add-to-collection" or "sell" or "collection-item")
            return;

        foreach (var option in screen.Options!)
        {
            if (screen.Id == "card-detail")
            {
                var variantChip = CreateButton(option, _viewModel.SelectedVariantLabel == option);
                variantChip.ClassId = option;
                variantChip.Clicked += (_, _) =>
                {
                    _viewModel.SelectVariantCommand.Execute(option);
                    UpdateVariantButtons();
                };
                _content.Children.Add(variantChip);
            }
            else if (screen.Id == "preferences-tcg")
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

    private Border CreatePortfolioChart()
{
// Simulated 7-day price history for the portfolio chart (replace with real API data later)
var values = new double[] { 12450, 12380, 12520, 12490, 12610, 12580, 12720 };
var min = values.Min();
var max = values.Max();
var range = max - min > 0 ? max - min : 1;
var chartHeight = 120.0;
var pointCount = values.Length;
var grid = new Grid
{
HeightRequest = chartHeight,
ColumnSpacing = 0,
RowSpacing = 0,
VerticalOptions = LayoutOptions.End,
HorizontalOptions = LayoutOptions.Fill
};
// Add column definitions
for (int i = 0; i < pointCount; i++)
grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
// Build vertical bar segments representing price movement per period
for (int i = 0; i < pointCount - 1; i++)
{
var norm = (values[i] - min) / range;
var barHeight = Math.Max(chartHeight * norm, 2);
var isUp = values[i + 1] >= values[i];
var bar = new Border
{
WidthRequest = 6,
HeightRequest = barHeight,
HorizontalOptions = LayoutOptions.Center,
VerticalOptions = LayoutOptions.End,
StrokeThickness = 0,
StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(3) },
Background = new SolidColorBrush(isUp ? Color.FromArgb("#22D3EE") : Color.FromArgb("#FB7185")),
Margin = new Thickness(0, 0, 0, 0)
};
grid.Add(bar, i, 0);
}
// Add data points as circles
for (int i = 0; i < pointCount; i++)
{
var norm = (values[i] - min) / range;
var y = chartHeight * (1 - norm);
var dot = new Border
{
WidthRequest = 8,
HeightRequest = 8,
StrokeThickness = 0,
StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(4) },
Background = new SolidColorBrush(Color.FromArgb("#22D3EE")),
VerticalOptions = LayoutOptions.End,
HorizontalOptions = LayoutOptions.Center,
Margin = new Thickness(0, 0, 0, y)
};
grid.Add(dot, i, 0);
}
return new Border
{
Content = grid,
Padding = new Thickness(16, 8, 16, 16),
StrokeThickness = 1,
StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(16) }
};
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
        button.SetDynamicResource(Button.TextColorProperty, primary ? "TextInverse" : "TextPrimary");
        button.MinimumHeightRequest = 48;
        button.FontSize = 15;
        BindInteraction(button);
        UiMotion.AttachPress(button);
        return button;
    }

    private Button CreateGoogleSignInButton()
    {
        var button = new Button
        {
            ImageSource = "google_g.png",
            WidthRequest = 52,
            HeightRequest = 52,
            Padding = new Thickness(14),
            CornerRadius = 14,
            HorizontalOptions = LayoutOptions.Center,
            BackgroundColor = ColorResource("SurfaceElevated")
        };
        BindInteraction(button);
        UiMotion.AttachPress(button);
        SemanticProperties.SetDescription(button, "Continuar com Google");
        return button;
    }

    private static Color ColorResource(string key) => (Color)Application.Current!.Resources[key];
}
