using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public sealed class CompactNavigationStateTests
{
    [Fact]
    public void Agenda_starts_with_list_and_returns_through_accounts()
    {
        var state = new CompactNavigationState();
        Assert.Equal(CompactPane.List, state.Current);
        state.Show(CompactPane.Detail);
        Assert.True(state.Back());
        Assert.Equal(CompactPane.List, state.Current);
        Assert.True(state.Back());
        Assert.Equal(CompactPane.Accounts, state.Current);
        Assert.False(state.Back());
    }

    [Fact]
    public void Mail_account_folder_message_back_path_is_stable()
    {
        var state = new CompactNavigationState(CompactPane.Accounts);
        Assert.False(state.CanGoBack);
        state.Show(CompactPane.List);
        state.Show(CompactPane.Detail, CompactPane.List);
        state.Back();
        Assert.Equal(CompactPane.List, state.Current);
        state.Back();
        Assert.Equal(CompactPane.Accounts, state.Current);
        state.Show(CompactPane.List);
        Assert.True(state.CanGoBack);
    }

    [Fact]
    public void Account_setup_returns_directly_to_accounts_even_when_opened_from_detail()
    {
        var state = new CompactNavigationState();
        state.Show(CompactPane.Detail);
        state.Show(CompactPane.Detail, CompactPane.Accounts);
        state.Back();
        Assert.Equal(CompactPane.Accounts, state.Current);
    }

    [Fact]
    public void Reentering_compose_keeps_its_original_return_step()
    {
        var state = new CompactNavigationState(CompactPane.Accounts);
        state.Show(CompactPane.Detail);
        state.Show(CompactPane.Detail);
        state.Back();
        Assert.Equal(CompactPane.Accounts, state.Current);
    }

    [Fact]
    public void Deep_link_returns_to_list_without_requiring_prior_list_visit()
    {
        var state = new CompactNavigationState(CompactPane.Accounts);
        state.Show(CompactPane.Detail, CompactPane.List);
        state.Back();
        Assert.Equal(CompactPane.List, state.Current);
    }

    [Fact]
    public void Back_invalidates_async_folder_navigation()
    {
        var state = new CompactNavigationState();
        int request = state.Version;
        state.Back();
        Assert.False(state.IsCurrent(request));
        Assert.Equal(CompactPane.Accounts, state.Current);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void A_new_intent_invalidates_old_load_even_when_step_does_not_change(int value)
    {
        var step = (CompactPane)value;
        var state = new CompactNavigationState(step);
        int old = state.Version;
        state.Show(step);
        Assert.False(state.IsCurrent(old));
        Assert.True(state.IsCurrent(state.Version));
    }

    [Fact]
    public void Back_then_forward_never_revives_old_async_request()
    {
        var state = new CompactNavigationState();
        int request = state.Version;
        state.Back();
        state.Show(CompactPane.List);
        Assert.Equal(CompactPane.List, state.Current);
        Assert.False(state.IsCurrent(request));
    }
}
