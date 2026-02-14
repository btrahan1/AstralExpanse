using Microsoft.JSInterop;

namespace AstralExpanse.Services;

public class BabylonService
{
    private readonly IJSRuntime _jsRuntime;
    private DotNetObjectReference<BabylonService>? _objRef;

    public event Action<string, string>? OnObjectClicked;

    public BabylonService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    public async Task InitializeEngine(string canvasId)
    {
        _objRef = DotNetObjectReference.Create(this);
        await _jsRuntime.InvokeVoidAsync("AstralEngine.init", canvasId, _objRef);
    }

    public async Task SpawnAsteroidField(string jsonContent, int count, float radius)
    {
        await _jsRuntime.InvokeVoidAsync("AstralEngine.spawnAsteroidField", jsonContent, count, radius);
    }

    public async Task<string> LoadModel(string jsonContent, float[]? pos = null, float scale = 1.0f, float[]? rot = null)
    {
        return await _jsRuntime.InvokeAsync<string>("AstralEngine.loadProceduralModel", jsonContent, pos ?? new float[] { 0, 0, 0 }, scale, rot ?? new float[] { 0, 0, 0 });
    }

    public async Task MoveModel(string id, float[] targetPos, float duration)
    {
        await _jsRuntime.InvokeVoidAsync("AstralEngine.moveModel", id, targetPos, duration);
    }

    public async Task SetSelected(string? id)
    {
        await _jsRuntime.InvokeVoidAsync("AstralEngine.setSelected", id);
    }

    public async Task<float[]?> GetModelPosition(string id)
    {
        return await _jsRuntime.InvokeAsync<float[]?>("AstralEngine.getModelPosition", id);
    }

    public async Task<List<RadarEntity>> GetRadarData()
    {
        return await _jsRuntime.InvokeAsync<List<RadarEntity>>("AstralEngine.getRadarData");
    }

    public event Action<string>? OnMoveComplete;

    [JSInvokable]
    public void NotifyMoveComplete(string id)
    {
        OnMoveComplete?.Invoke(id);
    }

    [JSInvokable]
    public void OnObjectPicked(string name, string id)
    {
        OnObjectClicked?.Invoke(name, id);
    }

    public void Dispose()
    {
        _objRef?.Dispose();
    }
}
