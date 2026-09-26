using CommunityToolkit.Mvvm.ComponentModel;
using Vaulta.Identity.Contracts;

namespace Vaulta.App.State;

public partial class SessionState : ObservableObject
{
    [ObservableProperty]
    private UserSummaryDto? user;

    public bool IsAuthenticated => User is not null;

    partial void OnUserChanged(UserSummaryDto? value) => OnPropertyChanged(nameof(IsAuthenticated));
}
