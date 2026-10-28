namespace Matchmaker.Web.Components.Shared;

/// <summary>
/// Pages interactives : le circuit Blazor vit aussi longtemps que l'onglet. Chaque action prend ses services
/// (et donc son DbContext) dans une portée à part, libérée aussitôt.
/// </summary>
public static class ServiceScopes
{
    public static async Task<T> UsingScopeAsync<T>(this IServiceScopeFactory scopes, Func<IServiceProvider, Task<T>> work)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await work(scope.ServiceProvider);
    }

    public static async Task UsingScopeAsync(this IServiceScopeFactory scopes, Func<IServiceProvider, Task> work)
    {
        await using var scope = scopes.CreateAsyncScope();
        await work(scope.ServiceProvider);
    }
}
