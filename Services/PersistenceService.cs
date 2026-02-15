using Microsoft.JSInterop;
using System.Text.Json;
using System.Threading.Tasks;

namespace AstralExpanse.Services;

public class PersistenceService
{
    private readonly IJSRuntime _js;
    private const string SaveKey = "astral_expanse_save";

    public PersistenceService(IJSRuntime js)
    {
        _js = js;
    }

    public async Task SaveGame(GameStateService state)
    {
        var json = JsonSerializer.Serialize(state);
        await _js.InvokeVoidAsync("localStorage.setItem", SaveKey, json);
    }

    public async Task<string?> LoadGameJson()
    {
        return await _js.InvokeAsync<string?>("localStorage.getItem", SaveKey);
    }

    public async Task ClearSave()
    {
        await _js.InvokeVoidAsync("localStorage.removeItem", SaveKey);
    }

    public async Task<bool> SaveExists()
    {
        var save = await LoadGameJson();
        return !string.IsNullOrEmpty(save);
    }
}
